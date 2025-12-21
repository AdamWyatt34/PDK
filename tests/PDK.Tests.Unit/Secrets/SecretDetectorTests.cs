namespace PDK.Tests.Unit.Secrets;

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using PDK.Core.Secrets;
using Xunit;

public class SecretDetectorTests
{
    private readonly SecretDetector _detector;

    public SecretDetectorTests()
    {
        _detector = new SecretDetector();
    }

    [Theory]
    [InlineData("PASSWORD")]
    [InlineData("password")]
    [InlineData("Password")]
    [InlineData("DB_PASSWORD")]
    [InlineData("USER_PASSWORD")]
    [InlineData("MY_PASSWD")]
    [InlineData("passwd")]
    [InlineData("USER_PWD")]
    [InlineData("pwd")]
    public void IsPotentialSecret_PasswordVariants_ReturnsTrue(string name)
    {
        _detector.IsPotentialSecret(name).Should().BeTrue($"'{name}' should be detected as potential secret");
    }

    [Theory]
    [InlineData("SECRET")]
    [InlineData("secret")]
    [InlineData("MY_SECRET")]
    [InlineData("SECRET_KEY")]
    [InlineData("CLIENT_SECRET")]
    public void IsPotentialSecret_SecretVariants_ReturnsTrue(string name)
    {
        _detector.IsPotentialSecret(name).Should().BeTrue($"'{name}' should be detected as potential secret");
    }

    [Theory]
    [InlineData("TOKEN")]
    [InlineData("token")]
    [InlineData("AUTH_TOKEN")]
    [InlineData("ACCESS_TOKEN")]
    [InlineData("REFRESH_TOKEN")]
    [InlineData("BEARER_TOKEN")]
    [InlineData("accesstoken")]
    [InlineData("refreshtoken")]
    public void IsPotentialSecret_TokenVariants_ReturnsTrue(string name)
    {
        _detector.IsPotentialSecret(name).Should().BeTrue($"'{name}' should be detected as potential secret");
    }

    [Theory]
    [InlineData("API_KEY")]
    [InlineData("api_key")]
    [InlineData("APIKEY")]
    [InlineData("apikey")]
    [InlineData("API-KEY")]
    [InlineData("MY_API_KEY")]
    [InlineData("AZURE_API_KEY")]
    public void IsPotentialSecret_ApiKeyVariants_ReturnsTrue(string name)
    {
        _detector.IsPotentialSecret(name).Should().BeTrue($"'{name}' should be detected as potential secret");
    }

    [Theory]
    [InlineData("KEY")]
    [InlineData("key")]
    [InlineData("ENCRYPTION_KEY")]
    [InlineData("SIGNING_KEY")]
    [InlineData("PRIVATE_KEY")]
    [InlineData("privatekey")]
    [InlineData("PRIVATE-KEY")]
    public void IsPotentialSecret_KeyVariants_ReturnsTrue(string name)
    {
        _detector.IsPotentialSecret(name).Should().BeTrue($"'{name}' should be detected as potential secret");
    }

    [Theory]
    [InlineData("AUTH")]
    [InlineData("auth")]
    [InlineData("AUTHORIZATION")]
    [InlineData("AUTHENTICATION")]
    [InlineData("OAUTH")]
    public void IsPotentialSecret_AuthVariants_ReturnsTrue(string name)
    {
        _detector.IsPotentialSecret(name).Should().BeTrue($"'{name}' should be detected as potential secret");
    }

    [Theory]
    [InlineData("CREDENTIAL")]
    [InlineData("credentials")]
    [InlineData("AZURE_CREDENTIALS")]
    [InlineData("AWS_CREDENTIAL")]
    public void IsPotentialSecret_CredentialVariants_ReturnsTrue(string name)
    {
        _detector.IsPotentialSecret(name).Should().BeTrue($"'{name}' should be detected as potential secret");
    }

    [Theory]
    [InlineData("CERTIFICATE")]
    [InlineData("certificate")]
    [InlineData("CERT")]
    [InlineData("SSL_CERT")]
    [InlineData("TLS_CERTIFICATE")]
    public void IsPotentialSecret_CertificateVariants_ReturnsTrue(string name)
    {
        _detector.IsPotentialSecret(name).Should().BeTrue($"'{name}' should be detected as potential secret");
    }

