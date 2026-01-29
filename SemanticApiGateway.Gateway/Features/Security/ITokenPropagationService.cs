namespace SemanticApiGateway.Gateway.Features.Security;

/// <summary>
/// Handles JWT token extraction and propagation to downstream microservices
/// Ensures user identity context flows through the system
/// </summary>
public interface ITokenPropagationService
{
    /// <summary>
    /// Extract JWT token from HTTP context
    /// </summary>
    /// <param name="context">HTTP context from current request</param>
    /// <returns>JWT token string or null if not found</returns>
    string? ExtractToken(HttpContext context);

    /// <summary>
    /// Get the user ID from the JWT token claims
    /// </summary>
    /// <param name="token">JWT token</param>
    /// <returns>User ID or null if not found</returns>
    string? GetUserIdFromToken(string token);

    /// <summary>
    /// Validate JWT token signature and expiration
    /// </summary>
    /// <param name="token">JWT token to validate</param>
    /// <returns>True if token is valid</returns>
    bool ValidateToken(string token);

    /// <summary>
    /// Get claims from a JWT token
    /// </summary>
    /// <param name="token">JWT token</param>
    /// <returns>Dictionary of claim names to values</returns>
    Dictionary<string, string> GetTokenClaims(string token);
}
