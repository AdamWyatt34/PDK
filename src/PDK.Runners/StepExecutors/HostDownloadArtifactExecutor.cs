namespace PDK.Runners.StepExecutors;

using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PDK.Core.Artifacts;
using PDK.Core.Models;

/// <summary>
/// Executes artifact download steps on the host machine: the artifact is extracted directly
/// into the resolved target directory under the host workspace.
/// </summary>
public class HostDownloadArtifactExecutor : IHostStepExecutor
{
    private readonly IArtifactManager _artifactManager;
    private readonly ILogger<HostDownloadArtifactExecutor> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="HostDownloadArtifactExecutor"/> class.
    /// </summary>
    /// <param name="artifactManager">The artifact manager for retrieving artifacts.</param>
    /// <param name="logger">The logger for diagnostics.</param>
    public HostDownloadArtifactExecutor(
        IArtifactManager artifactManager,
        ILogger<HostDownloadArtifactExecutor> logger)
    {
        _artifactManager = artifactManager ?? throw new ArgumentNullException(nameof(artifactManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public string StepType => "downloadartifact";

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
                return Failure(step, "Artifact definition is required for download artifact step.", startTime, stopwatch);
            }

            if (step.Artifact.Operation != ArtifactOperation.Download)
            {
                return Failure(step, $"Expected Download operation but got {step.Artifact.Operation}.", startTime, stopwatch);
            }

            var artifact = step.Artifact;
            var artifactName = artifact.Name?.Trim() ?? string.Empty;
            var downloadAll = artifactName.Length == 0;

            if (!downloadAll)
            {
                var nameError = ArtifactNames.TryGetValidationError(artifactName);
                if (nameError != null)
                {
                    return Failure(step, $"Invalid artifact name '{artifactName}': {nameError}", startTime, stopwatch);
                }
            }

            var artifactContext = context.ArtifactContext
                                  ?? ArtifactContext.ForWorkspace(
                                      string.IsNullOrWhiteSpace(context.WorkspacePath)
                                          ? Directory.GetCurrentDirectory()
                                          : context.WorkspacePath);

            var targetPath = context.ResolvePath(artifact.TargetPath);
            if (!downloadAll && ArtifactStepSupport.UsesNamedSubdirectory(artifact, step.With))
            {
                targetPath = Path.Combine(targetPath, ArtifactStepSupport.GetDownloadDirectoryName(artifactName));
            }

            _logger.LogInformation(
                "Downloading {What} to '{TargetPath}'",
                downloadAll ? "all artifacts of the run" : $"artifact '{artifactName}'",
                targetPath);

            var downloadResult = await _artifactManager.DownloadAsync(
                artifactContext,
                downloadAll ? null : artifactName,
                targetPath,
                artifact.Options,
                progress: null,
                cancellationToken);

            stopwatch.Stop();

            _logger.LogInformation(
                "Successfully downloaded {FileCount} files to {TargetPath}",
                downloadResult.FileCount,
                targetPath);

            return new StepExecutionResult
            {
                StepName = step.Name,
                Success = true,
                ExitCode = 0,
                Output = ArtifactStepSupport.DescribeDownload(downloadResult, targetPath),
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
            _logger.LogError(ex, "Artifact download failed: {Message}", ex.Message);
            return Failure(step, $"Artifact download failed: {ex.Message}", startTime, stopwatch);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during artifact download: {Message}", ex.Message);
            return Failure(step, $"Unexpected error: {ex.Message}", startTime, stopwatch);
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
