namespace PDK.Tests.Unit.Secrets;

using FluentAssertions;
using PDK.Core.ErrorHandling;
using PDK.Core.Models;
using PDK.Core.Secrets;
using Xunit;

public class SecretExceptionTests
{
    [Fact]
    public void SecretException_DerivesFromPdkException_WithCodeAndSuggestions()
    {
        // Act
        var ex = SecretException.NotFound("API_KEY");

        // Assert
        ex.Should().BeAssignableTo<PdkException>();
        ex.ErrorCode.Should().Be(ErrorCodes.SecretNotFound);
        ex.SecretName.Should().Be("API_KEY");
        ex.HasSuggestions.Should().BeTrue();
        ex.Suggestions.Should().Contain(s => s.Contains("pdk secret set API_KEY"));
        ex.GetFormattedMessage().Should().Be($"[{ErrorCodes.SecretNotFound}] Secret 'API_KEY' not found");
    }

    [Fact]
    public void InvalidName_MessageExplainsProblem_AndSuggestsValidName()
    {
        // Act
        var ex = SecretException.InvalidName("has-hyphen");

        // Assert
        ex.ErrorCode.Should().Be(ErrorCodes.SecretInvalidName);
        ex.Message.Should().Contain("has-hyphen");
        ex.Message.Should().Contain("'-'");
        ex.Suggestions.Should().Contain(s => s.Contains("'has_hyphen'"));
        ex.Suggestions.Should().Contain(s => s.Contains("[A-Za-z_][A-Za-z0-9_]*"));
    }

    [Theory]
    [InlineData("123abc", "must not start with a digit")]
    [InlineData("has space", "unsupported character")]
    [InlineData("", "empty")]
    [InlineData("   ", "empty")]
    public void InvalidName_DescribesSpecificProblem(string name, string expectedFragment)
    {
        SecretException.InvalidName(name).Message.Should().Contain(expectedFragment);
    }

    [Theory]
    [InlineData("has-hyphen", "has_hyphen")]
    [InlineData("has.dot", "has_dot")]
    [InlineData("has space", "has_space")]
    [InlineData("123abc", "_123abc")]
    [InlineData("VALID_NAME", "VALID_NAME")]
    [InlineData("", "MY_SECRET")]
    [InlineData(null, "MY_SECRET")]
    public void SuggestValidName_ProducesValidNames(string? input, string expected)
    {
        SecretException.SuggestValidName(input).Should().Be(expected);
        SecretException.SuggestValidName(input).Should().MatchRegex("^[A-Za-z_][A-Za-z0-9_]*$");
    }

    [Fact]
    public void DecryptionFailed_WithName_NamesTheSecret()
    {
        var ex = SecretException.DecryptionFailed("DB_PASSWORD", new InvalidOperationException("bad tag"), "authentication failed");

        ex.ErrorCode.Should().Be(ErrorCodes.SecretDecryptionFailed);
        ex.SecretName.Should().Be("DB_PASSWORD");
        ex.Message.Should().Be("Failed to decrypt secret 'DB_PASSWORD': authentication failed");
        ex.Message.Should().NotContain("unknown");
        ex.Suggestions.Should().Contain(s => s.Contains("pdk secret set DB_PASSWORD"));
        ex.InnerException.Should().BeOfType<InvalidOperationException>();
    }

    [Fact]
    public void DecryptionFailed_WithoutName_DoesNotSayUnknown()
    {
        var ex = SecretException.DecryptionFailed(null, reason: "truncated");

        ex.SecretName.Should().BeNull();
        ex.Message.Should().Be("Failed to decrypt secret value: truncated");
        ex.Message.Should().NotContain("unknown");
        ex.Suggestions.Should().NotContain(s => s.Contains("pdk secret set"));
    }

    [Fact]
    public void StorageLocked_MentionsLockFileAndTimeout()
    {
        var ex = SecretException.StorageLocked("/tmp/secrets.json.lock", TimeSpan.FromSeconds(5));

        ex.ErrorCode.Should().Be(ErrorCodes.SecretStorageFailed);
        ex.Message.Should().Contain("/tmp/secrets.json.lock");
        ex.Message.Should().Contain("5s");
        ex.Suggestions.Should().Contain(s => s.Contains("Another pdk process"));
    }

    [Fact]
    public void KeyFileInvalid_MentionsPathAndRecovery()
    {
        var ex = SecretException.KeyFileInvalid("/home/u/.pdk/secret.key", "bad header");

        ex.ErrorCode.Should().Be(ErrorCodes.SecretStorageFailed);
        ex.Message.Should().Contain("/home/u/.pdk/secret.key");
        ex.Message.Should().Contain("bad header");
        ex.Suggestions.Should().Contain(s => s.Contains("backup"));
    }

    [Fact]
    public void StorageFailed_IncludesInnerMessage()
    {
        var ex = SecretException.StorageFailed("/x/secrets.json", new IOException("disk full"));

        ex.ErrorCode.Should().Be(ErrorCodes.SecretStorageFailed);
        ex.Message.Should().Contain("/x/secrets.json");
        ex.Message.Should().Contain("disk full");
    }
}
