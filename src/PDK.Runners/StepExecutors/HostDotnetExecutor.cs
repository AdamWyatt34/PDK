namespace PDK.Runners.StepExecutors;

using System.Text;
using Microsoft.Extensions.Logging;
using PDK.Core.Models;
using PDK.Runners.Models;

/// <summary>
/// Executes .NET CLI commands on the host machine including restore, build, test, publish, and run operations.
/// Handles project path wildcards, configuration settings, and build arguments.
/// </summary>
public class HostDotnetExecutor : IHostStepExecutor
{
    private readonly ILogger<HostDotnetExecutor> _logger;

    private static readonly HashSet<string> SupportedCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "restore",
        "build",
        "test",
        "publish",
        "run",
        "pack",
        "clean"
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="HostDotnetExecutor"/> class.
    /// </summary>
    /// <param name="logger">The logger for diagnostic output.</param>
    public HostDotnetExecutor(ILogger<HostDotnetExecutor> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public string StepType => "dotnet";

    /// <inheritdoc/>
    public async Task<StepExecutionResult> ExecuteAsync(
        Step step,
        HostExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var startTime = DateTimeOffset.Now;

        try
        {
            // 1. Validate dotnet CLI is available
            if (!await context.ProcessExecutor.IsToolAvailableAsync("dotnet", cancellationToken))
            {
                _logger.LogError("dotnet CLI is not available on the host system");
                return CreateFailedResult(
                    step.Name,
                    "dotnet CLI is not installed or not in PATH. Please install .NET SDK: https://dotnet.microsoft.com/download",
                    startTime);
            }

            // 2. Extract and validate command
            if (!step.With.TryGetValue("command", out var command) || string.IsNullOrWhiteSpace(command))
            {
                throw new ArgumentException(
                    $"The 'command' input is required for dotnet step '{step.Name}'. " +
                    $"Supported commands: {string.Join(", ", SupportedCommands)}",
                    nameof(step));
            }

            ValidateCommand(command, step.Name);

            // 3. Extract optional inputs
            step.With.TryGetValue("projects", out var projects);
            step.With.TryGetValue("configuration", out var configuration);
            step.With.TryGetValue("arguments", out var arguments);
            step.With.TryGetValue("outputPath", out var outputPath);

            // 4. Merge environment variables
            var mergedEnvironment = MergeEnvironments(context, step);

            // 5. Resolve working directory
            var workingDirectory = context.ResolvePath(step.WorkingDirectory);

            // Ensure working directory exists
            if (!Directory.Exists(workingDirectory))
            {
                Directory.CreateDirectory(workingDirectory);
            }

            // 6. Expand project paths with wildcards
            var expandedProjects = ExpandProjectPaths(projects, workingDirectory, step.Name);

            // 7. Build dotnet command
            var dotnetCommand = BuildDotnetCommand(
                command,
                expandedProjects,
                configuration,
                outputPath,
                arguments);

            _logger.LogDebug(
                "Executing dotnet command for step '{StepName}': {Command}",
                step.Name, dotnetCommand);

            // 8. Execute dotnet command
            var result = await context.ProcessExecutor.ExecuteAsync(
                dotnetCommand,
                workingDirectory,
                mergedEnvironment,
                cancellationToken: cancellationToken);

            var endTime = DateTimeOffset.Now;

            _logger.LogDebug(
                "dotnet step '{StepName}' completed with exit code {ExitCode}",
                step.Name, result.ExitCode);

            // 9. Return result
            return new StepExecutionResult
            {
                StepName = step.Name,
                Success = result.Success,
                ExitCode = result.ExitCode,
                Output = result.StandardOutput,
                ErrorOutput = result.StandardError,
                Duration = endTime - startTime,
                StartTime = startTime,
                EndTime = endTime
            };
        }
        catch (ArgumentException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "dotnet step '{StepName}' failed with exception", step.Name);

            return CreateFailedResult(
                step.Name,
                $"dotnet step failed: {ex.Message}",
                startTime);
        }
    }

    /// <summary>
    /// Validates that the specified command is supported by the dotnet executor.
    /// </summary>
    private static void ValidateCommand(string command, string stepName)
    {
        if (!SupportedCommands.Contains(command))
        {
            throw new ArgumentException(
                $"Unsupported dotnet command '{command}' in step '{stepName}'. " +
                $"Supported commands: {string.Join(", ", SupportedCommands)}",
                nameof(command));
        }
    }

