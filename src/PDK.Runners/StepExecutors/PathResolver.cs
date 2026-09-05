namespace PDK.Runners.StepExecutors;

using PDK.Core.Models;
using IContainerManager = PDK.Runners.IContainerManager;

/// <summary>
/// Resolves file paths and expands wildcard patterns in container environments.
/// </summary>
public static class PathResolver
{
    /// <summary>
    /// Resolves a path relative to the workspace root.
    /// </summary>
    /// <param name="path">The path to resolve (can be absolute or relative).</param>
    /// <param name="workspaceRoot">The workspace root path to use as a base for relative paths.</param>
    /// <returns>The resolved absolute path.</returns>
    /// <remarks>
    /// If the path is already rooted (starts with '/'), it is returned as-is.
    /// Otherwise, it is combined with the workspace root.
    /// </remarks>
    public static string ResolvePath(string path, string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return workspaceRoot;
        }

        var normalizedPath = path.Trim().Replace('\\', '/');

        if (normalizedPath.StartsWith('/'))
        {
            return NormalizePath(normalizedPath);
        }

        if (normalizedPath.StartsWith("./", StringComparison.Ordinal))
        {
            normalizedPath = normalizedPath[2..];
        }

        var combined = $"{workspaceRoot.TrimEnd('/')}/{normalizedPath}";
        return NormalizePath(combined);
    }

    /// <summary>
    /// Resolves the working directory for a step, combining the execution context
    /// and step-specific working directory.
    /// </summary>
    /// <param name="step">The step containing an optional working directory.</param>
    /// <param name="context">The execution context containing the container workspace path.</param>
    /// <returns>The resolved absolute working directory path in the container.</returns>
    public static string ResolveWorkingDirectory(Step step, ExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(context);

        return ResolvePath(step.WorkingDirectory ?? string.Empty, context.ContainerWorkspacePath);
    }

    /// <summary>
    /// Expands a wildcard pattern to matching file paths in the container.
    /// </summary>
    /// <param name="containerManager">The container manager to use for command execution.</param>
    /// <param name="containerId">The ID of the container.</param>
    /// <param name="pattern">The wildcard pattern to expand (e.g., "**/*.csproj", "*.sln").</param>
    /// <param name="workingDirectory">The working directory to search from (defaults to current directory).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Matching file paths relative to the working directory (forward slashes, sorted), or an empty list.</returns>
    public static Task<IReadOnlyList<string>> ExpandWildcardAsync(
        IContainerManager containerManager,
        string containerId,
        string pattern,
        string workingDirectory = ".",
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        }

        return ExpandWildcardsAsync(containerManager, containerId, new[] { pattern }, workingDirectory, cancellationToken);
    }

    /// <summary>
    /// Expands several wildcard patterns (with optional <c>!</c> exclusions) to matching file paths in the container.
    /// </summary>
    /// <param name="containerManager">The container manager to use for command execution.</param>
    /// <param name="containerId">The ID of the container.</param>
    /// <param name="patterns">The include patterns and <c>!</c>-prefixed exclude patterns.</param>
    /// <param name="workingDirectory">The working directory to search from.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Matching file paths relative to the working directory (forward slashes, sorted), or an empty list.</returns>
    /// <remarks>
    /// Files are listed with <c>find</c> (skipping <c>.git</c>) and matched here with proper glob semantics:
    /// <c>**</c> spans directories (including zero directories, so <c>dir/**/x</c> matches <c>dir/x</c>) while
    /// <c>*</c> and <c>?</c> stay within one path segment.
    /// </remarks>
    public static async Task<IReadOnlyList<string>> ExpandWildcardsAsync(
        IContainerManager containerManager,
        string containerId,
        IReadOnlyList<string> patterns,
        string workingDirectory = ".",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(containerManager);
        ArgumentNullException.ThrowIfNull(patterns);

        var cleaned = patterns.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()).ToList();
        if (cleaned.Count == 0 || cleaned.All(p => p.StartsWith('!')))
        {
            return Array.Empty<string>();
        }

        try
        {
            var findCommand = BuildFindCommand(cleaned);

            var result = await containerManager.ExecuteCommandAsync(
                containerId,
                findCommand,
                workingDirectory,
                null,
                cancellationToken).ConfigureAwait(false);

            if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.StandardOutput))
            {
                return Array.Empty<string>();
            }

            var paths = result.StandardOutput
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .Where(p => p.Length > 0);

            return GlobPattern.Filter(paths, cleaned, ignoreCase: false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Builds the <c>find</c> command that lists candidate files for the patterns. When every include pattern
    /// ends with the same file-name glob, a <c>-name</c> filter keeps the listing small.
    /// </summary>
    internal static string BuildFindCommand(IReadOnlyList<string> patterns)
    {
        var includes = patterns.Where(p => !p.StartsWith('!')).Select(GlobPattern.Normalize).ToList();
        var leaves = includes
            .Select(p => p.Contains('/') ? p[(p.LastIndexOf('/') + 1)..] : p)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var nameFilter = string.Empty;
        if (leaves.Count == 1 && leaves[0].Length > 0 && !leaves[0].Contains("**", StringComparison.Ordinal))
        {
            nameFilter = $" -name {ShellQuote.Posix(leaves[0])}";
        }

        return $"find . -path ./.git -prune -o -type f{nameFilter} -print";
    }

    /// <summary>
    /// Normalizes a path by removing redundant slashes and resolving relative components.
    /// </summary>
    private static string NormalizePath(string path)
    {
        while (path.Contains("//", StringComparison.Ordinal))
        {
            path = path.Replace("//", "/", StringComparison.Ordinal);
        }

        var components = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var normalized = new List<string>();

        foreach (var component in components)
        {
            if (component == ".")
            {
                continue;
            }

            if (component == "..")
            {
                if (normalized.Count > 0)
                {
                    normalized.RemoveAt(normalized.Count - 1);
                }
            }
            else
            {
                normalized.Add(component);
            }
        }

        var result = string.Join("/", normalized);
        return path.StartsWith('/') ? "/" + result : result;
    }
}
