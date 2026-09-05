using PDK.Core.Models;

namespace PDK.Runners;

/// <summary>
/// Runs jobs in dependency order, up to <c>maxParallel</c> at a time. A job starts as soon as every job it
/// depends on has finished (whatever the outcome; the job's own condition decides whether it runs), which
/// keeps the sequential order deterministic when <c>maxParallel</c> is 1 and lets independent jobs overlap
/// otherwise.
/// </summary>
public static class JobScheduler
{
    /// <summary>
    /// Runs one job. Receives the job id, the job, its start number (1-based, in start order) and the
    /// results of every job that has finished so far.
    /// </summary>
    public delegate Task<JobExecutionResult> JobRunner(
        string jobId,
        Job job,
        int number,
        IReadOnlyDictionary<string, JobExecutionResult> finished,
        CancellationToken cancellationToken);

    /// <summary>
    /// Schedules <paramref name="jobs"/> (already in a valid topological order) and returns the results keyed by job id.
    /// </summary>
    /// <param name="jobs">The jobs to run, in topological order.</param>
    /// <param name="dependencyIds">Resolves the ids of the jobs a job depends on.</param>
    /// <param name="runJob">Runs one job.</param>
    /// <param name="maxParallel">Maximum number of jobs running at once (1 = sequential).</param>
    /// <param name="cancellationToken">Cancels the run; running jobs observe it, and the first exception is rethrown once every started job has finished.</param>
    /// <returns>The results keyed by job id.</returns>
    public static async Task<IReadOnlyDictionary<string, JobExecutionResult>> RunAsync(
        IReadOnlyList<KeyValuePair<string, Job>> jobs,
        Func<string, Job, IReadOnlyList<string>> dependencyIds,
        JobRunner runJob,
        int maxParallel,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(jobs);
        ArgumentNullException.ThrowIfNull(dependencyIds);
        ArgumentNullException.ThrowIfNull(runJob);
        maxParallel = Math.Max(1, maxParallel);

        var finished = new Dictionary<string, JobExecutionResult>(StringComparer.Ordinal);
        var pending = new List<KeyValuePair<string, Job>>(jobs);
        var running = new Dictionary<string, Task<JobExecutionResult>>(StringComparer.Ordinal);
        var known = new HashSet<string>(jobs.Select(j => j.Key), StringComparer.Ordinal);
        var number = 0;

        try
        {
            while (pending.Count > 0 || running.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Start every job whose dependencies have finished, up to the limit, in list order
                foreach (var entry in pending.ToList())
                {
                    if (running.Count >= maxParallel)
                    {
                        break;
                    }

                    var waiting = dependencyIds(entry.Key, entry.Value)
                        .Any(dep => known.Contains(dep) && !finished.ContainsKey(dep));
                    if (waiting)
                    {
                        continue;
                    }

                    pending.Remove(entry);
                    var snapshot = new Dictionary<string, JobExecutionResult>(finished, StringComparer.Ordinal);
                    running[entry.Key] = runJob(entry.Key, entry.Value, ++number, snapshot, cancellationToken);
                }

                if (running.Count == 0)
                {
                    if (pending.Count > 0)
                    {
                        var stuck = string.Join(", ", pending.Select(p => p.Key));
                        throw new InvalidOperationException($"Jobs cannot be scheduled because their dependencies never complete: {stuck}");
                    }

                    break;
                }

                var completedTask = await Task.WhenAny(running.Values).ConfigureAwait(false);
                var completedId = running.First(kv => ReferenceEquals(kv.Value, completedTask)).Key;
                running.Remove(completedId);
                finished[completedId] = await completedTask.ConfigureAwait(false);
            }
        }
        catch
        {
            // Let the other jobs wind down (they observe the same token) before propagating
            if (running.Count > 0)
            {
                try
                {
                    await Task.WhenAll(running.Values).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // The first failure is the one reported
                }
            }

            throw;
        }

        return finished;
    }
}
