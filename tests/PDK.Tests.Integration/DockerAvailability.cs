using System.Runtime.InteropServices;
using Docker.DotNet;

namespace PDK.Tests.Integration;

/// <summary>
/// Probes, once per test process, whether a Docker daemon that runs Linux containers is reachable.
/// Used by <see cref="DockerFactAttribute"/> and <see cref="DockerTheoryAttribute"/> to skip
/// daemon-dependent tests instead of failing them on machines without Docker.
/// </summary>
/// <remarks>
/// The endpoint mirrors what <c>DockerContainerManager</c> connects to: <c>DOCKER_HOST</c> when set,
/// otherwise the platform default (<c>npipe://./pipe/docker_engine</c> on Windows,
/// <c>unix:///var/run/docker.sock</c> elsewhere).
/// <para>
/// The <c>PDK_DOCKER_TESTS</c> environment variable overrides the probe:
/// <c>require</c> never skips (the tests then fail with the real connection error, which is what a CI
/// runner that is expected to have Docker should do), <c>skip</c> always skips.
/// </para>
/// </remarks>
internal static class DockerAvailability
{
    private const string ModeVariable = "PDK_DOCKER_TESTS";

    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

    private static readonly Lazy<(bool IsAvailable, string SkipReason)> Probe =
        new(ProbeDaemon, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// Gets a value indicating whether Docker-dependent tests can run.
    /// </summary>
    public static bool IsAvailable => Probe.Value.IsAvailable;

    /// <summary>
    /// Gets the reason to report when Docker-dependent tests are skipped.
    /// </summary>
    public static string SkipReason => Probe.Value.SkipReason;

    /// <summary>
    /// Resolves the Docker endpoint the tests will talk to.
    /// </summary>
    public static Uri ResolveEndpoint()
    {
        var dockerHost = Environment.GetEnvironmentVariable("DOCKER_HOST");
        if (!string.IsNullOrWhiteSpace(dockerHost) && Uri.TryCreate(dockerHost, UriKind.Absolute, out var configured))
        {
            return configured;
        }

        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? new Uri("npipe://./pipe/docker_engine")
            : new Uri("unix:///var/run/docker.sock");
    }

    private static (bool IsAvailable, string SkipReason) ProbeDaemon()
    {
        var mode = Environment.GetEnvironmentVariable(ModeVariable)?.Trim().ToLowerInvariant();

        if (mode == "skip")
        {
            return (false, $"Docker daemon not available ({ModeVariable}=skip)");
        }

        if (mode == "require")
        {
            // Never skip: a missing daemon must surface as a real test failure.
            return (true, string.Empty);
        }

        var endpoint = ResolveEndpoint();
        var reason = TryPing(endpoint);

        return reason == null
            ? (true, string.Empty)
            : (false, reason);
    }

    /// <summary>
    /// Pings the daemon and checks that it runs Linux containers.
    /// </summary>
    /// <returns><c>null</c> when the daemon is usable, otherwise the skip reason.</returns>
    private static string? TryPing(Uri endpoint)
    {
        try
        {
            using var client = new DockerClientConfiguration(endpoint, defaultTimeout: ProbeTimeout).CreateClient();

            var probe = Task.Run(async () =>
            {
                using var cts = new CancellationTokenSource(ProbeTimeout);
                await client.System.PingAsync(cts.Token);
                var info = await client.System.GetSystemInfoAsync(cts.Token);
                return info.OSType;
            });

            if (!probe.Wait(ProbeTimeout + TimeSpan.FromSeconds(1)))
            {
                return $"Docker daemon not available (no response from {endpoint} within {ProbeTimeout.TotalSeconds:0}s)";
            }

            var osType = probe.Result;
            if (!string.Equals(osType, "linux", StringComparison.OrdinalIgnoreCase))
            {
                return $"Docker daemon not available for Linux containers (daemon at {endpoint} runs '{osType}' containers)";
            }

            return null;
        }
        catch (Exception ex)
        {
            return $"Docker daemon not available at {endpoint}: {Describe(ex)}";
        }
    }

    private static string Describe(Exception exception)
    {
        var inner = exception;
        while (inner is AggregateException { InnerException: not null } aggregate)
        {
            inner = aggregate.InnerException;
        }

        while (inner.InnerException != null)
        {
            inner = inner.InnerException;
        }

        return inner.Message;
    }
}
