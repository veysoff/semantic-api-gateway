namespace SemanticApiGateway.Gateway.Models;

/// <summary>
/// Categorizes errors as transient or permanent to guide recovery decisions.
/// </summary>
public enum ErrorCategory
{
    /// <summary>
    /// Transient error that may succeed on retry (e.g., timeout, temporary unavailability).
    /// </summary>
    Transient,

    /// <summary>
    /// Permanent error that won't succeed on retry (e.g., invalid input, authorization failure).
    /// </summary>
    Permanent,

    /// <summary>
    /// Unknown error category (default).
    /// </summary>
    Unknown
}
