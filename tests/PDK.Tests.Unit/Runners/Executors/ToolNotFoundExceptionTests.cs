using FluentAssertions;
using PDK.Core.ErrorHandling;
using PDK.Runners.StepExecutors;

namespace PDK.Tests.Unit.Runners.Executors;

public class ToolNotFoundExceptionTests
{
    #region Constructor Tests

    [Fact]
    public void Constructor_WithSuggestions_SetsAllProperties()
    {
        // Arrange
        var toolName = "dotnet";
        var imageName = "ubuntu:latest";
        var suggestions = new List<string> { "Suggestion 1", "Suggestion 2" };

        // Act
        var exception = new ToolNotFoundException(toolName, imageName, suggestions);

        // Assert
        exception.ToolName.Should().Be(toolName);
        exception.ImageName.Should().Be(imageName);
        exception.ErrorCode.Should().Be(ErrorCodes.ToolNotFound);
        exception.Suggestions.Should().BeEquivalentTo(suggestions);
    }

    [Fact]
    public void Constructor_WithSuggestions_BuildsCorrectMessage()
    {
        // Arrange
        var toolName = "node";
        var imageName = "alpine:latest";
        var suggestions = new List<string> { "Install node" };

        // Act
        var exception = new ToolNotFoundException(toolName, imageName, suggestions);

        // Assert
        exception.Message.Should().Contain("node");
        exception.Message.Should().Contain("alpine:latest");
        exception.Message.Should().Contain("not found");
    }

    [Fact]
    public void Constructor_WithoutSuggestions_GeneratesToolSpecificSuggestions()
    {
        // Arrange
        var toolName = "dotnet";
        var imageName = "ubuntu:latest";

        // Act
        var exception = new ToolNotFoundException(toolName, imageName);

        // Assert
        exception.ToolName.Should().Be(toolName);
        exception.ImageName.Should().Be(imageName);
        exception.Suggestions.Should().NotBeEmpty();
        exception.Suggestions.Should().Contain(s => s.Contains("dotnet/sdk"));
    }

    #endregion

    #region Tool-Specific Suggestions Tests

    [Fact]
    public void Constructor_WithDotnet_GeneratesDotnetSuggestions()
    {
        // Act
        var exception = new ToolNotFoundException("dotnet", "ubuntu:latest");

        // Assert
        exception.Suggestions.Should().Contain(s => s.Contains("mcr.microsoft.com/dotnet/sdk"));
        exception.Suggestions.Should().Contain(s => s.Contains("Install .NET SDK"));
    }

    [Theory]
    [InlineData("node")]
    [InlineData("npm")]
    [InlineData("npx")]
    public void Constructor_WithNodeTools_GeneratesNodeSuggestions(string toolName)
    {
        // Act
        var exception = new ToolNotFoundException(toolName, "ubuntu:latest");

        // Assert
        exception.Suggestions.Should().Contain(s => s.Contains("node:"));
        exception.Suggestions.Should().Contain(s => s.Contains("nodejs") || s.Contains("Node.js"));
    }

    [Theory]
    [InlineData("python")]
    [InlineData("python3")]
    [InlineData("pip")]
    [InlineData("pip3")]
    public void Constructor_WithPythonTools_GeneratesPythonSuggestions(string toolName)
    {
        // Act
        var exception = new ToolNotFoundException(toolName, "ubuntu:latest");

        // Assert
        exception.Suggestions.Should().Contain(s => s.Contains("python:"));
        exception.Suggestions.Should().Contain(s => s.Contains("python3") || s.Contains("Python"));
    }

    [Theory]
    [InlineData("java")]
    [InlineData("javac")]
    public void Constructor_WithJavaTools_GeneratesJavaSuggestions(string toolName)
    {
        // Act
        var exception = new ToolNotFoundException(toolName, "ubuntu:latest");

        // Assert
        exception.Suggestions.Should().Contain(s => s.Contains("openjdk:") || s.Contains("JDK"));
    }

    [Theory]
    [InlineData("maven")]
    [InlineData("mvn")]
    public void Constructor_WithMavenTools_GeneratesMavenSuggestions(string toolName)
    {
        // Act
        var exception = new ToolNotFoundException(toolName, "ubuntu:latest");

        // Assert
        exception.Suggestions.Should().Contain(s => s.Contains("maven:") || s.Contains("Maven"));
    }

    [Fact]
    public void Constructor_WithGradle_GeneratesGradleSuggestions()
    {
        // Act
        var exception = new ToolNotFoundException("gradle", "ubuntu:latest");

        // Assert
        exception.Suggestions.Should().Contain(s => s.Contains("gradle:") || s.Contains("Gradle"));
        exception.Suggestions.Should().Contain(s => s.Contains("gradlew"));
    }

    [Theory]
    [InlineData("go")]
    [InlineData("golang")]
    public void Constructor_WithGoTools_GeneratesGoSuggestions(string toolName)
    {
        // Act
        var exception = new ToolNotFoundException(toolName, "ubuntu:latest");

        // Assert
        exception.Suggestions.Should().Contain(s => s.Contains("golang:") || s.Contains("Go"));
    }

