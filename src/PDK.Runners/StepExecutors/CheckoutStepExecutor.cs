namespace PDK.Runners.StepExecutors;

using PDK.Core.Models;
using PDK.Runners.Models;

/// <summary>
/// Executes checkout steps to clone or pull git repositories.
/// Handles git operations including clone, pull, and branch/tag checkout.
/// </summary>
public class CheckoutStepExecutor : IStepExecutor
{
    /// <inheritdoc/>
    public string StepType => "checkout";

    /// <inheritdoc/>
    public async Task<StepExecutionResult> ExecuteAsync(
        Step step,
        ExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        // Resolve repository URL (null means "self" checkout - use current workspace)
        var repositoryUrl = GetRepositoryUrl(step);

        // Resolve optional ref/branch/tag
        var checkoutRef = GetCheckoutRef(step);

        var startTime = DateTimeOffset.Now;
        var outputBuilder = new System.Text.StringBuilder();
        var errorBuilder = new System.Text.StringBuilder();
        var lastExitCode = 0;

        try
        {
            // Check if repository already exists in workspace
            var repoExists = await CheckRepositoryExistsAsync(context, cancellationToken);

            if (repositoryUrl == null)
            {
                // Self checkout - workspace should already have the code mounted
                // This is the common case for local development
                outputBuilder.AppendLine("Using local workspace (self checkout)");

                if (repoExists)
                {
                    // Optionally pull latest if we're in a git repo
                    // Skip pull for local development - user's workspace is authoritative
                    outputBuilder.AppendLine("Workspace contains git repository - using as-is");
                }
                else
                {
                    // No git repo, but that's OK for self checkout
                    // The workspace files are mounted and ready to use
                    outputBuilder.AppendLine("Workspace ready (no git repository detected)");
                }
            }
            else if (repoExists)
            {
                // Repository exists - pull latest changes
                var pullResult = await ExecuteGitCommandAsync(
                    "git pull",
                    context,
                    cancellationToken);

                outputBuilder.AppendLine(pullResult.StandardOutput);
                errorBuilder.AppendLine(pullResult.StandardError);
                lastExitCode = pullResult.ExitCode;

                if (!pullResult.Success)
                {
                    throw new ContainerException(
                        $"Failed to pull latest changes for step '{step.Name}'. Exit code: {pullResult.ExitCode}")
                    {
                        ContainerId = context.ContainerId,
                        Command = "git pull"
                    };
                }
            }
            else
            {
                // Repository doesn't exist - clone it
                var cloneCommand = $"git clone {repositoryUrl} {context.ContainerWorkspacePath}";
                var cloneResult = await ExecuteGitCommandAsync(
                    cloneCommand,
                    context,
                    cancellationToken);

                outputBuilder.AppendLine(cloneResult.StandardOutput);
                errorBuilder.AppendLine(cloneResult.StandardError);
                lastExitCode = cloneResult.ExitCode;

                if (!cloneResult.Success)
                {
                    var errorMessage = $"Failed to clone repository for step '{step.Name}'. Exit code: {cloneResult.ExitCode}";
                    if (!string.IsNullOrWhiteSpace(cloneResult.StandardError))
                    {
                        errorMessage += $"\nGit error: {cloneResult.StandardError}";
                    }

                    throw new ContainerException(errorMessage)
                    {
                        ContainerId = context.ContainerId,
                        Command = cloneCommand
                    };
                }
            }

            // Checkout specific ref/branch/tag if specified (only for explicit repos)
            if (!string.IsNullOrWhiteSpace(checkoutRef) && repositoryUrl != null)
            {
                var checkoutCommand = $"git checkout {checkoutRef}";
                var checkoutResult = await ExecuteGitCommandAsync(
                    checkoutCommand,
                    context,
                    cancellationToken);

                outputBuilder.AppendLine(checkoutResult.StandardOutput);
                errorBuilder.AppendLine(checkoutResult.StandardError);
                lastExitCode = checkoutResult.ExitCode;

                if (!checkoutResult.Success)
                {
                    var errorMessage = $"Failed to checkout ref '{checkoutRef}' for step '{step.Name}'. Exit code: {checkoutResult.ExitCode}";
                    if (!string.IsNullOrWhiteSpace(checkoutResult.StandardError))
                    {
                        errorMessage += $"\nGit error: {checkoutResult.StandardError}";
                    }

                    throw new ContainerException(errorMessage)
                    {
                        ContainerId = context.ContainerId,
                        Command = checkoutCommand
                    };
                }
            }

            var endTime = DateTimeOffset.Now;

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
        catch (ContainerException)
        {
            // Re-throw container exceptions as-is
            throw;
        }
        catch (Exception ex)
        {
            var endTime = DateTimeOffset.Now;

            // Return failed result for other exceptions
            return new StepExecutionResult
            {
                StepName = step.Name,
                Success = false,
                ExitCode = lastExitCode,
                Output = outputBuilder.ToString(),
                ErrorOutput = ex.Message,
                Duration = endTime - startTime,
                StartTime = startTime,
                EndTime = endTime
            };
        }
    }

    /// <summary>
    /// Extracts the repository URL from the step configuration.
    /// Returns null for "self" checkout (use current workspace).
    /// </summary>
    /// <param name="step">The step containing checkout configuration.</param>
    /// <returns>The repository URL to clone, or null for self/local checkout.</returns>
    private static string? GetRepositoryUrl(Step step)
    {
        // Try to get repository from With dictionary
        if (step.With.TryGetValue("repository", out var repository))
        {
            if (string.IsNullOrWhiteSpace(repository))
            {
                // Empty repository means use current workspace (self)
                return null;
            }

            // Handle special value "self" (Azure DevOps style)
            if (string.Equals(repository, "self", StringComparison.OrdinalIgnoreCase))
            {
                return null; // Use current workspace
            }

            return repository;
        }

        // No repository specified = checkout self (current workspace)
        // This is the default behavior for actions/checkout@v4
        return null;
    }

    /// <summary>
    /// Extracts the optional ref/branch/tag to checkout from the step configuration.
    /// </summary>
    /// <param name="step">The step containing checkout configuration.</param>
    /// <returns>The ref/branch/tag to checkout, or null if not specified.</returns>
    private static string? GetCheckoutRef(Step step)
    {
        // Priority: ref > branch > tag
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
    /// <param name="context">The execution context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if a git repository exists, false otherwise.</returns>
    private async Task<bool> CheckRepositoryExistsAsync(
        ExecutionContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await context.ContainerManager.ExecuteCommandAsync(
                context.ContainerId,
                $"git -C {context.ContainerWorkspacePath} rev-parse --git-dir",
                context.ContainerWorkspacePath,
                null,
                cancellationToken);

            return result.Success;
        }
        catch
        {
            // If command fails or throws, repository doesn't exist
            return false;
        }
    }

    /// <summary>
    /// Executes a git command in the container.
    /// </summary>
    /// <param name="command">The git command to execute.</param>
    /// <param name="context">The execution context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The execution result containing output and exit code.</returns>
    private async Task<ExecutionResult> ExecuteGitCommandAsync(
        string command,
        ExecutionContext context,
        CancellationToken cancellationToken)
    {
        return await context.ContainerManager.ExecuteCommandAsync(
            context.ContainerId,
            command,
            context.ContainerWorkspacePath,
            context.Environment as IDictionary<string, string>,
            cancellationToken);
    }
}
