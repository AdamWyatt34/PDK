using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

namespace PDK.Runners.Docker;

/// <summary>
/// Records which PDK process created a container (as labels) and decides whether a container found on
/// the daemon is an orphan: one whose creating process no longer runs on this machine. Containers of a
/// live process (another <c>pdk run</c>, or a job still being set up), containers kept on request and
/// containers created from another host are never treated as orphans.
/// </summary>
internal static class ContainerOwnership
{
    /// <summary>Label carrying the machine name of the creating process.</summary>
    public const string HostLabel = "pdk.host";

    /// <summary>Label carrying the id of the creating process.</summary>
    public const string ProcessLabel = "pdk.pid";

    /// <summary>Label carrying the UTC start time of the creating process (guards against pid reuse).</summary>
    public const string ProcessStartLabel = "pdk.pid.start";

    /// <summary>Label marking a container kept for inspection (<c>--keep-containers</c>).</summary>
    public const string KeepLabel = "pdk.keep";

    private static readonly TimeSpan StartTimeTolerance = TimeSpan.FromSeconds(5);
    private static readonly Lazy<DateTimeOffset?> CurrentProcessStart = new(() => ProbeProcess(Environment.ProcessId).StartedAt);

    /// <summary>
    /// What is known about a process id on this machine.
    /// </summary>
    /// <param name="Exists">Whether a process with the id is running.</param>
    /// <param name="StartedAt">When it started (UTC), when that could be read.</param>
    public readonly record struct OwnerProcess(bool Exists, DateTimeOffset? StartedAt)
    {
        /// <summary>No process with the id is running.</summary>
        public static OwnerProcess Missing => new(false, null);
    }

    /// <summary>
    /// Adds the ownership labels for the current process.
    /// </summary>
    /// <param name="labels">The container labels.</param>
    /// <param name="keep">Whether the container is kept after the run.</param>
    public static void Stamp(IDictionary<string, string> labels, bool keep)
    {
        ArgumentNullException.ThrowIfNull(labels);

        labels[HostLabel] = Environment.MachineName;
        labels[ProcessLabel] = Environment.ProcessId.ToString(CultureInfo.InvariantCulture);

        if (CurrentProcessStart.Value is { } startedAt)
        {
            labels[ProcessStartLabel] = startedAt.ToString("O", CultureInfo.InvariantCulture);
        }

        if (keep)
        {
            labels[KeepLabel] = "true";
        }
    }

    /// <summary>
    /// Decides whether a container may be removed as an orphan.
    /// </summary>
    /// <param name="labels">The container's labels.</param>
    /// <param name="state">The container state as reported by the daemon (created, running, exited, ...).</param>
    /// <param name="machineName">This machine's name.</param>
    /// <param name="probe">Looks up a process id on this machine.</param>
    /// <returns>True when the container belongs to nobody who is still running.</returns>
    public static bool IsOrphan(
        IDictionary<string, string>? labels,
        string? state,
        string machineName,
        Func<int, OwnerProcess> probe)
    {
        ArgumentNullException.ThrowIfNull(probe);

        if (labels != null &&
            labels.TryGetValue(KeepLabel, out var keep) &&
            string.Equals(keep, "true", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (labels == null ||
            !labels.TryGetValue(HostLabel, out var host) ||
            !labels.TryGetValue(ProcessLabel, out var pidText) ||
            !int.TryParse(pidText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid))
        {
            // No owner recorded (earlier PDK versions): only a container that is no longer running is an orphan.
            return IsFinished(state);
        }

        if (!string.Equals(host, machineName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var owner = probe(pid);
        if (!owner.Exists)
        {
            return true;
        }

        if (owner.StartedAt is { } startedAt &&
            labels.TryGetValue(ProcessStartLabel, out var recordedText) &&
            DateTimeOffset.TryParse(recordedText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var recorded))
        {
            // The id is in use, but by a process started at another time: the owner died and its id was reused.
            return (startedAt - recorded).Duration() > StartTimeTolerance;
        }

        return false;
    }

    /// <summary>
    /// Looks up a process id on this machine.
    /// </summary>
    /// <param name="pid">The process id.</param>
    /// <returns>Whether the process runs and, when readable, its start time.</returns>
    public static OwnerProcess ProbeProcess(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);

            bool exited;
            try
            {
                exited = process.HasExited;
            }
            catch (Win32Exception)
            {
                exited = false; // Not ours to inspect; assume it is alive.
            }

            if (exited)
            {
                return OwnerProcess.Missing;
            }

            DateTimeOffset? startedAt = null;
            try
            {
                startedAt = new DateTimeOffset(process.StartTime.ToUniversalTime());
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or UnauthorizedAccessException)
            {
                // Start time not readable for another user's process; the process still counts as alive.
            }

            return new OwnerProcess(true, startedAt);
        }
        catch (ArgumentException)
        {
            return OwnerProcess.Missing;
        }
        catch (InvalidOperationException)
        {
            return OwnerProcess.Missing;
        }
    }

    private static bool IsFinished(string? state)
    {
        return string.Equals(state, "exited", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(state, "created", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(state, "dead", StringComparison.OrdinalIgnoreCase);
    }
}
