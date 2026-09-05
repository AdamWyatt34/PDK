using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PDK.Core.Artifacts;
using PDK.Core.Models;
using PDK.Runners.Utilities;

namespace PDK.Runners.StepExecutors;

/// <summary>
/// Executes artifact download steps by retrieving artifacts from storage
/// and copying them into a container.
/// </summary>
public class DownloadArtifactExecutor : IStepExecutor
{
    private readonly IArtifactManager _artifactManager;
    private readonly ILogger<DownloadArtifactExecutor> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DownloadArtifactExecutor"/> class.
    /// </summary>
    /// <param name="artifactManager">The artifact manager for retrieving artifacts.</param>
    /// <param name="logger">The logger for diagnostics.</param>
    public DownloadArtifactExecutor(
        IArtifactManager artifactManager,
        ILogger<DownloadArtifactExecutor> logger)
    {
        _artifactManager = artifactManager ?? throw new ArgumentNullException(nameof(artifactManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public string StepType => "downloadartifact";

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
        string? tempPath = null;

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

            // The Docker archive API resolves relative paths against '/', so the target must be absolute.
            var targetPath = PathResolver.ResolvePath(artifact.TargetPath ?? string.Empty, context.ContainerWorkspacePath);
            if (!downloadAll && ArtifactStepSupport.UsesNamedSubdirectory(artifact, step.With))
            {
                targetPath = PathResolver.ResolvePath(ArtifactStepSupport.GetDownloadDirectoryName(artifactName), targetPath);
            }

            _logger.LogInformation(
                "Downloading {What} to '{TargetPath}'",
                downloadAll ? "all artifacts of the run" : $"artifact '{artifactName}'",
                targetPath);

            tempPath = Path.Combine(Path.GetTempPath(), $"pdk-artifact-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempPath);

            var downloadResult = await _artifactManager.DownloadAsync(
                artifactContext,
                downloadAll ? null : artifactName,
                tempPath,
                artifact.Options,
                progress: null,
                cancellationToken);

            _logger.LogDebug(
                "Downloaded {FileCount} files from {Count} artifact(s) to temp path {TempPath}",
                downloadResult.FileCount,
                downloadResult.Artifacts.Count,
                tempPath);

            await EnsureContainerDirectoryExistsAsync(context, targetPath, cancellationToken);

            if (downloadResult.FileCount > 0 || downloadResult.Artifacts.Count > 0)
            {
                using var tarStream = await TarArchiveHelper.CreateTarAsync(tempPath, cancellationToken);

                _logger.LogDebug("Copying tar archive to container at {TargetPath}", targetPath);

                await context.ContainerManager.PutArchiveToContainerAsync(
                    context.ContainerId,
                    targetPath,
                    tarStream,
                    cancellationToken);
            }

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
        catch (ContainerException ex)
        {
            _logger.LogError(ex, "Container operation failed: {Message}", ex.Message);
            return Failure(step, $"Container operation failed: {ex.Message}", startTime, stopwatch);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during artifact download: {Message}", ex.Message);
            return Failure(step, $"Unexpected error: {ex.Message}", startTime, stopwatch);
        }
        finally
        {
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
    /// Ensures the (absolute) target directory exists in the container.
    /// </summary>
    private async Task EnsureContainerDirectoryExistsAsync(
        ExecutionContext context,
        string targetPath,
        CancellationToken cancellationToken)
    {
        var mkdirCommand = $"mkdir -p {QuoteForShell(targetPath)}";

        _logger.LogDebug("Creating target directory in container: {Command}", mkdirCommand);

        var result = await context.ContainerManager.ExecuteCommandAsync(
            context.ContainerId,
            mkdirCommand,
            workingDirectory: null,
            environment: null,
            cancellationToken);

        if (result.ExitCode != 0)
        {
            throw new ContainerException(
                $"Failed to create target directory '{targetPath}' in container: {result.StandardError}");
        }
    }

    private static string QuoteForShell(string value)
    {
        return "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
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
