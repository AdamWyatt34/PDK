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
        var startTime = DateTimeOffset.Now;
        var stopwatch = Stopwatch.StartNew();
        string? tempPath = null;

        try
        {
            // Validate artifact definition
            if (step.Artifact == null)
            {
                throw new InvalidOperationException(
                    "Artifact definition is required for download artifact step.");
            }

            if (step.Artifact.Operation != ArtifactOperation.Download)
            {
                throw new InvalidOperationException(
                    $"Expected Download operation but got {step.Artifact.Operation}.");
            }

            var artifact = step.Artifact;
            var targetPath = artifact.TargetPath ?? $"{context.ContainerWorkspacePath}/artifacts";

            _logger.LogInformation(
                "Downloading artifact '{ArtifactName}' to '{TargetPath}'",
                artifact.Name,
                targetPath);

            // Step 1: Check if artifact exists
            var exists = await _artifactManager.ExistsAsync(artifact.Name);
            if (!exists)
            {
                stopwatch.Stop();
                _logger.LogError("Artifact '{ArtifactName}' not found", artifact.Name);

                return new StepExecutionResult
                {
                    StepName = step.Name,
                    Success = false,
                    ExitCode = 1,
                    Output = string.Empty,
                    ErrorOutput = $"Artifact '{artifact.Name}' not found. " +
                                  "Ensure the artifact was uploaded in a previous step.",
                    Duration = stopwatch.Elapsed,
                    StartTime = startTime,
                    EndTime = DateTimeOffset.Now
                };
            }

            // Step 2: Create temp directory and download artifact
            tempPath = Path.Combine(Path.GetTempPath(), $"pdk-artifact-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempPath);

            _logger.LogDebug("Downloading artifact to temp path: {TempPath}", tempPath);

            var downloadResult = await _artifactManager.DownloadAsync(
                artifact.Name,
                tempPath,
                artifact.Options,
                progress: null,
                cancellationToken);

            _logger.LogDebug(
                "Downloaded {FileCount} files from artifact '{ArtifactName}'",
                downloadResult.FileCount,
                artifact.Name);

            // Step 3: Create target directory in container if needed
            await EnsureContainerDirectoryExistsAsync(context, targetPath, cancellationToken);

            // Step 4: Create tar archive and copy to container
            _logger.LogDebug("Creating tar archive from downloaded files");

            using var tarStream = await TarArchiveHelper.CreateTarAsync(tempPath, cancellationToken);

            _logger.LogDebug("Copying tar archive to container at {TargetPath}", targetPath);

            await context.ContainerManager.PutArchiveToContainerAsync(
                context.ContainerId,
                targetPath,
                tarStream,
                cancellationToken);

            stopwatch.Stop();

            _logger.LogInformation(
                "Successfully downloaded artifact '{ArtifactName}': {FileCount} files to {TargetPath}",
                artifact.Name,
                downloadResult.FileCount,
                targetPath);

            return new StepExecutionResult
            {
                StepName = step.Name,
                Success = true,
                ExitCode = 0,
                Output = $"Downloaded {downloadResult.FileCount} files from artifact '{artifact.Name}' " +
                         $"to {targetPath}",
                ErrorOutput = string.Empty,
                Duration = stopwatch.Elapsed,
                StartTime = startTime,
                EndTime = DateTimeOffset.Now
            };
        }
        catch (ArtifactException ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Artifact download failed: {Message}", ex.Message);

            return new StepExecutionResult
            {
                StepName = step.Name,
                Success = false,
                ExitCode = 1,
                Output = string.Empty,
                ErrorOutput = $"Artifact download failed: {ex.Message}",
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
            _logger.LogError(ex, "Unexpected error during artifact download: {Message}", ex.Message);

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
    /// Ensures the target directory exists in the container.
    /// </summary>
    private async Task EnsureContainerDirectoryExistsAsync(
        ExecutionContext context,
        string targetPath,
        CancellationToken cancellationToken)
    {
        var mkdirCommand = $"mkdir -p {targetPath}";

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
}
