using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using InfraLLM.Api.Authentication;
using InfraLLM.Api.Hubs;
using InfraLLM.Api.Middleware;
using InfraLLM.Api.Services;
using InfraLLM.Core.Interfaces;
using InfraLLM.Infrastructure.Data;
using InfraLLM.Infrastructure.Data.Repositories;
using InfraLLM.Infrastructure.Services;
using InfraLLM.Infrastructure.Services.Mcp;

var builder = WebApplication.CreateBuilder(args);

// Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();

builder.Host.UseSerilog();

// Database
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Identity — use AddIdentityCore instead of AddIdentity to avoid cookie auth overrides
builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.Password.RequireDigit = true;
        options.Password.RequiredLength = 8;
        options.Password.RequireNonAlphanumeric = false;
        options.User.RequireUniqueEmail = true;
    })
    .AddRoles<IdentityRole>()
    .AddSignInManager()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

// JWT Authentication
var jwtSecret = builder.Configuration["Jwt:Secret"]
    ?? throw new InvalidOperationException("JWT secret not configured");

// Credential encryption
var credentialMasterKey = builder.Configuration["CredentialEncryption:MasterKey"]
    ?? throw new InvalidOperationException("Credential encryption master key not configured");

if (!builder.Environment.IsDevelopment()
    && credentialMasterKey.StartsWith("CHANGE_ME", StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException("Credential encryption master key must be changed in production");
}

builder.Services.Configure<CredentialEncryptionOptions>(builder.Configuration.GetSection("CredentialEncryption"));

// Combined authentication: JWT for browser sessions, AccessToken for API/MCP clients.
// A policy scheme inspects the incoming request to route to the correct handler.
const string combinedScheme = "JwtOrAccessToken";

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = combinedScheme;
        options.DefaultAuthenticateScheme = combinedScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddPolicyScheme(combinedScheme, "JWT or Access Token", opts =>
    {
        opts.ForwardDefaultSelector = ctx =>
        {
            // X-API-Key header → AccessToken scheme
            if (ctx.Request.Headers.ContainsKey("X-API-Key"))
                return AccessTokenAuthHandler.SchemeName;

            // ?api_key=... query string → AccessToken scheme
            if (ctx.Request.Query.ContainsKey("api_key"))
                return AccessTokenAuthHandler.SchemeName;

            // Authorization: Bearer infra_... → AccessToken scheme
            var auth = ctx.Request.Headers.Authorization.FirstOrDefault();
            if (!string.IsNullOrEmpty(auth) &&
                auth.StartsWith("Bearer infra_", StringComparison.OrdinalIgnoreCase))
                return AccessTokenAuthHandler.SchemeName;

            // Everything else (JWT, SignalR access_token) → JWT scheme
            return JwtBearerDefaults.AuthenticationScheme;
        };
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret))
        };

        // Allow JWT token in SignalR query string
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            }
        };
    })
    .AddScheme<AccessTokenAuthOptions, AccessTokenAuthHandler>(
        AccessTokenAuthHandler.SchemeName, _ => { });

// Repositories
builder.Services.AddScoped<IHostRepository, HostRepository>();
builder.Services.AddScoped<IPolicyRepository, PolicyRepository>();
builder.Services.AddScoped<IAuditRepository, AuditRepository>();
builder.Services.AddScoped<ISessionRepository, SessionRepository>();
builder.Services.AddScoped<ICredentialRepository, CredentialRepository>();
builder.Services.AddScoped<IPromptSettingsRepository, PromptSettingsRepository>();
builder.Services.AddScoped<IJobRepository, JobRepository>();
builder.Services.AddScoped<IHostNoteRepository, HostNoteRepository>();

// Services
builder.Services.AddScoped<IPolicyService, PolicyValidationService>();
builder.Services.AddScoped<IAuditLogger, AuditLogService>();
builder.Services.AddScoped<ICommandExecutor, SshCommandExecutor>();
builder.Services.AddSingleton<ISshConnectionPool, SshConnectionPool>();
builder.Services.AddHttpClient<ILlmService, AnthropicLlmService>();
builder.Services.AddSingleton<ICredentialEncryptionService, CredentialEncryptionService>();
builder.Services.AddSingleton<IChatTaskManager, ChatTaskManager>();
builder.Services.AddHostedService<InfraLLM.Api.Services.JobsCronHostedService>();

// MCP Services
builder.Services.AddScoped<IMcpServerRepository, McpServerRepository>();
builder.Services.AddScoped<IMcpClientFactory, McpClientFactory>();
builder.Services.AddScoped<IMcpToolRegistry, McpToolRegistry>();
// Singleton cache keeps stdio processes alive across requests (avoids uvx/npx cold-start per call)
builder.Services.AddSingleton<StdioMcpClientCache>();
// Hosted service that pre-warms all enabled stdio servers in the background at startup
builder.Services.AddHostedService<StdioMcpWarmupService>();
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient("McpClient")
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
    });

// SignalR
builder.Services.AddSignalR();

// Controllers
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
    });

builder.Services.AddOpenApi();

// CORS — in production behind nginx reverse proxy, everything is same-origin
// so CORS isn't strictly needed. Keep it flexible for development.
var corsOrigins = (builder.Configuration["Cors:Origins"] ?? "*")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        if (corsOrigins.Contains("*"))
        {
            policy.SetIsOriginAllowed(_ => true)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
        }
        else
        {
            policy.WithOrigins(corsOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
        }
    });
});

var app = builder.Build();

// Middleware pipeline — CORS must be first so preflight OPTIONS requests get headers
app.UseCors("Frontend");
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<CommandHub>("/hubs/command");
app.MapHub<ChatHub>("/hubs/chat");

// Auto-migrate on startup (safe for containerized deployments with a single replica)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await db.Database.MigrateAsync();
}

app.Run();
