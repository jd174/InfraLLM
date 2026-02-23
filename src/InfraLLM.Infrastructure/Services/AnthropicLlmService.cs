using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using InfraLLM.Core.Interfaces;
using InfraLLM.Core.Models;

namespace InfraLLM.Infrastructure.Services;

public class AnthropicLlmService : ILlmService
{
    public const string ProviderAnthropic = "anthropic";
    public const string ProviderOpenAi = "openai";
    public const string ProviderOllama = "ollama";

    private readonly IConfiguration _config;
    private readonly ILogger<AnthropicLlmService> _logger;
    private readonly HttpClient _httpClient;
    private readonly ICommandExecutor _commandExecutor;
    private readonly IHostRepository _hostRepository;
    private readonly IHostNoteRepository _hostNoteRepository;
    private readonly IMcpToolRegistry _mcpToolRegistry;
    private readonly JsonSerializerOptions _jsonOptions;

    private static readonly Dictionary<string, decimal> ModelPricing = new()
    {
        ["claude-sonnet-4-5-20250929"] = 0.003m,   // per 1K input tokens (approximate)
        ["claude-haiku-4-5-20251001"] = 0.0008m,
        ["gpt-5"] = 0.005m,
        ["gpt-4.1"] = 0.0025m,
        ["gpt-4o"] = 0.0025m,
    };

    private const int MaxToolLoopIterations = 10;

    public AnthropicLlmService(
        IConfiguration config,
        ILogger<AnthropicLlmService> logger,
        HttpClient httpClient,
        ICommandExecutor commandExecutor,
        IHostRepository hostRepository,
        IHostNoteRepository hostNoteRepository,
        IMcpToolRegistry mcpToolRegistry)
    {
        _config = config;
        _logger = logger;
        _httpClient = httpClient;
        _commandExecutor = commandExecutor;
        _hostRepository = hostRepository;
        _hostNoteRepository = hostNoteRepository;
        _mcpToolRegistry = mcpToolRegistry;

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }

    public async Task<LlmResponse> SendMessageAsync(
        string userId,
        string sessionId,
        Guid organizationId,
        string message,
        List<Host> availableHosts,
        List<Policy> policies,
        List<Message> conversationHistory,
        string? customSystemPrompt,
        string? personalizationPrompt,
        string? modelOverride,
        CancellationToken ct = default)
    {
        return await SendMessageInternalAsync(
            userId,
            sessionId,
            organizationId,
            message,
            availableHosts,
            policies,
            conversationHistory,
            customSystemPrompt,
            personalizationPrompt,
            modelOverride,
            null,
            null,
            ct);
    }

    public async Task<LlmResponse> SendMessageStreamAsync(
        string userId,
        string sessionId,
        Guid organizationId,
        string message,
        List<Host> availableHosts,
        List<Policy> policies,
        List<Message> conversationHistory,
        string? customSystemPrompt,
        string? personalizationPrompt,
        string? modelOverride,
        Func<string, Task> onTextDelta,
        Func<string, Task>? onStatusUpdate = null,
        CancellationToken ct = default)
    {
        if (onTextDelta == null) throw new ArgumentNullException(nameof(onTextDelta));

        return await SendMessageInternalAsync(
            userId,
            sessionId,
            organizationId,
            message,
            availableHosts,
            policies,
            conversationHistory,
            customSystemPrompt,
            personalizationPrompt,
            modelOverride,
            onTextDelta,
                onStatusUpdate,
            ct);
    }

