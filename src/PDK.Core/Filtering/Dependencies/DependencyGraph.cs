using PDK.Core.Models;

namespace PDK.Core.Filtering.Dependencies;

/// <summary>
/// Represents a directed graph of step dependencies. Nodes are keyed by (job, step index),
/// so duplicate step names never collide and never produce self-loops.
/// </summary>
public class DependencyGraph
{
    private readonly Dictionary<string, HashSet<string>> _dependencies = new(StringComparer.Ordinal);
    private readonly Dictionary<string, StepNode> _nodes = new(StringComparer.Ordinal);

    /// <summary>
    /// Represents a step node in the dependency graph.
    /// </summary>
    /// <param name="Id">The graph key (see <see cref="GetStepId(string, int)"/>).</param>
    /// <param name="Name">The step display name.</param>
    /// <param name="Index">The 1-based index of the step within its job.</param>
    /// <param name="JobName">The job the step belongs to.</param>
    public record StepNode(string Id, string Name, int Index, string JobName);

    /// <summary>
    /// Adds a step node to the graph.
    /// </summary>
    /// <param name="step">The step to add.</param>
    /// <param name="index">The 1-based index of the step.</param>
    /// <param name="jobName">The key of the job containing the step (see <see cref="GetJobKey"/>).</param>
    public void AddNode(Step step, int index, string jobName)
    {
        var id = GetStepId(jobName, index);
        var node = new StepNode(id, step.Name ?? $"Step {index}", index, jobName);
        _nodes[id] = node;

        if (!_dependencies.ContainsKey(id))
        {
            _dependencies[id] = new HashSet<string>(StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// Adds a dependency edge: dependent depends on dependency. Self edges are ignored.
    /// </summary>
    /// <param name="dependentId">The step that depends on another.</param>
    /// <param name="dependencyId">The step that is depended upon.</param>
    public void AddDependency(string dependentId, string dependencyId)
    {
        if (string.Equals(dependentId, dependencyId, StringComparison.Ordinal))
        {
            return;
        }

        if (!_dependencies.ContainsKey(dependentId))
        {
            _dependencies[dependentId] = new HashSet<string>(StringComparer.Ordinal);
        }

        _dependencies[dependentId].Add(dependencyId);
    }

    /// <summary>
    /// Gets the direct dependencies of a step.
    /// </summary>
    /// <param name="stepId">The step ID.</param>
    /// <returns>The IDs of steps this step directly depends on.</returns>
    public IReadOnlySet<string> GetDirectDependencies(string stepId)
    {
        return _dependencies.TryGetValue(stepId, out var deps)
            ? deps
            : new HashSet<string>(StringComparer.Ordinal);
    }

    /// <summary>
    /// Gets all transitive dependencies of a step (direct and indirect).
    /// </summary>
    /// <param name="stepId">The step ID.</param>
    /// <returns>All step IDs this step depends on (transitively).</returns>
    public IReadOnlySet<string> GetTransitiveDependencies(string stepId)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var result = new HashSet<string>(StringComparer.Ordinal);

        CollectDependencies(stepId, visited, result);

        return result;
    }

    /// <summary>
    /// Gets all steps that depend on the given step (direct dependents).
    /// </summary>
    /// <param name="stepId">The step ID.</param>
    /// <returns>The IDs of steps that depend on this step.</returns>
    public IReadOnlySet<string> GetDirectDependents(string stepId)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (nodeId, deps) in _dependencies)
        {
            if (deps.Contains(stepId))
            {
                result.Add(nodeId);
            }
        }

        return result;
    }

    /// <summary>
    /// Gets all transitive dependents of a step (steps that depend on it directly or indirectly).
    /// </summary>
    /// <param name="stepId">The step ID.</param>
    /// <returns>All step IDs that depend on this step.</returns>
    public IReadOnlySet<string> GetTransitiveDependents(string stepId)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var result = new HashSet<string>(StringComparer.Ordinal);

        CollectDependents(stepId, visited, result);

