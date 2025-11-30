namespace PDK.Runners.StepExecutors;

using PDK.Core.Models;
using IContainerManager = PDK.Runners.IContainerManager;
using PDK.Runners.Models;

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

        var normalizedPath = path.Trim();

        // If absolute path (starts with /), use as-is
        if (normalizedPath.StartsWith('/'))
        {
            return NormalizePath(normalizedPath);
        }

        // Remove leading ./ if present
        if (normalizedPath.StartsWith("./"))
        {
            normalizedPath = normalizedPath.Substring(2);
        }

        // Combine with workspace root
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
    /// <remarks>
    /// If the step specifies a working directory, it is resolved relative to the container workspace path.
    /// Otherwise, the context's container workspace path is used.
    /// </remarks>
    public static string ResolveWorkingDirectory(Step step, ExecutionContext context)
    {
        if (string.IsNullOrWhiteSpace(step.WorkingDirectory))
        {
            return context.ContainerWorkspacePath;
        }

        var workingDir = step.WorkingDirectory.Trim();

        // If absolute path, use as-is
        if (workingDir.StartsWith('/'))
        {
            return NormalizePath(workingDir);
        }

        // Remove leading ./ if present
        if (workingDir.StartsWith("./"))
        {
            workingDir = workingDir.Substring(2);
        }

        // Combine with workspace path
        var combined = $"{context.ContainerWorkspacePath.TrimEnd('/')}/{workingDir}";
        return NormalizePath(combined);
    }

    /// <summary>
    /// Expands wildcard patterns to matching file paths in the container.
    /// </summary>
    /// <param name="containerManager">The container manager to use for command execution.</param>
    /// <param name="containerId">The ID of the container.</param>
    /// <param name="pattern">The wildcard pattern to expand (e.g., "**/*.csproj", "*.sln").</param>
    /// <param name="workingDirectory">The working directory to search from (defaults to current directory).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of matching file paths, or an empty list if no matches found.</returns>
    /// <remarks>
    /// <para>Uses the container's 'find' command to expand wildcards accurately.</para>
    /// <para>Supported patterns:</para>
    /// <list type="bullet">
    /// <item><description>**/*.csproj - All .csproj files recursively</description></item>
    /// <item><description>*.sln - Solution files in current directory</description></item>
    /// <item><description>src/**/*.cs - All .cs files under src/ recursively</description></item>
    /// </list>
    /// </remarks>
    public static async Task<IReadOnlyList<string>> ExpandWildcardAsync(
        IContainerManager containerManager,
        string containerId,
        string pattern,
        string workingDirectory = ".",
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return Array.Empty<string>();
        }

        try
        {
            // Convert glob pattern to find-compatible pattern
            var findPattern = ConvertGlobToFindPattern(pattern);

            // Execute find command in the container
            var result = await containerManager.ExecuteCommandAsync(
                containerId,
                $"find . -path '{findPattern}' -type f",
                workingDirectory,
                null,
                cancellationToken);

            if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.StandardOutput))
            {
                return Array.Empty<string>();
            }

            // Parse output into list of paths
            var paths = result.StandardOutput
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToList();

            return paths.AsReadOnly();
        }
        catch
        {
            // Return empty list on any error
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Normalizes a path by removing redundant slashes and resolving relative components.
    /// </summary>
    /// <param name="path">The path to normalize.</param>
    /// <returns>The normalized path.</returns>
    private static string NormalizePath(string path)
    {
        // Remove double slashes
        while (path.Contains("//"))
        {
            path = path.Replace("//", "/");
        }

        // Split path into components
        var components = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var normalized = new List<string>();

        foreach (var component in components)
        {
            if (component == ".")
            {
                // Skip current directory references
                continue;
            }
            else if (component == "..")
            {
                // Go up one directory (remove last component if exists)
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

        // Reconstruct path (preserve leading slash for absolute paths)
        var result = string.Join("/", normalized);
        return path.StartsWith('/') ? "/" + result : result;
    }

    /// <summary>
    /// Converts a glob pattern to a find-compatible pattern.
    /// </summary>
    /// <param name="pattern">The glob pattern (e.g., "**/*.csproj").</param>
    /// <returns>A find-compatible pattern (e.g., "*/*.csproj").</returns>
    private static string ConvertGlobToFindPattern(string pattern)
    {
        // Remove leading ./ if present
        if (pattern.StartsWith("./"))
        {
            pattern = pattern.Substring(2);
        }

        // If pattern doesn't start with *, add ./ prefix for find
        if (!pattern.StartsWith('*') && !pattern.StartsWith("./"))
        {
            pattern = "./" + pattern;
        }

        return pattern;
    }
}
