using System.ComponentModel;
using System.Net.Sockets;
using System.Text;
using Docker.DotNet;
using PDK.Core.Docker;

namespace PDK.Runners.Docker;

/// <summary>
/// Turns the exceptions raised while contacting the Docker daemon into a <see cref="DockerErrorType"/>
/// and a message that names the endpoint that was tried.
/// </summary>
internal static class DockerErrorClassifier
{
    /// <summary>
    /// The longest Unix socket path the Docker client can address: <c>sun_path</c> minus the terminator on
    /// the most restrictive platform the client supports.
    /// </summary>
    internal const int MaxUnixSocketPathLength = 91;

    private const int LinuxNoSuchFile = 2;
    private const int LinuxPermissionDenied = 13;
    private const int LinuxConnectionRefused = 111;
    private const int WindowsFileNotFound = 2;
    private const int WindowsAccessDenied = 5;
    private const int WindowsConnectionRefused = 10061;
    private const int WindowsPermissionDenied = 10013;

    /// <summary>
    /// Classifies an exception chain.
    /// </summary>
    /// <param name="exception">The exception thrown by the Docker client.</param>
    /// <param name="endpoint">The endpoint that was contacted.</param>
    /// <param name="environment">The host environment, used to check whether a local socket exists.</param>
    /// <returns>The error category and a user-facing message.</returns>
    public static (DockerErrorType Type, string Message) Classify(
        Exception exception,
        DockerEndpoint endpoint,
        IDockerHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(environment);

        // Whether a local socket is on disk decides between "not installed" and "not running" where the
        // socket stack alone cannot: Windows reports a missing AF_UNIX path as a refused connection, and
        // each platform surfaces ENOENT through a different exception type.
        var socketMissing = LocalSocketIsMissing(endpoint, environment);

        foreach (var current in Flatten(exception))
        {
            switch (current)
            {
                case SocketException socket:
                    if (IsAccessDenied(socket))
                    {
                        return (DockerErrorType.PermissionDenied, PermissionMessage(endpoint));
                    }

                    if (socketMissing || (IsNotFound(socket) && endpoint.IsLocal))
                    {
                        return (DockerErrorType.NotInstalled, NotFoundMessage(endpoint));
                    }

                    return (DockerErrorType.NotRunning,
                        NotRunningMessage(endpoint, IsConnectionRefused(socket) ? "connection refused" : socket.Message));

                case FileNotFoundException:
                case DirectoryNotFoundException:
                    return (DockerErrorType.NotInstalled, NotFoundMessage(endpoint));

                case UnauthorizedAccessException:
                    return (DockerErrorType.PermissionDenied, PermissionMessage(endpoint));

                case Win32Exception win32 when win32.NativeErrorCode == WindowsFileNotFound:
                    return (DockerErrorType.NotInstalled, NotFoundMessage(endpoint));

                case Win32Exception win32 when win32.NativeErrorCode == WindowsAccessDenied:
                    return (DockerErrorType.PermissionDenied, PermissionMessage(endpoint));

                case TimeoutException timeout:
                    return (DockerErrorType.NotRunning, $"Docker daemon at {endpoint.Uri} did not respond: {timeout.Message}");

                case DockerApiException api:
                    return (DockerErrorType.Unknown,
                        $"Docker daemon at {endpoint.Uri} returned an error ({(int)api.StatusCode}): {api.Message}");

                case ArgumentException when SocketPathTooLong(endpoint):
                    return (DockerErrorType.Unknown, SocketPathTooLongMessage(endpoint));
            }
        }

        var text = string.Join(" | ", Flatten(exception).Select(e => e.Message));

        if (ContainsAny(text, "Connection refused", "No connection could be made", "actively refused"))
        {
            return socketMissing
                ? (DockerErrorType.NotInstalled, NotFoundMessage(endpoint))
                : (DockerErrorType.NotRunning, NotRunningMessage(endpoint, "connection refused"));
        }

        if (ContainsAny(text, "No such file or directory", "cannot find the file", "could not find", "not found", "does not exist"))
        {
            return (DockerErrorType.NotInstalled, NotFoundMessage(endpoint));
        }

        if (ContainsAny(text, "Permission denied", "Access is denied", "access denied", "Unauthorized"))
        {
            return (DockerErrorType.PermissionDenied, PermissionMessage(endpoint));
        }

        // The connection attempt failed for a reason this platform's socket stack reports in a way not
        // recognised above; a socket that is not on disk is still the most useful thing to report.
        if (socketMissing && IsTransportFailure(exception))
        {
            return (DockerErrorType.NotInstalled, NotFoundMessage(endpoint));
        }

        var chain = string.Join(" -> ", Flatten(exception).Select(e => $"{e.GetType().Name}: {e.Message}"));
        return (DockerErrorType.Unknown, $"Unknown error checking Docker availability at {endpoint.Uri}: {chain}");
    }

