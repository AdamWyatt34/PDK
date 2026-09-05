namespace PDK.Tests.Unit.Runners.Docker;

using PDK.Runners.Docker;

/// <summary>
/// In-memory host environment for Docker discovery and container configuration tests.
/// </summary>
internal sealed class FakeDockerHostEnvironment : IDockerHostEnvironment
{
    public bool IsWindows { get; set; }

    public bool IsLinux { get; set; } = true;

    public bool IsMacOS { get; set; }

    public string HomeDirectory { get; set; } = "/home/tester";

    public Dictionary<string, string?> Variables { get; } = new(StringComparer.Ordinal);

    public HashSet<string> Files { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, string> FileContents { get; } = new(StringComparer.Ordinal);

    public HashSet<string> Directories { get; } = new(StringComparer.Ordinal);

    public List<string> EnsuredDirectories { get; } = new();

    public (uint UserId, uint GroupId)? EffectiveUser { get; set; } = (1000u, 1000u);

    public bool ThrowOnEnsureDirectory { get; set; }

    public string? GetEnvironmentVariable(string name)
    {
        return Variables.TryGetValue(name, out var value) ? value : null;
    }

    public bool FileExists(string path)
    {
        return Files.Contains(path) || FileContents.ContainsKey(path);
    }

    public bool DirectoryExists(string path)
    {
        return Directories.Contains(path);
    }

    public string ReadAllText(string path)
    {
        if (FileContents.TryGetValue(path, out var content))
        {
            return content;
        }

        throw new FileNotFoundException("File not found", path);
    }

    public void EnsureDirectory(string path)
    {
        if (ThrowOnEnsureDirectory)
        {
            throw new IOException("cannot create directory");
        }

        EnsuredDirectories.Add(path);
        Directories.Add(path);
    }

    public (uint UserId, uint GroupId)? GetEffectiveUser()
    {
        return EffectiveUser;
    }
}
