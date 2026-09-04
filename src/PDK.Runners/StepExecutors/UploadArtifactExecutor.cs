using System.Diagnostics;
using Docker.DotNet;
using Microsoft.Extensions.Logging;
using PDK.Core.Artifacts;
using PDK.Core.ErrorHandling;
using PDK.Core.Models;
using PDK.Runners.Utilities;

namespace PDK.Runners.StepExecutors;

/// <summary>
/// Executes artifact upload steps by copying the matched paths out of the container
/// (one archive per matched path, preserving the directory structure) and handing them
/// to the artifact manager.
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
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(context);

        var startTime = DateTimeOffset.Now;
        var stopwatch = Stopwatch.StartNew();
        string? stagingRoot = null;

        try
        {
            if (step.Artifact == null)
            {
                return Failure(step, "Artifact definition is required for upload artifact step.", startTime, stopwatch);
            }

            if (step.Artifact.Operation != ArtifactOperation.Upload)
            {
                return Failure(step, $"Expected Upload operation but got {step.Artifact.Operation}.", startTime, stopwatch);
            }

            if (context.ArtifactContext == null)
            {
                return Failure(step, "ArtifactContext is required for artifact operations.", startTime, stopwatch);
            }

            var artifact = step.Artifact;
            var artifactContext = context.ArtifactContext;

            var nameError = ArtifactNames.TryGetValidationError(artifact.Name);
            if (nameError != null)
            {
                return Failure(step, $"Invalid artifact name '{artifact.Name}': {nameError}", startTime, stopwatch);
            }

            var patterns = NormalizePatterns(artifact.Patterns);
            if (!patterns.Any(p => !ArtifactPathResolver.IsExclusion(p)))
            {
                return Failure(step, "Artifact upload requires at least one path to upload.", startTime, stopwatch);
            }

            var containerBase = PathResolver.ResolvePath(artifact.TargetPath ?? string.Empty, context.ContainerWorkspacePath);

            _logger.LogInformation(
                "Uploading artifact '{ArtifactName}' with {PatternCount} pattern(s) from {BasePath}",
                artifact.Name,
                patterns.Count,
                containerBase);

            // The staging directory mirrors the container's file system root so that relative and
            // absolute container paths keep their structure.
            stagingRoot = Path.Combine(Path.GetTempPath(), $"pdk-artifact-{Guid.NewGuid():N}");
            Directory.CreateDirectory(stagingRoot);

            var searchPaths = ResolveSearchPaths(patterns, containerBase);
            var extractedFiles = 0;

            foreach (var searchPath in searchPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                extractedFiles += await FetchFromContainerAsync(context, searchPath, stagingRoot, cancellationToken);
            }

            if (extractedFiles == 0)
            {
                return HandleNoFilesFound(step, artifact, patterns, Array.Empty<string>(), startTime, stopwatch);
            }

            var sourcePath = ToStagingPath(stagingRoot, containerBase);
            var hostPatterns = patterns
                .Select(p => ToHostPattern(p, containerBase, stagingRoot))
                .ToList();

            var uploadContext = artifactContext with { SourcePath = sourcePath };

            UploadResult uploadResult;
            try
            {
                uploadResult = await _artifactManager.UploadAsync(
                    artifact.Name,
                    hostPatterns,
                    uploadContext,
                    artifact.Options,
                    progress: null,
                    cancellationToken);
            }
            catch (ArtifactException ex) when (ex.ErrorCode == ErrorCodes.ArtifactNoFilesMatched)
            {
                return HandleNoFilesFound(step, artifact, patterns, Array.Empty<string>(), startTime, stopwatch);
            }

            if (uploadResult.FileCount == 0)
            {
                return HandleNoFilesFound(step, artifact, patterns, uploadResult.Warnings, startTime, stopwatch);
            }

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
                Output = ArtifactStepSupport.DescribeUpload(uploadResult),
                ErrorOutput = string.Empty,
                Duration = stopwatch.Elapsed,
                StartTime = startTime,
                EndTime = DateTimeOffset.Now
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ArtifactException ex)
        {
            _logger.LogError(ex, "Artifact upload failed: {Message}", ex.Message);
            return Failure(step, $"Artifact upload failed: {ex.Message}", startTime, stopwatch);
        }
        catch (ContainerException ex)
        {
            _logger.LogError(ex, "Container operation failed: {Message}", ex.Message);
            return Failure(step, $"Container operation failed: {ex.Message}", startTime, stopwatch);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during artifact upload: {Message}", ex.Message);
            return Failure(step, $"Unexpected error: {ex.Message}", startTime, stopwatch);
        }
        finally
        {
            CleanupStaging(stagingRoot);
        }
    }

    /// <summary>
    /// Trims the patterns and drops empty ones.
    /// </summary>
    private static List<string> NormalizePatterns(IEnumerable<string>? patterns)
    {
        return (patterns ?? Array.Empty<string>())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .ToList();
    }

    /// <summary>
    /// Resolves the absolute container path of a pattern (without a leading '!').
    /// </summary>
    private static string ResolveContainerPattern(string pattern, string containerBase)
    {
        var body = ArtifactPathResolver.IsExclusion(pattern) ? pattern.TrimStart()[1..] : pattern;
        var normalized = ArtifactPathResolver.Normalize(body);

        return normalized.Length == 0
            ? containerBase
            : PathResolver.ResolvePath(normalized, containerBase);
    }

    /// <summary>
    /// Computes the container paths that have to be copied out: the non-glob prefix of every
    /// inclusion pattern, minus paths that are already covered by an ancestor.
    /// </summary>
    internal static IReadOnlyList<string> ResolveSearchPaths(IEnumerable<string> patterns, string containerBase)
    {
        var candidates = patterns
            .Where(p => !ArtifactPathResolver.IsExclusion(p))
            .Select(p => ArtifactPathResolver.GetSearchPath(ResolveContainerPattern(p, containerBase)))
            .Where(p => p.Length > 0 && p != "/")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(p => p.Length)
            .ThenBy(p => p, StringComparer.Ordinal)
            .ToList();

        var result = new List<string>();
        foreach (var candidate in candidates)
        {
            if (result.Any(kept => ArtifactPathResolver.IsUnder(candidate, kept, StringComparison.Ordinal)))
            {
                continue;
            }

            result.Add(candidate);
        }

        return result;
    }

    /// <summary>
    /// Maps a container path to its location inside the staging directory.
    /// </summary>
    internal static string ToStagingPath(string stagingRoot, string containerPath)
    {
        var relative = containerPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        return relative.Length == 0 ? stagingRoot : Path.Combine(stagingRoot, relative);
    }

    /// <summary>
    /// Rewrites a pattern so that it points into the staging directory on the host.
    /// </summary>
    internal static string ToHostPattern(string pattern, string containerBase, string stagingRoot)
    {
        var isExclusion = ArtifactPathResolver.IsExclusion(pattern);
        var containerPattern = ResolveContainerPattern(pattern, containerBase);
        var hostPattern = ToStagingPath(stagingRoot, containerPattern);
        return isExclusion ? "!" + hostPattern : hostPattern;
    }

    /// <summary>
    /// Copies one container path (file or directory tree) into the staging directory with a single
    /// archive request. Returns the number of files extracted; a missing path yields zero.
    /// </summary>
    private async Task<int> FetchFromContainerAsync(
        ExecutionContext context,
        string containerPath,
        string stagingRoot,
        CancellationToken cancellationToken)
    {
        // The archive returned by Docker is rooted at the last path segment, so extracting it into the
        // staged parent directory recreates the container layout.
        var parent = ArtifactPathResolver.GetParent(containerPath);
        var extractTo = ToStagingPath(stagingRoot, parent);

        _logger.LogDebug("Copying {Path} from container to {Target}", containerPath, extractTo);

        Stream archive;
        try
        {
            archive = await context.ContainerManager.GetArchiveFromContainerAsync(
                context.ContainerId,
                containerPath,
                cancellationToken);
        }
        catch (ContainerException ex) when (IsNotFound(ex))
        {
            _logger.LogDebug("Path {Path} does not exist in the container", containerPath);
            return 0;
        }

        using (archive)
        {
            Directory.CreateDirectory(extractTo);
            var count = await TarArchiveHelper.ExtractTarAsync(archive, extractTo, cancellationToken);
            _logger.LogDebug("Extracted {Count} file(s) from {Path}", count, containerPath);
            return count;
        }
    }

    private static bool IsNotFound(ContainerException exception)
    {
        if (exception.InnerException is DockerApiException { StatusCode: System.Net.HttpStatusCode.NotFound })
        {
            return true;
        }

        return exception.Message.Contains("not found", StringComparison.OrdinalIgnoreCase)
               || exception.Message.Contains("No such file", StringComparison.OrdinalIgnoreCase);
    }

    private StepExecutionResult HandleNoFilesFound(
        Step step,
        ArtifactDefinition artifact,
        IEnumerable<string> patterns,
        IReadOnlyList<string> warnings,
        DateTimeOffset startTime,
        Stopwatch stopwatch)
    {
        stopwatch.Stop();

        var behavior = artifact.Options?.IfNoFilesFound ?? IfNoFilesFound.Error;
        var message = ArtifactStepSupport.DescribeNoFiles(patterns);

        switch (behavior)
        {
            case IfNoFilesFound.Error:
                _logger.LogError("{Message}", message);
                return new StepExecutionResult
                {
                    StepName = step.Name,
                    Success = false,
                    ExitCode = 1,
                    Output = string.Empty,
                    ErrorOutput = message,
                    Duration = stopwatch.Elapsed,
                    StartTime = startTime,
                    EndTime = DateTimeOffset.Now
                };

            case IfNoFilesFound.Warn:
                _logger.LogWarning("{Message}", message);
                var output = "Warning: " + message + ". No artifact was uploaded.";
                foreach (var warning in warnings)
                {
                    output += Environment.NewLine + "Warning: " + warning;
                }

                return new StepExecutionResult
                {
                    StepName = step.Name,
                    Success = true,
                    ExitCode = 0,
                    Output = output,
                    ErrorOutput = string.Empty,
                    Duration = stopwatch.Elapsed,
                    StartTime = startTime,
                    EndTime = DateTimeOffset.Now
                };

            default:
                _logger.LogDebug("{Message}", message);
                return new StepExecutionResult
                {
                    StepName = step.Name,
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

    private static StepExecutionResult Failure(Step step, string message, DateTimeOffset startTime, Stopwatch stopwatch)
    {
        stopwatch.Stop();
        return new StepExecutionResult
        {
            StepName = step.Name,
            Success = false,
            ExitCode = 1,
            Output = string.Empty,
            ErrorOutput = message,
            Duration = stopwatch.Elapsed,
            StartTime = startTime,
            EndTime = DateTimeOffset.Now
        };
    }

    private void CleanupStaging(string? stagingRoot)
    {
        if (stagingRoot == null || !Directory.Exists(stagingRoot))
        {
            return;
        }

        try
        {
            Directory.Delete(stagingRoot, recursive: true);
            _logger.LogDebug("Cleaned up staging directory: {Path}", stagingRoot);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clean up staging directory: {Path}", stagingRoot);
        }
    }
}
