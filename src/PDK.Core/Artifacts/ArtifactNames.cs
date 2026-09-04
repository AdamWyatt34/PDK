namespace PDK.Core.Artifacts;

/// <summary>
/// Validation and sanitization rules for artifact names.
/// </summary>
/// <remarks>
/// The accepted character set mirrors GitHub Actions (<c>actions/upload-artifact</c>) and Azure
/// Pipelines: any name is allowed except the empty/whitespace-only name and names that contain
/// <c>" : &lt; &gt; | * ? \r \n \ /</c>. Because names may contain characters that are not valid in
/// every file system, the on-disk directory name is derived with <see cref="GetDirectoryName"/> and
/// the original name is kept in the artifact metadata.
/// </remarks>
public static class ArtifactNames
{
    /// <summary>
    /// The maximum accepted artifact name length.
    /// </summary>
    public const int MaxLength = 256;

    /// <summary>
    /// Characters that are rejected in artifact names (the GitHub / Azure DevOps rule set).
    /// </summary>
    public static readonly IReadOnlyList<char> InvalidCharacters = new[]
    {
        '"', ':', '<', '>', '|', '*', '?', '\r', '\n', '\\', '/'
    };

    /// <summary>
    /// Prefix of every artifact directory inside a step directory.
    /// </summary>
    public const string DirectoryPrefix = "artifact-";

    /// <summary>
    /// Checks whether the given artifact name is acceptable.
    /// </summary>
    /// <param name="name">The name to check.</param>
    /// <returns>True when the name is valid.</returns>
    public static bool IsValid(string? name)
    {
        return TryGetValidationError(name) == null;
    }

    /// <summary>
    /// Validates an artifact name and throws <see cref="ArtifactException"/> when it is invalid.
    /// </summary>
    /// <param name="name">The name to validate.</param>
    /// <exception cref="ArtifactException">The name is invalid (error code PDK-E-ARTIFACT-001).</exception>
    public static void Validate(string? name)
    {
        var error = TryGetValidationError(name);
        if (error != null)
        {
            throw ArtifactException.InvalidName(name ?? "null", error);
        }
    }

    /// <summary>
    /// Returns a human readable description of why a name is invalid, or null when it is valid.
    /// </summary>
    /// <param name="name">The name to check.</param>
    /// <returns>The validation error, or null.</returns>
    public static string? TryGetValidationError(string? name)
    {
        if (name == null || string.IsNullOrWhiteSpace(name))
        {
            return "Artifact name cannot be empty";
        }

        if (name.Length > MaxLength)
        {
            return $"Artifact name cannot be longer than {MaxLength} characters";
        }

        var invalid = name.Where(c => InvalidCharacters.Contains(c)).Distinct().ToList();
        if (invalid.Count > 0)
        {
            var shown = string.Join(" ", invalid.Select(DescribeChar));
            return $"Artifact name contains invalid character(s): {shown}";
        }

        return null;
    }

    /// <summary>
    /// Gets the directory name used to store an artifact (<c>artifact-&lt;sanitized name&gt;</c>).
    /// </summary>
    /// <param name="name">The artifact name.</param>
    /// <returns>A directory name that is valid on every supported file system.</returns>
    public static string GetDirectoryName(string name)
    {
        var sanitized = SanitizeForFileSystem(name);
        return DirectoryPrefix + (sanitized.Length == 0 ? "unnamed" : sanitized);
    }

    /// <summary>
    /// Replaces characters that are not valid in file names on Windows, Linux or macOS with '_' and
    /// trims trailing dots and spaces (which Windows does not allow).
    /// </summary>
    /// <param name="name">The value to sanitize.</param>
    /// <returns>The sanitized value (may be empty).</returns>
    public static string SanitizeForFileSystem(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return string.Empty;
        }

        var chars = name.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            var c = chars[i];
            if (InvalidCharacters.Contains(c) || char.IsControl(c) || c == '\0')
            {
                chars[i] = '_';
            }
        }

        var sanitized = new string(chars).TrimEnd('.', ' ').TrimStart(' ');

        // Windows reserved device names (CON, PRN, AUX, NUL, COM1-9, LPT1-9) are not usable as directory names.
        var stem = sanitized.Split('.')[0];
        if (IsReservedDeviceName(stem))
        {
            sanitized = "_" + sanitized;
        }

        return sanitized;
    }

    private static bool IsReservedDeviceName(string stem)
    {
        if (stem.Length is < 3 or > 4)
        {
            return false;
        }

        var upper = stem.ToUpperInvariant();
        return upper is "CON" or "PRN" or "AUX" or "NUL"
            || (upper.Length == 4 && (upper.StartsWith("COM", StringComparison.Ordinal) || upper.StartsWith("LPT", StringComparison.Ordinal)) && char.IsDigit(upper[3]));
    }

    private static string DescribeChar(char c) => c switch
    {
        '\r' => "'\\r'",
        '\n' => "'\\n'",
        _ => $"'{c}'"
    };
}
