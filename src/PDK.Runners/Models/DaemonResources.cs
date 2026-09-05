namespace PDK.Runners.Models;

/// <summary>
/// Resources reported by the Docker daemon (<c>docker info</c>: <c>NCPU</c> and <c>MemTotal</c>).
/// On Docker Desktop these describe the Linux VM, not the host, and are therefore the right
/// bounds for container resource limits.
/// </summary>
/// <param name="CpuCount">Number of CPUs available to the daemon.</param>
/// <param name="TotalMemoryBytes">Total memory available to the daemon, in bytes.</param>
public sealed record DaemonResources(long CpuCount, long TotalMemoryBytes);
