namespace PDK.CLI.ErrorHandling;

using PDK.Core.ErrorHandling;
using PDK.Core.Models;

/// <summary>
/// Generates contextual suggestions for resolving errors.
/// </summary>
public sealed class ErrorSuggestionEngine
{
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
                "Exit code 137: process killed by SIGKILL (often out of memory)",
                "Increase available memory for Docker",
                "Optimize your process to use less memory"
            ],
            143 => [
                "Exit code 143: process terminated (SIGTERM)",
                "The step may have exceeded the timeout"
            ],
            _ when IsSignalExitCode(exitCode) => [
                $"Exit code {exitCode}: process killed by signal {exitCode - 128}{DescribeSignal(exitCode - 128)}",
                "Check system resources and logs"
            ],
            _ => [
                $"Step failed with exit code {exitCode}",
                "Review the error output for details"
            ]
        };
    }

    /// <summary>
    /// Determines whether an exit code denotes "killed by signal" (128 + signal number).
    /// Only the conventional range 129-159 is interpreted that way.
    /// </summary>
    public static bool IsSignalExitCode(int exitCode) => exitCode > 128 && exitCode < 160;

    /// <summary>
    /// Gets the documentation reference for an error code (<c>docs/errors.md#code</c>).
    /// </summary>
    /// <param name="errorCode">The error code.</param>
    /// <returns>The documentation reference.</returns>
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
            ErrorCodes.DockerUnavailable => "pdk doctor",
            ErrorCodes.DockerImageNotFound when exception.Context.ImageName != null =>
                $"docker pull {exception.Context.ImageName}",
            ErrorCodes.ContainerExecutionFailed when exception.Context.ContainerId != null =>
                $"docker logs {exception.Context.ContainerId}",
            ErrorCodes.InvalidYamlSyntax when exception.Context.PipelineFile != null =>
                $"pdk validate --file \"{exception.Context.PipelineFile}\"",
            ErrorCodes.ConfigInvalidJson or ErrorCodes.ConfigValidationFailed or ErrorCodes.ConfigInvalidVersion
                when exception.Context.PipelineFile != null =>
                $"pdk config validate \"{exception.Context.PipelineFile}\"",
            ErrorCodes.SecretNotFound => "pdk secret list",
            ErrorCodes.FileNotFound => "ls -la",
            _ => null
        };
    }

    private static string DescribeSignal(int signal)
    {
        var name = signal switch
        {
            1 => "SIGHUP",
            2 => "SIGINT",
            3 => "SIGQUIT",
            4 => "SIGILL",
            5 => "SIGTRAP",
            6 => "SIGABRT",
            7 => "SIGBUS",
            8 => "SIGFPE",
            9 => "SIGKILL",
            10 => "SIGUSR1",
            11 => "SIGSEGV",
            12 => "SIGUSR2",
            13 => "SIGPIPE",
            14 => "SIGALRM",
            15 => "SIGTERM",
            _ => null
        };

        return name == null ? string.Empty : $" ({name})";
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
                "Log out and log back in for the group change to take effect",
                "Try running with --host mode"
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
            ErrorCodes.MissingDependency => [
                "Check the spelling of the job or step referenced in 'needs' / 'dependsOn'",
                "Run 'pdk list' to see the jobs defined in the pipeline"
            ],
            ErrorCodes.SelfDependency => [
                "Remove the self-reference from 'needs' / 'dependsOn'"
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
            ErrorCodes.DockerUnavailable => [
                "Start Docker, or run without it: pdk run --host",
                "Run 'pdk doctor' to diagnose the Docker installation",
                "Set runner.fallback to 'host' in pdk.config.json to fall back automatically"
            ],
            ErrorCodes.RunnerCapabilityMismatch => [
                "Run with Docker: pdk run --docker (custom images and Docker steps need Docker)",
                "Remove the Docker-only features from the job, or pick a runner label such as ubuntu-latest",
                "Run 'pdk doctor' to check whether Docker is available"
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

            // Configuration errors
            ErrorCodes.ConfigFileNotFound => [
                "Check the path passed to --config",
                "PDK discovers .pdkrc or pdk.config.json in the current directory, then ~/.pdkrc and ~/.pdk/config.json"
            ],
            ErrorCodes.ConfigInvalidJson => [
                "Fix the JSON syntax (quotes, commas, brackets); comments and trailing commas are tolerated",
                "Validate the file with 'pdk config validate'"
            ],
            ErrorCodes.ConfigValidationFailed => [
                "Fix the listed fields; see docs/configuration.md for the schema",
                "Validate the file with 'pdk config validate'"
            ],
            ErrorCodes.ConfigInvalidVersion => [
                "Set \"version\": \"1.0\" at the top level of the configuration file"
            ],
            ErrorCodes.ConfigInvalidVariableName => [
                "Variable names must match ^[A-Z_][A-Z0-9_]*$ (e.g. BUILD_CONFIG)"
            ],
            ErrorCodes.ConfigInvalidMemoryLimit => [
                "Use a number followed by k, m or g (e.g. '512m', '2g')"
            ],
            ErrorCodes.ConfigInvalidCpuLimit => [
                "CPU limit must be at least 0.1 (e.g. 0.5, 2.0)"
            ],
            ErrorCodes.ConfigInvalidLogLevel => [
                "Valid log levels: Trace, Debug, Information (Info), Warning (Warn), Error, Critical"
            ],
            ErrorCodes.ConfigInvalidRetentionDays => [
                "artifacts.retentionDays must be 0 or greater"
            ],

            // Variable errors
            ErrorCodes.VariableCircularReference => [
                "Break the cycle: a variable must not reference itself directly or through other variables",
                "Run with --dry-run to see the resolved variables"
            ],
            ErrorCodes.VariableRecursionLimit => [
                "Simplify nested variable references; the expansion depth limit was exceeded",
                "Check for variables that reference each other in a loop"
            ],
            ErrorCodes.VariableRequired => [
                "Define the variable with --var NAME=value, in the configuration file, or as PDK_VAR_NAME",
                "Use ${NAME:-default} to provide a default value"
            ],
            ErrorCodes.VariableInvalidSyntax => [
                "Use ${NAME}, ${NAME:-default} or ${NAME:?message}; names match [A-Za-z_][A-Za-z0-9_]*",
                "Escape a literal reference with \\${NAME}"
            ],
            ErrorCodes.VariableFileNotFound => [
                "Check the path passed to --var-file",
                "The file must contain a JSON object of NAME: value pairs"
            ],

            // Secret errors
            ErrorCodes.SecretEncryptionFailed => [
                "Retry the operation; if it keeps failing, delete ~/.pdk/secrets.json and ~/.pdk/secret.key and store the secrets again",
                "Check that the current user can write to the secret store"
            ],
            ErrorCodes.SecretDecryptionFailed => [
                "The secret store may have been created on another machine or user account; secrets are bound to both",
                "Set the secret again: pdk secret set NAME"
            ],
            ErrorCodes.SecretNotFound => [
                "List stored secrets: pdk secret list",
                "Set the secret: pdk secret set NAME, or pass it with --secret NAME=value / PDK_SECRET_NAME"
            ],
            ErrorCodes.SecretStorageFailed => [
                "Check permissions on ~/.pdk/secrets.json and ~/.pdk/secret.key (they must be writable by you only)",
                "Retry the operation"
            ],
            ErrorCodes.SecretInvalidName => [
                "Secret names must match ^[A-Z_][A-Z0-9_]*$ (e.g. API_TOKEN)"
            ],

            // Artifact errors
            ErrorCodes.ArtifactInvalidName => [
                "Artifact names cannot be empty, longer than 256 characters, or contain \" : < > | * ? \\ / or line breaks"
            ],
            ErrorCodes.ArtifactNoFilesMatched => [
                "Check the path/glob pattern of the artifact step; paths are relative to the workspace",
                "Make sure the files are produced by an earlier step"
            ],
            ErrorCodes.ArtifactAlreadyExists => [
                "Use a different artifact name, or remove the existing artifact from the artifact store"
            ],
            ErrorCodes.ArtifactNotFound => [
                "Upload the artifact in an earlier job/step before downloading it",
                "Check the artifact name for typos"
            ],
            ErrorCodes.ArtifactPermissionDenied => [
                "Check permissions on the artifact store (artifacts.basePath, default .pdk/artifacts)"
            ],
            ErrorCodes.ArtifactDiskSpaceLow => [
                "Free disk space, or point artifacts.basePath at a volume with more room"
            ],
            ErrorCodes.ArtifactCorruptMetadata => [
                "Delete the artifact directory and upload the artifact again"
            ],
            ErrorCodes.ArtifactCompressionFailed => [
                "Check disk space and that the files to compress are readable"
            ],
            ErrorCodes.ArtifactDecompressionFailed => [
                "The artifact archive may be corrupt; upload it again"
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
