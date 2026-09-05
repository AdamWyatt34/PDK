namespace PDK.Tests.Unit.Logging;

using FluentAssertions;
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
    [Theory]
    [InlineData("Authorization: Bearer eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.abc123", "Authorization: Bearer ***")]
    [InlineData("Authorization: Basic dXNlcjpwYXNzd29yZA==", "Authorization: Basic ***")]
    [InlineData("authorization=Bearer abc.def.ghi", "authorization=Bearer ***")]
    [InlineData("Authorization: token ghp_abcdefghijklmnop", "Authorization: token ***")]
    [InlineData("Authorization: rawcredential123", "Authorization: ***")]
    [InlineData("curl -H \"Authorization: Bearer abcdef123456\" https://api", "curl -H \"Authorization: Bearer ***\" https://api")]
    [InlineData("curl -H 'Authorization: Bearer abcdef123456' https://api", "curl -H 'Authorization: Bearer ***' https://api")]
    [InlineData("Bearer abcdefgh12345678", "Bearer ***")]
    public void MaskSecretsEnhanced_MasksBearerAndAuthorizationHeaders(string input, string expected)
    {
        var masker = new SecretMasker();

        masker.MaskSecretsEnhanced(input).Should().Be(expected);
    }

    [Fact]
    public void MaskSecretsEnhanced_BearerFollowedByOrdinaryWords_IsLeftAlone()
    {
        var masker = new SecretMasker();

        masker.MaskSecretsEnhanced("bearer tokens are used here").Should().Be("bearer tokens are used here");
    }

    [Theory]
    [InlineData("password: \"with spaces\"", "password: \"***\"")]
    [InlineData("password: 'single quoted value'", "password: '***'")]
    [InlineData("password=\"quoted=with;separators\"", "password=\"***\"")]
    [InlineData("token=abc123", "token=***")]
    [InlineData("token: abc123", "token: ***")]
    [InlineData("--password=hunter2", "--password=***")]
    [InlineData("--password hunter2 stays", "--password hunter2 stays")]
    [InlineData("password : hunter2", "password : ***")]
    [InlineData("DB_PASSWORD=hunter2", "DB_PASSWORD=***")]
    [InlineData("db.password=hunter2", "db.password=***")]
    [InlineData("SECRET2=abc", "SECRET2=***")]
    [InlineData("api-key=abc", "api-key=***")]
    [InlineData("MyApiKey=abc", "MyApiKey=***")]
    [InlineData("token=abc&user=bob", "token=***&user=bob")]
    [InlineData("token=abc; next", "token=***; next")]
    [InlineData("(password=abc)", "(password=***)")]
    public void MaskSecretsEnhanced_KeywordPatterns_PreserveKeyAndSeparator(string input, string expected)
    {
        var masker = new SecretMasker();

        masker.MaskSecretsEnhanced(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("AUTHOR=Jane")]
    [InlineData("MONKEY=banana")]
    [InlineData("TOKEN_URL=https://example.com/token")]
    [InlineData("token_count: 5")]
    [InlineData("PASSWORD_LENGTH=12")]
    [InlineData("authorization_mode: strict")]
    [InlineData("tokens=abc")]
    [InlineData("password=")]
    [InlineData("secret: null")]
    [InlineData("auth: true")]
    public void MaskSecretsEnhanced_NonSecretKeys_AreNotMasked(string input)
    {
        var masker = new SecretMasker();

        masker.MaskSecretsEnhanced(input).Should().Be(input);
    }

    [Fact]
    public void MaskSecretsEnhanced_Json_StaysValidJson()
    {
        // Arrange
        var masker = new SecretMasker();
        const string input = """{"password": "s3cret", "token": null, "count": 3, "api_key": 12345, "nested": {"secret": "a\"b\\c"}, "user": "bob"}""";

        // Act
        var result = masker.MaskSecretsEnhanced(input);

        // Assert
        result.Should().NotContain("s3cret").And.NotContain("12345").And.NotContain("a\\\"b");
        result.Should().Contain("\"token\": null").And.Contain("\"user\": \"bob\"");

        using var document = System.Text.Json.JsonDocument.Parse(result);
        document.RootElement.GetProperty("password").GetString().Should().Be("***");
        document.RootElement.GetProperty("api_key").GetString().Should().Be("***");
        document.RootElement.GetProperty("nested").GetProperty("secret").GetString().Should().Be("***");
        document.RootElement.GetProperty("token").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
    }

    [Fact]
    public void MaskSecretsEnhanced_IsIdempotent()
    {
        var masker = new SecretMasker();
        masker.RegisterSecret("registered-value");
        const string input = "registered-value password=abc Authorization: Bearer abcdefghijkl {\"token\": \"x\"} https://u:p@h/";

        var once = masker.MaskSecretsEnhanced(input);
        var twice = masker.MaskSecretsEnhanced(once);

        twice.Should().Be(once);
    }

    [Theory]
    [InlineData("postgres://admin:s3cret@db.internal:5432/app", "postgres://***:***@db.internal:5432/app")]
    [InlineData("redis://:onlypass@cache/0", "redis://:onlypass@cache/0")]
    [InlineData("https://user:p%40ss@example.com/x", "https://***:***@example.com/x")]
    public void MaskSecretsEnhanced_MasksCredentialsInAnyUrlScheme(string input, string expected)
    {
        var masker = new SecretMasker();

        masker.MaskSecretsEnhanced(input).Should().Be(expected);
    }

    [Fact]
    public void MaskDictionary_KeysThatMerelyContainKeywords_AreNotMasked()
    {
        // Arrange
        var masker = new SecretMasker();
        var data = new Dictionary<string, object?>
        {
            ["monkey"] = "banana",
            ["author"] = "jane",
            ["turkey_region"] = "eu",
            ["apiKey"] = "abc"
        };

        // Act
        var result = masker.MaskDictionary(data);

        // Assert
        result["monkey"].Should().Be("banana");
        result["author"].Should().Be("jane");
        result["turkey_region"].Should().Be("eu");
        result["apiKey"].Should().Be("***");
    }

    [Fact]
    public void MaskDictionary_MasksStringsInsideLists()
    {
        var masker = new SecretMasker();
        masker.RegisterSecret("list-secret");
        var data = new Dictionary<string, object?>
        {
            ["args"] = new List<object> { "--token=abc", "list-secret", 42 }
        };

        var result = masker.MaskDictionary(data);

        var args = ((IEnumerable<object?>)result["args"]!).ToList();
        args[0].Should().Be("--token=***");
        args[1].Should().Be("***");
        args[2].Should().Be(42);
    }
}