    private static bool LocalSocketIsMissing(DockerEndpoint endpoint, IDockerHostEnvironment environment)
    {
        return endpoint.SocketPath is { Length: > 0 } path
               && !environment.FileExists(path)
               && !environment.DirectoryExists(path);
    }

    private static bool SocketPathTooLong(DockerEndpoint endpoint)
    {
        return endpoint.SocketPath is { Length: > 0 } path
               && Encoding.UTF8.GetByteCount(path) > MaxUnixSocketPathLength;
    }

    private static bool IsTransportFailure(Exception exception)
    {
        return Flatten(exception).Any(e => e is HttpRequestException or IOException or Win32Exception);
    }

    private static IEnumerable<Exception> Flatten(Exception exception)
    {
        var seen = new HashSet<Exception>();
        var stack = new Stack<Exception>();
        stack.Push(exception);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!seen.Add(current))
            {
                continue;
            }

            yield return current;

            if (current is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions.Reverse())
                {
                    stack.Push(inner);
                }
            }
            else if (current.InnerException != null)
            {
                stack.Push(current.InnerException);
            }
        }
    }

    private static bool IsConnectionRefused(SocketException socket)
    {
        return socket.SocketErrorCode == SocketError.ConnectionRefused ||
               socket.NativeErrorCode is LinuxConnectionRefused or WindowsConnectionRefused;
    }

    private static bool IsNotFound(SocketException socket)
    {
        return socket.SocketErrorCode == SocketError.AddressNotAvailable ||
               socket.NativeErrorCode == LinuxNoSuchFile ||
               socket.Message.Contains("No such file", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAccessDenied(SocketException socket)
    {
        return socket.SocketErrorCode == SocketError.AccessDenied ||
               socket.NativeErrorCode is LinuxPermissionDenied or WindowsPermissionDenied;
    }

    private static bool ContainsAny(string text, params string[] fragments)
    {
        return fragments.Any(f => text.Contains(f, StringComparison.OrdinalIgnoreCase));
    }

    private static string NotRunningMessage(DockerEndpoint endpoint, string detail)
    {
        return $"Docker daemon is not running: {detail} at {endpoint.Uri} ({endpoint.Source}). " +
               "Start Docker (Docker Desktop, 'sudo systemctl start docker', 'colima start', ...) " +
               "or point DOCKER_HOST / your Docker context at the daemon.";
    }

    private static string NotFoundMessage(DockerEndpoint endpoint)
    {
        var searched = endpoint.SearchedPaths.Count > 0
            ? $" Searched: {string.Join(", ", endpoint.SearchedPaths)}."
            : string.Empty;

        return $"Docker is not installed or its socket was not found at {endpoint.Uri} ({endpoint.Source}).{searched} " +
               "Install Docker (https://docs.docker.com/get-docker/), start Docker Desktop, " +
               "or set DOCKER_HOST to the daemon endpoint.";
    }

    private static string PermissionMessage(DockerEndpoint endpoint)
    {
        return $"Permission denied accessing Docker at {endpoint.Uri} ({endpoint.Source}). " +
               "On Linux add your user to the docker group ('sudo usermod -aG docker $USER', then log out and back in); " +
               "on Windows make sure Docker Desktop is running for your user.";
    }

    private static string SocketPathTooLongMessage(DockerEndpoint endpoint)
    {
        var path = endpoint.SocketPath ?? string.Empty;
        return $"The Docker socket path {path} ({endpoint.Source}) is {Encoding.UTF8.GetByteCount(path)} bytes long, " +
               $"but Unix socket paths are limited to {MaxUnixSocketPathLength} bytes. " +
               "Point DOCKER_HOST or your Docker context at a shorter path (for example a symlink such as /var/run/docker.sock).";
    }
}
