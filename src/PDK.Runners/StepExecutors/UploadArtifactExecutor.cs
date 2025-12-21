using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PDK.Core.Artifacts;
using PDK.Core.Models;
using PDK.Runners.Utilities;

namespace PDK.Runners.StepExecutors;

/// <summary>
/// Executes artifact upload steps by copying files from a container
/// and uploading them to the artifact storage.
/// </summary>
public class UploadArtifactExecutor : IStepExecutor
{
    private readonly IArtifactManager _artifactManager;
    private readonly ILogger<UploadArtifactExecutor> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="UploadArtifactExecutor"/> class.
    /// </summary>
    /// <param name="artifactManager">The artifact manager for storing artifacts.</param>
    /// <param name="logger">The logger for diagnostics.</param>
    public UploadArtifactExecutor(
        IArtifactManager artifactManager,
        ILogger<UploadArtifactExecutor> logger)
    {
        _artifactManager = artifactManager ?? throw new ArgumentNullException(nameof(artifactManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public string StepType => "uploadartifact";

    /// <inheritdoc/>
    public async Task<StepExecutionResult> ExecuteAsync(
        Step step,
        ExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var startTime = DateTimeOffset.Now;
        var stopwatch = Stopwatch.StartNew();
        string? tempPath = null;

        try
        {
            // Validate artifact definition
            if (step.Artifact == null)
            {
                throw new InvalidOperationException(
                    "Artifact definition is required for upload artifact step.");
            }

            if (step.Artifact.Operation != ArtifactOperation.Upload)
            {
                throw new InvalidOperationException(
                    $"Expected Upload operation but got {step.Artifact.Operation}.");
            }

            // Validate artifact context
            if (context.ArtifactContext == null)
            {
                throw new InvalidOperationException(
                    "ArtifactContext is required for artifact operations.");
            }

            var artifact = step.Artifact;
            var artifactContext = context.ArtifactContext;

            _logger.LogInformation(
                "Uploading artifact '{ArtifactName}' with {PatternCount} pattern(s)",
                artifact.Name,
                artifact.Patterns.Length);

            // Step 1: Find files in container matching patterns
            var basePath = artifact.TargetPath ?? context.ContainerWorkspacePath;
            var files = await FindFilesInContainerAsync(
                context,
                basePath,
                artifact.Patterns,
                cancellationToken);

            _logger.LogDebug("Found {FileCount} files matching patterns", files.Count);

            // Handle no files found
            if (files.Count == 0)
            {
                return HandleNoFilesFound(
                    artifact,
                    step.Name,
                    startTime,
                    stopwatch);
            }

            // Step 2: Create temp directory and copy files from container
            tempPath = Path.Combine(Path.GetTempPath(), $"pdk-artifact-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempPath);

            _logger.LogDebug("Copying {FileCount} files from container to {TempPath}", files.Count, tempPath);

            await CopyFilesFromContainerAsync(
                context,
                basePath,
                files,
                tempPath,
                cancellationToken);

            // Step 3: Upload to artifact manager
            _logger.LogDebug("Uploading files to artifact storage");

            // Build relative patterns from container paths
            // Files are extracted to tempPath with relative paths from basePath
            var patterns = files.Select(f =>
            {
                if (f.StartsWith(basePath))
                {
                    return f[(basePath.Length + 1)..]; // Remove basePath prefix to get relative path
                }
                return Path.GetFileName(f); // Fallback to just filename
            }).ToArray();

            // Create a modified artifact context pointing to the temp directory
            // where we extracted the files from the container
            var uploadContext = new ArtifactContext
            {
                WorkspacePath = tempPath,
                RunId = artifactContext.RunId,
                JobName = artifactContext.JobName,
                StepIndex = artifactContext.StepIndex,
                StepName = artifactContext.StepName
            };

            var uploadResult = await _artifactManager.UploadAsync(
                artifact.Name,
                patterns,
                uploadContext,
                artifact.Options,
                progress: null,
                cancellationToken);

            stopwatch.Stop();

            _logger.LogInformation(
                "Successfully uploaded artifact '{ArtifactName}': {FileCount} files, {TotalSize} bytes",
                artifact.Name,
                uploadResult.FileCount,
                uploadResult.TotalSizeBytes);

            return new StepExecutionResult
            {
                StepName = step.Name,
                Success = true,
                ExitCode = 0,
                Output = $"Uploaded {uploadResult.FileCount} files to artifact '{artifact.Name}' " +
                         $"({FormatBytes(uploadResult.TotalSizeBytes)})",
                ErrorOutput = string.Empty,
                Duration = stopwatch.Elapsed,
                StartTime = startTime,
                EndTime = DateTimeOffset.Now
            };
        }
        catch (ArtifactException ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Artifact upload failed: {Message}", ex.Message);

            return new StepExecutionResult
            {
                StepName = step.Name,
                Success = false,
                ExitCode = 1,
                Output = string.Empty,
                ErrorOutput = $"Artifact upload failed: {ex.Message}",
                Duration = stopwatch.Elapsed,
                StartTime = startTime,
                EndTime = DateTimeOffset.Now
            };
        }
        catch (ContainerException ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Container operation failed: {Message}", ex.Message);

            return new StepExecutionResult
            {
                StepName = step.Name,
                Success = false,
                ExitCode = 1,
                Output = string.Empty,
                ErrorOutput = $"Container operation failed: {ex.Message}",
                Duration = stopwatch.Elapsed,
                StartTime = startTime,
                EndTime = DateTimeOffset.Now
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Unexpected error during artifact upload: {Message}", ex.Message);

            return new StepExecutionResult
            {
                StepName = step.Name,
                Success = false,
                ExitCode = 1,
                Output = string.Empty,
                ErrorOutput = $"Unexpected error: {ex.Message}",
                Duration = stopwatch.Elapsed,
                StartTime = startTime,
                EndTime = DateTimeOffset.Now
            };
        }
        finally
        {
            // Always cleanup temp directory
            if (tempPath != null && Directory.Exists(tempPath))
            {
                try
                {
                    Directory.Delete(tempPath, recursive: true);
                    _logger.LogDebug("Cleaned up temp directory: {TempPath}", tempPath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to cleanup temp directory: {TempPath}", tempPath);
                }
            }
        }
    }

    /// <summary>
    /// Finds files in the container matching the specified patterns.
    /// </summary>
    private async Task<List<string>> FindFilesInContainerAsync(
        ExecutionContext context,
        string basePath,
        string[] patterns,
        CancellationToken cancellationToken)
    {
        var files = new List<string>();

        // Build find command for each pattern
        // Convert glob patterns to find command patterns
        foreach (var pattern in patterns)
        {
            // Skip exclusion patterns for now (handled by artifact manager)
            if (pattern.StartsWith("!"))
            {
                continue;
            }

            // Build find command based on pattern
            var findCommand = BuildFindCommand(basePath, pattern);

            _logger.LogDebug("Executing find command: {Command}", findCommand);

            var result = await context.ContainerManager.ExecuteCommandAsync(
                context.ContainerId,
                findCommand,
                workingDirectory: null,
                environment: null,
                cancellationToken);

            if (result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.StandardOutput))
            {
                var foundFiles = result.StandardOutput
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Select(f => f.Trim())
                    .Where(f => !string.IsNullOrEmpty(f))
                    .ToList();

                files.AddRange(foundFiles);
            }
        }

        // Remove duplicates and sort
        return files.Distinct().OrderBy(f => f).ToList();
    }

    /// <summary>
    /// Builds a find command for the given glob pattern.
    /// </summary>
    private static string BuildFindCommand(string basePath, string pattern)
    {
        // Handle different glob patterns:
        // **/*.dll -> find . -name "*.dll" -type f
        // bin/** -> find bin -type f
        // *.log -> find . -maxdepth 1 -name "*.log" -type f

        string searchPath;
        string namePattern;
        string maxDepthFlag = "";

        if (pattern.StartsWith("**/"))
        {
            // Recursive search from base path
            searchPath = basePath;
            namePattern = pattern[3..]; // Remove **/
        }
        else if (pattern.Contains("/**/"))
        {
            // Directory prefix with recursive search
            var parts = pattern.Split("/**/", 2);
            searchPath = Path.Combine(basePath, parts[0]).Replace('\\', '/');
            namePattern = parts[1];
        }
        else if (pattern.Contains("/"))
        {
            // Directory path included
            var lastSlash = pattern.LastIndexOf('/');
            var dir = pattern[..lastSlash];
            searchPath = Path.Combine(basePath, dir).Replace('\\', '/');
            namePattern = pattern[(lastSlash + 1)..];
            if (!namePattern.Contains('*') && !namePattern.Contains('?'))
            {
                // Exact filename
                return $"find {searchPath} -name \"{namePattern}\" -type f 2>/dev/null";
            }
        }
        else
        {
            // Simple pattern in current directory only
            searchPath = basePath;
            namePattern = pattern;
            maxDepthFlag = "-maxdepth 1 ";
        }

        // Handle patterns that are just directory names (e.g., "bin/**")
        if (namePattern == "*" || namePattern == "**")
        {
            return $"find {searchPath} {maxDepthFlag}-type f 2>/dev/null";
        }

        return $"find {searchPath} {maxDepthFlag}-name \"{namePattern}\" -type f 2>/dev/null";
    }

    /// <summary>
    /// Copies files from the container to a local directory.
    /// </summary>
    private async Task CopyFilesFromContainerAsync(
        ExecutionContext context,
        string containerBasePath,
        List<string> files,
        string targetPath,
        CancellationToken cancellationToken)
    {
        // Get archive from container for each file
        // For better performance, we could batch files by directory
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogDebug("Copying file from container: {File}", file);

            try
            {
                // Get the tar archive for this file
                using var tarStream = await context.ContainerManager.GetArchiveFromContainerAsync(
                    context.ContainerId,
                    file,
                    cancellationToken);

                // Calculate relative path for extraction
                var relativePath = file;
                if (file.StartsWith(containerBasePath))
                {
                    relativePath = file[(containerBasePath.Length + 1)..];
                }

                // Extract tar to temp path
                var fileDir = Path.GetDirectoryName(Path.Combine(targetPath, relativePath));
                if (!string.IsNullOrEmpty(fileDir))
                {
                    Directory.CreateDirectory(fileDir);
                }

                await TarArchiveHelper.ExtractTarAsync(tarStream, targetPath, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to copy file from container: {File}", file);
                throw new ContainerException($"Failed to copy file '{file}' from container: {ex.Message}", ex);
            }
        }
    }

    /// <summary>
    /// Handles the case when no files match the patterns.
    /// </summary>
    private StepExecutionResult HandleNoFilesFound(
        ArtifactDefinition artifact,
        string stepName,
        DateTimeOffset startTime,
        Stopwatch stopwatch)
    {
        stopwatch.Stop();

        var behavior = artifact.Options?.IfNoFilesFound ?? IfNoFilesFound.Error;
        var message = $"No files found matching patterns: {string.Join(", ", artifact.Patterns)}";

        switch (behavior)
        {
            case IfNoFilesFound.Error:
                _logger.LogError(message);
                return new StepExecutionResult
                {
                    StepName = stepName,
                    Success = false,
                    ExitCode = 1,
                    Output = string.Empty,
                    ErrorOutput = message,
                    Duration = stopwatch.Elapsed,
                    StartTime = startTime,
                    EndTime = DateTimeOffset.Now
                };

            case IfNoFilesFound.Warn:
                _logger.LogWarning(message);
                return new StepExecutionResult
                {
                    StepName = stepName,
                    Success = true,
                    ExitCode = 0,
                    Output = $"Warning: {message}",
                    ErrorOutput = string.Empty,
                    Duration = stopwatch.Elapsed,
                    StartTime = startTime,
                    EndTime = DateTimeOffset.Now
                };

            case IfNoFilesFound.Ignore:
            default:
                _logger.LogDebug(message);
                return new StepExecutionResult
                {
                    StepName = stepName,
                    Success = true,
                    ExitCode = 0,
                    Output = "No files to upload (ignored)",
                    ErrorOutput = string.Empty,
                    Duration = stopwatch.Elapsed,
                    StartTime = startTime,
                    EndTime = DateTimeOffset.Now
                };
        }
    }

    /// <summary>
    /// Formats a byte count as a human-readable string.
    /// </summary>
    private static string FormatBytes(long bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB"];
        int order = 0;
        double size = bytes;

        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }

        return $"{size:0.##} {sizes[order]}";
    }
}
