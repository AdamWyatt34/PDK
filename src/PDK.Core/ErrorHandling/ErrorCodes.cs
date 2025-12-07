namespace PDK.Core.ErrorHandling;

/// <summary>
/// Error code constants following format: PDK-{severity}-{component}-{number}
/// Severity: E (error), W (warning), I (info)
/// Component: DOCKER, PARSER, RUNNER, FILE, NET, CONFIG
/// </summary>
public static class ErrorCodes
{
    #region Docker Errors (PDK-E-DOCKER-XXX)

    /// <summary>Docker daemon is not running or not accessible.</summary>
    public const string DockerNotRunning = "PDK-E-DOCKER-001";

    /// <summary>Docker is not installed on the system.</summary>
    public const string DockerNotInstalled = "PDK-E-DOCKER-002";

    /// <summary>Permission denied when accessing Docker daemon.</summary>
    public const string DockerPermissionDenied = "PDK-E-DOCKER-003";

    /// <summary>Docker image could not be found or pulled.</summary>
    public const string DockerImageNotFound = "PDK-E-DOCKER-004";

    /// <summary>Failed to create Docker container.</summary>
    public const string ContainerCreationFailed = "PDK-E-DOCKER-005";

    /// <summary>Container execution failed with an error.</summary>
    public const string ContainerExecutionFailed = "PDK-E-DOCKER-006";

    #endregion

    #region Parser Errors (PDK-E-PARSER-XXX)

    /// <summary>YAML syntax is invalid.</summary>
    public const string InvalidYamlSyntax = "PDK-E-PARSER-001";

    /// <summary>Step type is not supported.</summary>
    public const string UnsupportedStepType = "PDK-E-PARSER-002";

    /// <summary>Required field is missing.</summary>
    public const string MissingRequiredField = "PDK-E-PARSER-003";

    /// <summary>Circular dependency detected in jobs.</summary>
    public const string CircularDependency = "PDK-E-PARSER-004";

    /// <summary>Pipeline structure is invalid.</summary>
    public const string InvalidPipelineStructure = "PDK-E-PARSER-005";

    /// <summary>Unknown or unsupported CI/CD provider.</summary>
    public const string UnknownProvider = "PDK-E-PARSER-006";

    #endregion

    #region Runner Errors (PDK-E-RUNNER-XXX)

    /// <summary>Step execution failed.</summary>
    public const string StepExecutionFailed = "PDK-E-RUNNER-001";

    /// <summary>Step execution timed out.</summary>
    public const string StepTimeout = "PDK-E-RUNNER-002";

    /// <summary>Command not found in execution environment.</summary>
    public const string CommandNotFound = "PDK-E-RUNNER-003";

    /// <summary>Required tool is not available.</summary>
    public const string ToolNotFound = "PDK-E-RUNNER-004";

    /// <summary>Job execution failed.</summary>
    public const string JobExecutionFailed = "PDK-E-RUNNER-005";

    /// <summary>Step executor not supported.</summary>
    public const string UnsupportedExecutor = "PDK-E-RUNNER-006";

    #endregion

    #region File Errors (PDK-E-FILE-XXX)

    /// <summary>Specified file was not found.</summary>
    public const string FileNotFound = "PDK-E-FILE-001";

    /// <summary>Access to file was denied.</summary>
    public const string FileAccessDenied = "PDK-E-FILE-002";

    /// <summary>Specified directory was not found.</summary>
    public const string DirectoryNotFound = "PDK-E-FILE-003";

    /// <summary>File path is invalid.</summary>
    public const string InvalidFilePath = "PDK-E-FILE-004";

    #endregion

    #region Network Errors (PDK-E-NET-XXX)

    /// <summary>Network operation timed out.</summary>
    public const string NetworkTimeout = "PDK-E-NET-001";

    /// <summary>Connection was refused.</summary>
    public const string ConnectionRefused = "PDK-E-NET-002";

    /// <summary>DNS resolution failed.</summary>
    public const string DnsResolutionFailed = "PDK-E-NET-003";

    #endregion

    #region Config Warnings (PDK-W-CONFIG-XXX)

    /// <summary>Optional configuration is missing.</summary>
    public const string MissingOptionalConfig = "PDK-W-CONFIG-001";

    /// <summary>Configuration option is deprecated.</summary>
    public const string DeprecatedConfig = "PDK-W-CONFIG-002";

    #endregion

    #region Unknown/Generic

    /// <summary>Unknown or unclassified error.</summary>
    public const string Unknown = "PDK-E-UNKNOWN-001";

