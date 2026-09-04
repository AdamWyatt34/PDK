namespace PDK.Core.Models;


/// <summary>
/// Orders jobs by their dependencies and selects the jobs a run should execute.
/// </summary>
public static class JobGraph
{
    /// <summary>
    /// Returns the pipeline's jobs in dependency order (a job always comes after the jobs it depends on).
    /// Jobs with equal rank keep their declaration order.
    /// </summary>
    /// <param name="pipeline">The pipeline.</param>
    /// <returns>Ordered (job id, job) pairs.</returns>
    /// <exception cref="PdkException">A dependency refers to an unknown job or the graph has a cycle.</exception>
    public static IReadOnlyList<KeyValuePair<string, Job>> Order(Pipeline pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);

        var ids = pipeline.Jobs.Keys.ToList();
        var index = ids.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i, StringComparer.Ordinal);
        var dependencies = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var (id, job) in pipeline.Jobs)
        {
            var resolved = new List<string>();
            foreach (var dependency in job.DependsOn ?? [])
            {
                var target = ResolveId(pipeline, dependency);
                if (target == null)
                {
                    throw new PdkException(
                        "PDK-E-JOB-001",
                        $"Job '{id}' depends on '{dependency}', which is not defined in the pipeline",
                        context: null,
                        suggestions: new[] { $"Define a job named '{dependency}' or fix the dependency list of '{id}'" });
                }

                if (!resolved.Contains(target, StringComparer.Ordinal))
                {
                    resolved.Add(target);
                }
            }

            dependencies[id] = resolved;
        }

        // Kahn's algorithm; ties are broken by declaration order so runs are deterministic.
        var remaining = new Dictionary<string, int>(StringComparer.Ordinal);
        var dependents = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            remaining[id] = dependencies[id].Count;
            dependents[id] = new List<string>();
        }

        foreach (var (id, deps) in dependencies)
        {
            foreach (var dep in deps)
            {
                dependents[dep].Add(id);
            }
        }

        var ready = new SortedSet<string>(Comparer<string>.Create((a, b) => index[a].CompareTo(index[b])));
        foreach (var id in ids.Where(id => remaining[id] == 0))
        {
            ready.Add(id);
        }

        var ordered = new List<KeyValuePair<string, Job>>(ids.Count);
        while (ready.Count > 0)
        {
            var current = ready.Min!;
            ready.Remove(current);
            ordered.Add(new KeyValuePair<string, Job>(current, pipeline.Jobs[current]));

            foreach (var dependent in dependents[current])
            {
                if (--remaining[dependent] == 0)
                {
                    ready.Add(dependent);
                }
            }
        }

        if (ordered.Count != ids.Count)
        {
            var cyclic = ids.Where(id => remaining[id] > 0).ToList();
            throw new PdkException(
                "PDK-E-JOB-002",
                $"Circular job dependency detected among: {string.Join(", ", cyclic)}",
                context: null,
                suggestions: new[] { "Remove one of the dependencies so that the job graph is acyclic" });
        }

        return ordered;
    }

    /// <summary>
    /// Selects the jobs to run: the whole pipeline in dependency order, or one job (optionally with
    /// its transitive dependencies first).
    /// </summary>
    /// <param name="pipeline">The pipeline.</param>
    /// <param name="jobId">The id of the selected job, or null for every job.</param>
    /// <param name="includeDependencies">Whether the transitive dependencies of the selected job run first.</param>
    /// <returns>Ordered (job id, job) pairs.</returns>
    public static IReadOnlyList<KeyValuePair<string, Job>> Select(Pipeline pipeline, string? jobId, bool includeDependencies)
    {
        var ordered = Order(pipeline);
        if (string.IsNullOrEmpty(jobId))
        {
            return ordered;
        }

        var wanted = new HashSet<string>(StringComparer.Ordinal) { jobId };
        if (includeDependencies)
        {
            var stack = new Stack<string>();
            stack.Push(jobId);
            while (stack.Count > 0)
            {
                var current = stack.Pop();
                if (!pipeline.Jobs.TryGetValue(current, out var job))
                {
                    continue;
                }

                foreach (var dependency in job.DependsOn ?? [])
                {
                    var target = ResolveId(pipeline, dependency);
                    if (target != null && wanted.Add(target))
                    {
                        stack.Push(target);
                    }
                }
            }
        }

        return ordered.Where(pair => wanted.Contains(pair.Key)).ToList();
    }

    /// <summary>
    /// Resolves the ids of the jobs a job depends on.
    /// </summary>
    /// <param name="pipeline">The pipeline.</param>
    /// <param name="job">The job.</param>
    /// <returns>The dependency job ids that exist in the pipeline.</returns>
    public static IReadOnlyList<string> DependencyIds(Pipeline pipeline, Job job)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(job);

        var result = new List<string>();
        foreach (var dependency in job.DependsOn ?? [])
        {
            var target = ResolveId(pipeline, dependency);
            if (target != null && !result.Contains(target, StringComparer.Ordinal))
            {
                result.Add(target);
            }
        }

        return result;
    }

    /// <summary>
    /// Resolves a job reference (id or display name, case-insensitive) to the pipeline's job id.
    /// </summary>
    /// <param name="pipeline">The pipeline.</param>
    /// <param name="reference">The job id or name.</param>
    /// <returns>The job id, or null when no job matches.</returns>
    public static string? ResolveId(Pipeline pipeline, string reference)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        if (string.IsNullOrWhiteSpace(reference))
        {
            return null;
        }

        if (pipeline.Jobs.ContainsKey(reference))
        {
            return reference;
        }

        foreach (var (id, job) in pipeline.Jobs)
        {
            if (string.Equals(id, reference, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(job.Id, reference, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(job.Name, reference, StringComparison.OrdinalIgnoreCase))
            {
                return id;
            }
        }

        return null;
    }
}
