using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.IdentityModel.Tokens;
using Microsoft.SemanticKernel;
using Serilog;
using SemanticApiGateway.Gateway.Configuration;
using SemanticApiGateway.Gateway.Features.PluginOrchestration;
using SemanticApiGateway.Gateway.Features.Reasoning;
using SemanticApiGateway.Gateway.Features.Security;
using SemanticApiGateway.Gateway.Features.Observability;
using SemanticApiGateway.Gateway.Features.ErrorHandling;
using SemanticApiGateway.Gateway.Features.RateLimiting;
using SemanticApiGateway.Gateway.Features.SecretManagement;
using SemanticApiGateway.Gateway.Features.AuditTrail;
using SemanticApiGateway.Gateway.Features.Caching;
using SemanticApiGateway.Gateway.Features.LLM;
using SemanticApiGateway.Gateway.Features.Streaming;
using SemanticApiGateway.Gateway.Middleware;
using SemanticApiGateway.Gateway.Endpoints;
using OpenTelemetry.Trace;
using System.Text;

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .CreateLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Add Serilog
    builder.Host.UseSerilog();

    // Add services to the container
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();
    builder.Services.AddHealthChecks();
    builder.Services.AddHttpContextAccessor();

    // Configure CORS with restricted policy
    builder.Services.AddCors(options =>
    {
        if (builder.Environment.IsDevelopment())
        {
            // Development: Allow localhost origins only
            options.AddPolicy("AllowLocalhost", policy =>
            {
                policy.WithOrigins("http://localhost:3000", "http://localhost:5000", "https://localhost:5000", "https://localhost:5001")
                      .AllowCredentials()
                      .WithMethods("GET", "POST", "PUT", "DELETE", "OPTIONS")
                      .AllowAnyHeader();
            });
        }
        else
        {
            // Production: Explicit whitelist only
            var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                ?? new[] { "https://yourdomain.com" };

            options.AddPolicy("AllowSpecific", policy =>
            {
                policy.WithOrigins(allowedOrigins)
                      .AllowCredentials()
                      .WithMethods("GET", "POST", "PUT", "DELETE", "OPTIONS")
                      .WithExposedHeaders("Content-Length", "X-JSON-Response-Available");
            });
        }
    });

    // Register Resilience Configuration from appsettings
    builder.Services.Configure<ResilienceConfiguration>(
        builder.Configuration.GetSection("Resilience"));

    // Register Authentication with proper JWT validation
    builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        var audience = builder.Configuration["Auth:Audience"] ?? "api://semantic-gateway";
        var issuer = builder.Configuration["Auth:Issuer"] ?? "https://localhost:5001";
        var secretKey = builder.Configuration["Auth:SecretKey"] ?? "super-secret-key-change-in-production-minimum-32-characters";

        // SECURITY: Proper JWT validation
        options.TokenValidationParameters.ValidateIssuer = true;
        options.TokenValidationParameters.ValidateAudience = true;
        options.TokenValidationParameters.ValidateLifetime = true;
        options.TokenValidationParameters.ValidIssuer = issuer;
        options.TokenValidationParameters.ValidAudience = audience;

        // Use symmetric key for signing validation
        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
        options.TokenValidationParameters.IssuerSigningKey = signingKey;
    });

    builder.Services.AddAuthorization();

    // Register LLM Providers and Orchestrator
    builder.Services.AddSingleton<ILLMProvider, OpenAIProvider>();
    builder.Services.AddSingleton<ILLMProvider, AnthropicProvider>();
    builder.Services.AddSingleton<ILLMProviderOrchestrator, LLMProviderOrchestrator>();

    // Register Caching Service
    builder.Services.AddSingleton<ICacheService, InMemoryCacheService>();

    // Register Semantic Kernel (fallback, uses orchestrator in StepwisePlannerEngine)
    builder.Services.AddScoped(sp =>
    {
        var kernelBuilder = Kernel.CreateBuilder();
        var apiKey = builder.Configuration["OpenAI:ApiKey"];

        // Only add OpenAI if API key is properly configured (not a test placeholder)
        if (!string.IsNullOrEmpty(apiKey) && apiKey.StartsWith("sk-") && apiKey.Length > 20)
        {
            kernelBuilder.AddOpenAIChatCompletion(
                modelId: "gpt-4",
                apiKey: apiKey);
        }

        return kernelBuilder.Build();
    });

    // Register HttpClient with timeout
    builder.Services.AddHttpClient("resilient")
        .ConfigureHttpClient(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
        });

    // Register Gateway Services
    builder.Services.AddSingleton<IGatewayActivitySource, GatewayActivitySource>();
    builder.Services.AddSingleton<IPluginRegistry, PluginRegistry>();
    builder.Services.AddScoped<IOpenApiPluginLoader, OpenApiPluginLoader>();
    builder.Services.AddScoped<VariableResolver>();
    builder.Services.AddScoped<IReasoningEngine, StepwisePlannerEngine>();
    builder.Services.AddScoped<IStreamingExecutionService, StreamingExecutionService>();
    builder.Services.AddScoped<ITokenPropagationService, TokenPropagationService>();
    builder.Services.AddScoped<ISemanticGuardrailService, SemanticGuardrailService>();
    builder.Services.AddScoped<IExceptionHandler, GlobalExceptionHandler>();
    builder.Services.AddSingleton<IErrorRecoveryService, ErrorRecoveryService>();
    builder.Services.AddSingleton<ICircuitBreakerService, CircuitBreakerService>();
    builder.Services.AddSingleton<IRateLimitingService, RateLimitingService>();
    builder.Services.AddSingleton<ISecretRotationService, SecretRotationService>();
    builder.Services.AddSingleton<IAuditService, InMemoryAuditService>();

    // Register secret provider based on environment
    if (builder.Environment.IsDevelopment())
    {
        builder.Services.AddScoped<ISecretProvider, LocalSecretProvider>();
        Log.Information("Using LocalSecretProvider for development");
    }
    else
    {
        builder.Services.AddScoped<ISecretProvider, KeyVaultSecretProvider>();
        Log.Information("Using KeyVaultSecretProvider for production");
    }

    builder.Services.AddHostedService<PluginRefreshService>();

    // Register HttpClient factories
    builder.Services.AddHttpClient<IOpenApiPluginLoader, OpenApiPluginLoader>();
    builder.Services.AddTransient<TokenPropagationHandler>();
    builder.Services.AddHttpClient("gateway")
        .AddHttpMessageHandler<TokenPropagationHandler>();

    // Add OpenTelemetry
    builder.Services
        .AddOpenTelemetry()
        .WithTracing(tracing =>
        {
            tracing
                .AddSource("SemanticApiGateway")
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddConsoleExporter();
        });

    var app = builder.Build();

    // Configure the HTTP request pipeline
    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }
    else
    {
        // Security: Add HTTPS redirection in production
        app.UseHttpsRedirection();

        // Security: Add HSTS header
        app.UseHsts();
    }

    // Global exception handling (must be first in pipeline)
    app.UseExceptionHandler(_ => { });

    // Audit trail logging
    app.UseAuditTrail();

    // Rate limiting (before routing to check before handlers)
    app.UseRateLimiting();

    app.UseRouting();

    // Use appropriate CORS policy based on environment
    if (app.Environment.IsDevelopment())
    {
        app.UseCors("AllowLocalhost");
    }
    else
    {
        app.UseCors("AllowSpecific");
    }

    app.UseAuthentication();
    app.UseAuthorization();

    // Health check endpoint
    app.MapHealthChecks("/health");

    // Map minimal API endpoints
    app.MapIntentEndpoints();

    // Log startup info
    Log.Information("Gateway starting up on {Urls}", string.Join(", ", app.Urls));

    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    await Log.CloseAndFlushAsync();
}
