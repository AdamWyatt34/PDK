namespace PDK.CLI.ErrorHandling;

using PDK.Core.ErrorHandling;
using PDK.Core.Models;

/// <summary>
/// Generates contextual suggestions for resolving errors.
/// </summary>
public sealed class ErrorSuggestionEngine
{
    private const string DocsBaseUrl = "https://docs.pdk.dev/errors/";

    /// <summary>
    /// Gets suggestions for a PdkException based on error code and context.
    /// </summary>
    /// <param name="exception">The exception to get suggestions for.</param>
    /// <returns>A list of suggestions.</returns>
    public IReadOnlyList<string> GetSuggestions(PdkException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        // If the exception already has suggestions, return them
        if (exception.HasSuggestions)
        {
            return exception.Suggestions;
        }

        // Generate suggestions based on error code and context
        return GetSuggestions(exception.ErrorCode, exception.Context);
    }

    /// <summary>
    /// Gets suggestions for a specific error code.
    /// </summary>
    /// <param name="errorCode">The error code.</param>
    /// <param name="context">The error context.</param>
    /// <returns>A list of suggestions.</returns>
    public IReadOnlyList<string> GetSuggestions(string errorCode, ErrorContext? context = null)
    {
        var suggestions = new List<string>();

        // Add error-code specific suggestions
        suggestions.AddRange(GetErrorCodeSuggestions(errorCode));

        // Add context-specific suggestions
        if (context != null)
        {
            suggestions.AddRange(GetContextSuggestions(context));
        }

        // Add exit code suggestions if available
        if (context?.ExitCode.HasValue == true)
        {
            suggestions.AddRange(GetExitCodeSuggestions(context.ExitCode.Value));
        }

        return suggestions;
    }

    /// <summary>
    /// Gets suggestions based on an exit code.
    /// </summary>
    /// <param name="exitCode">The exit code.</param>
    /// <returns>A list of suggestions.</returns>
    public IReadOnlyList<string> GetExitCodeSuggestions(int exitCode)
    {
        return exitCode switch
        {
            0 => [],
            1 => [
                "Exit code 1 indicates a general error",
                "Review the error output above for details",
                "Run with --verbose for more information"
            ],
            2 => [
                "Exit code 2 indicates incorrect usage or command syntax",
                "Check the command arguments and options"
            ],
            126 => [
                "Exit code 126: command found but not executable",
                "Check file permissions (chmod +x)"
            ],
            127 => [
                "Exit code 127: command not found",
                "The tool may not be installed in the container",
                "Consider using a different base image"
            ],
            128 => [
                "Exit code 128: invalid exit argument"
            ],
            137 => [
                "Exit code 137: container killed (out of memory)",
                "Increase available memory for Docker",
                "Optimize your process to use less memory"
            ],
            143 => [
                "Exit code 143: process terminated (SIGTERM)",
                "The step may have exceeded the timeout"
            ],
            _ when exitCode > 128 => [
                $"Process killed by signal {exitCode - 128}",
                "Check system resources and logs"
            ],
            _ => [
                $"Step failed with exit code {exitCode}",
                "Review the error output for details"
            ]
        };
    }

    /// <summary>
    /// Gets the documentation URL for an error code.
    /// </summary>
    /// <param name="errorCode">The error code.</param>
    /// <returns>The documentation URL.</returns>
    public string GetDocumentationUrl(string errorCode)
    {
        return ErrorCodes.GetDocumentationUrl(errorCode);
    }

    /// <summary>
    /// Gets a troubleshooting command for the error.
    /// </summary>
    /// <param name="exception">The exception.</param>
    /// <returns>A troubleshooting command, or null if not applicable.</returns>
    public string? GetTroubleshootingCommand(PdkException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception.ErrorCode switch
        {
            ErrorCodes.DockerNotRunning => "docker info",
            ErrorCodes.DockerNotInstalled => "docker --version",
            ErrorCodes.DockerPermissionDenied => "groups $USER | grep docker",
            ErrorCodes.DockerImageNotFound when exception.Context.ImageName != null =>
                $"docker pull {exception.Context.ImageName}",
            ErrorCodes.ContainerExecutionFailed when exception.Context.ContainerId != null =>
                $"docker logs {exception.Context.ContainerId}",
            ErrorCodes.InvalidYamlSyntax when exception.Context.PipelineFile != null =>
                $"pdk validate --file \"{exception.Context.PipelineFile}\"",
            ErrorCodes.FileNotFound => "ls -la",
            _ => null
        };
    }

