namespace PDK.Tests.Unit.Diagnostics;

using FluentAssertions;
using PDK.Core.Diagnostics;
using Xunit;

/// <summary>
/// Unit tests for CiDetector.
/// </summary>
public class CiDetectorTests : IDisposable
{
    private readonly Dictionary<string, string?> _originalEnvVars = new();
    private readonly string[] _ciVariables =
    [
        "CI", "GITHUB_ACTIONS", "AZURE_PIPELINES", "TF_BUILD",
        "GITLAB_CI", "JENKINS_URL", "TRAVIS", "CIRCLECI",
        "BUILDKITE", "TEAMCITY_VERSION"
    ];

    public CiDetectorTests()
    {
        // Store original values and clear CI variables
        foreach (var var in _ciVariables)
        {
            _originalEnvVars[var] = Environment.GetEnvironmentVariable(var);
            Environment.SetEnvironmentVariable(var, null);
        }
    }

    public void Dispose()
    {
        // Restore original values
        foreach (var (var, value) in _originalEnvVars)
        {
            Environment.SetEnvironmentVariable(var, value);
        }
    }

    [Fact]
    public void IsRunningInCi_ReturnsFalse_WhenNoCiVariablesSet()
    {
        // Act
        var result = CiDetector.IsRunningInCi();

        // Assert
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData("CI", "true")]
    [InlineData("GITHUB_ACTIONS", "true")]
    [InlineData("AZURE_PIPELINES", "true")]
    [InlineData("TF_BUILD", "True")]
    [InlineData("GITLAB_CI", "true")]
    [InlineData("JENKINS_URL", "http://jenkins.example.com")]
    [InlineData("TRAVIS", "true")]
    [InlineData("CIRCLECI", "true")]
    [InlineData("BUILDKITE", "true")]
    [InlineData("TEAMCITY_VERSION", "2023.1")]
    public void IsRunningInCi_ReturnsTrue_WhenCiVariableIsSet(string variable, string value)
    {
        // Arrange
        Environment.SetEnvironmentVariable(variable, value);

        // Act
        var result = CiDetector.IsRunningInCi();

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void GetCiSystemName_ReturnsNull_WhenNotInCi()
    {
        // Act
        var result = CiDetector.GetCiSystemName();

        // Assert
        result.Should().BeNull();
    }

    [Theory]
    [InlineData("GITHUB_ACTIONS", "true", "GitHub Actions")]
    [InlineData("AZURE_PIPELINES", "true", "Azure Pipelines")]
    [InlineData("TF_BUILD", "true", "Azure Pipelines")]
    [InlineData("GITLAB_CI", "true", "GitLab CI")]
    [InlineData("JENKINS_URL", "http://jenkins.example.com", "Jenkins")]
    [InlineData("TRAVIS", "true", "Travis CI")]
    [InlineData("CIRCLECI", "true", "CircleCI")]
    [InlineData("BUILDKITE", "true", "Buildkite")]
    [InlineData("TEAMCITY_VERSION", "2023.1", "TeamCity")]
    public void GetCiSystemName_ReturnsCorrectName(string variable, string value, string expectedName)
    {
        // Arrange
        Environment.SetEnvironmentVariable(variable, value);

        // Act
        var result = CiDetector.GetCiSystemName();

        // Assert
        result.Should().Be(expectedName);
    }

    [Fact]
    public void GetCiSystemName_ReturnsUnknownCi_WhenOnlyCiVariableSet()
    {
        // Arrange
        Environment.SetEnvironmentVariable("CI", "true");

        // Act
        var result = CiDetector.GetCiSystemName();

        // Assert
        result.Should().Be("Unknown CI");
    }
}
