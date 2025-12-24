using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using PDK.Core.Models;

namespace PDK.Runners.Docker;

/// <summary>
/// Executes pipeline steps in parallel based on their dependencies.
/// Uses topological sorting to determine execution order and runs independent steps concurrently.
/// </summary>
public class ParallelExecutor
{
    private readonly ILogger<ParallelExecutor> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ParallelExecutor"/> class.
    /// </summary>
    /// <param name="logger">The logger for structured logging.</param>
    public ParallelExecutor(ILogger<ParallelExecutor> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Executes steps in parallel based on their dependencies.
    /// Steps with unmet dependencies wait for those dependencies to complete.
    /// </summary>
    /// <param name="steps">The steps to execute.</param>
    /// <param name="executor">The function that executes a single step.</param>
    /// <param name="maxParallelism">Maximum number of steps to run concurrently.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The list of step execution results in completion order.</returns>
    public async Task<List<StepExecutionResult>> ExecuteStepsAsync(
        List<Step> steps,
        Func<Step, CancellationToken, Task<StepExecutionResult>> executor,
        int maxParallelism = 4,
        CancellationToken cancellationToken = default)
    {
        if (steps.Count == 0)
        {
            return new List<StepExecutionResult>();
        }

        // Build execution levels (groups of steps that can run in parallel)
        var levels = BuildExecutionLevels(steps);
        var results = new ConcurrentBag<StepExecutionResult>();
        var failureDetected = false;

        _logger.LogDebug("Parallel execution: {StepCount} steps in {LevelCount} levels, max parallelism {MaxParallelism}",
            steps.Count, levels.Count, maxParallelism);

        foreach (var level in levels)
        {
            if (failureDetected || cancellationToken.IsCancellationRequested)
            {
                break;
            }

            _logger.LogDebug("Executing level with {StepCount} steps", level.Count);

            // Use SemaphoreSlim to limit parallelism
            using var semaphore = new SemaphoreSlim(maxParallelism);
            var tasks = new List<Task>();

            foreach (var step in level)
            {
                if (failureDetected)
                {
                    break;
                }

                await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

                var task = Task.Run(async () =>
                {
                    try
                    {
                        var result = await executor(step, cancellationToken).ConfigureAwait(false);
                        results.Add(result);

                        if (!result.Success && !step.ContinueOnError)
                        {
                            failureDetected = true;
                            _logger.LogWarning("Step {StepName} failed, stopping parallel execution", step.Name);
                        }
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }, cancellationToken);

                tasks.Add(task);
            }

            // Wait for all tasks in this level to complete
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        // Return results in original step order for predictability
        return results
            .OrderBy(r => steps.FindIndex(s => s.Name == r.StepName || s.Id == r.StepName))
            .ToList();
    }

    /// <summary>
    /// Builds execution levels from steps based on their dependencies.
    /// Each level contains steps that can run in parallel (all their dependencies are in previous levels).
    /// </summary>
    /// <param name="steps">The steps to organize into levels.</param>
    /// <returns>A list of execution levels, each containing steps that can run in parallel.</returns>
    public List<List<Step>> BuildExecutionLevels(List<Step> steps)
    {
        var levels = new List<List<Step>>();

        if (steps.Count == 0)
        {
            return levels;
        }

        // Build a map from step ID/name to step
        var stepMap = new Dictionary<string, Step>(StringComparer.OrdinalIgnoreCase);
        foreach (var step in steps)
        {
            if (!string.IsNullOrEmpty(step.Id))
            {
                stepMap[step.Id] = step;
            }
            if (!string.IsNullOrEmpty(step.Name))
            {
                stepMap[step.Name] = step;
            }
        }

        // Track which steps have been assigned to a level
        var assigned = new HashSet<Step>();
        var remaining = new HashSet<Step>(steps);

        while (remaining.Count > 0)
        {
            var level = new List<Step>();

            foreach (var step in remaining.ToList())
            {
                var dependencies = step.Needs ?? new List<string>();
                var allDependenciesSatisfied = true;

                foreach (var dep in dependencies)
                {
                    if (stepMap.TryGetValue(dep, out var depStep) && !assigned.Contains(depStep))
                    {
                        allDependenciesSatisfied = false;
                        break;
                    }
                }

                if (allDependenciesSatisfied)
                {
                    level.Add(step);
                }
            }

            // If no steps can be added, we have a circular dependency or invalid references
            if (level.Count == 0 && remaining.Count > 0)
            {
                _logger.LogWarning("Circular dependency detected, falling back to sequential execution for remaining {Count} steps", remaining.Count);
                level.AddRange(remaining);
                remaining.Clear();
            }
            else
            {
                foreach (var step in level)
                {
                    assigned.Add(step);
                    remaining.Remove(step);
                }
            }

            if (level.Count > 0)
            {
                levels.Add(level);
            }
        }

        // Log the execution plan
        for (int i = 0; i < levels.Count; i++)
        {
            _logger.LogDebug("Level {Level}: {Steps}",
                i + 1,
                string.Join(", ", levels[i].Select(s => s.Name ?? s.Id ?? "unnamed")));
        }

        return levels;
    }

    /// <summary>
    /// Checks if steps have any dependencies defined.
    /// </summary>
    /// <param name="steps">The steps to check.</param>
    /// <returns>True if any step has dependencies, false if all steps are independent.</returns>
    public static bool HasDependencies(List<Step> steps)
    {
        return steps.Any(s => s.Needs?.Count > 0);
    }
}