    private static IEnumerable<string> GetErrorCodeSuggestions(string errorCode)
    {
        return errorCode switch
        {
            // Docker errors
            ErrorCodes.DockerNotRunning => [
                "Start Docker Desktop (Windows/Mac)",
                "Run: sudo systemctl start docker (Linux)",
                "Check Docker service: docker info",
                "Try running with --host mode"
            ],
            ErrorCodes.DockerNotInstalled => [
                "Install Docker Desktop: https://www.docker.com/products/docker-desktop",
                "Install Docker Engine: https://docs.docker.com/engine/install/",
                "Try running with --host mode"
            ],
            ErrorCodes.DockerPermissionDenied => [
                "Add your user to the docker group: sudo usermod -aG docker $USER",
                "Log out and log back in for the group change to take effect"
            ],
            ErrorCodes.DockerImageNotFound => [
                "Check if the image name is correct",
                "Verify the image exists on Docker Hub or your registry",
                "Check your network connection"
            ],
            ErrorCodes.ContainerCreationFailed => [
                "Check available disk space",
                "Try removing unused containers: docker container prune"
            ],
            ErrorCodes.ContainerExecutionFailed => [
                "Check the container logs for details",
                "Run with --verbose for additional debugging output"
            ],

            // Parser errors
            ErrorCodes.InvalidYamlSyntax => [
                "Check for incorrect indentation (use spaces, not tabs)",
                "Verify quotes are balanced",
                "Ensure list items start with '-'"
            ],
            ErrorCodes.UnsupportedStepType => [
                "Supported step types: run, uses, action",
                "Check the pipeline syntax documentation"
            ],
            ErrorCodes.MissingRequiredField => [
                "Check the documentation for required fields",
                "Verify your pipeline structure"
            ],
            ErrorCodes.CircularDependency => [
                "Review the 'needs' or 'dependsOn' fields in your jobs",
                "Ensure jobs don't form a cycle"
            ],
            ErrorCodes.InvalidPipelineStructure => [
                "Verify your pipeline follows the correct format",
                "Check the documentation for your CI/CD provider"
            ],
            ErrorCodes.UnknownProvider => [
                "Supported providers: GitHub Actions, Azure DevOps",
                "Ensure the pipeline file is in the correct location"
            ],

            // Runner errors
            ErrorCodes.StepExecutionFailed => [
                "Review the error output above",
                "Run with --verbose for more details"
            ],
            ErrorCodes.StepTimeout => [
                "Increase the timeout value",
                "Optimize the step to run faster"
            ],
            ErrorCodes.CommandNotFound => [
                "The tool may not be installed in the container",
                "Consider using a different base image"
            ],
            ErrorCodes.ToolNotFound => [
                "Install the required tool",
                "Use a container image that includes the tool"
            ],
            ErrorCodes.JobExecutionFailed => [
                "Review the failed steps",
                "Check individual step logs for errors"
            ],
            ErrorCodes.UnsupportedExecutor => [
                "Check the supported step executors",
                "Some features may require additional configuration"
            ],

            // File errors
            ErrorCodes.FileNotFound => [
                "Check the file path for typos",
                "Verify the file exists at the specified location",
                "Use absolute paths if relative paths are not working"
            ],
            ErrorCodes.FileAccessDenied => [
                "Check file permissions",
                "Ensure you have read access to the file"
            ],
            ErrorCodes.DirectoryNotFound => [
                "Check the directory path",
                "Create the directory if needed"
            ],
            ErrorCodes.InvalidFilePath => [
                "Verify the path format is correct",
                "Check for invalid characters in the path"
            ],

            // Network errors
            ErrorCodes.NetworkTimeout => [
                "Check your network connection",
                "Try again later"
            ],
            ErrorCodes.ConnectionRefused => [
                "Verify the service is running",
                "Check firewall settings"
            ],
            ErrorCodes.DnsResolutionFailed => [
                "Check your DNS configuration",
                "Verify the hostname is correct"
            ],

            // Config warnings
            ErrorCodes.MissingOptionalConfig => [
                "This is optional and can be ignored",
                "Add the configuration if needed"
            ],
            ErrorCodes.DeprecatedConfig => [
                "Update to the new configuration format",
                "Check the documentation for migration steps"
            ],

            // Unknown
            _ => [
                "Review the error message for details",
                "Run with --verbose for more information"
            ]
        };
    }

    private static IEnumerable<string> GetContextSuggestions(ErrorContext context)
    {
        var suggestions = new List<string>();

        // Add line number hint if available
        if (context.LineNumber.HasValue && !string.IsNullOrEmpty(context.PipelineFile))
        {
            suggestions.Add($"See {System.IO.Path.GetFileName(context.PipelineFile)} line {context.LineNumber}");
        }

        // Add step hint if available
        if (!string.IsNullOrEmpty(context.StepName))
        {
            suggestions.Add($"Check the '{context.StepName}' step configuration");
        }

        // Add job hint if available
        if (!string.IsNullOrEmpty(context.JobName) && string.IsNullOrEmpty(context.StepName))
        {
            suggestions.Add($"Check the '{context.JobName}' job configuration");
        }

        return suggestions;
    }
}
