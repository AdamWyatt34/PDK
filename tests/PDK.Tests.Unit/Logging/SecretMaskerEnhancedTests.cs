namespace PDK.Tests.Unit.Logging;

using PDK.Core.Logging;
using Xunit;

/// <summary>
/// Unit tests for enhanced <see cref="SecretMasker"/> functionality.
/// </summary>
public class SecretMaskerEnhancedTests
{
    [Fact]
    public void MaskSecretsEnhanced_MasksUrlCredentials()
    {
        // Arrange
        var masker = new SecretMasker();
        const string input = "Connecting to https://user:password123@api.example.com/endpoint";

        // Act
        var result = masker.MaskSecretsEnhanced(input);

        // Assert
        Assert.DoesNotContain("password123", result);
        Assert.Contains("https://***:***@api.example.com", result);
    }

    [Fact]
    public void MaskSecretsEnhanced_MasksKeywordPatterns()
    {
        // Arrange
        var masker = new SecretMasker();

        // Act & Assert
        Assert.DoesNotContain("secretvalue", masker.MaskSecretsEnhanced("password=secretvalue"));
        Assert.DoesNotContain("mytoken123", masker.MaskSecretsEnhanced("token: mytoken123"));
        Assert.DoesNotContain("apikey999", masker.MaskSecretsEnhanced("api_key=apikey999"));
        Assert.DoesNotContain("authdata", masker.MaskSecretsEnhanced("auth=authdata"));
    }

    [Fact]
    public void MaskSecretsEnhanced_MasksJsonKeyValues()
    {
        // Arrange
        var masker = new SecretMasker();
        const string input = """{"password": "secret123", "username": "admin"}""";

        // Act
        var result = masker.MaskSecretsEnhanced(input);

        // Assert
        Assert.DoesNotContain("secret123", result);
        Assert.Contains("admin", result); // username should not be masked
    }

    [Fact]
    public void MaskSecretsEnhanced_CombinesAllMaskingMethods()
    {
        // Arrange
        var masker = new SecretMasker();
        masker.RegisterSecret("registeredsecret");
        const string input = @"
            Registered: registeredsecret
            URL: https://user:urlpass@example.com
            Config: password=configpass
        ";

        // Act
        var result = masker.MaskSecretsEnhanced(input);

        // Assert
        Assert.DoesNotContain("registeredsecret", result);
        Assert.DoesNotContain("urlpass", result);
        Assert.DoesNotContain("configpass", result);
    }

    [Fact]
    public void RedactionEnabled_WhenFalse_DoesNotMask()
    {
        // Arrange
        var masker = new SecretMasker { RedactionEnabled = false };
        masker.RegisterSecret("mysecret");
        const string input = "The secret is mysecret and password=test123";

        // Act
        var result = masker.MaskSecretsEnhanced(input);

        // Assert - nothing should be masked
        Assert.Contains("mysecret", result);
        Assert.Contains("test123", result);
    }

    [Fact]
    public void MaskDictionary_MasksSensitiveKeyValues()
    {
        // Arrange
        var masker = new SecretMasker();
        var data = new Dictionary<string, object?>
        {
            ["username"] = "admin",
            ["password"] = "secret123",
            ["api_key"] = "key-abc123",
            ["data"] = "normal data"
        };

        // Act
        var result = masker.MaskDictionary(data);

        // Assert
        Assert.Equal("admin", result["username"]);
        Assert.Equal("***", result["password"]);
        Assert.Equal("***", result["api_key"]);
        Assert.Equal("normal data", result["data"]);
    }

    [Fact]
    public void MaskDictionary_MasksNestedDictionaries()
    {
        // Arrange
        var masker = new SecretMasker();
        var data = new Dictionary<string, object?>
        {
            ["config"] = new Dictionary<string, object?>
            {
                ["database"] = new Dictionary<string, object?>
                {
                    ["host"] = "localhost",
                    ["password"] = "dbpass123"
                }
            }
        };

        // Act
        var result = masker.MaskDictionary(data);

        // Assert
        var config = (IDictionary<string, object?>)result["config"]!;
        var database = (IDictionary<string, object?>)config["database"]!;
        Assert.Equal("localhost", database["host"]);
        Assert.Equal("***", database["password"]);
    }

    [Fact]
    public void MaskDictionary_HandlesNullValues()
    {
        // Arrange
        var masker = new SecretMasker();
        var data = new Dictionary<string, object?>
        {
            ["password"] = null,
            ["api_key"] = null
        };

        // Act
        var result = masker.MaskDictionary(data);

        // Assert
        Assert.Null(result["password"]);
        Assert.Null(result["api_key"]);
    }

    [Theory]
    [InlineData("password")]
    [InlineData("passwd")]
    [InlineData("pwd")]
    [InlineData("secret")]
    [InlineData("token")]
    [InlineData("api_key")]
    [InlineData("apikey")]
    [InlineData("auth")]
    [InlineData("credential")]
    [InlineData("bearer")]
    [InlineData("private_key")]
    [InlineData("access_token")]
    [InlineData("refresh_token")]
    public void MaskSecretsEnhanced_MasksAllSensitiveKeywords(string keyword)
    {
        // Arrange
        var masker = new SecretMasker();
        var input = $"{keyword}=sensitivevalue123";

        // Act
        var result = masker.MaskSecretsEnhanced(input);

        // Assert
        Assert.DoesNotContain("sensitivevalue123", result);
        Assert.Contains("***", result);
    }

    [Fact]
    public void MaskSecretsEnhanced_PreservesNonSensitiveContent()
    {
        // Arrange
        var masker = new SecretMasker();
        const string input = "Normal log message with user=john and status=active";

        // Act
        var result = masker.MaskSecretsEnhanced(input);

        // Assert
        Assert.Contains("user=john", result);
        Assert.Contains("status=active", result);
    }

    [Fact]
    public void MaskSecretsEnhanced_IsCaseInsensitive()
    {
        // Arrange
        var masker = new SecretMasker();

        // Act & Assert
        Assert.Contains("***", masker.MaskSecretsEnhanced("PASSWORD=secret"));
        Assert.Contains("***", masker.MaskSecretsEnhanced("Password=secret"));
        Assert.Contains("***", masker.MaskSecretsEnhanced("TOKEN=abc"));
        Assert.Contains("***", masker.MaskSecretsEnhanced("Token=abc"));
    }

    [Fact]
    public void MaskSecretsEnhanced_HandlesEmptyString()
    {
        // Arrange
        var masker = new SecretMasker();

        // Act
        var result = masker.MaskSecretsEnhanced(string.Empty);

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void MaskSecretsEnhanced_HandlesNull()
    {
        // Arrange
        var masker = new SecretMasker();

        // Act
        var result = masker.MaskSecretsEnhanced(null!);

        // Assert
        Assert.Null(result);
    }
}
