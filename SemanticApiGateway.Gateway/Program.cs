using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.SemanticKernel;
using Serilog;
using SemanticApiGateway.Gateway.Configuration;
using SemanticApiGateway.Gateway.Features.PluginOrchestration;
using SemanticApiGateway.Gateway.Features.Reasoning;
using SemanticApiGateway.Gateway.Features.Security;
using SemanticApiGateway.Gateway.Features.Observability;
using SemanticApiGateway.Gateway.Endpoints;
using System.IdentityModel.Tokens.Jwt;
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
        var authority = builder.Configuration["Auth:Authority"] ?? "https://localhost:5001";
        var audience = builder.Configuration["Auth:Audience"] ?? "api://semantic-gateway";
        var issuer = builder.Configuration["Auth:Issuer"] ?? "https://localhost:5001";

        // SECURITY: Proper JWT validation
        options.Authority = authority;
        options.TokenValidationParameters.ValidateIssuer = true;
        options.TokenValidationParameters.ValidateAudience = true;
        options.TokenValidationParameters.ValidateLifetime = true;
        options.TokenValidationParameters.ValidIssuer = issuer;
        options.TokenValidationParameters.ValidAudience = audience;

        // SECURITY: Use signing key validation (for development, use symmetric key; production should use asymmetric)
        if (builder.Environment.IsDevelopment())
        {
            // Development: Use a shared secret key
            var secretKey = builder.Configuration["Auth:SecretKey"] ?? "super-secret-key-change-in-production-minimum-32-characters";
            var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
            options.TokenValidationParameters.IssuerSigningKey = signingKey;
        }
        else
        {
            // Production: Should use Authority endpoint for key validation or configure trusted keys
            options.Authority = authority;
            options.TokenValidationParameters.IssuerSigningKeyResolver = (token, securityToken, kid, parameters) =>
            {
                // In production, fetch keys from well-known configuration endpoint
                // or configure trusted signing keys from your identity provider
                var keys = new List<SecurityKey>();

                // TODO: Replace with actual key configuration from your identity provider
                // Example for Azure AD:
                // options.Authority = "https://login.microsoftonline.com/{tenant}/v2.0";
                // options.Audience = "{client-id}";

                if (keys.Count == 0)
                {
                    throw new InvalidOperationException("No signing keys configured. Configure Auth:SigningKeys in appsettings.Production.json");
                }

                return keys;
            };
        }
    });

    builder.Services.AddAuthorization();

    // Register Semantic Kernel
    builder.Services.AddScoped(sp =>
    {
        var kernelBuilder = Kernel.CreateBuilder()
            .AddOpenAIChatCompletion(
                modelId: "gpt-4",
                apiKey: builder.Configuration["OpenAI:ApiKey"] ?? throw new InvalidOperationException("OpenAI:ApiKey not configured"));

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
    builder.Services.AddScoped<ITokenPropagationService, TokenPropagationService>();
    builder.Services.AddScoped<ISemanticGuardrailService, SemanticGuardrailService>();
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