    public async Task<string?> GenerateSessionTitleAsync(
        string userId,
        List<Message> conversationHistory,
        CancellationToken ct = default)
    {
        var provider = ResolveProvider(_config);
        if (!IsProviderConfigured(_config))
        {
            _logger.LogWarning("LLM provider is not configured (session title generation)");
            return null;
        }

        if (provider == ProviderOpenAi || provider == ProviderOllama)
        {
            return await GenerateSessionTitleOpenAiCompatibleAsync(provider, userId, conversationHistory, ct);
        }

        var model = _config["Anthropic:TitleModel"] ?? _config["Anthropic:Model"] ?? "claude-sonnet-4-5-20250929";
        var maxTokens = int.Parse(_config["Anthropic:TitleMaxTokens"] ?? "32");

        var prompt = BuildSessionTitlePrompt(conversationHistory);
        if (string.IsNullOrWhiteSpace(prompt)) return null;

        var request = new Dictionary<string, object>
        {
            ["model"] = model,
            ["max_tokens"] = maxTokens,
            ["system"] = "Generate a concise 3-6 word title for the conversation. Return only the title, no quotes.",
            ["messages"] = new List<Dictionary<string, object>>
            {
                new() { ["role"] = "user", ["content"] = prompt }
            }
        };

        var json = JsonSerializer.Serialize(request, _jsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await SendProviderRequestAsync(provider, HttpMethod.Post, "/v1/messages", content, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Anthropic API error (session title): {Status} - {Body}", response.StatusCode, responseBody);
            return null;
        }

        var apiResponse = JsonSerializer.Deserialize<AnthropicResponse>(responseBody, _jsonOptions);
        if (apiResponse == null) return null;

        var title = string.Join("", apiResponse.Content
            .Where(b => b.Type == "text" && !string.IsNullOrWhiteSpace(b.Text))
            .Select(b => b.Text))
            .Trim();

        if (string.IsNullOrWhiteSpace(title)) return null;

        title = title.Trim().Trim('"', '\'', '\u201c', '\u201d');
        if (title.StartsWith("Title:", StringComparison.OrdinalIgnoreCase))
        {
            title = title[6..].Trim();
        }
        title = title.TrimEnd('.', '!', '?');

        if (title.Length > 80)
        {
            title = title[..80].Trim();
        }

        _logger.LogInformation("Generated session title for user {UserId}: {Title}", userId, title);
        return string.IsNullOrWhiteSpace(title) ? null : title;
    }

    private async Task<LlmResponse> SendMessageInternalAsync(
        string userId,
        string sessionId,
        Guid organizationId,
        string message,
        List<Host> availableHosts,
        List<Policy> policies,
        List<Message> conversationHistory,
        string? customSystemPrompt,
        string? personalizationPrompt,
        string? modelOverride,
        Func<string, Task>? onTextDelta,
        Func<string, Task>? onStatusUpdate,
        CancellationToken ct)
    {
        var provider = ResolveProvider(_config);
        if (!IsProviderConfigured(_config))
        {
            _logger.LogWarning("LLM provider is not configured");
            return new LlmResponse
            {
                MessageId = Guid.NewGuid().ToString(),
                Content = "LLM service is not configured. Please configure an LLM provider and API key.",
                TokensUsed = 0,
                Cost = 0
            };
        }

        if (provider == ProviderOpenAi || provider == ProviderOllama)
        {
            return await SendOpenAiCompatibleMessageInternalAsync(
                provider,
                userId,
                sessionId,
                organizationId,
                message,
                availableHosts,
                policies,
                conversationHistory,
                customSystemPrompt,
                personalizationPrompt,
                modelOverride,
                onTextDelta,
                onStatusUpdate,
                ct);
        }

        var model = modelOverride ?? _config["Anthropic:Model"] ?? "claude-sonnet-4-5-20250929";
        var maxTokens = int.Parse(_config["Anthropic:MaxTokens"] ?? "8192");

        // Discover MCP tools for this organization (cached)
        var mcpToolJsonStrings = await _mcpToolRegistry.GetToolDefinitionsAsync(organizationId, ct);

        // Build system prompt with host and policy context
        var systemPrompt = await BuildSystemPromptAsync(
            userId,
            availableHosts,
            policies,
            customSystemPrompt,
            personalizationPrompt,
            mcpToolJsonStrings);

        // Build messages from conversation history (limit to last 50, filter empty content)
        var messages = conversationHistory
            .OrderBy(m => m.CreatedAt)
            .TakeLast(50)
            .Where(m => !string.IsNullOrWhiteSpace(m.Content))
            .Select(m => new Dictionary<string, object> { ["role"] = m.Role, ["content"] = m.Content })
            .ToList();

        // Add the current user message
        messages.Add(new Dictionary<string, object> { ["role"] = "user", ["content"] = message });

        // Build tool definitions: built-in SSH tools + MCP tools
        var allTools = BuildAllToolDefinitions(mcpToolJsonStrings);

        var totalTokens = 0;
        var allTextContent = new StringBuilder();
        var allToolCalls = new List<ToolCall>();
        string? lastMessageId = null;

        // Tool execution loop: keep calling until we get a final text response (end_turn)
        if (onStatusUpdate != null)
        {
            await onStatusUpdate("Thinking...");
        }

        for (var iteration = 0; iteration < MaxToolLoopIterations; iteration++)
        {
            var request = new Dictionary<string, object>
            {
                ["model"] = model,
                ["max_tokens"] = maxTokens,
                ["system"] = systemPrompt,
                ["tools"] = allTools,
                ["messages"] = messages
            };
            _logger.LogInformation(
                "Sending message to Anthropic API for user {UserId}, session {SessionId} (iteration {Iteration})",
                userId, sessionId, iteration);

            AnthropicResponse? apiResponse;

            if (onTextDelta != null)
            {
                request["stream"] = true;
                apiResponse = await SendStreamingRequestAsync(request, onTextDelta, ct);
            }
            else
            {
                var json = JsonSerializer.Serialize(request, _jsonOptions);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await SendProviderRequestAsync(ProviderAnthropic, HttpMethod.Post, "/v1/messages", content, ct);
                var responseBody = await response.Content.ReadAsStringAsync(ct);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("Anthropic API error: {Status} - {Body}", response.StatusCode, responseBody);
                    return new LlmResponse
                    {
                        MessageId = Guid.NewGuid().ToString(),
                        Content = allTextContent.Length > 0
                            ? allTextContent.ToString()
                            : $"LLM request failed: {response.StatusCode}",
                        TokensUsed = totalTokens,
                        Cost = 0
                    };
                }

                apiResponse = JsonSerializer.Deserialize<AnthropicResponse>(responseBody, _jsonOptions);
            }
            if (apiResponse == null)
            {
                return new LlmResponse
                {
                    MessageId = Guid.NewGuid().ToString(),
                    Content = allTextContent.Length > 0
                        ? allTextContent.ToString()
                        : "Failed to parse LLM response",
                    TokensUsed = totalTokens,
                    Cost = 0
                };
            }

            lastMessageId = apiResponse.Id;
            totalTokens += (apiResponse.Usage?.InputTokens ?? 0) + (apiResponse.Usage?.OutputTokens ?? 0);

            // Extract text content and tool_use blocks from response
            var toolUseBlocks = new List<AnthropicContentBlock>();

            foreach (var block in apiResponse.Content)
            {
                if (block.Type == "text" && !string.IsNullOrEmpty(block.Text))
                {
                    allTextContent.Append(block.Text);
                }
                else if (block.Type == "tool_use")
                {
                    toolUseBlocks.Add(block);
                    allToolCalls.Add(new ToolCall
                    {
                        ToolName = block.Name ?? "",
                        Parameters = block.Input ?? new Dictionary<string, object>()
                    });
                }
            }

            if (apiResponse.StopReason == "max_tokens")
            {
                _logger.LogWarning("LLM response truncated due to max_tokens limit");
                allTextContent.Append("\n\n[Response truncated: max tokens reached]");
            }

            // If stop_reason is NOT tool_use, we're done
            if (apiResponse.StopReason != "tool_use" || toolUseBlocks.Count == 0)
            {
                _logger.LogInformation("LLM conversation complete after {Iterations} iteration(s)", iteration + 1);
                break;
            }

            // Claude wants to use tools — execute them and continue the loop
            _logger.LogInformation("LLM requested {Count} tool call(s), executing...", toolUseBlocks.Count);
            if (onStatusUpdate != null)
            {
                var toolNames = toolUseBlocks
                    .Select(b => b.Name)
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Distinct()
                    .ToList();

                var status = toolNames.Count > 0
                    ? $"Running tools: {string.Join(", ", toolNames)}"
                    : $"Running {toolUseBlocks.Count} tool call(s)...";

                await onStatusUpdate(status);
            }

            // Add the assistant's response (with tool_use blocks) to messages
            var assistantContentBlocks = apiResponse.Content.Select(block =>
            {
                if (block.Type == "text")
                    return new Dictionary<string, object> { ["type"] = "text", ["text"] = block.Text ?? "" };
                else // tool_use
                    return new Dictionary<string, object>
                    {
                        ["type"] = "tool_use",
                        ["id"] = block.Id ?? "",
                        ["name"] = block.Name ?? "",
                        ["input"] = block.Input ?? new Dictionary<string, object>()
                    };
            }).ToList();

            messages.Add(new Dictionary<string, object>
            {
                ["role"] = "assistant",
                ["content"] = assistantContentBlocks
            });

            // Execute each tool and build tool_result blocks
            var toolResults = new List<Dictionary<string, object>>();
            foreach (var toolBlock in toolUseBlocks)
            {
                var toolResult = await ExecuteToolAsync(userId, organizationId, toolBlock, onStatusUpdate, ct);
                toolResults.Add(new Dictionary<string, object>
                {
                    ["type"] = "tool_result",
                    ["tool_use_id"] = toolBlock.Id ?? "",
                    ["content"] = toolResult
                });
            }

            // Add tool results as a user message
            messages.Add(new Dictionary<string, object>
            {
                ["role"] = "user",
                ["content"] = toolResults
            });

            if (onStatusUpdate != null)
            {
                await onStatusUpdate("Thinking...");
            }
        }

        var costPer1K = ModelPricing.GetValueOrDefault(model, 0.003m);
        var cost = totalTokens * costPer1K / 1000m;

        return new LlmResponse
        {
            MessageId = lastMessageId ?? Guid.NewGuid().ToString(),
            Content = allTextContent.ToString(),
            ToolCalls = allToolCalls,
            TokensUsed = totalTokens,
            Cost = cost
        };
    }

