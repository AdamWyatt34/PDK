using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PDK.Runners.Docker;

/// <summary>
/// Discovers the Docker daemon endpoint the same way the Docker CLI does:
/// <list type="number">
/// <item><description><c>DOCKER_HOST</c> environment variable.</description></item>
/// <item><description>The current Docker context (<c>DOCKER_CONTEXT</c> or <c>currentContext</c> in
/// <c>~/.docker/config.json</c>), whose endpoint is stored in <c>~/.docker/contexts/meta/&lt;sha256(name)&gt;/meta.json</c>.</description></item>
/// <item><description>The first existing socket among the well-known locations of Docker Engine, Docker Desktop,
/// Colima, OrbStack, Rancher Desktop, Lima and Podman (or the default named pipe on Windows).</description></item>
/// </list>
/// </summary>
public static class DockerEndpointResolver
{
    /// <summary>The default Docker Engine socket on Linux.</summary>
    public const string DefaultUnixSocket = "/var/run/docker.sock";

    /// <summary>The default Docker Desktop named pipe on Windows.</summary>
    public const string DefaultNamedPipe = "npipe://./pipe/docker_engine";

    /// <summary>
    /// Resolves the endpoint using the real host environment.
    /// </summary>
    /// <returns>The resolved endpoint. Never null: falls back to the platform default.</returns>
    public static DockerEndpoint Resolve() => Resolve(DockerHostEnvironment.Instance);

    /// <summary>
    /// Resolves the endpoint using the supplied host environment.
    /// </summary>
    /// <param name="environment">The host environment.</param>
    /// <returns>The resolved endpoint. Never null: falls back to the platform default.</returns>
    public static DockerEndpoint Resolve(IDockerHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        var searched = new List<string>();

        // 1. DOCKER_HOST
        var dockerHost = environment.GetEnvironmentVariable("DOCKER_HOST");
        if (!string.IsNullOrWhiteSpace(dockerHost))
        {
            if (TryParseEndpoint(dockerHost, environment, out var hostUri, out var problem))
            {
                return new DockerEndpoint(hostUri, "DOCKER_HOST environment variable") { SearchedPaths = searched };
            }

            searched.Add($"DOCKER_HOST={dockerHost} ({problem})");
        }

        // 2. Docker context
        var contextEndpoint = ResolveFromContext(environment, searched);
        if (contextEndpoint != null)
        {
            return contextEndpoint with { SearchedPaths = searched };
        }

        // 3. Well-known sockets / named pipe
        if (environment.IsWindows)
        {
            return new DockerEndpoint(new Uri(DefaultNamedPipe), "default named pipe") { SearchedPaths = searched };
        }

        foreach (var candidate in GetSocketCandidates(environment))
        {
            searched.Add(candidate);
            if (environment.FileExists(candidate))
            {
                return new DockerEndpoint(ToUnixUri(candidate), $"socket {candidate}") { SearchedPaths = searched };
            }
        }

        return new DockerEndpoint(ToUnixUri(DefaultUnixSocket), "default (no Docker socket found)")
        {
            SearchedPaths = searched
        };
    }

