namespace PDK.Runners.StepExecutors;

using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PDK.Core.Artifacts;
using PDK.Core.ErrorHandling;
using PDK.Core.Models;

/// <summary>
/// Executes artifact upload steps on the host machine: the path patterns are evaluated
/// directly in the host workspace and handed to the artifact manager.
/// </summary>
public class HostUploadArtifactExecutor : IHostStepExecutor
{
    private readonly IArtifactManager _artifactManager;
    private readonly ILogger<HostUploadArtifactExecutor> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="HostUploadArtifactExecutor"/> class.
    /// </summary>
    /// <param name="artifactManager">The artifact manager for storing artifacts.</param>
    /// <param name="logger">The logger for diagnostics.</param>
    public HostUploadArtifactExecutor(
        IArtifactManager artifactManager,
        ILogger<HostUploadArtifactExecutor> logger)
    {
        _artifactManager = artifactManager ?? throw new ArgumentNullException(nameof(artifactManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public string StepType => "uploadartifact";

    /// <inheritdoc/>
    public async Task<StepExecutionResult> ExecuteAsync(
        Step step,
        HostExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(context);

        var startTime = DateTimeOffset.Now;
        var stopwatch = Stopwatch.StartNew();

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

            var nameError = ArtifactNames.TryGetValidationError(artifact.Name);
            if (nameError != null)
            {
                return Failure(step, $"Invalid artifact name '{artifact.Name}': {nameError}", startTime, stopwatch);
            }

            var patterns = (artifact.Patterns ?? Array.Empty<string>())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p.Trim())
                .ToList();

            if (!patterns.Any(p => !ArtifactPathResolver.IsExclusion(p)))
            {
                return Failure(step, "Artifact upload requires at least one path to upload.", startTime, stopwatch);
            }

            var sourcePath = context.ResolvePath(artifact.TargetPath);
            var workspacePath = string.IsNullOrWhiteSpace(context.ArtifactContext.WorkspacePath)
                ? context.WorkspacePath
                : context.ArtifactContext.WorkspacePath;

            var uploadContext = context.ArtifactContext with
            {
                WorkspacePath = workspacePath,
                SourcePath = sourcePath
            };

            _logger.LogInformation(
                "Uploading artifact '{ArtifactName}' with {PatternCount} pattern(s) from {SourcePath}",
                artifact.Name,
                patterns.Count,
                sourcePath);

            UploadResult uploadResult;
            try
            {
                uploadResult = await _artifactManager.UploadAsync(
                    artifact.Name,
                    patterns,
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during artifact upload: {Message}", ex.Message);
            return Failure(step, $"Unexpected error: {ex.Message}", startTime, stopwatch);
        }
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
}