        return result;
    }

    /// <summary>
    /// Gets a step node by ID.
    /// </summary>
    public StepNode? GetNode(string stepId)
    {
        return _nodes.TryGetValue(stepId, out var node) ? node : null;
    }

    /// <summary>
    /// Gets all nodes in the graph.
    /// </summary>
    public IReadOnlyCollection<StepNode> Nodes => _nodes.Values;

    /// <summary>
    /// Detects if there's a cycle in the dependency graph.
    /// </summary>
    /// <returns>True if a cycle exists.</returns>
    public bool HasCycle()
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var recursionStack = new HashSet<string>(StringComparer.Ordinal);

        foreach (var nodeId in _nodes.Keys)
        {
            if (HasCycleFrom(nodeId, visited, recursionStack))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Gets a topological ordering of the steps (dependencies first).
    /// </summary>
    /// <returns>Steps in dependency order, or null if a cycle exists.</returns>
    public IReadOnlyList<StepNode>? GetTopologicalOrder()
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<StepNode>();

        foreach (var nodeId in _nodes.Keys)
        {
            if (!TopologicalVisit(nodeId, visited, new HashSet<string>(StringComparer.Ordinal), result))
            {
                return null; // Cycle detected
            }
        }

        result.Reverse();
        return result;
    }

    private void CollectDependencies(string stepId, HashSet<string> visited, HashSet<string> result)
    {
        if (!visited.Add(stepId))
        {
            return;
        }

        if (_dependencies.TryGetValue(stepId, out var deps))
        {
            foreach (var dep in deps)
            {
                result.Add(dep);
                CollectDependencies(dep, visited, result);
            }
        }
    }

    private void CollectDependents(string stepId, HashSet<string> visited, HashSet<string> result)
    {
        if (!visited.Add(stepId))
        {
            return;
        }

        foreach (var (nodeId, deps) in _dependencies)
        {
            if (deps.Contains(stepId))
            {
                result.Add(nodeId);
                CollectDependents(nodeId, visited, result);
            }
        }
    }

    private bool HasCycleFrom(string nodeId, HashSet<string> visited, HashSet<string> recursionStack)
    {
        if (recursionStack.Contains(nodeId))
        {
            return true;
        }

        if (visited.Contains(nodeId))
        {
            return false;
        }

        visited.Add(nodeId);
        recursionStack.Add(nodeId);

        if (_dependencies.TryGetValue(nodeId, out var deps))
        {
            foreach (var dep in deps)
            {
                if (HasCycleFrom(dep, visited, recursionStack))
                {
                    return true;
                }
            }
        }

        recursionStack.Remove(nodeId);
        return false;
    }

    private bool TopologicalVisit(string nodeId, HashSet<string> visited, HashSet<string> recursionStack, List<StepNode> result)
    {
        if (recursionStack.Contains(nodeId))
        {
            return false; // Cycle
        }

        if (visited.Contains(nodeId))
        {
            return true;
        }

        visited.Add(nodeId);
        recursionStack.Add(nodeId);

        if (_dependencies.TryGetValue(nodeId, out var deps))
        {
            foreach (var dep in deps)
            {
                if (!TopologicalVisit(dep, visited, recursionStack, result))
                {
                    return false;
                }
            }
        }

        recursionStack.Remove(nodeId);

        if (_nodes.TryGetValue(nodeId, out var node))
        {
            result.Add(node);
        }

        return true;
    }

    /// <summary>
    /// Gets the key used to identify a job in the graph: its id, or its name when it has no id.
    /// </summary>
    public static string GetJobKey(Job job)
    {
        ArgumentNullException.ThrowIfNull(job);

        if (!string.IsNullOrEmpty(job.Id))
        {
            return job.Id;
        }

        return !string.IsNullOrEmpty(job.Name) ? job.Name : "job";
    }

    /// <summary>
    /// Creates the graph key of the step at <paramref name="index"/> (1-based) in the given job.
    /// </summary>
    public static string GetStepId(string jobKey, int index)
    {
        return $"{jobKey}::{index}";
    }

    /// <summary>
    /// Creates the graph key of the step at <paramref name="index"/> (1-based) in <paramref name="job"/>.
    /// </summary>
    public static string GetStepId(Job job, int index)
    {
        return GetStepId(GetJobKey(job), index);
    }
}
