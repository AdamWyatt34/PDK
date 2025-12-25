namespace PDK.Runners.StepExecutors;

using System.Text;
using Microsoft.Extensions.Logging;
using PDK.Core.Models;
using PDK.Runners.Models;

/// <summary>
/// Executes checkout steps on the host machine using native git commands.
/// Handles git operations including clone, pull, and branch/tag checkout.
/// </summary>
public class HostCheckoutExecutor : IHostStepExecutor
{
    private readonly ILogger<HostCheckoutExecutor> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="HostCheckoutExecutor"/> class.
    /// </summary>
    /// <param name="logger">The logger for diagnostic output.</param>
    public HostCheckoutExecutor(ILogger<HostCheckoutExecutor> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public string StepType => "checkout";

    /// <inheritdoc/>
    public async Task<StepExecutionResult> ExecuteAsync(
        Step step,
        HostExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var startTime = DateTimeOffset.Now;
        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();
        var lastExitCode = 0;

        try
        {
            // Validate git is available on the host
            if (!await context.ProcessExecutor.IsToolAvailableAsync("git", cancellationToken))
            {
                _logger.LogError("Git is not available on the host system");
                return CreateFailedResult(
                    step.Name,
                    "Git is not installed or not in PATH. Please install git: https://git-scm.com/",
                    startTime);
            }

            // Resolve repository URL (null means "self" checkout - use current workspace)
            var repositoryUrl = GetRepositoryUrl(step);

            // Resolve optional ref/branch/tag
            var checkoutRef = GetCheckoutRef(step);

            _logger.LogDebug(
                "Checkout step '{StepName}': repository={Repository}, ref={Ref}",
                step.Name, repositoryUrl ?? "(self)", checkoutRef ?? "(default)");

            // Check if repository already exists in workspace
            var repoExists = await CheckRepositoryExistsAsync(context, cancellationToken);

            if (repositoryUrl == null)
            {
                // Self checkout - workspace should already have the code
                outputBuilder.AppendLine("Using local workspace (self checkout)");

                if (repoExists)
                {
                    outputBuilder.AppendLine("Workspace contains git repository - using as-is");
                    _logger.LogDebug("Self checkout: existing git repository found");
                }
                else
                {
                    outputBuilder.AppendLine("Workspace ready (no git repository detected)");
                    _logger.LogDebug("Self checkout: no git repository, using workspace as-is");
                }
            }
            else if (repoExists)
            {
                // Repository exists - pull latest changes
                _logger.LogInformation("Pulling latest changes for {Repository}", repositoryUrl);

                var pullResult = await ExecuteGitCommandAsync(
                    "git pull",
                    context,
                    cancellationToken);

                outputBuilder.AppendLine(pullResult.StandardOutput);
                if (!string.IsNullOrWhiteSpace(pullResult.StandardError))
                {
                    errorBuilder.AppendLine(pullResult.StandardError);
                }
                lastExitCode = pullResult.ExitCode;

                if (!pullResult.Success)
                {
                    return CreateFailedResult(
                        step.Name,
                        $"Failed to pull latest changes. Exit code: {pullResult.ExitCode}\n{pullResult.StandardError}",
                        startTime,
                        lastExitCode);
                }
            }
            else
            {
                // Repository doesn't exist - clone it
                _logger.LogInformation("Cloning repository {Repository} to {Workspace}",
                    repositoryUrl, context.WorkspacePath);

                // Ensure workspace directory exists
                if (!Directory.Exists(context.WorkspacePath))
                {
                    Directory.CreateDirectory(context.WorkspacePath);
                }

                var cloneCommand = $"git clone {repositoryUrl} .";
                var cloneResult = await ExecuteGitCommandAsync(
                    cloneCommand,
                    context,
                    cancellationToken);

                outputBuilder.AppendLine(cloneResult.StandardOutput);
                if (!string.IsNullOrWhiteSpace(cloneResult.StandardError))
                {
                    // Git clone often outputs to stderr even on success
                    if (cloneResult.Success)
                    {
                        outputBuilder.AppendLine(cloneResult.StandardError);
                    }
                    else
                    {
                        errorBuilder.AppendLine(cloneResult.StandardError);
                    }
                }
                lastExitCode = cloneResult.ExitCode;

                if (!cloneResult.Success)
                {
                    var errorMessage = $"Failed to clone repository. Exit code: {cloneResult.ExitCode}";
                    if (!string.IsNullOrWhiteSpace(cloneResult.StandardError))
                    {
                        errorMessage += $"\nGit error: {cloneResult.StandardError}";
                    }

                    return CreateFailedResult(step.Name, errorMessage, startTime, lastExitCode);
                }

                outputBuilder.AppendLine($"Successfully cloned {repositoryUrl}");
            }

            // Checkout specific ref/branch/tag if specified (only for explicit repos)
            if (!string.IsNullOrWhiteSpace(checkoutRef) && repositoryUrl != null)
            {
                _logger.LogInformation("Checking out ref: {Ref}", checkoutRef);

                var checkoutCommand = $"git checkout {checkoutRef}";
                var checkoutResult = await ExecuteGitCommandAsync(
                    checkoutCommand,
                    context,
                    cancellationToken);

                outputBuilder.AppendLine(checkoutResult.StandardOutput);
                if (!string.IsNullOrWhiteSpace(checkoutResult.StandardError))
                {
                    if (checkoutResult.Success)
                    {
                        outputBuilder.AppendLine(checkoutResult.StandardError);
                    }
                    else
                    {
                        errorBuilder.AppendLine(checkoutResult.StandardError);
                    }
                }
                lastExitCode = checkoutResult.ExitCode;

                if (!checkoutResult.Success)
                {
                    var errorMessage = $"Failed to checkout ref '{checkoutRef}'. Exit code: {checkoutResult.ExitCode}";
                    if (!string.IsNullOrWhiteSpace(checkoutResult.StandardError))
                    {
                        errorMessage += $"\nGit error: {checkoutResult.StandardError}";
                    }

                    return CreateFailedResult(step.Name, errorMessage, startTime, lastExitCode);
                }

                outputBuilder.AppendLine($"Checked out {checkoutRef}");
            }

            var endTime = DateTimeOffset.Now;

            _logger.LogDebug("Checkout step '{StepName}' completed successfully", step.Name);

            return new StepExecutionResult
            {
                StepName = step.Name,
                Success = true,
                ExitCode = lastExitCode,
                Output = outputBuilder.ToString(),
                ErrorOutput = string.Empty,
                Duration = endTime - startTime,
                StartTime = startTime,
                EndTime = endTime
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Checkout step '{StepName}' failed with exception", step.Name);

            return CreateFailedResult(
                step.Name,
                $"Checkout failed: {ex.Message}",
                startTime,
                lastExitCode);
        }
    }

    /// <summary>
    /// Extracts the repository URL from the step configuration.
    /// Returns null for "self" checkout (use current workspace).
    /// </summary>
    private static string? GetRepositoryUrl(Step step)
    {
        if (step.With.TryGetValue("repository", out var repository))
        {
            if (string.IsNullOrWhiteSpace(repository))
            {
                return null;
            }

            if (string.Equals(repository, "self", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return repository;
        }

        return null;
    }

    /// <summary>
    /// Extracts the optional ref/branch/tag to checkout from the step configuration.
    /// </summary>
    private static string? GetCheckoutRef(Step step)
    {
        if (step.With.TryGetValue("ref", out var refValue) && !string.IsNullOrWhiteSpace(refValue))
        {
            return refValue;
        }

        if (step.With.TryGetValue("branch", out var branch) && !string.IsNullOrWhiteSpace(branch))
        {
            return branch;
        }

        if (step.With.TryGetValue("tag", out var tag) && !string.IsNullOrWhiteSpace(tag))
        {
            return tag;
        }

        return null;
    }

    /// <summary>
    /// Checks if a git repository exists in the workspace.
    /// </summary>
    private async Task<bool> CheckRepositoryExistsAsync(
        HostExecutionContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            // Check if .git directory exists (fast check)
            var gitDir = Path.Combine(context.WorkspacePath, ".git");
            if (Directory.Exists(gitDir))
            {
                return true;
            }

            // Also try git command for bare repos or worktrees
            var result = await context.ProcessExecutor.ExecuteAsync(
                "git rev-parse --git-dir",
                context.WorkspacePath,
                timeout: TimeSpan.FromSeconds(10),
                cancellationToken: cancellationToken);

            return result.Success;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Executes a git command on the host.
    /// </summary>
    private async Task<ExecutionResult> ExecuteGitCommandAsync(
        string command,
        HostExecutionContext context,
        CancellationToken cancellationToken)
    {
        return await context.ProcessExecutor.ExecuteAsync(
            command,
            context.WorkspacePath,
            context.Environment as IDictionary<string, string>,
            cancellationToken: cancellationToken);
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
