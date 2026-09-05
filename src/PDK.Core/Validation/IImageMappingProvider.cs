namespace PDK.Core.Validation;

/// <summary>
/// Maps a <c>runs-on</c> / <c>vmImage</c> value to the container image the Docker runner would use.
/// The runtime mapper (<c>PDK.Runners.IImageMapper</c>) is not visible from PDK.Core, so the CLI
/// implements this interface by delegating to it (<c>PDK.CLI.DryRun.ImageMappingProvider</c>) and
/// registers it in DI; <see cref="ExecutionPlanBuilder"/> falls back to a built-in table when no
/// provider is available.
/// </summary>
public interface IImageMappingProvider
{
    /// <summary>
    /// Maps a runner specification to a container image.
    /// </summary>
    /// <param name="runsOn">The runner specification (e.g. <c>ubuntu-latest</c> or a custom image).</param>
    /// <returns>The image name, or null when the runner has no Docker image (e.g. macOS) or is unknown.</returns>
    string? MapRunnerToImage(string runsOn);
}
