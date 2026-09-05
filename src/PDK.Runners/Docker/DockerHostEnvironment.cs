using System.Runtime.InteropServices;

namespace PDK.Runners.Docker;

/// <summary>
/// The real host environment: forwards to <see cref="Environment"/>, <see cref="File"/> and libc.
/// </summary>
internal sealed class DockerHostEnvironment : IDockerHostEnvironment
{
    /// <summary>
    /// Gets the shared instance.
    /// </summary>
    public static DockerHostEnvironment Instance { get; } = new();

    private DockerHostEnvironment()
    {
    }

    /// <inheritdoc/>
    public bool IsWindows => OperatingSystem.IsWindows();

    /// <inheritdoc/>
    public bool IsLinux => OperatingSystem.IsLinux();

    /// <inheritdoc/>
    public bool IsMacOS => OperatingSystem.IsMacOS();

    /// <inheritdoc/>
    public string HomeDirectory
    {
        get
        {
            var home = Environment.GetEnvironmentVariable("HOME");
            if (!string.IsNullOrEmpty(home))
            {
                return home;
            }

            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return string.IsNullOrEmpty(profile) ? Path.GetTempPath() : profile;
        }
    }

    /// <inheritdoc/>
    public string? GetEnvironmentVariable(string name) => Environment.GetEnvironmentVariable(name);

    /// <inheritdoc/>
    public bool FileExists(string path) => File.Exists(path);

    /// <inheritdoc/>
    public bool DirectoryExists(string path) => Directory.Exists(path);

    /// <inheritdoc/>
    public string ReadAllText(string path) => File.ReadAllText(path);

    /// <inheritdoc/>
    public void EnsureDirectory(string path) => Directory.CreateDirectory(path);

    /// <inheritdoc/>
    public (uint UserId, uint GroupId)? GetEffectiveUser()
    {
        if (!IsLinux && !IsMacOS)
        {
            return null;
        }

        try
        {
            return (NativeMethods.GetEffectiveUserId(), NativeMethods.GetEffectiveGroupId());
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    private static class NativeMethods
    {
        [DllImport("libc", EntryPoint = "geteuid")]
        internal static extern uint GetEffectiveUserId();

        [DllImport("libc", EntryPoint = "getegid")]
        internal static extern uint GetEffectiveGroupId();
    }
}