    /// <summary>
    /// Expands wildcard patterns in project paths to actual file paths.
    /// </summary>
    private static string? ExpandProjectPaths(
        string? projects,
        string workingDirectory,
        string stepName)
    {
        if (string.IsNullOrWhiteSpace(projects))
        {
            return null;
        }

        // Check if projects contains wildcards
        if (!ContainsWildcard(projects))
        {
            return projects.Trim();
        }

        // Expand wildcards using Directory.GetFiles
        var pattern = projects.Trim();
        var searchPath = workingDirectory;
        var searchPattern = pattern;

        // Handle relative paths in pattern
        if (pattern.Contains(Path.DirectorySeparatorChar) || pattern.Contains('/'))
        {
            var lastSep = Math.Max(pattern.LastIndexOf(Path.DirectorySeparatorChar), pattern.LastIndexOf('/'));
            var subPath = pattern.Substring(0, lastSep);
            searchPattern = pattern.Substring(lastSep + 1);
            searchPath = Path.Combine(workingDirectory, subPath);
        }

        if (!Directory.Exists(searchPath))
        {
            throw new ArgumentException(
                $"Directory '{searchPath}' not found for pattern '{projects}' in step '{stepName}'.",
                nameof(projects));
        }

        var matchingFiles = Directory.GetFiles(searchPath, searchPattern, SearchOption.AllDirectories);

        if (matchingFiles.Length == 0)
        {
            throw new ArgumentException(
                $"No project files found matching pattern '{projects}' in step '{stepName}'. " +
                "Please verify the project path or wildcard pattern.",
                nameof(projects));
        }

        // Make paths relative to working directory for cleaner output
        var relativePaths = matchingFiles.Select(f =>
        {
            if (f.StartsWith(workingDirectory))
            {
                return f.Substring(workingDirectory.Length).TrimStart(Path.DirectorySeparatorChar, '/');
            }
            return f;
        });

        return string.Join(" ", relativePaths.Select(p => $"\"{p}\""));
    }

    /// <summary>
    /// Builds the dotnet CLI command string from the provided inputs.
    /// </summary>
    private static string BuildDotnetCommand(
        string command,
        string? projects,
        string? configuration,
        string? outputPath,
        string? arguments)
    {
        var parts = new List<string> { "dotnet", command };

        // Add project/solution paths
        if (!string.IsNullOrWhiteSpace(projects))
        {
            parts.Add(projects);
        }

        // Add configuration flag (for build, test, publish, pack commands)
        if (!string.IsNullOrWhiteSpace(configuration) &&
            IsConfigurationSupported(command))
        {
            parts.Add($"--configuration {configuration}");
        }

        // Add output path flag (for publish and pack commands)
        if (!string.IsNullOrWhiteSpace(outputPath) &&
            (command.Equals("publish", StringComparison.OrdinalIgnoreCase) ||
             command.Equals("pack", StringComparison.OrdinalIgnoreCase)))
        {
            parts.Add($"--output \"{outputPath}\"");
        }

        // Add additional arguments
        if (!string.IsNullOrWhiteSpace(arguments))
        {
            parts.Add(arguments);
        }

        return string.Join(" ", parts);
    }

    /// <summary>
    /// Checks if configuration flag is supported for the command.
    /// </summary>
    private static bool IsConfigurationSupported(string command)
    {
        return command.Equals("build", StringComparison.OrdinalIgnoreCase) ||
               command.Equals("test", StringComparison.OrdinalIgnoreCase) ||
               command.Equals("publish", StringComparison.OrdinalIgnoreCase) ||
               command.Equals("pack", StringComparison.OrdinalIgnoreCase) ||
               command.Equals("run", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks if a path contains wildcard characters.
    /// </summary>
    private static bool ContainsWildcard(string path)
    {
        return path.Contains('*') || path.Contains('?');
    }

    /// <summary>
    /// Merges environment variables from context and step, with step values taking precedence.
    /// </summary>
    private static IDictionary<string, string> MergeEnvironments(
        HostExecutionContext context,
        Step step)
    {
        var merged = new Dictionary<string, string>(context.Environment);

        if (step.Environment != null)
        {
            foreach (var kvp in step.Environment)
            {
                merged[kvp.Key] = kvp.Value;
            }
        }

        return merged;
    }

    /// <summary>
    /// Creates a failed step execution result.
    /// </summary>
    private static StepExecutionResult CreateFailedResult(
        string stepName,
        string errorMessage,
        DateTimeOffset startTime,
        int exitCode = -1)
    {
        var endTime = DateTimeOffset.Now;

        return new StepExecutionResult
        {
            StepName = stepName,
            Success = false,
            ExitCode = exitCode,
            Output = string.Empty,
            ErrorOutput = errorMessage,
            Duration = endTime - startTime,
            StartTime = startTime,
            EndTime = endTime
        };
    }
}
