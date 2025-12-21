namespace PDK.Core.Configuration;

using System.Text.RegularExpressions;

/// <summary>
/// Validates PDK configuration against the schema rules.
/// </summary>
public partial class ConfigurationValidator
{
    /// <summary>
    /// Pattern for valid variable names: starts with uppercase letter or underscore,
    /// followed by uppercase letters, digits, or underscores.
    /// </summary>
    private static readonly Regex VariableNamePattern = VariableNameRegex();

    /// <summary>
    /// Pattern for valid memory limit: number followed by k, m, or g (case-insensitive).
    /// </summary>
    private static readonly Regex MemoryLimitPattern = MemoryLimitRegex();

    /// <summary>
    /// Valid log levels (case-insensitive).
    /// </summary>
    private static readonly HashSet<string> ValidLogLevels = new(StringComparer.OrdinalIgnoreCase)
    {
        "Info", "Debug", "Warning", "Error"
    };

    /// <summary>
    /// The minimum allowed CPU limit.
    /// </summary>
    private const double MinCpuLimit = 0.1;

    /// <summary>
    /// Validates a configuration object against the schema rules.
    /// </summary>
    /// <param name="config">The configuration to validate.</param>
    /// <returns>A validation result with any errors found.</returns>
    public ValidationResult Validate(PdkConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var errors = new List<ValidationError>();

        // Validate version (required, must be "1.0")
        ValidateVersion(config.Version, errors);

        // Validate variable names
        ValidateVariables(config.Variables, errors);

        // Validate secret names (same rules as variables)
        ValidateSecrets(config.Secrets, errors);

        // Validate Docker configuration
        ValidateDockerConfig(config.Docker, errors);

        // Validate artifacts configuration
        ValidateArtifactsConfig(config.Artifacts, errors);

        // Validate logging configuration
        ValidateLoggingConfig(config.Logging, errors);

        return errors.Count == 0
            ? ValidationResult.Success()
            : ValidationResult.Failure(errors);
    }

    private static void ValidateVersion(string? version, List<ValidationError> errors)
    {
        if (string.IsNullOrEmpty(version))
        {
            errors.Add(new ValidationError
            {
                Path = "version",
                Message = "Version is required and must be '1.0'"
            });
        }
        else if (version != "1.0")
        {
            errors.Add(new ValidationError
            {
                Path = "version",
                Message = $"Invalid version '{version}'. Must be '1.0'"
            });
        }
    }

    private static void ValidateVariables(Dictionary<string, string> variables, List<ValidationError> errors)
    {
        if (variables == null) return;

        foreach (var (name, _) in variables)
        {
            if (!VariableNamePattern.IsMatch(name))
            {
                errors.Add(new ValidationError
                {
                    Path = $"variables.{name}",
                    Message = $"Invalid variable name '{name}'. Must match pattern ^[A-Z_][A-Z0-9_]*$ (uppercase letters, digits, and underscores only, starting with letter or underscore)"
                });
            }
        }
    }

    private static void ValidateSecrets(Dictionary<string, string> secrets, List<ValidationError> errors)
    {
        if (secrets == null) return;

        foreach (var (name, _) in secrets)
        {
            if (!VariableNamePattern.IsMatch(name))
            {
                errors.Add(new ValidationError
                {
                    Path = $"secrets.{name}",
                    Message = $"Invalid secret name '{name}'. Must match pattern ^[A-Z_][A-Z0-9_]*$ (uppercase letters, digits, and underscores only, starting with letter or underscore)"
                });
            }
        }
    }

    private static void ValidateDockerConfig(DockerConfig? docker, List<ValidationError> errors)
    {
        if (docker == null) return;

        // Validate memory limit format
        if (!string.IsNullOrEmpty(docker.MemoryLimit) && !MemoryLimitPattern.IsMatch(docker.MemoryLimit))
        {
            errors.Add(new ValidationError
            {
                Path = "docker.memoryLimit",
                Message = $"Invalid memory limit '{docker.MemoryLimit}'. Must be a number followed by k, m, or g (e.g., '512m', '2g')"
            });
        }

        // Validate CPU limit
        if (docker.CpuLimit.HasValue && docker.CpuLimit.Value < MinCpuLimit)
        {
            errors.Add(new ValidationError
            {
                Path = "docker.cpuLimit",
                Message = $"Invalid CPU limit '{docker.CpuLimit}'. Must be at least {MinCpuLimit}"
            });
        }
    }

    private static void ValidateArtifactsConfig(ArtifactsConfig? artifacts, List<ValidationError> errors)
    {
        if (artifacts == null) return;

        // Validate retention days
        if (artifacts.RetentionDays.HasValue && artifacts.RetentionDays.Value < 0)
        {
            errors.Add(new ValidationError
            {
                Path = "artifacts.retentionDays",
                Message = $"Invalid retention days '{artifacts.RetentionDays}'. Must be 0 or greater"
            });
        }
    }

    private static void ValidateLoggingConfig(LoggingConfig? logging, List<ValidationError> errors)
    {
        if (logging == null) return;

        // Validate log level
        if (!string.IsNullOrEmpty(logging.Level) && !ValidLogLevels.Contains(logging.Level))
        {
            errors.Add(new ValidationError
            {
                Path = "logging.level",
                Message = $"Invalid log level '{logging.Level}'. Valid values: Info, Debug, Warning, Error"
            });
        }

        // Validate max size
        if (logging.MaxSizeMb.HasValue && logging.MaxSizeMb.Value <= 0)
        {
            errors.Add(new ValidationError
            {
                Path = "logging.maxSizeMb",
                Message = $"Invalid max size '{logging.MaxSizeMb}'. Must be greater than 0"
            });
        }
    }

    [GeneratedRegex(@"^[A-Z_][A-Z0-9_]*$", RegexOptions.Compiled)]
    private static partial Regex VariableNameRegex();

    [GeneratedRegex(@"^[0-9]+(k|m|g)$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex MemoryLimitRegex();
}
