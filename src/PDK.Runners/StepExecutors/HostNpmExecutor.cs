namespace PDK.Runners.StepExecutors;

using Microsoft.Extensions.Logging;
using PDK.Core.Models;
using PDK.Runners.Models;

/// <summary>
/// Executes npm commands on the host machine including install, ci, build, test, and custom script execution.
/// Validates npm and Node.js availability before execution.
/// </summary>
public class HostNpmExecutor : IHostStepExecutor
{
    private readonly ILogger<HostNpmExecutor> _logger;

    private static readonly HashSet<string> SupportedCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "install",
        "ci",
        "build",
        "test",
        "run",
        "start",
        "publish"
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="HostNpmExecutor"/> class.
    /// </summary>
    /// <param name="logger">The logger for diagnostic output.</param>
    public HostNpmExecutor(ILogger<HostNpmExecutor> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public string StepType => "npm";

    /// <inheritdoc/>
    public async Task<StepExecutionResult> ExecuteAsync(
        Step step,
        HostExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var startTime = DateTimeOffset.Now;

        try
        {
            // 1. Validate npm CLI is available
            if (!await context.ProcessExecutor.IsToolAvailableAsync("npm", cancellationToken))
            {
                _logger.LogError("npm is not available on the host system");
                return CreateFailedResult(
                    step.Name,
                    "npm is not installed or not in PATH. Please install Node.js: https://nodejs.org/",
                    startTime);
            }

            // 2. Extract command with default
            step.With.TryGetValue("command", out var command);
            if (string.IsNullOrWhiteSpace(command))
            {
                command = "install"; // Default command
            }

            // 3. Validate command is supported
            ValidateCommand(command, step.Name);

            // 4. Extract optional inputs
            step.With.TryGetValue("script", out var script);
            step.With.TryGetValue("arguments", out var arguments);

            // 5. Special validation: "run" command requires script
            if (command.Equals("run", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(script))
                {
                    throw new ArgumentException(
                        $"The 'script' input is required when command is 'run' for npm step '{step.Name}'.",
                        nameof(step));
                }
            }

            // 6. Merge environment variables
            var mergedEnvironment = MergeEnvironments(context, step);

            // 7. Resolve working directory
            var workingDirectory = context.ResolvePath(step.WorkingDirectory);

            // Ensure working directory exists
            if (!Directory.Exists(workingDirectory))
            {
                Directory.CreateDirectory(workingDirectory);
            }

            // 8. Build npm command
            var npmCommand = BuildNpmCommand(command, script, arguments);

            _logger.LogDebug(
                "Executing npm command for step '{StepName}': {Command}",
                step.Name, npmCommand);

            // 9. Execute npm command
            var result = await context.ProcessExecutor.ExecuteAsync(
                npmCommand,
                workingDirectory,
                mergedEnvironment,
                cancellationToken: cancellationToken);

            var endTime = DateTimeOffset.Now;

            _logger.LogDebug(
                "npm step '{StepName}' completed with exit code {ExitCode}",
                step.Name, result.ExitCode);

            // 10. Return result
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
            _logger.LogError(ex, "npm step '{StepName}' failed with exception", step.Name);

            return CreateFailedResult(
                step.Name,
                $"npm step failed: {ex.Message}",
                startTime);
        }
    }

    /// <summary>
    /// Validates that the specified command is supported by the npm executor.
    /// </summary>
    private static void ValidateCommand(string command, string stepName)
    {
        if (!SupportedCommands.Contains(command))
        {
            throw new ArgumentException(
                $"Unsupported npm command '{command}' in step '{stepName}'. " +
                $"Supported commands: {string.Join(", ", SupportedCommands)}",
                nameof(command));
        }
    }

    /// <summary>
    /// Builds the npm CLI command string from the provided inputs.
    /// </summary>
    private static string BuildNpmCommand(
        string command,
        string? script,
        string? arguments)
    {
        var parts = new List<string>();

        // Special case: "build" must use "npm run build"
        if (command.Equals("build", StringComparison.OrdinalIgnoreCase))
        {
            parts.Add("npm");
            parts.Add("run");
            parts.Add("build");

            if (!string.IsNullOrWhiteSpace(arguments))
            {
                parts.Add("--");
                parts.Add(arguments);
            }

            return string.Join(" ", parts);
        }

        // Handle "run" with custom script
        if (command.Equals("run", StringComparison.OrdinalIgnoreCase))
        {
            parts.Add("npm");
            parts.Add("run");
            parts.Add(script!);

            if (!string.IsNullOrWhiteSpace(arguments))
            {
                parts.Add("--");
                parts.Add(arguments);
            }

            return string.Join(" ", parts);
        }

        // Handle "start" command
        if (command.Equals("start", StringComparison.OrdinalIgnoreCase))
        {
            parts.Add("npm");
            parts.Add("start");

            if (!string.IsNullOrWhiteSpace(arguments))
            {
                parts.Add("--");
                parts.Add(arguments);
            }

            return string.Join(" ", parts);
        }

        // For all other commands (install, ci, test, publish)
        parts.Add("npm");
        parts.Add(command);

        if (!string.IsNullOrWhiteSpace(arguments))
        {
            parts.Add(arguments);
        }

        return string.Join(" ", parts);
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
