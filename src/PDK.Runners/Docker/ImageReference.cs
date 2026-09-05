using System.Text.RegularExpressions;

namespace PDK.Runners.Docker;

/// <summary>
/// A parsed Docker image reference: <c>[registry[:port]/]repository[:tag][@digest]</c>.
/// </summary>
public sealed record ImageReference
{
    private static readonly Regex TagPattern = new(
        "^[A-Za-z0-9_][A-Za-z0-9_.-]{0,127}$",
        RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    private static readonly Regex DigestPattern = new(
        "^[a-z0-9]+(?:[+._-][a-z0-9]+)*:[A-Fa-f0-9]{32,}$",
        RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    private static readonly Regex ComponentPattern = new(
        "^[a-z0-9]+(?:(?:[._]|__|-+)[a-z0-9]+)*$",
        RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    private static readonly Regex RegistryPattern = new(
        "^[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?(?:\\.[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?)*(?::[0-9]{1,5})?$",
        RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    private ImageReference(string? registry, string repository, string? tag, string? digest)
    {
        Registry = registry;
        Repository = repository;
        Tag = tag;
        Digest = digest;
    }

    /// <summary>Gets the registry host (with optional port), or null for Docker Hub.</summary>
    public string? Registry { get; }

    /// <summary>Gets the repository path (e.g. <c>library/ubuntu</c>, <c>dotnet/sdk</c>).</summary>
    public string Repository { get; }

    /// <summary>Gets the tag, or null when none was specified.</summary>
    public string? Tag { get; }

    /// <summary>Gets the content digest (e.g. <c>sha256:...</c>), or null when none was specified.</summary>
    public string? Digest { get; }

    /// <summary>Gets the image name without tag or digest, including the registry when present.</summary>
    public string Name => Registry == null ? Repository : $"{Registry}/{Repository}";

    /// <summary>Gets the registry host used for authentication (<c>docker.io</c> for Docker Hub).</summary>
    public string RegistryHost => Registry ?? "docker.io";

    /// <summary>Gets the value to send as the <c>tag</c> of a pull request: the digest, the tag, or <c>latest</c>.</summary>
    public string PullTag => Digest ?? Tag ?? "latest";

    /// <summary>
    /// Gets the reference in canonical form with an explicit tag when neither tag nor digest was given.
    /// </summary>
    public string Canonical
    {
        get
        {
            var name = Name;
            if (Tag != null)
            {
                name += ":" + Tag;
            }
            else if (Digest == null)
            {
                name += ":latest";
            }

            if (Digest != null)
            {
                name += "@" + Digest;
            }

            return name;
        }
    }

    /// <summary>
    /// Parses an image reference.
    /// </summary>
    /// <param name="value">The reference text.</param>
    /// <returns>The parsed reference.</returns>
    /// <exception cref="ArgumentException">Thrown when the reference is not valid.</exception>
    public static ImageReference Parse(string value)
    {
        if (!TryParse(value, out var reference))
        {
            throw new ArgumentException(
                $"Image reference '{value}' is not valid. Expected [registry[:port]/]name[:tag][@digest].",
                nameof(value));
        }

        return reference;
    }

    /// <summary>
    /// Tries to parse an image reference.
    /// </summary>
    /// <param name="value">The reference text.</param>
    /// <param name="reference">The parsed reference when successful.</param>
    /// <returns>True when the reference is valid; otherwise, false.</returns>
    public static bool TryParse(string? value, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out ImageReference? reference)
    {
        reference = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var remaining = value.Trim();
        if (remaining.Length > 255 || remaining.Any(char.IsWhiteSpace))
        {
            return false;
        }

        try
        {
            string? digest = null;
            var at = remaining.IndexOf('@');
            if (at >= 0)
            {
                digest = remaining[(at + 1)..];
                remaining = remaining[..at];
                if (!DigestPattern.IsMatch(digest))
                {
                    return false;
                }
            }

            string? tag = null;
            var lastSlash = remaining.LastIndexOf('/');
            var lastColon = remaining.LastIndexOf(':');
            if (lastColon > lastSlash)
            {
                tag = remaining[(lastColon + 1)..];
                remaining = remaining[..lastColon];
                if (!TagPattern.IsMatch(tag))
                {
                    return false;
                }
            }

            string? registry = null;
            var firstSlash = remaining.IndexOf('/');
            if (firstSlash > 0)
            {
                var first = remaining[..firstSlash];
                if (first.Contains('.') || first.Contains(':') ||
                    string.Equals(first, "localhost", StringComparison.OrdinalIgnoreCase) ||
                    first.Any(char.IsUpper))
                {
                    if (!RegistryPattern.IsMatch(first))
                    {
                        return false;
                    }

                    registry = first;
                    remaining = remaining[(firstSlash + 1)..];
                }
            }

            if (remaining.Length == 0)
            {
                return false;
            }

            foreach (var component in remaining.Split('/'))
            {
                if (!ComponentPattern.IsMatch(component))
                {
                    return false;
                }
            }

            reference = new ImageReference(registry, remaining, tag, digest);
            return true;
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    /// <inheritdoc/>
    public override string ToString() => Canonical;
}