    #endregion

    /// <summary>
    /// Gets a human-readable description for an error code.
    /// </summary>
    /// <param name="errorCode">The error code.</param>
    /// <returns>A description of the error.</returns>
    public static string GetDescription(string errorCode)
    {
        return errorCode switch
        {
            // Docker errors
            DockerNotRunning => "Docker daemon is not running or not accessible",
            DockerNotInstalled => "Docker is not installed on the system",
            DockerPermissionDenied => "Permission denied when accessing Docker daemon",
            DockerImageNotFound => "Docker image could not be found or pulled",
            ContainerCreationFailed => "Failed to create Docker container",
            ContainerExecutionFailed => "Container execution failed with an error",

            // Parser errors
            InvalidYamlSyntax => "YAML syntax is invalid",
            UnsupportedStepType => "Step type is not supported",
            MissingRequiredField => "Required field is missing",
            CircularDependency => "Circular dependency detected in jobs",
            InvalidPipelineStructure => "Pipeline structure is invalid",
            UnknownProvider => "Unknown or unsupported CI/CD provider",

            // Runner errors
            StepExecutionFailed => "Step execution failed",
            StepTimeout => "Step execution timed out",
            CommandNotFound => "Command not found in execution environment",
            ToolNotFound => "Required tool is not available",
            JobExecutionFailed => "Job execution failed",
            UnsupportedExecutor => "Step executor not supported",

            // File errors
            FileNotFound => "Specified file was not found",
            FileAccessDenied => "Access to file was denied",
            DirectoryNotFound => "Specified directory was not found",
            InvalidFilePath => "File path is invalid",

            // Network errors
            NetworkTimeout => "Network operation timed out",
            ConnectionRefused => "Connection was refused",
            DnsResolutionFailed => "DNS resolution failed",

            // Config warnings
            MissingOptionalConfig => "Optional configuration is missing",
            DeprecatedConfig => "Configuration option is deprecated",

            // Unknown
            Unknown => "An unknown error occurred",

            _ => $"Unknown error code: {errorCode}"
        };
    }

    /// <summary>
    /// Gets the documentation URL for an error code.
    /// </summary>
    /// <param name="errorCode">The error code.</param>
    /// <returns>A URL to the error documentation.</returns>
    public static string GetDocumentationUrl(string errorCode)
    {
        // Extract the code portion for the URL
        var normalizedCode = errorCode.Replace("-", "_").ToLowerInvariant();
        return $"https://docs.pdk.dev/errors/{normalizedCode}";
    }

    /// <summary>
    /// Gets all defined error codes.
    /// </summary>
    /// <returns>An enumerable of all error code constants.</returns>
    public static IEnumerable<string> GetAllCodes()
    {
        yield return DockerNotRunning;
        yield return DockerNotInstalled;
        yield return DockerPermissionDenied;
        yield return DockerImageNotFound;
        yield return ContainerCreationFailed;
        yield return ContainerExecutionFailed;

        yield return InvalidYamlSyntax;
        yield return UnsupportedStepType;
        yield return MissingRequiredField;
        yield return CircularDependency;
        yield return InvalidPipelineStructure;
        yield return UnknownProvider;

        yield return StepExecutionFailed;
        yield return StepTimeout;
        yield return CommandNotFound;
        yield return ToolNotFound;
        yield return JobExecutionFailed;
        yield return UnsupportedExecutor;

        yield return FileNotFound;
        yield return FileAccessDenied;
        yield return DirectoryNotFound;
        yield return InvalidFilePath;

        yield return NetworkTimeout;
        yield return ConnectionRefused;
        yield return DnsResolutionFailed;

        yield return MissingOptionalConfig;
        yield return DeprecatedConfig;

        yield return Unknown;
    }

    /// <summary>
    /// Determines if an error code indicates an error (E) or warning (W).
    /// </summary>
    /// <param name="errorCode">The error code.</param>
    /// <returns>True if this is a warning, false if an error.</returns>
    public static bool IsWarning(string errorCode)
    {
        return errorCode.Contains("-W-");
    }

    /// <summary>
    /// Extracts the component from an error code.
    /// </summary>
    /// <param name="errorCode">The error code.</param>
    /// <returns>The component name (e.g., "DOCKER", "PARSER").</returns>
    public static string GetComponent(string errorCode)
    {
        var parts = errorCode.Split('-');
        return parts.Length >= 3 ? parts[2] : "UNKNOWN";
    }
}
