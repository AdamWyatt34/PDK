using System.Diagnostics;
using System.Collections.Concurrent;

namespace PDK.Core.Expressions;

/// <summary>
/// Best-effort git metadata for a workspace (commit, branch, remote repository), used to populate
/// the <c>github.*</c> and <c>Build.*</c> contexts. Never throws; missing values are empty strings.
/// </summary>
public sealed record GitInfo
{
    private static readonly ConcurrentDictionary<string, GitInfo> Cache = new(StringComparer.Ordinal);

    /// <summary>Full commit SHA, or empty.</summary>
    public string Sha { get; init; } = string.Empty;

    /// <summary>Branch name (e.g. <c>main</c>), or empty when detached/unknown.</summary>
    public string Branch { get; init; } = string.Empty;

    /// <summary>Full ref (e.g. <c>refs/heads/main</c>), or empty.</summary>
    public string Ref { get; init; } = string.Empty;

    /// <summary><c>owner/repo</c> derived from the origin remote, or empty.</summary>
    public string Repository { get; init; } = string.Empty;

    /// <summary>Remote URL of origin, or empty.</summary>
    public string RemoteUrl { get; init; } = string.Empty;

    /// <summary>Whether the directory is inside a git work tree.</summary>
    public bool IsRepository { get; init; }

    /// <summary>Owner part of <see cref="Repository"/>.</summary>
    public string Owner => Repository.Contains('/') ? Repository[..Repository.IndexOf('/')] : string.Empty;

    /// <summary>Repository name part of <see cref="Repository"/>.</summary>
    public string Name => Repository.Contains('/') ? Repository[(Repository.IndexOf('/') + 1)..] : Repository;

    /// <summary>Short (7 char) SHA.</summary>
    public string ShortSha => Sha.Length >= 7 ? Sha[..7] : Sha;

    /// <summary>An empty instance for non-repository directories.</summary>
    public static GitInfo Empty { get; } = new();

    /// <summary>Reads git metadata for <paramref name="workspace"/> (cached per path).</summary>
    public static GitInfo Read(string? workspace)
    {
        var path = string.IsNullOrEmpty(workspace) ? Directory.GetCurrentDirectory() : workspace;
        return Cache.GetOrAdd(path, ReadUncached);
    }

    /// <summary>Clears the cache (tests).</summary>
    public static void ClearCache() => Cache.Clear();

    private static GitInfo ReadUncached(string path)
    {
        if (!Directory.Exists(path))
        {
            return Empty;
        }

        var inside = Run(path, "rev-parse", "--is-inside-work-tree");
        if (!string.Equals(inside, "true", StringComparison.OrdinalIgnoreCase))
        {
            return Empty;
        }

        var sha = Run(path, "rev-parse", "HEAD") ?? string.Empty;
        var branch = Run(path, "symbolic-ref", "--short", "-q", "HEAD") ?? string.Empty;
        var remote = Run(path, "config", "--get", "remote.origin.url") ?? string.Empty;
        var reference = branch.Length > 0 ? $"refs/heads/{branch}" : (sha.Length > 0 ? sha : string.Empty);

        return new GitInfo
        {
            IsRepository = true,
            Sha = sha,
            Branch = branch,
            Ref = reference,
            RemoteUrl = remote,
            Repository = ParseRepository(remote)
        };
    }

    /// <summary>Extracts <c>owner/repo</c> from an https, ssh or scp-style remote URL.</summary>
    public static string ParseRepository(string remote)
    {
        if (string.IsNullOrWhiteSpace(remote))
        {
            return string.Empty;
        }

        var value = remote.Trim();
        if (value.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            value = value[..^4];
        }

        // scp-like: git@github.com:owner/repo
        var colon = value.IndexOf(':');
        if (!value.Contains("://", StringComparison.Ordinal) && colon > 0)
        {
            value = value[(colon + 1)..];
        }
        else if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            value = uri.AbsolutePath;
        }

        value = value.Trim('/');
        var parts = value.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? $"{parts[^2]}/{parts[^1]}" : string.Empty;
    }

    private static string? Run(string workingDirectory, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo("git")
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var a in args)
            {
                psi.ArgumentList.Add(a);
            }

            using var process = Process.Start(psi);
            if (process == null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(5000))
            {
                try { process.Kill(true); } catch (InvalidOperationException) { }
                return null;
            }

            return process.ExitCode == 0 ? output.Trim() : null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return null;
        }
    }
}