    private async Task<LlmResponse> SendOpenAiCompatibleMessageInternalAsync(
        string provider,
        string userId,
        string sessionId,
        Guid organizationId,
        string message,
        List<Host> availableHosts,
        List<Policy> policies,
        List<Message> conversationHistory,
        string? customSystemPrompt,
        string? personalizationPrompt,
        string? modelOverride,
        Func<string, Task>? onTextDelta,
        Func<string, Task>? onStatusUpdate,
        CancellationToken ct)
    {
        var section = provider == ProviderOllama ? "Ollama" : "OpenAI";
        var model = modelOverride
            ?? _config[$"{section}:Model"]
            ?? (provider == ProviderOllama ? "llama3.1" : "gpt-4.1");
        var maxTokens = int.Parse(_config[$"{section}:MaxTokens"] ?? "8192");

        var mcpToolJsonStrings = await _mcpToolRegistry.GetToolDefinitionsAsync(organizationId, ct);
        var systemPrompt = await BuildSystemPromptAsync(
            userId,
            availableHosts,
            policies,
            customSystemPrompt,
            personalizationPrompt,
            mcpToolJsonStrings);

        var messages = new List<Dictionary<string, object>>
        {
            new()
            {
                ["role"] = "system",
                ["content"] = systemPrompt
            }
        };

        messages.AddRange(conversationHistory
            .OrderBy(m => m.CreatedAt)
            .TakeLast(50)
            .Where(m => !string.IsNullOrWhiteSpace(m.Content))
            .Select(m => new Dictionary<string, object>
            {
                ["role"] = m.Role,
                ["content"] = m.Content
            }));

        messages.Add(new Dictionary<string, object>
        {
            ["role"] = "user",
            ["content"] = message
        });

        var tools = BuildOpenAiToolDefinitions(mcpToolJsonStrings);

        var totalTokens = 0;
        var allTextContent = new StringBuilder();
        var allToolCalls = new List<ToolCall>();
        string? lastMessageId = null;

        if (onStatusUpdate != null)
            await onStatusUpdate("Thinking...");

        for (var iteration = 0; iteration < MaxToolLoopIterations; iteration++)
        {
            var request = new Dictionary<string, object>
            {
                ["model"] = model,
                ["max_tokens"] = maxTokens,
                ["messages"] = messages
            };

            if (tools.Count > 0)
            {
                request["tools"] = tools;
                request["tool_choice"] = "auto";
            }

            var json = JsonSerializer.Serialize(request, _jsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await SendProviderRequestAsync(provider, HttpMethod.Post, "/v1/chat/completions", content, ct);
            var responseBody = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("{Provider} API error: {Status} - {Body}", provider, response.StatusCode, responseBody);
                return new LlmResponse
                {
                    MessageId = Guid.NewGuid().ToString(),
                    Content = allTextContent.Length > 0
                        ? allTextContent.ToString()
                        : $"LLM request failed: {response.StatusCode}",
                    TokensUsed = totalTokens,
                    Cost = 0
                };
            }

            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;
            lastMessageId = root.TryGetProperty("id", out var idEl) ? idEl.GetString() : lastMessageId;

            if (root.TryGetProperty("usage", out var usageEl))
            {
                if (usageEl.TryGetProperty("prompt_tokens", out var inputEl)) totalTokens += inputEl.GetInt32();
                if (usageEl.TryGetProperty("completion_tokens", out var outputEl)) totalTokens += outputEl.GetInt32();
            }

            if (!root.TryGetProperty("choices", out var choicesEl)
                || choicesEl.ValueKind != JsonValueKind.Array
                || choicesEl.GetArrayLength() == 0)
            {
                break;
            }

            var choice = choicesEl[0];
            if (!choice.TryGetProperty("message", out var messageEl))
            {
                break;
            }

            var assistantText = ExtractOpenAiMessageText(messageEl);
            if (!string.IsNullOrWhiteSpace(assistantText))
            {
                allTextContent.Append(assistantText);
                if (onTextDelta != null)
                {
                    await onTextDelta(assistantText);
                }
            }

            var parsedToolCalls = new List<Dictionary<string, object>>();
            var parsedToolBlocks = new List<AnthropicContentBlock>();

            if (messageEl.TryGetProperty("tool_calls", out var toolCallsEl)
                && toolCallsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var toolCallEl in toolCallsEl.EnumerateArray())
                {
                    var toolCallId = toolCallEl.TryGetProperty("id", out var tcIdEl) ? tcIdEl.GetString() ?? string.Empty : string.Empty;
                    if (!toolCallEl.TryGetProperty("function", out var functionEl)) continue;

                    var functionName = functionEl.TryGetProperty("name", out var nameEl)
                        ? nameEl.GetString() ?? string.Empty
                        : string.Empty;

                    var functionArgsRaw = functionEl.TryGetProperty("arguments", out var argsEl)
                        ? argsEl.GetString() ?? "{}"
                        : "{}";

                    Dictionary<string, object> functionArgs;
                    try
                    {
                        functionArgs = JsonSerializer.Deserialize<Dictionary<string, object>>(functionArgsRaw, _jsonOptions) ?? [];
                    }
                    catch
                    {
                        functionArgs = [];
                    }

                    parsedToolCalls.Add(new Dictionary<string, object>
                    {
                        ["id"] = toolCallId,
                        ["type"] = "function",
                        ["function"] = new Dictionary<string, object>
                        {
                            ["name"] = functionName,
                            ["arguments"] = functionArgsRaw
                        }
                    });

                    parsedToolBlocks.Add(new AnthropicContentBlock
                    {
                        Id = toolCallId,
                        Name = functionName,
                        Input = functionArgs,
                        Type = "tool_use"
                    });

                    allToolCalls.Add(new ToolCall
                    {
                        ToolName = functionName,
                        Parameters = functionArgs
                    });
                }
            }

            if (parsedToolBlocks.Count == 0)
            {
                break;
            }

            if (onStatusUpdate != null)
            {
                var toolNames = parsedToolBlocks
                    .Select(b => b.Name)
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Distinct()
                    .ToList();

                var status = toolNames.Count > 0
                    ? $"Running tools: {string.Join(", ", toolNames)}"
                    : $"Running {parsedToolBlocks.Count} tool call(s)...";

                await onStatusUpdate(status);
            }

            var assistantMessage = new Dictionary<string, object>
            {
                ["role"] = "assistant",
                ["content"] = assistantText,
                ["tool_calls"] = parsedToolCalls
            };
            messages.Add(assistantMessage);

            foreach (var toolBlock in parsedToolBlocks)
            {
                var toolResult = await ExecuteToolAsync(userId, organizationId, toolBlock, onStatusUpdate, ct);
                messages.Add(new Dictionary<string, object>
                {
                    ["role"] = "tool",
                    ["tool_call_id"] = toolBlock.Id ?? string.Empty,
                    ["content"] = toolResult
                });
            }

            if (onStatusUpdate != null)
            {
                await onStatusUpdate("Thinking...");
            }
        }

        var costPer1K = ModelPricing.GetValueOrDefault(model, 0.003m);
        var cost = totalTokens * costPer1K / 1000m;