    /// <summary>
    /// Gets the socket locations that are probed, in order, on Unix-like hosts.
    /// </summary>
    /// <param name="environment">The host environment.</param>
    /// <returns>The candidate socket paths.</returns>
    public static IReadOnlyList<string> GetSocketCandidates(IDockerHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        var home = environment.HomeDirectory;
        var runtimeDir = environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        var candidates = new List<string> { DefaultUnixSocket };

        if (!string.IsNullOrWhiteSpace(runtimeDir))
        {
            candidates.Add(Path.Combine(runtimeDir, "docker.sock"));
        }

        candidates.Add(Path.Combine(home, ".docker", "run", "docker.sock"));      // Docker Desktop (macOS)
        candidates.Add(Path.Combine(home, ".docker", "desktop", "docker.sock"));  // Docker Desktop (Linux)
        candidates.Add(Path.Combine(home, ".colima", "default", "docker.sock"));  // Colima
        candidates.Add(Path.Combine(home, ".colima", "docker.sock"));
        candidates.Add(Path.Combine(home, ".orbstack", "run", "docker.sock"));    // OrbStack
        candidates.Add(Path.Combine(home, ".rd", "docker.sock"));                 // Rancher Desktop
        candidates.Add(Path.Combine(home, ".lima", "default", "sock", "docker.sock")); // Lima

        if (!string.IsNullOrWhiteSpace(runtimeDir))
        {
            candidates.Add(Path.Combine(runtimeDir, "podman", "podman.sock"));    // rootless Podman
        }

        candidates.Add("/run/podman/podman.sock");                                 // Podman (root)

        return candidates.Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Converts a Docker host string (<c>unix://</c>, <c>npipe://</c>, <c>tcp://</c>, <c>http(s)://</c> or a bare socket path)
    /// into the URI form understood by the Docker client.
    /// </summary>
    /// <param name="value">The host string.</param>
    /// <param name="environment">The host environment (used for <c>DOCKER_TLS_VERIFY</c>).</param>
    /// <param name="uri">The resulting URI.</param>
    /// <param name="problem">A description of why the value could not be used.</param>
    /// <returns>True when the value was converted; otherwise, false.</returns>
    public static bool TryParseEndpoint(string value, IDockerHostEnvironment environment, out Uri uri, out string problem)
    {
        ArgumentNullException.ThrowIfNull(environment);

        uri = null!;
        problem = string.Empty;

        if (string.IsNullOrWhiteSpace(value))
        {
            problem = "empty value";
            return false;
        }

        value = value.Trim();

        if (value.StartsWith('/'))
        {
            uri = ToUnixUri(value);
            return true;
        }

        var schemeSeparator = value.IndexOf("://", StringComparison.Ordinal);
        if (schemeSeparator <= 0)
        {
            // Docker treats "host:port" without a scheme as tcp.
            if (Uri.TryCreate("tcp://" + value, UriKind.Absolute, out _))
            {
                return TryParseEndpoint("tcp://" + value, environment, out uri, out problem);
            }

            problem = "not a valid endpoint";
            return false;
        }

        var scheme = value[..schemeSeparator].ToLowerInvariant();
        var rest = value[(schemeSeparator + 3)..];

        switch (scheme)
        {
            case "unix":
                if (rest.Length == 0)
                {
                    problem = "unix:// endpoint without a socket path";
                    return false;
                }

                uri = ToUnixUri(rest.StartsWith('/') ? rest : "/" + rest);
                return true;

            case "npipe":
            {
                var pipeName = rest.TrimEnd('/').Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
                if (string.IsNullOrEmpty(pipeName))
                {
                    problem = "npipe:// endpoint without a pipe name";
                    return false;
                }

                uri = new Uri($"npipe://./pipe/{pipeName}");
                return true;
            }

            case "tcp":
            case "http":
            case "https":
            {
                if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed) || string.IsNullOrEmpty(parsed.Host))
                {
                    problem = "not a valid TCP endpoint";
                    return false;
                }

                var tls = scheme == "https" ||
                          (scheme == "tcp" && !string.IsNullOrEmpty(environment.GetEnvironmentVariable("DOCKER_TLS_VERIFY")));
                var builder = new UriBuilder(parsed)
                {
                    Scheme = tls ? "https" : "http",
                    Port = parsed.Port > 0 ? parsed.Port : (tls ? 2376 : 2375)
                };

                uri = builder.Uri;
                return true;
            }

            case "ssh":
                problem = "ssh:// endpoints are not supported; use a Docker context with a unix socket or a tcp endpoint";
                return false;

            default:
                problem = $"unsupported scheme '{scheme}'";
                return false;
        }
    }

    /// <summary>
    /// Computes the directory name Docker uses for a context under <c>contexts/meta</c> (SHA-256 of the name).
    /// </summary>
    /// <param name="contextName">The context name.</param>
    /// <returns>The lowercase hexadecimal digest.</returns>
    public static string GetContextDirectoryName(string contextName)
    {
        ArgumentNullException.ThrowIfNull(contextName);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(contextName))).ToLowerInvariant();
    }

    private static DockerEndpoint? ResolveFromContext(IDockerHostEnvironment environment, List<string> searched)
    {
        var configDir = environment.GetEnvironmentVariable("DOCKER_CONFIG");
        if (string.IsNullOrWhiteSpace(configDir))
        {
            configDir = Path.Combine(environment.HomeDirectory, ".docker");
        }

        var contextName = environment.GetEnvironmentVariable("DOCKER_CONTEXT");
        if (string.IsNullOrWhiteSpace(contextName))
        {
            var configPath = Path.Combine(configDir, "config.json");
            if (environment.FileExists(configPath))
            {
                try
                {
                    using var document = JsonDocument.Parse(environment.ReadAllText(configPath));
                    if (document.RootElement.ValueKind == JsonValueKind.Object &&
                        document.RootElement.TryGetProperty("currentContext", out var current) &&
                        current.ValueKind == JsonValueKind.String)
                    {
                        contextName = current.GetString();
                    }
                }
                catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
                {
                    searched.Add($"{configPath} (unreadable: {ex.Message})");
                }
            }
        }

        if (string.IsNullOrWhiteSpace(contextName) ||
            string.Equals(contextName, "default", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var metaPath = Path.Combine(configDir, "contexts", "meta", GetContextDirectoryName(contextName), "meta.json");
        if (!environment.FileExists(metaPath))
        {
            searched.Add($"{metaPath} (context '{contextName}' metadata not found)");
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(environment.ReadAllText(metaPath));
            if (document.RootElement.TryGetProperty("Endpoints", out var endpoints) &&
                endpoints.TryGetProperty("docker", out var docker) &&
                docker.TryGetProperty("Host", out var host) &&
                host.ValueKind == JsonValueKind.String)
            {
                var hostValue = host.GetString();
                if (!string.IsNullOrWhiteSpace(hostValue))
                {
                    if (TryParseEndpoint(hostValue, environment, out var uri, out var problem))
                    {
                        return new DockerEndpoint(uri, $"Docker context '{contextName}'");
                    }

                    searched.Add($"{metaPath} (context '{contextName}' host '{hostValue}': {problem})");
                    return null;
                }
            }

            searched.Add($"{metaPath} (context '{contextName}' has no docker endpoint)");
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            searched.Add($"{metaPath} (unreadable: {ex.Message})");
        }

        return null;
    }

    private static Uri ToUnixUri(string socketPath)
    {
        return new Uri("unix://" + socketPath);
    }
}
