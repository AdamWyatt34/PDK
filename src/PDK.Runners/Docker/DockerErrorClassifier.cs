using System.ComponentModel;
using System.Net.Sockets;
using Docker.DotNet;
using PDK.Core.Docker;

namespace PDK.Runners.Docker;

/// <summary>
/// Turns the exceptions raised while contacting the Docker daemon into a <see cref="DockerErrorType"/>
/// and a message that names the endpoint that was tried.
/// </summary>
internal static class DockerErrorClassifier
{
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
    /// <returns>The error category and a user-facing message.</returns>
    public static (DockerErrorType Type, string Message) Classify(Exception exception, DockerEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(endpoint);

        foreach (var current in Flatten(exception))
        {
            switch (current)
            {
                case SocketException socket:
                    if (IsConnectionRefused(socket))
                    {
                        return (DockerErrorType.NotRunning, NotRunningMessage(endpoint, "connection refused"));
                    }

                    if (IsNotFound(socket))
                    {
                        return endpoint.IsLocal
                            ? (DockerErrorType.NotInstalled, NotFoundMessage(endpoint))
                            : (DockerErrorType.NotRunning, NotRunningMessage(endpoint, socket.Message));
                    }

                    if (IsAccessDenied(socket))
                    {
                        return (DockerErrorType.PermissionDenied, PermissionMessage(endpoint));
                    }

                    return (DockerErrorType.NotRunning, NotRunningMessage(endpoint, socket.Message));

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
            }
        }

        var text = string.Join(" | ", Flatten(exception).Select(e => e.Message));

        if (ContainsAny(text, "Connection refused", "No connection could be made", "actively refused"))
        {
            return (DockerErrorType.NotRunning, NotRunningMessage(endpoint, "connection refused"));
        }

        if (ContainsAny(text, "No such file or directory", "cannot find the file", "could not find", "not found", "does not exist"))
        {
            return (DockerErrorType.NotInstalled, NotFoundMessage(endpoint));
        }

        if (ContainsAny(text, "Permission denied", "Access is denied", "access denied", "Unauthorized"))
        {
            return (DockerErrorType.PermissionDenied, PermissionMessage(endpoint));
        }

        return (DockerErrorType.Unknown,
            $"Unknown error checking Docker availability at {endpoint.Uri}: {exception.Message}");
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
}
