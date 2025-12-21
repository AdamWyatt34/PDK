namespace PDK.Core.Configuration;

using PDK.Core.ErrorHandling;
using PDK.Core.Models;

/// <summary>
/// Exception thrown when configuration loading, parsing, or validation fails.
/// </summary>
public class ConfigurationException : PdkException
{
    /// <summary>
    /// Gets the path to the configuration file that caused the error.
    /// </summary>
    public string? ConfigFilePath { get; }

    /// <summary>
    /// Gets the validation errors if this exception represents validation failure.
    /// </summary>
    public IReadOnlyList<ValidationError> ValidationErrors { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigurationException"/> class.
    /// </summary>
    /// <param name="errorCode">The error code.</param>
    /// <param name="message">The error message.</param>
    /// <param name="configFilePath">The configuration file path.</param>
    /// <param name="context">The error context.</param>
    /// <param name="suggestions">Recovery suggestions.</param>
    /// <param name="validationErrors">Validation errors if applicable.</param>
    /// <param name="innerException">The inner exception.</param>
    public ConfigurationException(
        string errorCode,
        string message,
        string? configFilePath = null,
        ErrorContext? context = null,
        IEnumerable<string>? suggestions = null,
        IEnumerable<ValidationError>? validationErrors = null,
        Exception? innerException = null)
        : base(errorCode, message, context, suggestions, innerException)
    {
        ConfigFilePath = configFilePath;
        ValidationErrors = validationErrors?.ToList() ?? [];
    }

    /// <summary>
    /// Creates an exception for when a configuration file is not found.
    /// </summary>
    /// <param name="path">The path that was not found.</param>
    /// <returns>A new ConfigurationException.</returns>
    public static ConfigurationException FileNotFound(string path)
    {
        return new ConfigurationException(
            ErrorCodes.ConfigFileNotFound,
            $"Configuration file not found: {path}",
            configFilePath: path,
            context: new ErrorContext { PipelineFile = path },
            suggestions:
            [
                "Verify the file path is correct",
                "Create a configuration file using 'pdk init' or manually",
                "Use --config to specify an alternative configuration file"
            ]);
    }

    /// <summary>
    /// Creates an exception for invalid JSON in a configuration file.
    /// </summary>
    /// <param name="path">The configuration file path.</param>
    /// <param name="innerException">The JSON parsing exception.</param>
    /// <returns>A new ConfigurationException.</returns>
    public static ConfigurationException InvalidJson(string path, Exception innerException)
    {
        var message = innerException.Message;

        return new ConfigurationException(
            ErrorCodes.ConfigInvalidJson,
            $"Invalid JSON in configuration file '{path}': {message}",
            configFilePath: path,
            context: new ErrorContext { PipelineFile = path },
            suggestions:
            [
                "Check the JSON syntax using a JSON validator",
                "Ensure all strings are properly quoted",
                "Verify commas between properties and no trailing commas"
            ],
            innerException: innerException);
    }

    /// <summary>
    /// Creates an exception for configuration validation failures.
    /// </summary>
    /// <param name="path">The configuration file path.</param>
    /// <param name="errors">The validation errors.</param>
    /// <returns>A new ConfigurationException.</returns>
    public static ConfigurationException ValidationFailed(string path, IEnumerable<ValidationError> errors)
    {
        var errorList = errors.ToList();
        var errorMessages = string.Join("; ", errorList.Select(e => $"{e.Path}: {e.Message}"));

        return new ConfigurationException(
            ErrorCodes.ConfigValidationFailed,
            $"Configuration validation failed for '{path}': {errorMessages}",
            configFilePath: path,
            context: new ErrorContext { PipelineFile = path },
            suggestions:
            [
                "Review the configuration file against the schema",
                "Check the documentation for valid configuration values",
                "Run 'pdk config validate' for detailed validation feedback"
            ],
            validationErrors: errorList);
    }

    /// <summary>
    /// Creates an exception for invalid configuration version.
    /// </summary>
    /// <param name="path">The configuration file path.</param>
    /// <param name="version">The invalid version found.</param>
    /// <returns>A new ConfigurationException.</returns>
    public static ConfigurationException InvalidVersion(string path, string? version)
    {
        return new ConfigurationException(
            ErrorCodes.ConfigInvalidVersion,
            $"Invalid configuration version '{version ?? "(missing)"}' in '{path}'. Expected '1.0'.",
            configFilePath: path,
            context: new ErrorContext { PipelineFile = path },
            suggestions:
            [
                "Set the 'version' field to '1.0'",
                "The version field is required and must be '1.0'"
            ]);
    }

    /// <summary>
    /// Creates an exception for invalid variable name.
    /// </summary>
    /// <param name="path">The configuration file path.</param>
    /// <param name="variableName">The invalid variable name.</param>
    /// <returns>A new ConfigurationException.</returns>
    public static ConfigurationException InvalidVariableName(string path, string variableName)
    {
        return new ConfigurationException(
            ErrorCodes.ConfigInvalidVariableName,
            $"Invalid variable name '{variableName}' in '{path}'.",
            configFilePath: path,
            context: new ErrorContext { PipelineFile = path },
            suggestions:
            [
                "Variable names must start with an uppercase letter or underscore",
                "Variable names can only contain uppercase letters, digits, and underscores",
                $"Pattern: ^[A-Z_][A-Z0-9_]*$ (e.g., BUILD_CONFIG, MY_VAR_1)"
            ]);
    }

    /// <summary>
    /// Creates an exception for invalid memory limit format.
    /// </summary>
    /// <param name="path">The configuration file path.</param>
    /// <param name="memoryLimit">The invalid memory limit value.</param>
    /// <returns>A new ConfigurationException.</returns>
    public static ConfigurationException InvalidMemoryLimit(string path, string memoryLimit)
    {
        return new ConfigurationException(
            ErrorCodes.ConfigInvalidMemoryLimit,
            $"Invalid memory limit '{memoryLimit}' in '{path}'.",
            configFilePath: path,
            context: new ErrorContext { PipelineFile = path },
            suggestions:
            [
                "Memory limit must be a number followed by k, m, or g",
                "Examples: '512m', '2g', '1024k'"
            ]);
    }

    /// <summary>
    /// Creates an exception for invalid CPU limit.
    /// </summary>
    /// <param name="path">The configuration file path.</param>
    /// <param name="cpuLimit">The invalid CPU limit value.</param>
    /// <returns>A new ConfigurationException.</returns>
    public static ConfigurationException InvalidCpuLimit(string path, double cpuLimit)
    {
        return new ConfigurationException(
            ErrorCodes.ConfigInvalidCpuLimit,
            $"Invalid CPU limit '{cpuLimit}' in '{path}'. Minimum value is 0.1.",
            configFilePath: path,
            context: new ErrorContext { PipelineFile = path },
            suggestions:
            [
                "CPU limit must be at least 0.1",
                "Examples: '0.5' (half a CPU), '2.0' (two CPUs)"
            ]);
    }

    /// <summary>
    /// Creates an exception for invalid log level.
    /// </summary>
    /// <param name="path">The configuration file path.</param>
    /// <param name="logLevel">The invalid log level value.</param>
    /// <returns>A new ConfigurationException.</returns>
    public static ConfigurationException InvalidLogLevel(string path, string logLevel)
    {
        return new ConfigurationException(
            ErrorCodes.ConfigInvalidLogLevel,
            $"Invalid log level '{logLevel}' in '{path}'.",
            configFilePath: path,
            context: new ErrorContext { PipelineFile = path },
            suggestions:
            [
                "Valid log levels: Info, Debug, Warning, Error",
                "Log level is case-insensitive"
            ]);
    }

    /// <summary>
    /// Creates an exception for invalid retention days.
    /// </summary>
    /// <param name="path">The configuration file path.</param>
    /// <param name="retentionDays">The invalid retention days value.</param>
    /// <returns>A new ConfigurationException.</returns>
    public static ConfigurationException InvalidRetentionDays(string path, int retentionDays)
    {
        return new ConfigurationException(
            ErrorCodes.ConfigInvalidRetentionDays,
            $"Invalid retention days '{retentionDays}' in '{path}'. Value must be 0 or greater.",
            configFilePath: path,
            context: new ErrorContext { PipelineFile = path },
            suggestions:
            [
                "Retention days must be 0 or a positive integer",
                "Use 0 to disable automatic cleanup"
            ]);
    }
}
