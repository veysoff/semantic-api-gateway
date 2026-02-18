namespace SemanticApiGateway.Gateway.Features.Security;

/// <summary>
/// DelegatingHandler that automatically injects JWT tokens into downstream HTTP requests
/// Ensures token propagation to all microservices
/// </summary>
public class TokenPropagationHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ITokenPropagationService _tokenPropagationService;
    private readonly ILogger<TokenPropagationHandler> _logger;

    public TokenPropagationHandler(
        IHttpContextAccessor httpContextAccessor,
        ITokenPropagationService tokenPropagationService,
        ILogger<TokenPropagationHandler> logger)
    {
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
        _tokenPropagationService = tokenPropagationService ?? throw new ArgumentNullException(nameof(tokenPropagationService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var httpContext = _httpContextAccessor.HttpContext;

        if (httpContext != null)
        {
            // Extract token from current request
            var token = _tokenPropagationService.ExtractToken(httpContext);

            if (!string.IsNullOrEmpty(token))
            {
                // Inject token into downstream request
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                _logger.LogDebug("Token propagated to downstream request for {RequestUri}", request.RequestUri);
            }
            else
            {
                _logger.LogDebug("No token to propagate for {RequestUri}", request.RequestUri);
            }
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
