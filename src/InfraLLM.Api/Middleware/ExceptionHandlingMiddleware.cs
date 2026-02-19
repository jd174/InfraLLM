using System.Net;
using System.Text.Json;

namespace InfraLLM.Api.Middleware;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Unauthorized access attempt");
            await WriteErrorResponse(context, HttpStatusCode.Unauthorized, "Unauthorized", "AUTH_ERROR");
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Invalid operation: {Message}", ex.Message);
            await WriteErrorResponse(context, HttpStatusCode.BadRequest, ex.Message, "INVALID_OPERATION");
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Invalid argument: {Message}", ex.Message);
            await WriteErrorResponse(context, HttpStatusCode.BadRequest, ex.Message, "INVALID_ARGUMENT");
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning(ex, "Resource not found: {Message}", ex.Message);
            await WriteErrorResponse(context, HttpStatusCode.NotFound, ex.Message, "NOT_FOUND");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "External service error: {Message}", ex.Message);
            await WriteErrorResponse(context, HttpStatusCode.BadGateway, "External service unavailable", "EXTERNAL_SERVICE_ERROR");
        }
        catch (TaskCanceledException ex) when (ex.CancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Request cancelled by client");
            context.Response.StatusCode = 499; // Client Closed Request
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Operation was cancelled");
            context.Response.StatusCode = 499;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception: {Message}", ex.Message);
            await WriteErrorResponse(context, HttpStatusCode.InternalServerError, "An unexpected error occurred", "INTERNAL_ERROR");
        }
    }

    private static async Task WriteErrorResponse(HttpContext context, HttpStatusCode statusCode, string message, string code)
    {
        if (context.Response.HasStarted)
            return;
            
        context.Response.StatusCode = (int)statusCode;
        context.Response.ContentType = "application/json";

        var response = JsonSerializer.Serialize(new 
        { 
            error = message, 
            code,
            statusCode = (int)statusCode,
            timestamp = DateTime.UtcNow
        });
        await context.Response.WriteAsync(response);
    }
}
