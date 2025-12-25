using Microsoft.Extensions.Logging;
using PDK.Core.ErrorHandling;
using PDK.Core.Models;

namespace PDK.Core.Validation.Phases;

/// <summary>
/// Validates job and step dependencies: existence, circular references, and execution order.
/// </summary>
public class DependencyValidationPhase : IValidationPhase
{
    private readonly ILogger<DependencyValidationPhase> _logger;

    public string Name => "Dependency Validation";
    public int Order => 4;

    public DependencyValidationPhase(ILogger<DependencyValidationPhase> logger)
    {
        _logger = logger;
    }

    public Task<IReadOnlyList<DryRunValidationError>> ValidateAsync(
        Pipeline pipeline,
        ValidationContext context,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<DryRunValidationError>();

        _logger.LogDebug("Starting dependency validation for pipeline: {PipelineName}", pipeline.Name);

        // Validate job dependencies
        ValidateJobDependencies(pipeline, context, errors);

        // Validate step dependencies within each job
        foreach (var (jobId, job) in pipeline.Jobs)
        {
            ValidateStepDependencies(jobId, job, errors);
        }

        _logger.LogDebug("Dependency validation completed with {ErrorCount} errors", errors.Count);

        return Task.FromResult<IReadOnlyList<DryRunValidationError>>(errors);
    }

    private void ValidateJobDependencies(
        Pipeline pipeline,
        ValidationContext context,
        List<DryRunValidationError> errors)
    {
        var jobIds = pipeline.Jobs.Keys.ToHashSet();

        // Check each job's dependencies
        foreach (var (jobId, job) in pipeline.Jobs)
        {
            if (job.DependsOn == null || job.DependsOn.Count == 0)
            {
                continue;
            }

            foreach (var dependency in job.DependsOn)
            {
                // Check if dependency exists
                if (!jobIds.Contains(dependency))
                {
                    errors.Add(DryRunValidationError.DependencyError(
                        ErrorCodes.CircularDependency,
                        $"Job '{jobId}' depends on non-existent job '{dependency}'",
                        jobId: jobId,
                        suggestions: new[]
                        {
                            $"Available jobs: {string.Join(", ", jobIds)}",
                            "Check the job name spelling",
                            "Ensure the dependent job is defined in the pipeline"
                        }));
                }

                // Check for self-dependency
                if (dependency == jobId)
                {
                    errors.Add(DryRunValidationError.DependencyError(
                        ErrorCodes.CircularDependency,
                        $"Job '{jobId}' cannot depend on itself",
                        jobId: jobId,
                        suggestions: "Remove the self-reference from 'needs' or 'depends_on'"));
                }
            }
        }

        // Check for circular dependencies
        var circularPath = DetectCircularJobDependencies(pipeline);
        if (circularPath != null)
        {
            var cyclePath = string.Join(" -> ", circularPath);
            errors.Add(DryRunValidationError.DependencyError(
                ErrorCodes.CircularDependency,
                $"Circular dependency detected in jobs: {cyclePath}",
                suggestions: new[]
                {
                    "Review the dependency chain and remove the circular reference",
                    "Consider restructuring jobs to break the cycle"
                }));
        }
        else
        {
            // Calculate execution order using topological sort
            var executionOrder = CalculateJobExecutionOrder(pipeline);
            foreach (var (jobId, order) in executionOrder)
            {
                context.JobExecutionOrder[jobId] = order;
            }
        }
    }

    private void ValidateStepDependencies(
        string jobId,
        Job job,
        List<DryRunValidationError> errors)
    {
        // Build set of step IDs
        var stepIds = new HashSet<string>();
        foreach (var step in job.Steps)
        {
            if (!string.IsNullOrEmpty(step.Id))
            {
                stepIds.Add(step.Id);
            }
        }

        // Check each step's dependencies
        for (int i = 0; i < job.Steps.Count; i++)
        {
            var step = job.Steps[i];
            var stepIndex = i + 1;
            var stepName = step.Name ?? $"Step {stepIndex}";

            if (step.Needs == null || step.Needs.Count == 0)
            {
                continue;
            }

            foreach (var need in step.Needs)
            {
                if (string.IsNullOrWhiteSpace(need))
                {
                    continue; // Already caught by SchemaValidationPhase
                }

                // Check if dependency exists
                if (!stepIds.Contains(need))
                {
                    errors.Add(DryRunValidationError.DependencyError(
                        ErrorCodes.CircularDependency,
                        $"Step '{stepName}' in job '{jobId}' depends on non-existent step '{need}'",
                        jobId: jobId,
                        suggestions: new[]
                        {
                            stepIds.Count > 0
                                ? $"Available step IDs: {string.Join(", ", stepIds)}"
                                : "No steps have IDs defined. Add 'id' field to steps you want to reference.",
                            "Check the step ID spelling",
                            "Ensure the dependent step is defined before the current step"
                        }));
                }

                // Check for self-dependency
                if (step.Id != null && need == step.Id)
                {
                    errors.Add(DryRunValidationError.DependencyError(
                        ErrorCodes.CircularDependency,
                        $"Step '{stepName}' in job '{jobId}' cannot depend on itself",
                        jobId: jobId,
                        suggestions: "Remove the self-reference from 'needs'"));
                }
            }
        }

        // Check for circular step dependencies
        var circularPath = DetectCircularStepDependencies(job);
        if (circularPath != null)
        {
            var cyclePath = string.Join(" -> ", circularPath);
            errors.Add(DryRunValidationError.DependencyError(
                ErrorCodes.CircularDependency,
                $"Circular dependency detected in steps of job '{jobId}': {cyclePath}",
                jobId: jobId,
                suggestions: new[]
                {
                    "Review the step dependency chain and remove the circular reference",
                    "Consider restructuring steps to break the cycle"
                }));
        }
    }

