namespace PDK.Runners.Docker;

/// <summary>
/// The Docker daemon endpoint PDK talks to, together with how it was chosen.
/// </summary>
/// <param name="Uri">The endpoint URI (<c>unix://</c>, <c>npipe://</c>, <c>http://</c> or <c>https://</c>).</param>
/// <param name="Source">A human-readable description of where the endpoint came from
/// (e.g. "DOCKER_HOST environment variable", "Docker context 'desktop-linux'", "socket /var/run/docker.sock").</param>
public sealed record DockerEndpoint(Uri Uri, string Source)
{
    /// <summary>
    /// Gets the socket paths (and other locations) that were examined before this endpoint was chosen.
    /// Used to produce actionable "Docker not found" messages.
    /// </summary>
    public IReadOnlyList<string> SearchedPaths { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Gets the host path of the Unix socket when the endpoint is a <c>unix://</c> URI; otherwise null.
    /// </summary>
    public string? SocketPath => string.Equals(Uri.Scheme, "unix", StringComparison.OrdinalIgnoreCase)
        ? Uri.LocalPath
        : null;

    /// <summary>
    /// Gets a value indicating whether the endpoint is a Windows named pipe.
    /// </summary>
    public bool IsNamedPipe => string.Equals(Uri.Scheme, "npipe", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Gets a value indicating whether the endpoint is a local socket or pipe (as opposed to TCP).
    /// </summary>
    public bool IsLocal => SocketPath != null || IsNamedPipe;

    /// <inheritdoc/>
    public override string ToString() => Uri.ToString();
}
