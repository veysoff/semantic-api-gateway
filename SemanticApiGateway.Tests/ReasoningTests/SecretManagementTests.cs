using Xunit;
using Moq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SemanticApiGateway.Gateway.Features.SecretManagement;

namespace SemanticApiGateway.Tests.ReasoningTests;

public class LocalSecretProviderTests
{
    private readonly Mock<IConfiguration> _mockConfig;
    private readonly Mock<ILogger<LocalSecretProvider>> _mockLogger;
    private readonly LocalSecretProvider _provider;

    public LocalSecretProviderTests()
    {
        _mockConfig = new Mock<IConfiguration>();
        _mockLogger = new Mock<ILogger<LocalSecretProvider>>();
        _provider = new LocalSecretProvider(_mockConfig.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task GetSecretAsync_SecretExists_ReturnsValue()
    {
        _mockConfig.Setup(c => c[$"Secrets:api-key"]).Returns("secret-value-123");

        var result = await _provider.GetSecretAsync("api-key");

        Assert.Equal("secret-value-123", result);
    }

    [Fact]
    public async Task GetSecretAsync_SecretMissing_ThrowsKeyNotFoundException()
    {
        _mockConfig.Setup(c => c[$"Secrets:missing-secret"]).Returns((string)null);

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _provider.GetSecretAsync("missing-secret"));

        Assert.Contains("missing-secret", ex.Message);
    }

    [Fact]
    public async Task TryGetSecretAsync_SecretExists_ReturnsTrueWithValue()
    {
        _mockConfig.Setup(c => c[$"Secrets:db-password"]).Returns("password123");

        var result = await _provider.TryGetSecretAsync("db-password");

        Assert.True(result.Found);
        Assert.Equal("password123", result.Value);
    }

    [Fact]
    public async Task TryGetSecretAsync_SecretMissing_ReturnsFalseWithEmpty()
    {
        _mockConfig.Setup(c => c[$"Secrets:missing"]).Returns((string)null);

        var result = await _provider.TryGetSecretAsync("missing");

        Assert.False(result.Found);
        Assert.Equal(string.Empty, result.Value);
    }

    [Fact]
    public async Task SecretExistsAsync_SecretExists_ReturnsTrue()
    {
        _mockConfig.Setup(c => c[$"Secrets:jwt-key"]).Returns("jwt-secret");

        var exists = await _provider.SecretExistsAsync("jwt-key");

        Assert.True(exists);
    }

    [Fact]
    public async Task SecretExistsAsync_SecretMissing_ReturnsFalse()
    {
        _mockConfig.Setup(c => c[$"Secrets:nonexistent"]).Returns((string)null);

        var exists = await _provider.SecretExistsAsync("nonexistent");

        Assert.False(exists);
    }
}

public class SecretRotationServiceTests
{
    private readonly Mock<ILogger<SecretRotationService>> _mockLogger;
    private readonly SecretRotationService _service;

    public SecretRotationServiceTests()
    {
        _mockLogger = new Mock<ILogger<SecretRotationService>>();
        _service = new SecretRotationService(_mockLogger.Object);
    }

    [Fact]
    public async Task NotifySecretRotatedAsync_RecordsRotationTime()
    {
        var now = DateTime.UtcNow;

        await _service.NotifySecretRotatedAsync("api-key");
        var lastRotation = await _service.GetLastRotationTimeAsync("api-key");

        Assert.True(lastRotation >= now.AddSeconds(-1));
        Assert.True(lastRotation <= DateTime.UtcNow.AddSeconds(1));
    }

    [Fact]
    public async Task GetLastRotationTimeAsync_NoRotation_ReturnsUnixEpoch()
    {
        var result = await _service.GetLastRotationTimeAsync("never-rotated");

        Assert.Equal(DateTime.UnixEpoch, result);
    }

    [Fact]
    public async Task IsSecretRotatedSinceAsync_RotatedAfterTime_ReturnsTrue()
    {
        var checkTime = DateTime.UtcNow.AddMinutes(-5);

        await _service.NotifySecretRotatedAsync("db-password");
        var isRotated = await _service.IsSecretRotatedSinceAsync("db-password", checkTime);

        Assert.True(isRotated);
    }

    [Fact]
    public async Task IsSecretRotatedSinceAsync_RotatedBeforeTime_ReturnsFalse()
    {
        var beforeRotation = DateTime.UtcNow;
        await Task.Delay(100);
        await _service.NotifySecretRotatedAsync("jwt-secret");
        var afterRotation = DateTime.UtcNow.AddSeconds(1);

        var isRotated = await _service.IsSecretRotatedSinceAsync("jwt-secret", afterRotation);

        Assert.False(isRotated);
    }

    [Fact]
    public async Task IsSecretRotatedSinceAsync_SecretNotRotated_ReturnsFalse()
    {
        var checkTime = DateTime.UtcNow.AddDays(-1);

        var isRotated = await _service.IsSecretRotatedSinceAsync("unknown-secret", checkTime);

        Assert.False(isRotated);
    }

    [Fact]
    public async Task MultipleSecrets_MaintainIndependentRotationTimes()
    {
        await _service.NotifySecretRotatedAsync("secret-1");
        await Task.Delay(100);
        await _service.NotifySecretRotatedAsync("secret-2");

        var time1 = await _service.GetLastRotationTimeAsync("secret-1");
        var time2 = await _service.GetLastRotationTimeAsync("secret-2");

        Assert.True(time2 > time1);
    }
}
