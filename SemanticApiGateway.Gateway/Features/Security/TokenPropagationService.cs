using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace SemanticApiGateway.Gateway.Features.Security;

/// <summary>
/// Extracts JWT tokens from HTTP context and provides token utilities with proper cryptographic validation
/// </summary>
public class TokenPropagationService : ITokenPropagationService
{
    private readonly ILogger<TokenPropagationService> _logger;
    private readonly IConfiguration _configuration;
    private readonly TokenValidationParameters _tokenValidationParameters;

    public TokenPropagationService(ILogger<TokenPropagationService> logger, IConfiguration configuration)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

        // Initialize token validation parameters with proper configuration
        _tokenValidationParameters = BuildTokenValidationParameters();
    }

    private TokenValidationParameters BuildTokenValidationParameters()
    {
        var issuer = _configuration["Auth:Issuer"] ?? "https://localhost:5001";
        var audience = _configuration["Auth:Audience"] ?? "api://semantic-gateway";
        var secretKey = _configuration["Auth:SecretKey"] ?? "super-secret-key-change-in-production-minimum-32-characters";

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));

        return new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = signingKey,
            ValidateIssuer = true,
            ValidIssuer = issuer,
            ValidateAudience = true,
            ValidAudience = audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(5) // Allow 5 seconds clock skew
        };
    }

    public string? ExtractToken(HttpContext context)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));

        var authHeader = context.Request.Headers["Authorization"].FirstOrDefault();

        if (string.IsNullOrEmpty(authHeader))
        {
            _logger.LogDebug("No Authorization header found");
            return null;
        }

        const string bearerScheme = "Bearer ";
        if (!authHeader.StartsWith(bearerScheme, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Authorization header does not use Bearer scheme");
            return null;
        }

        var token = authHeader.Substring(bearerScheme.Length).Trim();
        _logger.LogDebug("Token extracted from Authorization header");
        return token;
    }

    public string? GetUserIdFromToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        try
        {
            // SECURITY: Validate token signature and claims before extraction
            var principal = ValidateAndGetPrincipal(token);
            if (principal == null)
                return null;

            // Try common claim names
            var subClaim = principal.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier);
            if (subClaim != null)
                return subClaim.Value;

            var sidClaim = principal.Claims.FirstOrDefault(c => c.Type == "sub");
            if (sidClaim != null)
                return sidClaim.Value;

            var oidClaim = principal.Claims.FirstOrDefault(c => c.Type == "oid");
            if (oidClaim != null)
                return oidClaim.Value;

            _logger.LogWarning("Could not find user ID in validated token claims");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract user ID from token");
            return null;
        }
    }

    public bool ValidateToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return false;

        try
        {
            // SECURITY: Full cryptographic validation including signature
            var principal = ValidateAndGetPrincipal(token);
            _logger.LogDebug("Token validation successful");
            return principal != null;
        }
        catch (SecurityTokenException ex)
        {
            _logger.LogWarning(ex, "Token validation failed: Invalid token signature or claims");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Token validation failed");
            return false;
        }
    }

    public Dictionary<string, string> GetTokenClaims(string token)
    {
        var claims = new Dictionary<string, string>();

        if (string.IsNullOrWhiteSpace(token))
            return claims;

        try
        {
            // SECURITY: Validate token signature and claims before extraction
            var principal = ValidateAndGetPrincipal(token);
            if (principal == null)
                return claims;

            foreach (var claim in principal.Claims)
            {
                claims[claim.Type] = claim.Value;
            }

            _logger.LogDebug("Extracted {ClaimCount} validated claims from token", claims.Count);
        }
        catch (SecurityTokenException ex)
        {
            _logger.LogWarning(ex, "Failed to extract claims from token: Invalid signature");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract claims from token");
        }

        return claims;
    }

    /// <summary>
    /// Validates the token signature and claims cryptographically
    /// </summary>
    private ClaimsPrincipal? ValidateAndGetPrincipal(string token)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler();

            // SECURITY: This performs full cryptographic validation
            // Validates signature, issuer, audience, and lifetime
            var principal = handler.ValidateToken(token, _tokenValidationParameters, out var validatedToken);

            if (validatedToken is not JwtSecurityToken jwtToken)
            {
                _logger.LogWarning("Token is not a valid JWT");
                return null;
            }

            return principal;
        }
        catch (SecurityTokenInvalidSignatureException ex)
        {
            _logger.LogWarning(ex, "Invalid token signature");
            throw;
        }
        catch (SecurityTokenExpiredException ex)
        {
            _logger.LogWarning(ex, "Token has expired");
            throw;
        }
        catch (SecurityTokenInvalidIssuerException ex)
        {
            _logger.LogWarning(ex, "Invalid token issuer");
            throw;
        }
        catch (SecurityTokenInvalidAudienceException ex)
        {
            _logger.LogWarning(ex, "Invalid token audience");
            throw;
        }
        catch (SecurityTokenException ex)
        {
            _logger.LogWarning(ex, "Token validation failed");
            throw;
        }
    }
}