        return new LlmResponse
        {
            MessageId = lastMessageId ?? Guid.NewGuid().ToString(),
            Content = allTextContent.ToString(),
            ToolCalls = allToolCalls,
            TokensUsed = totalTokens,
            Cost = cost
        };
    }

    private async Task<string?> GenerateSessionTitleOpenAiCompatibleAsync(
        string provider,
        string userId,
        List<Message> conversationHistory,
        CancellationToken ct)
    {
        var section = provider == ProviderOllama ? "Ollama" : "OpenAI";
        var model = _config[$"{section}:TitleModel"]
            ?? _config[$"{section}:Model"]
            ?? (provider == ProviderOllama ? "llama3.1" : "gpt-4.1");
        var maxTokens = int.Parse(_config[$"{section}:TitleMaxTokens"] ?? "32");

        var prompt = BuildSessionTitlePrompt(conversationHistory);
        if (string.IsNullOrWhiteSpace(prompt)) return null;

        var request = new Dictionary<string, object>
        {
            ["model"] = model,
            ["max_tokens"] = maxTokens,
            ["messages"] = new List<Dictionary<string, object>>
            {
                new()
                {
                    ["role"] = "system",
                    ["content"] = "Generate a concise 3-6 word title for the conversation. Return only the title, no quotes."
                },
                new()
                {
                    ["role"] = "user",
                    ["content"] = prompt
                }
            }
        };

        var json = JsonSerializer.Serialize(request, _jsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await SendProviderRequestAsync(provider, HttpMethod.Post, "/v1/chat/completions", content, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("{Provider} API error (session title): {Status} - {Body}", provider, response.StatusCode, responseBody);
            return null;
        }

        using var doc = JsonDocument.Parse(responseBody);
        var root = doc.RootElement;
        if (!root.TryGetProperty("choices", out var choicesEl)
            || choicesEl.ValueKind != JsonValueKind.Array
            || choicesEl.GetArrayLength() == 0)
        {
            return null;
        }

        var choice = choicesEl[0];
        if (!choice.TryGetProperty("message", out var messageEl)) return null;

        var title = ExtractOpenAiMessageText(messageEl).Trim();
        if (string.IsNullOrWhiteSpace(title)) return null;

        title = title.Trim().Trim('"', '\'', '\u201c', '\u201d');
        if (title.StartsWith("Title:", StringComparison.OrdinalIgnoreCase))
        {
            title = title[6..].Trim();
        }

        title = title.TrimEnd('.', '!', '?');
        if (title.Length > 80)
        {
            title = title[..80].Trim();
        }

        _logger.LogInformation("Generated session title ({Provider}) for user {UserId}: {Title}", provider, userId, title);
        return string.IsNullOrWhiteSpace(title) ? null : title;
    }

    private static string ExtractOpenAiMessageText(JsonElement messageEl)
    {
        if (!messageEl.TryGetProperty("content", out var contentEl)) return string.Empty;

        if (contentEl.ValueKind == JsonValueKind.String)
        {
            return contentEl.GetString() ?? string.Empty;
        }

        if (contentEl.ValueKind != JsonValueKind.Array) return string.Empty;

        var sb = new StringBuilder();
        foreach (var part in contentEl.EnumerateArray())
        {
            if (part.ValueKind != JsonValueKind.Object) continue;

            if (part.TryGetProperty("type", out var typeEl)
                && string.Equals(typeEl.GetString(), "text", StringComparison.OrdinalIgnoreCase)
                && part.TryGetProperty("text", out var textEl))
            {
                sb.Append(textEl.GetString());
            }
        }

        return sb.ToString();
    }

    private List<object> BuildOpenAiToolDefinitions(IReadOnlyList<string> mcpToolJsonStrings)
    {
        var tools = new List<object>();

        foreach (var builtIn in GetBuiltInToolDefinitions())
        {
            var name = builtIn.TryGetValue("name", out var nameObj) ? nameObj?.ToString() ?? string.Empty : string.Empty;
            var description = builtIn.TryGetValue("description", out var descObj) ? descObj?.ToString() ?? string.Empty : string.Empty;
            var parameters = builtIn.TryGetValue("input_schema", out var schemaObj)
                ? schemaObj ?? new Dictionary<string, object> { ["type"] = "object", ["properties"] = new Dictionary<string, object>() }
                : new Dictionary<string, object> { ["type"] = "object", ["properties"] = new Dictionary<string, object>() };

            tools.Add(new Dictionary<string, object>
            {
                ["type"] = "function",
                ["function"] = new Dictionary<string, object>
                {
                    ["name"] = name,
                    ["description"] = description,
                    ["parameters"] = parameters
                }
            });
        }

        foreach (var toolJson in mcpToolJsonStrings)
        {
            try
            {
                using var doc = JsonDocument.Parse(toolJson);
                var root = doc.RootElement;
                var name = root.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? string.Empty : string.Empty;
                if (string.IsNullOrWhiteSpace(name)) continue;

                var description = root.TryGetProperty("description", out var descEl) ? descEl.GetString() ?? string.Empty : string.Empty;
                object parameters = new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>()
                };

                if (root.TryGetProperty("input_schema", out var schemaEl))
                {
                    parameters = JsonSerializer.Deserialize<object>(schemaEl.GetRawText(), _jsonOptions)
                        ?? parameters;
                }
                else if (root.TryGetProperty("parameters", out var paramsEl))
                {
                    parameters = JsonSerializer.Deserialize<object>(paramsEl.GetRawText(), _jsonOptions)
                        ?? parameters;
                }

                tools.Add(new Dictionary<string, object>
                {
                    ["type"] = "function",
                    ["function"] = new Dictionary<string, object>
                    {
                        ["name"] = name,
                        ["description"] = description,
                        ["parameters"] = parameters
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse MCP tool definition JSON for OpenAI-compatible format: {Json}", toolJson);
            }
        }

        return tools;
    }

    private async Task<HttpResponseMessage> SendProviderRequestAsync(
        string provider,
        HttpMethod method,
        string path,
        HttpContent? content,
        CancellationToken ct)
    {
        var baseUrl = provider switch
        {
            ProviderOpenAi => _config["OpenAI:BaseUrl"] ?? "https://api.openai.com",
            ProviderOllama => _config["Ollama:BaseUrl"] ?? "http://localhost:11434",
            _ => _config["Anthropic:BaseUrl"] ?? "https://api.anthropic.com"
        };

        var requestUri = new Uri($"{baseUrl.TrimEnd('/')}/{path.TrimStart('/')}");
        var request = new HttpRequestMessage(method, requestUri)
        {
            Content = content
        };

        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

        if (provider == ProviderAnthropic)
        {
            request.Headers.Add("anthropic-version", "2023-06-01");
            var apiKey = _config["Anthropic:ApiKey"];
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                request.Headers.Add("x-api-key", apiKey);
            }
        }
        else
        {
            var apiKey = provider == ProviderOllama
                ? _config["Ollama:ApiKey"]
                : _config["OpenAI:ApiKey"];

            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
            }
        }

        return await _httpClient.SendAsync(request, ct);
    }

    public static string ResolveProvider(IConfiguration config)
    {
        var configured = config["Llm:Provider"]?.Trim().ToLowerInvariant();
        if (configured == ProviderAnthropic || configured == ProviderOpenAi || configured == ProviderOllama)
        {
            return configured;
        }

        if (!string.IsNullOrWhiteSpace(config["Anthropic:ApiKey"])) return ProviderAnthropic;
        if (!string.IsNullOrWhiteSpace(config["OpenAI:ApiKey"])) return ProviderOpenAi;

        return ProviderAnthropic;
    }

    public static bool IsProviderConfigured(IConfiguration config)
    {
        var provider = ResolveProvider(config);
        return provider switch
        {
            ProviderAnthropic => !string.IsNullOrWhiteSpace(config["Anthropic:ApiKey"]),
            ProviderOpenAi => !string.IsNullOrWhiteSpace(config["OpenAI:ApiKey"]),
            ProviderOllama => true,
            _ => false
        };
    }

    private async Task<AnthropicResponse?> SendStreamingRequestAsync(
        Dictionary<string, object> request,
        Func<string, Task> onTextDelta,
        CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(request, _jsonOptions);
        using var response = await _httpClient.SendAsync(
            new HttpRequestMessage(HttpMethod.Post, new Uri($"{(_config["Anthropic:BaseUrl"] ?? "https://api.anthropic.com").TrimEnd('/')}/v1/messages"))
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
                Headers =
                {
                    { "anthropic-version", "2023-06-01" },
                    { "x-api-key", _config["Anthropic:ApiKey"] ?? string.Empty }
                }
            },
            HttpCompletionOption.ResponseHeadersRead,
            ct);

        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("Anthropic API error: {Status} - {Body}", response.StatusCode, responseBody);
            return null;
        }

        var aggregated = new AnthropicResponse
        {
            Content = []
        };

        var contentBlocks = new Dictionary<int, AnthropicContentBlock>();
        var toolInputBuilders = new Dictionary<int, StringBuilder>();
        var textBuilders = new Dictionary<int, StringBuilder>();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (string.IsNullOrWhiteSpace(line))
                continue;

            if (!line.StartsWith("data: ", StringComparison.OrdinalIgnoreCase))
                continue;

            var data = line[6..].Trim();
            if (data == "[DONE]")
                break;

            using var doc = JsonDocument.Parse(data);
            var root = doc.RootElement;
            var type = root.GetProperty("type").GetString();

            switch (type)
            {
                case "message_start":
                {
                    if (root.TryGetProperty("message", out var messageElement))
                    {
                        aggregated.Id = messageElement.GetProperty("id").GetString();
                        aggregated.Role = messageElement.GetProperty("role").GetString();
                        aggregated.Type = messageElement.GetProperty("type").GetString();
                        if (messageElement.TryGetProperty("usage", out var usage))
                        {
                            aggregated.Usage = new AnthropicUsage
                            {
                                InputTokens = usage.GetProperty("input_tokens").GetInt32(),
                                OutputTokens = usage.GetProperty("output_tokens").GetInt32()
                            };
                        }
                    }
                    break;
                }
                case "content_block_start":
                {
                    var index = root.GetProperty("index").GetInt32();
                    var block = root.GetProperty("content_block");
                    var blockType = block.GetProperty("type").GetString() ?? "";
                    var contentBlock = new AnthropicContentBlock
                    {
                        Type = blockType
                    };

                    if (blockType == "text")
                    {
                        contentBlock.Text = "";
                        textBuilders[index] = new StringBuilder();
                    }
                    else if (blockType == "tool_use")
                    {
                        contentBlock.Id = block.GetProperty("id").GetString();
                        contentBlock.Name = block.GetProperty("name").GetString();
                        toolInputBuilders[index] = new StringBuilder();
                    }

                    contentBlocks[index] = contentBlock;
                    break;
                }
                case "content_block_delta":
                {
                    var index = root.GetProperty("index").GetInt32();
                    var delta = root.GetProperty("delta");
                    var deltaType = delta.GetProperty("type").GetString();

                    if (deltaType == "text_delta")
                    {
                        var text = delta.GetProperty("text").GetString() ?? "";
                        if (textBuilders.TryGetValue(index, out var builder))
                        {
                            builder.Append(text);
                            if (contentBlocks.TryGetValue(index, out var block))
                                block.Text = builder.ToString();
                        }
                        if (!string.IsNullOrEmpty(text))
                            await onTextDelta(text);
                    }
                    else if (deltaType == "input_json_delta")
                    {
                        var jsonDelta = delta.GetProperty("partial_json").GetString() ?? "";
                        if (toolInputBuilders.TryGetValue(index, out var builder))
                            builder.Append(jsonDelta);
                    }
                    break;
                }
                case "content_block_stop":
                {
                    var index = root.GetProperty("index").GetInt32();
                    if (contentBlocks.TryGetValue(index, out var block) && block.Type == "tool_use")
                    {
                        if (toolInputBuilders.TryGetValue(index, out var builder))
                        {
                            var jsonInput = builder.ToString();
                            if (!string.IsNullOrWhiteSpace(jsonInput))
                            {
                                try
                                {
                                    block.Input = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonInput, _jsonOptions);
                                }
                                catch
                                {
                                    block.Input = new Dictionary<string, object>();
                                }
                            }
                        }
                    }
                    break;
                }
                case "message_delta":
                {
                    if (root.TryGetProperty("delta", out var delta))
                    {
                        if (delta.TryGetProperty("stop_reason", out var stopReason))
                            aggregated.StopReason = stopReason.GetString();
                    }
                    if (root.TryGetProperty("usage", out var usage))
                    {
                        aggregated.Usage = new AnthropicUsage
                        {
                            InputTokens = aggregated.Usage?.InputTokens ?? 0,
                            OutputTokens = usage.GetProperty("output_tokens").GetInt32()
                        };
                    }
                    break;
                }
                case "message_stop":
                default:
                    break;
            }
        }

        aggregated.Content = contentBlocks
            .OrderBy(kvp => kvp.Key)
            .Select(kvp => kvp.Value)
            .ToList();

        if (string.IsNullOrWhiteSpace(aggregated.StopReason))
        {
            var hasToolUse = aggregated.Content.Any(block => block.Type == "tool_use");
            aggregated.StopReason = hasToolUse ? "tool_use" : "end_turn";
        }

        return aggregated;
    }

    private async Task<string> ExecuteToolAsync(
        string userId,
        Guid organizationId,
        AnthropicContentBlock toolBlock,
        Func<string, Task>? onStatusUpdate,
        CancellationToken ct)
    {
        try
        {
            var toolName = toolBlock.Name ?? "";
            var input = toolBlock.Input ?? new Dictionary<string, object>();

            _logger.LogInformation("Executing tool {ToolName} with input: {Input}",
                toolName, JsonSerializer.Serialize(input, _jsonOptions));

            // Route MCP tools to the registry
            if (_mcpToolRegistry.IsMcpTool(toolName))
            {
                // Rebuild input as JsonObject for the registry
                var inputJson = JsonSerializer.Serialize(input, _jsonOptions);
                var arguments = JsonNode.Parse(inputJson)?.AsObject() ?? new JsonObject();
                return await RunWithStatusAsync(
                    () => _mcpToolRegistry.DispatchToolCallAsync(toolName, arguments, organizationId, ct),
                    onStatusUpdate,
                    $"Running tool: {toolName}",
                    $"Still running tool: {toolName}",
                    ct);
            }

            // Built-in SSH tools
            switch (toolName)
            {
                case "execute_command":
                {
                    var hostId = GetStringParam(input, "host_id");
                    var command = GetStringParam(input, "command");
                    var dryRun = GetBoolParam(input, "dry_run");

                    if (string.IsNullOrEmpty(hostId) || string.IsNullOrEmpty(command))
                        return "Error: host_id and command are required parameters";

                    var statusCommand = TruncateForStatus(command, 120);
                    var result = await RunWithStatusAsync(
                        () => _commandExecutor.ExecuteAsync(userId, Guid.Parse(hostId), command, dryRun, ct),
                        onStatusUpdate,
                        $"Running command: {statusCommand}",
                        $"Still running: {statusCommand}",
                        ct);

                    var output = new StringBuilder();
                    output.AppendLine($"Exit Code: {result.ExitCode}");
                    if (!string.IsNullOrEmpty(result.StandardOutput))
                        output.AppendLine($"Output:\n{result.StandardOutput}");
                    if (!string.IsNullOrEmpty(result.StandardError))
                        output.AppendLine($"Stderr:\n{result.StandardError}");
                    if (result.WasDryRun)
                        output.AppendLine("[DRY RUN - command was not actually executed]");

                    return output.ToString();
                }

                case "read_file":
                {
                    var hostId = GetStringParam(input, "host_id");
                    var filePath = GetStringParam(input, "file_path");

                    if (string.IsNullOrEmpty(hostId) || string.IsNullOrEmpty(filePath))
                        return "Error: host_id and file_path are required parameters";

                    var result = await RunWithStatusAsync(
                        () => _commandExecutor.ExecuteAsync(
                            userId, Guid.Parse(hostId), $"cat {filePath}", false, ct),
                        onStatusUpdate,
                        $"Reading file: {TruncateForStatus(filePath, 120)}",
                        $"Still reading file: {TruncateForStatus(filePath, 120)}",
                        ct);

                    if (result.ExitCode != 0)
                        return $"Error reading file: {result.StandardError}";

                    return result.StandardOutput ?? "";
                }

                case "check_service_status":
                {
                    var hostId = GetStringParam(input, "host_id");
                    var serviceName = GetStringParam(input, "service_name");

                    if (string.IsNullOrEmpty(hostId) || string.IsNullOrEmpty(serviceName))
                        return "Error: host_id and service_name are required parameters";

                    var result = await RunWithStatusAsync(
                        () => _commandExecutor.ExecuteAsync(
                            userId, Guid.Parse(hostId), $"systemctl status {serviceName}", false, ct),
                        onStatusUpdate,
                        $"Checking service: {TruncateForStatus(serviceName, 120)}",
                        $"Still checking service: {TruncateForStatus(serviceName, 120)}",
                        ct);

                    return result.StandardOutput ?? result.StandardError ?? "";
                }

                case "update_host_notes":
                {
                    var hostId = GetStringParam(input, "host_id");
                    var content = GetStringParam(input, "content");

                    if (string.IsNullOrEmpty(hostId) || string.IsNullOrWhiteSpace(content))
                        return "Error: host_id and content are required parameters";

                    if (!Guid.TryParse(hostId, out var hostGuid))
                        return "Error: host_id must be a valid UUID";

                    var host = await _hostRepository.GetByIdAsync(hostGuid, ct);
                    if (host == null || host.OrganizationId != organizationId)
                        return "Error: host not found or access denied";

                    var note = new HostNote
                    {
                        OrganizationId = organizationId,
                        HostId = hostGuid,
                        Content = content.Trim(),
                        UpdatedByUserId = userId
                    };

                    await _hostNoteRepository.UpsertAsync(note, ct);
                    return $"Updated host notes for {host.Name} ({host.Id})";
                }

                case "tail_logs":
                {
                    var hostId = GetStringParam(input, "host_id");
                    var source = GetStringParam(input, "source");
                    var lines = GetIntParam(input, "lines", 50);
                    var sourceType = GetStringParam(input, "source_type");
                    if (string.IsNullOrEmpty(sourceType)) sourceType = "file";

                    if (string.IsNullOrEmpty(hostId) || string.IsNullOrEmpty(source))
                        return "Error: host_id and source are required parameters";

                    var command = sourceType == "journald"
                        ? $"journalctl -u {source} -n {lines} --no-pager"
                        : $"tail -n {lines} {source}";

                    var result = await RunWithStatusAsync(
                        () => _commandExecutor.ExecuteAsync(userId, Guid.Parse(hostId), command, false, ct),
                        onStatusUpdate,
                        $"Tailing logs: {TruncateForStatus(source, 100)}",
                        $"Still reading logs: {TruncateForStatus(source, 100)}",
                        ct);

                    if (result.ExitCode != 0)
                        return $"Error reading logs: {result.StandardError}";

                    return result.StandardOutput ?? "";
                }

                case "list_containers":
                {
                    var hostId = GetStringParam(input, "host_id");
                    var all = GetBoolParam(input, "all");

                    if (string.IsNullOrEmpty(hostId))
                        return "Error: host_id is a required parameter";

                    var command = all
                        ? "docker ps -a --format 'table {{.Names}}\\t{{.Image}}\\t{{.Status}}\\t{{.Ports}}'"
                        : "docker ps --format 'table {{.Names}}\\t{{.Image}}\\t{{.Status}}\\t{{.Ports}}'";

                    var result = await RunWithStatusAsync(
                        () => _commandExecutor.ExecuteAsync(userId, Guid.Parse(hostId), command, false, ct),
                        onStatusUpdate,
                        "Listing containers...",
                        "Still listing containers...",
                        ct);

                    if (result.ExitCode != 0)
                        return $"Error listing containers: {result.StandardError}";

                    return result.StandardOutput ?? "";
                }

                case "check_container_status":
                {
                    var hostId = GetStringParam(input, "host_id");
                    var containerName = GetStringParam(input, "container_name");
                    var logLines = GetIntParam(input, "log_lines", 20);

                    if (string.IsNullOrEmpty(hostId) || string.IsNullOrEmpty(containerName))
                        return "Error: host_id and container_name are required parameters";

                    var command = $"docker inspect --format='State: {{{{.State.Status}}}} | Started: {{{{.State.StartedAt}}}}' {containerName} 2>&1 && echo '--- Recent Logs ---' && docker logs --tail {logLines} {containerName} 2>&1";

                    var result = await RunWithStatusAsync(
                        () => _commandExecutor.ExecuteAsync(userId, Guid.Parse(hostId), command, false, ct),
                        onStatusUpdate,
                        $"Checking container: {TruncateForStatus(containerName, 100)}",
                        $"Still checking container: {TruncateForStatus(containerName, 100)}",
                        ct);

                    if (result.ExitCode != 0)
                        return $"Error checking container: {result.StandardError}";

                    return result.StandardOutput ?? "";
                }

                case "write_file":
                {
                    var hostId = GetStringParam(input, "host_id");
                    var filePath = GetStringParam(input, "file_path");
                    var content = GetStringParam(input, "content");
                    var backup = !input.ContainsKey("backup") || GetBoolParam(input, "backup"); // default true

                    if (string.IsNullOrEmpty(hostId) || string.IsNullOrEmpty(filePath) || content == null)
                        return "Error: host_id, file_path, and content are required parameters";

                    var output = new StringBuilder();

                    if (backup)
                    {
                        var backupCommand = $"cp {filePath} {filePath}.bak.$(date +%Y%m%d%H%M%S) 2>/dev/null || true";
                        var backupResult = await RunWithStatusAsync(
                            () => _commandExecutor.ExecuteAsync(userId, Guid.Parse(hostId), backupCommand, false, ct),
                            onStatusUpdate,
                            $"Backing up: {TruncateForStatus(filePath, 100)}",
                            $"Still backing up: {TruncateForStatus(filePath, 100)}",
                            ct);
                        if (backupResult.ExitCode == 0)
                            output.AppendLine($"Backup created: {filePath}.bak.<timestamp>");
                    }

                    // Use printf to avoid heredoc issues with special characters
                    var escapedContent = content.Replace("\\", "\\\\").Replace("'", "\\'");
                    var writeCommand = $"printf '%s' '{escapedContent}' > {filePath}";
                    var writeResult = await RunWithStatusAsync(
                        () => _commandExecutor.ExecuteAsync(userId, Guid.Parse(hostId), writeCommand, false, ct),
                        onStatusUpdate,
                        $"Writing file: {TruncateForStatus(filePath, 100)}",
                        $"Still writing file: {TruncateForStatus(filePath, 100)}",
                        ct);

                    if (writeResult.ExitCode != 0)
                        return $"{output}Error writing file: {writeResult.StandardError}";

                    output.AppendLine($"Successfully wrote {content.Length} characters to {filePath}");
                    return output.ToString().TrimEnd();
                }

                default:
                    _logger.LogWarning("Unknown tool requested: {ToolName}", toolName);
                    return $"Unknown tool: {toolName}";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tool execution failed for {ToolName}", toolBlock.Name);
            return $"Tool execution error: {ex.Message}";
        }
    }

    private static string GetStringParam(Dictionary<string, object> input, string key)
    {
        if (!input.TryGetValue(key, out var value)) return "";
        // JsonElement needs special handling
        if (value is JsonElement je) return je.GetString() ?? "";
        return value?.ToString() ?? "";
    }

    private static async Task<T> RunWithStatusAsync<T>(
        Func<Task<T>> operation,
        Func<string, Task>? onStatusUpdate,
        string startStatus,
        string runningStatus,
        CancellationToken ct)
    {
        if (onStatusUpdate != null)
        {
            await onStatusUpdate(startStatus);
        }

        var operationTask = operation();
        if (onStatusUpdate == null)
        {
            return await operationTask;
        }

        var delay = TimeSpan.FromSeconds(2);
        while (!operationTask.IsCompleted)
        {
            try
            {
                await Task.WhenAny(operationTask, Task.Delay(delay, ct));
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (operationTask.IsCompleted) break;
            await onStatusUpdate(runningStatus);
        }

        return await operationTask;
    }

    private static string TruncateForStatus(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return value ?? string.Empty;
        return value.Length <= maxLength ? value : value[..maxLength].Trim() + "…";
    }

    private static bool GetBoolParam(Dictionary<string, object> input, string key)
    {
        if (!input.TryGetValue(key, out var value)) return false;
        if (value is JsonElement je) return je.ValueKind == JsonValueKind.True;
        if (value is bool b) return b;
        return false;
    }

    private static int GetIntParam(Dictionary<string, object> input, string key, int defaultValue = 0)
    {
        if (!input.TryGetValue(key, out var value)) return defaultValue;
        if (value is JsonElement je && je.TryGetInt32(out var i)) return i;
        if (int.TryParse(value?.ToString(), out var parsed)) return parsed;
        return defaultValue;
    }

    private static string BuildSessionTitlePrompt(List<Message> conversationHistory)
    {
        if (conversationHistory == null || conversationHistory.Count == 0) return string.Empty;

        var snippets = conversationHistory
            .OrderBy(m => m.CreatedAt)
            .TakeLast(8)
            .Where(m => !string.IsNullOrWhiteSpace(m.Content))
            .Select(m => $"{m.Role}: {TrimForTitle(m.Content)}")
            .ToList();

        return snippets.Count == 0
            ? string.Empty
            : "Summarize this conversation as a short title:\n" + string.Join("\n", snippets);
    }

    private static string TrimForTitle(string content)
    {
        var normalized = string.Join(' ', content
            .Split(new[] { '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries));

        if (normalized.Length <= 280) return normalized;
        return normalized[..280].Trim();
    }

    public Task<string> BuildSystemPromptAsync(
        string userId,
        List<Host> availableHosts,
        List<Policy> policies,
        string? customSystemPrompt,
        string? personalizationPrompt)
        => BuildSystemPromptAsync(userId, availableHosts, policies, customSystemPrompt, personalizationPrompt, []);

    private Task<string> BuildSystemPromptAsync(
        string userId,
        List<Host> availableHosts,
        List<Policy> policies,
        string? customSystemPrompt,
        string? personalizationPrompt,
        IReadOnlyList<string> mcpToolJsonStrings)
    {
        var hostList = string.Join("\n", availableHosts.Select(h =>
        {
            var userPrefix = !string.IsNullOrEmpty(h.Username) ? $"{h.Username}@" : "";
            return $"- {h.Name} (ID: {h.Id}) [{h.Type}] {userPrefix}{h.Hostname}:{h.Port} - {h.Environment ?? "untagged"}";
        }));

        var policyList = string.Join("\n", policies.Select(p =>
        {
            var allowed = string.Join(", ", p.AllowedCommandPatterns);
            var denied = string.Join(", ", p.DeniedCommandPatterns);
            return $"- {p.Name}: Allow=[{allowed}] Deny=[{denied}]";
        }));

        var mcpToolSection = BuildMcpToolsSection(mcpToolJsonStrings);

        var basePrompt = $"""
            You are an infrastructure management assistant for InfraLLM. You help users debug, monitor, and manage their servers safely.

            Available hosts:
            {hostList}

            Active policies:
            {policyList}

            Built-in tools:
            - execute_command: Execute a shell command on a specified host
            - read_file: Read the contents of a file on a host
            - check_service_status: Check the status of a systemd service on a host
            - update_host_notes: Update the stored notes for a host
            {mcpToolSection}
            Rules:
            1. Always explain what you're about to do before executing commands.
            2. Never execute destructive commands (rm -rf, mkfs, dd, etc.) without explicit user confirmation.
            3. If a command is denied by policy, explain why and suggest safer alternatives.
            4. Prefer read-only commands (cat, ls, systemctl status) when gathering information.
            5. When troubleshooting, start with diagnostic commands before making changes.
            6. Always report command output back to the user clearly.
            """;

        var systemAppend = string.IsNullOrWhiteSpace(customSystemPrompt)
            ? string.Empty
            : $"""

            Additional system context:
            {customSystemPrompt}
            """;

        var personalizationAppend = string.IsNullOrWhiteSpace(personalizationPrompt)
            ? string.Empty
            : $"""

            Personalization:
            {personalizationPrompt}
            """;

        return Task.FromResult(basePrompt + systemAppend + personalizationAppend);
    }

    /// <summary>
    /// Generates the MCP tools section for the system prompt.
    /// Each MCP tool JSON string contains the namespaced name and description.
    /// </summary>
    private static string BuildMcpToolsSection(IReadOnlyList<string> mcpToolJsonStrings)
    {
        if (mcpToolJsonStrings.Count == 0)
            return string.Empty;

        var lines = new List<string> { "\nExternal MCP tools:" };
        foreach (var toolJson in mcpToolJsonStrings)
        {
            try
            {
                using var doc = JsonDocument.Parse(toolJson);
                var name = doc.RootElement.GetProperty("name").GetString() ?? "";
                var desc = doc.RootElement.TryGetProperty("description", out var descEl)
                    ? descEl.GetString() ?? ""
                    : "";
                lines.Add($"- {name}: {desc}");
            }
            catch
            {
                // Skip malformed tool definitions
            }
        }

        return lines.Count > 1 ? string.Join("\n", lines) : string.Empty;
    }

    /// <summary>
    /// Builds the complete list of tool definitions for the Anthropic API:
    /// the three built-in SSH tools plus all discovered MCP tools.
    /// MCP tool definitions are pre-serialized JSON strings that are deserialized here.
    /// </summary>
    private List<object> BuildAllToolDefinitions(IReadOnlyList<string> mcpToolJsonStrings)
    {
        // Start with built-in tools
        var tools = new List<object>(GetBuiltInToolDefinitions());

        // Parse and append MCP tool definitions
        foreach (var toolJson in mcpToolJsonStrings)
        {
            try
            {
                var toolDef = JsonSerializer.Deserialize<Dictionary<string, object>>(toolJson, _jsonOptions);
                if (toolDef != null)
                    tools.Add(toolDef);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse MCP tool definition JSON: {Json}", toolJson);
            }
        }

        return tools;
    }

    private static List<Dictionary<string, object>> GetBuiltInToolDefinitions() =>
    [
        new Dictionary<string, object>
        {
            ["name"] = "execute_command",
            ["description"] = "Execute a shell command on a specified host. The command will be validated against the user's policy before execution.",
            ["input_schema"] = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>
                {
                    ["host_id"] = new Dictionary<string, string> { ["type"] = "string", ["description"] = "The UUID of the target host" },
                    ["command"] = new Dictionary<string, string> { ["type"] = "string", ["description"] = "The shell command to execute" },
                    ["dry_run"] = new Dictionary<string, string> { ["type"] = "boolean", ["description"] = "If true, validate the command without executing it" }
                },
                ["required"] = new[] { "host_id", "command" }
            }
        },
        new Dictionary<string, object>
        {
            ["name"] = "read_file",
            ["description"] = "Read the contents of a file on a host. Uses 'cat' internally.",
            ["input_schema"] = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>
                {
                    ["host_id"] = new Dictionary<string, string> { ["type"] = "string", ["description"] = "The UUID of the target host" },
                    ["file_path"] = new Dictionary<string, string> { ["type"] = "string", ["description"] = "Absolute path to the file to read" }
                },
                ["required"] = new[] { "host_id", "file_path" }
            }
        },
        new Dictionary<string, object>
        {
            ["name"] = "check_service_status",
            ["description"] = "Check the status of a systemd service on a host.",
            ["input_schema"] = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>
                {
                    ["host_id"] = new Dictionary<string, string> { ["type"] = "string", ["description"] = "The UUID of the target host" },
                    ["service_name"] = new Dictionary<string, string> { ["type"] = "string", ["description"] = "Name of the systemd service (e.g., nginx, docker)" }
                },
                ["required"] = new[] { "host_id", "service_name" }
            }
        },
        new Dictionary<string, object>
        {
            ["name"] = "update_host_notes",
            ["description"] = "Update the stored notes for a host.",
            ["input_schema"] = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>
                {
                    ["host_id"] = new Dictionary<string, string> { ["type"] = "string", ["description"] = "The UUID of the target host" },
                    ["content"] = new Dictionary<string, string> { ["type"] = "string", ["description"] = "The updated notes content" }
                },
                ["required"] = new[] { "host_id", "content" }
            }
        },
        new Dictionary<string, object>
        {
            ["name"] = "tail_logs",
            ["description"] = "Retrieve the most recent log output from a log file or systemd journal on a managed host. Use this tool whenever investigating errors, service failures, crash reports, unexpected application behavior, security events, or any situation where recent log output would help diagnose a problem. For log files provide the absolute path as source (e.g. /var/log/syslog, /var/log/nginx/error.log, /var/log/auth.log, /var/log/postgresql/postgresql.log) with source_type='file' (default). For systemd services provide the service name as source (e.g. nginx, docker, sshd, postgresql) with source_type='journald'. This is the preferred tool over read_file for any log file because it returns only recent lines without transferring the full file.",
            ["input_schema"] = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>
                {
                    ["host_id"] = new Dictionary<string, string> { ["type"] = "string", ["description"] = "The UUID of the target host" },
                    ["source"] = new Dictionary<string, string> { ["type"] = "string", ["description"] = "File path (e.g., /var/log/syslog) or systemd service name (e.g., nginx)" },
                    ["lines"] = new Dictionary<string, object> { ["type"] = "integer", ["description"] = "Number of lines to return (default: 50)" },
                    ["source_type"] = new Dictionary<string, string> { ["type"] = "string", ["description"] = "Either 'file' (default) or 'journald'" }
                },
                ["required"] = new[] { "host_id", "source" }
            }
        },
        new Dictionary<string, object>
        {
            ["name"] = "list_containers",
            ["description"] = "List Docker containers on a host. Shows name, image, status, and ports.",
            ["input_schema"] = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>
                {
                    ["host_id"] = new Dictionary<string, string> { ["type"] = "string", ["description"] = "The UUID of the target host" },
                    ["all"] = new Dictionary<string, object> { ["type"] = "boolean", ["description"] = "If true, include stopped containers (default: false)" }
                },
                ["required"] = new[] { "host_id" }
            }
        },
        new Dictionary<string, object>
        {
            ["name"] = "check_container_status",
            ["description"] = "Get the state and recent logs for a specific Docker container.",
            ["input_schema"] = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>
                {
                    ["host_id"] = new Dictionary<string, string> { ["type"] = "string", ["description"] = "The UUID of the target host" },
                    ["container_name"] = new Dictionary<string, string> { ["type"] = "string", ["description"] = "Name or ID of the Docker container" },
                    ["log_lines"] = new Dictionary<string, object> { ["type"] = "integer", ["description"] = "Number of recent log lines to return (default: 20)" }
                },
                ["required"] = new[] { "host_id", "container_name" }
            }
        },
        new Dictionary<string, object>
        {
            ["name"] = "write_file",
            ["description"] = "Write content to a file on a host. Creates an automatic timestamped backup by default. Use this for safe config file edits.",
            ["input_schema"] = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>
                {
                    ["host_id"] = new Dictionary<string, string> { ["type"] = "string", ["description"] = "The UUID of the target host" },
                    ["file_path"] = new Dictionary<string, string> { ["type"] = "string", ["description"] = "Absolute path to the file to write" },
                    ["content"] = new Dictionary<string, string> { ["type"] = "string", ["description"] = "The content to write to the file" },
                    ["backup"] = new Dictionary<string, object> { ["type"] = "boolean", ["description"] = "If true (default), create a timestamped backup before overwriting" }
                },
                ["required"] = new[] { "host_id", "file_path", "content" }
            }
        },
    ];
}

// Response DTOs for Anthropic API (request is built as Dictionary<string, object> for flexibility)
internal class AnthropicResponse
{
    public string? Id { get; set; }
    public string? Type { get; set; }
    public string? Role { get; set; }
    public List<AnthropicContentBlock> Content { get; set; } = [];
    public AnthropicUsage? Usage { get; set; }
    public string? StopReason { get; set; }
}

internal class AnthropicContentBlock
{
    public string Type { get; set; } = string.Empty;
    public string? Text { get; set; }
    public string? Id { get; set; }
    public string? Name { get; set; }
    public Dictionary<string, object>? Input { get; set; }
}

internal class AnthropicUsage
{
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
}
