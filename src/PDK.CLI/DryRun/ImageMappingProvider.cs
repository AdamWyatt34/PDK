using PDK.Core.Validation;
using PDK.Runners;

namespace PDK.CLI.DryRun;

/// <summary>
/// Exposes the runtime <see cref="IImageMapper"/> to the dry-run plan builder through the
/// <see cref="IImageMappingProvider"/> abstraction (PDK.Core cannot reference PDK.Runners).
/// Register with <c>services.AddSingleton&lt;IImageMappingProvider, ImageMappingProvider&gt;()</c>.
/// </summary>
public sealed class ImageMappingProvider : IImageMappingProvider
{
    private readonly IImageMapper _imageMapper;

    /// <summary>
    /// Initializes a new instance of <see cref="ImageMappingProvider"/>.
    /// </summary>
    /// <param name="imageMapper">The runtime image mapper.</param>
    public ImageMappingProvider(IImageMapper imageMapper)
    {
        _imageMapper = imageMapper ?? throw new ArgumentNullException(nameof(imageMapper));
    }

    /// <inheritdoc />
    public string? MapRunnerToImage(string runsOn)
    {
        if (string.IsNullOrWhiteSpace(runsOn))
        {
            return null;
        }

        try
        {
            return _imageMapper.MapRunnerToImage(runsOn);
        }
        catch (ArgumentException)
        {
            // Unknown runner label or invalid image: the plan builder falls back to its table
            return null;
        }
    }
}