    [Theory]
    [InlineData("USERNAME")]
    [InlineData("USER_NAME")]
    [InlineData("EMAIL")]
    [InlineData("HOST")]
    [InlineData("PORT")]
    [InlineData("DATABASE")]
    [InlineData("CONNECTION_STRING")] // Might contain secrets but name itself isn't a pattern
    [InlineData("URL")]
    [InlineData("PATH")]
    [InlineData("DIRECTORY")]
    [InlineData("VERSION")]
    [InlineData("COUNT")]
    [InlineData("SIZE")]
    [InlineData("TIMEOUT")]
    public void IsPotentialSecret_NonSecretNames_ReturnsFalse(string name)
    {
        _detector.IsPotentialSecret(name).Should().BeFalse($"'{name}' should NOT be detected as potential secret");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsPotentialSecret_NullOrEmpty_ReturnsFalse(string? name)
    {
        _detector.IsPotentialSecret(name!).Should().BeFalse();
    }

    [Fact]
    public void WarnIfPotentialSecret_PotentialSecret_LogsWarning()
    {
        // Arrange
        var logger = new Mock<ILogger>();
        logger.Setup(l => l.IsEnabled(LogLevel.Warning)).Returns(true);

        // Act
        _detector.WarnIfPotentialSecret("API_KEY", "some-value-12345", logger.Object);

        // Assert
        logger.Verify(l => l.Log(
            LogLevel.Warning,
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void WarnIfPotentialSecret_NonSecret_DoesNotLog()
    {
        // Arrange
        var logger = new Mock<ILogger>();

        // Act
        _detector.WarnIfPotentialSecret("HOST", "localhost", logger.Object);

        // Assert
        logger.Verify(l => l.Log(
            It.IsAny<LogLevel>(),
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    [Fact]
    public void WarnIfPotentialSecret_NullLogger_DoesNotThrow()
    {
        // Act & Assert
        var act = () => _detector.WarnIfPotentialSecret("API_KEY", "value", null);
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ab")] // Too short
    [InlineData("abc")] // Just 3 chars, but still short for a secret
    public void WarnIfPotentialSecret_ShortOrEmptyValue_DoesNotLog(string? value)
    {
        // Arrange
        var logger = new Mock<ILogger>();

        // Act
        _detector.WarnIfPotentialSecret("API_KEY", value!, logger.Object);

        // Assert
        logger.Verify(l => l.Log(
            It.IsAny<LogLevel>(),
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    [Fact]
    public void GetSecretKeywords_ReturnsNonEmptyList()
    {
        // Act
        var keywords = _detector.GetSecretKeywords().ToList();

        // Assert
        keywords.Should().NotBeEmpty();
        keywords.Should().Contain("password");
        keywords.Should().Contain("secret");
        keywords.Should().Contain("token");
        keywords.Should().Contain("key");
    }

    [Theory]
    [InlineData("SIGNING")]
    [InlineData("signing")]
    [InlineData("ENCRYPTION")]
    [InlineData("encryption")]
    [InlineData("DECRYPT")]
    [InlineData("decrypt")]
    public void IsPotentialSecret_CryptoRelated_ReturnsTrue(string name)
    {
        _detector.IsPotentialSecret(name).Should().BeTrue($"'{name}' should be detected as potential secret");
    }

    [Theory]
    [InlineData("bearer")]
    [InlineData("BEARER")]
    [InlineData("BEARER_AUTH")]
    public void IsPotentialSecret_BearerVariants_ReturnsTrue(string name)
    {
        _detector.IsPotentialSecret(name).Should().BeTrue($"'{name}' should be detected as potential secret");
    }

    [Fact]
    public void IsPotentialSecret_CaseInsensitive()
    {
        // Arrange
        var variations = new[]
        {
            "PASSWORD", "password", "Password", "PASSWORD",
            "pAsSwoRd", "passWORD"
        };

        // Act & Assert
        foreach (var name in variations)
        {
            _detector.IsPotentialSecret(name).Should().BeTrue($"'{name}' should match case-insensitively");
        }
    }

    [Fact]
    public void IsPotentialSecret_KeywordInMiddle_Detected()
    {
        // Arrange - Keywords embedded in longer names
        var names = new[]
        {
            "MY_PASSWORD_VALUE",
            "NEW_TOKEN_123",
            "OLD_SECRET_KEY",
            "API_KEY_PRODUCTION"
        };

        // Act & Assert
        foreach (var name in names)
        {
            _detector.IsPotentialSecret(name).Should().BeTrue($"'{name}' should be detected");
        }
    }
}
