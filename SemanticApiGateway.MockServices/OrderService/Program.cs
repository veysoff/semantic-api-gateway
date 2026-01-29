using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using System.Text;
using OrderService.Endpoints;

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

    // Add services
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
        {
            Title = "Order Service API",
            Version = "v1",
            Description = "Microservice for managing orders"
        });
    });
    builder.Services.AddHealthChecks();

    // Add Authentication
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            var issuer = builder.Configuration["Auth:Issuer"] ?? "https://localhost:5001";
            var audience = builder.Configuration["Auth:Audience"] ?? "api://semantic-gateway";
            var secretKey = builder.Configuration["Auth:SecretKey"] ?? "super-secret-key-change-in-production-minimum-32-characters";

            var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));

            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = signingKey,
                ValidateIssuer = true,
                ValidIssuer = issuer,
                ValidateAudience = true,
                ValidAudience = audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(5)
            };
        });

    builder.Services.AddAuthorization();

    var app = builder.Build();

    // Configure pipeline
    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseRouting();
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapHealthChecks("/health");
    app.MapOrderEndpoints();

    Log.Information("OrderService starting on {Urls}", string.Join(", ", app.Urls));
    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "OrderService terminated unexpectedly");
}
finally
{
    await Log.CloseAndFlushAsync();
}