    private List<string>? DetectCircularJobDependencies(Pipeline pipeline)
    {
        var visited = new HashSet<string>();
        var recursionStack = new HashSet<string>();
        var path = new List<string>();

        foreach (var jobId in pipeline.Jobs.Keys)
        {
            if (HasCycleJobDFS(jobId, pipeline.Jobs, visited, recursionStack, path))
            {
                // Find the cycle start in the path
                var cycleStart = path.Last();
                var cycleStartIndex = path.IndexOf(cycleStart);
                return path.Skip(cycleStartIndex).Append(cycleStart).ToList();
            }
        }

        return null;
    }

    private bool HasCycleJobDFS(
        string jobId,
        IDictionary<string, Job> jobs,
        HashSet<string> visited,
        HashSet<string> recursionStack,
        List<string> path)
    {
        if (recursionStack.Contains(jobId))
        {
            path.Add(jobId);
            return true;
        }

        if (visited.Contains(jobId))
        {
            return false;
        }

        visited.Add(jobId);
        recursionStack.Add(jobId);
        path.Add(jobId);

        if (jobs.TryGetValue(jobId, out var job) && job.DependsOn != null)
        {
            foreach (var dependency in job.DependsOn)
            {
                if (jobs.ContainsKey(dependency) &&
                    HasCycleJobDFS(dependency, jobs, visited, recursionStack, path))
                {
                    return true;
                }
            }
        }

        recursionStack.Remove(jobId);
        path.RemoveAt(path.Count - 1);
        return false;
    }

    private List<string>? DetectCircularStepDependencies(Job job)
    {
        // Build step ID to step mapping
        var stepById = new Dictionary<string, Step>();
        foreach (var step in job.Steps)
        {
            if (!string.IsNullOrEmpty(step.Id))
            {
                stepById[step.Id] = step;
            }
        }

        var visited = new HashSet<string>();
        var recursionStack = new HashSet<string>();
        var path = new List<string>();

        foreach (var stepId in stepById.Keys)
        {
            if (HasCycleStepDFS(stepId, stepById, visited, recursionStack, path))
            {
                var cycleStart = path.Last();
                var cycleStartIndex = path.IndexOf(cycleStart);
                return path.Skip(cycleStartIndex).Append(cycleStart).ToList();
            }
        }

        return null;
    }

    private bool HasCycleStepDFS(
        string stepId,
        Dictionary<string, Step> stepById,
        HashSet<string> visited,
        HashSet<string> recursionStack,
        List<string> path)
    {
        if (recursionStack.Contains(stepId))
        {
            path.Add(stepId);
            return true;
        }

        if (visited.Contains(stepId))
        {
            return false;
        }

        visited.Add(stepId);
        recursionStack.Add(stepId);
        path.Add(stepId);

        if (stepById.TryGetValue(stepId, out var step) && step.Needs != null)
        {
            foreach (var need in step.Needs)
            {
                if (stepById.ContainsKey(need) &&
                    HasCycleStepDFS(need, stepById, visited, recursionStack, path))
                {
                    return true;
                }
            }
        }

        recursionStack.Remove(stepId);
        path.RemoveAt(path.Count - 1);
        return false;
    }

    private Dictionary<string, int> CalculateJobExecutionOrder(Pipeline pipeline)
    {
        var result = new Dictionary<string, int>();
        var inDegree = new Dictionary<string, int>();
        var adjacency = new Dictionary<string, List<string>>();

        // Initialize
        foreach (var jobId in pipeline.Jobs.Keys)
        {
            inDegree[jobId] = 0;
            adjacency[jobId] = new List<string>();
        }

        // Build graph
        foreach (var (jobId, job) in pipeline.Jobs)
        {
            if (job.DependsOn != null)
            {
                foreach (var dep in job.DependsOn)
                {
                    if (pipeline.Jobs.ContainsKey(dep))
                    {
                        adjacency[dep].Add(jobId);
                        inDegree[jobId]++;
                    }
                }
            }
        }

        // Topological sort using Kahn's algorithm
        var queue = new Queue<string>();
        foreach (var (jobId, degree) in inDegree)
        {
            if (degree == 0)
            {
                queue.Enqueue(jobId);
            }
        }

        int order = 1;
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            result[current] = order++;

            foreach (var neighbor in adjacency[current])
            {
                inDegree[neighbor]--;
                if (inDegree[neighbor] == 0)
                {
                    queue.Enqueue(neighbor);
                }
            }
        }

        return result;
    }
}