    [Theory]
    [InlineData("rust")]
    [InlineData("cargo")]
    [InlineData("rustc")]
    public void Constructor_WithRustTools_GeneratesRustSuggestions(string toolName)
    {
        // Act
        var exception = new ToolNotFoundException(toolName, "ubuntu:latest");

        // Assert
        exception.Suggestions.Should().Contain(s => s.Contains("rust:") || s.Contains("Rust") || s.Contains("rustup"));
    }

    [Fact]
    public void Constructor_WithGit_GeneratesGitSuggestions()
    {
        // Act
        var exception = new ToolNotFoundException("git", "ubuntu:latest");

        // Assert
        exception.Suggestions.Should().Contain(s => s.Contains("git"));
    }

    [Fact]
    public void Constructor_WithDocker_GeneratesDockerSuggestions()
    {
        // Act
        var exception = new ToolNotFoundException("docker", "ubuntu:latest");

        // Assert
        exception.Suggestions.Should().Contain(s => s.Contains("dind") || s.Contains("Docker-in-Docker"));
        exception.Suggestions.Should().Contain(s => s.Contains("docker.sock"));
    }

    [Fact]
    public void Constructor_WithKubectl_GeneratesKubectlSuggestions()
    {
        // Act
        var exception = new ToolNotFoundException("kubectl", "ubuntu:latest");

        // Assert
        exception.Suggestions.Should().Contain(s => s.Contains("kubectl") || s.Contains("bitnami"));
    }

    [Fact]
    public void Constructor_WithAwsCli_GeneratesAwsSuggestions()
    {
        // Act
        var exception = new ToolNotFoundException("aws", "ubuntu:latest");

        // Assert
        exception.Suggestions.Should().Contain(s => s.Contains("awscli") || s.Contains("aws-cli"));
    }

    [Fact]
    public void Constructor_WithAzureCli_GeneratesAzureSuggestions()
    {
        // Act
        var exception = new ToolNotFoundException("az", "ubuntu:latest");

        // Assert
        exception.Suggestions.Should().Contain(s => s.Contains("azure-cli") || s.Contains("Azure CLI"));
    }

    [Fact]
    public void Constructor_WithUnknownTool_GeneratesGenericSuggestions()
    {
        // Act
        var exception = new ToolNotFoundException("unknowntool", "ubuntu:latest");

        // Assert
        exception.Suggestions.Should().Contain(s => s.Contains("unknowntool"));
        exception.Suggestions.Should().Contain(s => s.Contains("Install"));
        exception.Suggestions.Should().Contain(s => s.Contains("documentation"));
    }

    [Fact]
    public void Constructor_AlwaysIncludesGeneralSuggestions()
    {
        // Act
        var exception = new ToolNotFoundException("anytool", "anyimage");

        // Assert
        exception.Suggestions.Should().Contain(s => s.Contains("setup step"));
        exception.Suggestions.Should().Contain(s => s.Contains("documentation"));
    }

    #endregion

    #region Case Sensitivity Tests

    [Theory]
    [InlineData("DOTNET")]
    [InlineData("Dotnet")]
    [InlineData("DotNet")]
    public void Constructor_ToolNameIsCaseInsensitive(string toolName)
    {
        // Act
        var exception = new ToolNotFoundException(toolName, "ubuntu:latest");

        // Assert
        exception.ToolName.Should().Be(toolName);
        exception.Suggestions.Should().Contain(s => s.Contains("dotnet/sdk"));
    }

    #endregion

    #region Error Message Format Tests

    [Fact]
    public void Message_ContainsToolName()
    {
        // Act
        var exception = new ToolNotFoundException("mytool", "myimage");

        // Assert
        exception.Message.Should().Contain("mytool");
    }

    [Fact]
    public void Message_ContainsImageName()
    {
        // Act
        var exception = new ToolNotFoundException("mytool", "myimage:latest");

        // Assert
        exception.Message.Should().Contain("myimage:latest");
    }

    [Fact]
    public void Message_IndicatesToolNotFound()
    {
        // Act
        var exception = new ToolNotFoundException("sometool", "someimage");

        // Assert
        exception.Message.Should().Contain("not found");
    }

    [Fact]
    public void Message_IndicatesContainerContext()
    {
        // Act
        var exception = new ToolNotFoundException("sometool", "someimage");

        // Assert
        exception.Message.Should().Contain("container");
    }

    #endregion

    #region ErrorContext Tests

    [Fact]
    public void Context_ContainsImageName()
    {
        // Arrange
        var imageName = "mcr.microsoft.com/dotnet/sdk:8.0";

        // Act
        var exception = new ToolNotFoundException("dotnet", imageName);

        // Assert
        exception.Context.Should().NotBeNull();
        exception.Context!.ImageName.Should().Be(imageName);
    }

    #endregion
}
