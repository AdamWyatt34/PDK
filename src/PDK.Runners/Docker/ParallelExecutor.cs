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
    /// <returns>The list of step execution results in original step order.</returns>
    /// <exception cref="InvalidOperationException">Thrown when a step depends on an unknown step or the dependencies form a cycle.</exception>
    public async Task<List<StepExecutionResult>> ExecuteStepsAsync(
        List<Step> steps,
        Func<Step, CancellationToken, Task<StepExecutionResult>> executor,
        int maxParallelism = 4,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(steps);
        ArgumentNullException.ThrowIfNull(executor);

        if (steps.Count == 0)
        {
            return new List<StepExecutionResult>();
        }

        if (maxParallelism < 1)
        {
            maxParallelism = 1;
        }

        // Build execution levels (groups of steps that can run in parallel); validates dependencies.
        var levels = BuildExecutionLevels(steps);
        var results = new ConcurrentBag<StepExecutionResult>();
        var failureDetected = 0;

        _logger.LogDebug("Parallel execution: {StepCount} steps in {LevelCount} levels, max parallelism {MaxParallelism}",
            steps.Count, levels.Count, maxParallelism);

        foreach (var level in levels)
        {
            if (Volatile.Read(ref failureDetected) != 0 || cancellationToken.IsCancellationRequested)
            {
                break;
            }

            _logger.LogDebug("Executing level with {StepCount} steps", level.Count);

            using var semaphore = new SemaphoreSlim(maxParallelism);
            var tasks = new List<Task>();

            foreach (var step in level)
            {
                if (Volatile.Read(ref failureDetected) != 0)
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
                            Interlocked.Exchange(ref failureDetected, 1);
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
    /// <exception cref="InvalidOperationException">
    /// Thrown when a step's <c>needs</c> references a step that does not exist, or when the dependencies form a cycle.
    /// </exception>
    public List<List<Step>> BuildExecutionLevels(List<Step> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);

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

        // Validate that every dependency refers to a known step
        foreach (var step in steps)
        {
            foreach (var dependency in step.Needs ?? new List<string>())
            {
                if (!stepMap.ContainsKey(dependency))
                {
                    throw new InvalidOperationException(
                        $"Step '{Describe(step)}' depends on unknown step '{dependency}'. " +
                        $"Known steps: {string.Join(", ", steps.Select(Describe))}.");
                }
            }
        }

        var assigned = new HashSet<Step>();
        var remaining = new HashSet<Step>(steps);

        while (remaining.Count > 0)
        {
            var level = new List<Step>();

            foreach (var step in steps.Where(remaining.Contains))
            {
                var dependencies = step.Needs ?? new List<string>();
                var allDependenciesSatisfied = dependencies.All(dep => assigned.Contains(stepMap[dep]));

                if (allDependenciesSatisfied)
                {
                    level.Add(step);
                }
            }

            if (level.Count == 0)
            {
                var cycle = FindCycle(remaining, stepMap);
                throw new InvalidOperationException(
                    $"Circular dependency detected among steps: {string.Join(" -> ", cycle.Select(Describe))}.");
            }

            foreach (var step in level)
            {
                assigned.Add(step);
                remaining.Remove(step);
            }

            levels.Add(level);
        }

        for (var i = 0; i < levels.Count; i++)
        {
            _logger.LogDebug("Level {Level}: {Steps}", i + 1, string.Join(", ", levels[i].Select(Describe)));
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
        ArgumentNullException.ThrowIfNull(steps);
        return steps.Any(s => s.Needs?.Count > 0);
    }

    private static string Describe(Step step)
    {
        return !string.IsNullOrEmpty(step.Id) ? step.Id : (!string.IsNullOrEmpty(step.Name) ? step.Name : "unnamed");
    }

    /// <summary>
    /// Finds a dependency cycle among the remaining steps (depth-first search over the <c>needs</c> edges).
    /// </summary>
    private static List<Step> FindCycle(HashSet<Step> remaining, Dictionary<string, Step> stepMap)
    {
        var visited = new HashSet<Step>();
        var path = new List<Step>();
        var onPath = new HashSet<Step>();

        foreach (var start in remaining)
        {
            var cycle = Visit(start);
            if (cycle != null)
            {
                return cycle;
            }
        }

        // Should not happen (no progress implies a cycle), but never return an empty description.
        return remaining.ToList();

        List<Step>? Visit(Step step)
        {
            if (onPath.Contains(step))
            {
                var index = path.IndexOf(step);
                var cycle = path.Skip(index).ToList();
                cycle.Add(step);
                return cycle;
            }

            if (!visited.Add(step))
            {
                return null;
            }

            path.Add(step);
            onPath.Add(step);

            foreach (var dependency in step.Needs ?? new List<string>())
            {
                if (stepMap.TryGetValue(dependency, out var next) && remaining.Contains(next))
                {
                    var cycle = Visit(next);
                    if (cycle != null)
                    {
                        return cycle;
                    }
                }
            }

            path.RemoveAt(path.Count - 1);
            onPath.Remove(step);
            return null;
        }
    }
}
