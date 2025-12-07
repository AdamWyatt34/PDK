namespace PDK.Runners.StepExecutors;

using PDK.Core.ErrorHandling;
using PDK.Core.Models;

/// <summary>
/// Exception thrown when a required tool is not available in the container.
/// </summary>
public class ToolNotFoundException : PdkException
{
    /// <summary>
    /// Gets the name of the tool that was not found.
    /// </summary>
    public string ToolName { get; }

    /// <summary>
    /// Gets the container image name where the tool was not found.
    /// </summary>
    public string ImageName { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ToolNotFoundException"/> class.
    /// </summary>
    /// <param name="toolName">The name of the tool that was not found.</param>
    /// <param name="imageName">The container image name where the tool was not found.</param>
    /// <param name="suggestions">A list of suggested solutions for resolving the missing tool.</param>
    public ToolNotFoundException(
        string toolName,
        string imageName,
        IReadOnlyList<string> suggestions)
        : base(
            ErrorCodes.ToolNotFound,
            BuildErrorMessage(toolName, imageName),
            ErrorContext.FromDocker(imageName: imageName),
            suggestions)
    {
        ToolName = toolName;
        ImageName = imageName;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ToolNotFoundException"/> class with auto-generated suggestions.
    /// </summary>
    /// <param name="toolName">The name of the tool that was not found.</param>
    /// <param name="imageName">The container image name where the tool was not found.</param>
    public ToolNotFoundException(string toolName, string imageName)
        : base(
            ErrorCodes.ToolNotFound,
            BuildErrorMessage(toolName, imageName),
            ErrorContext.FromDocker(imageName: imageName),
            GetToolSuggestions(toolName, imageName))
    {
        ToolName = toolName;
        ImageName = imageName;
    }

    /// <summary>
    /// Builds a formatted error message.
    /// </summary>
    /// <param name="toolName">The name of the tool that was not found.</param>
    /// <param name="imageName">The container image name.</param>
    /// <returns>A formatted error message.</returns>
    private static string BuildErrorMessage(string toolName, string imageName)
    {
        return $"Tool '{toolName}' not found in container image '{imageName}'";
    }

    /// <summary>
    /// Gets tool-specific suggestions for common tools.
    /// </summary>
    /// <param name="toolName">The name of the tool.</param>
    /// <param name="imageName">The container image name.</param>
    /// <returns>A list of suggestions.</returns>
    private static IReadOnlyList<string> GetToolSuggestions(string toolName, string imageName)
    {
        var suggestions = new List<string>();

        // Common tool suggestions
        var toolLower = toolName.ToLowerInvariant();

        switch (toolLower)
        {
            case "dotnet":
                suggestions.Add("Use a .NET SDK image: mcr.microsoft.com/dotnet/sdk:8.0");
                suggestions.Add("Install .NET SDK in your container");
                break;

            case "node" or "npm" or "npx":
                suggestions.Add("Use a Node.js image: node:18 or node:20");
                suggestions.Add("Install Node.js: apt-get install nodejs npm");
                break;

            case "python" or "python3" or "pip" or "pip3":
                suggestions.Add("Use a Python image: python:3.11");
                suggestions.Add("Install Python: apt-get install python3 python3-pip");
                break;

            case "java" or "javac":
                suggestions.Add("Use a JDK image: openjdk:17");
                suggestions.Add("Install OpenJDK: apt-get install openjdk-17-jdk");
                break;

            case "maven" or "mvn":
                suggestions.Add("Use a Maven image: maven:3.9");
                suggestions.Add("Install Maven: apt-get install maven");
                break;

            case "gradle":
                suggestions.Add("Use a Gradle image: gradle:8.5");
                suggestions.Add("Install Gradle or use the Gradle wrapper (./gradlew)");
                break;

            case "go" or "golang":
                suggestions.Add("Use a Go image: golang:1.21");
                suggestions.Add("Install Go: apt-get install golang-go");
                break;

            case "rust" or "cargo" or "rustc":
                suggestions.Add("Use a Rust image: rust:latest");
                suggestions.Add("Install Rust: curl --proto '=https' --tlsv1.2 -sSf https://sh.rustup.rs | sh");
                break;

            case "git":
                suggestions.Add("Install git: apt-get install git");
                suggestions.Add("Use an image with git pre-installed");
                break;

            case "docker":
                suggestions.Add("Use Docker-in-Docker: docker:dind");
                suggestions.Add("Mount Docker socket: -v /var/run/docker.sock:/var/run/docker.sock");
                break;

            case "kubectl":
                suggestions.Add("Install kubectl: curl -LO https://dl.k8s.io/release/$(curl -L -s https://dl.k8s.io/release/stable.txt)/bin/linux/amd64/kubectl");
                suggestions.Add("Use a kubectl image: bitnami/kubectl");
                break;

            case "aws":
                suggestions.Add("Install AWS CLI: pip install awscli");
                suggestions.Add("Use the AWS CLI image: amazon/aws-cli");
                break;

            case "az":
                suggestions.Add("Install Azure CLI: pip install azure-cli");
                suggestions.Add("Use the Azure CLI image: mcr.microsoft.com/azure-cli");
                break;

            default:
                suggestions.Add($"Install '{toolName}' in your container");
                suggestions.Add($"Use a container image that includes '{toolName}'");
                break;
        }

        // Add general suggestions
        suggestions.Add($"Add a setup step to install '{toolName}' before using it");
        suggestions.Add("Check the tool documentation for installation instructions");

        return suggestions;
    }
}
