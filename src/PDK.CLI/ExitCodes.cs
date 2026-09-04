namespace PDK.CLI;

/// <summary>
/// Process exit codes used by every PDK command.
/// </summary>
public static class ExitCodes
{
    /// <summary>The command completed successfully.</summary>
    public const int Success = 0;

    /// <summary>The pipeline failed, validation failed, or an unexpected error occurred.</summary>
    public const int Failure = 1;

    /// <summary>The command line was invalid (unknown option, bad value, conflicting flags, unknown job).</summary>
    public const int InvalidArguments = 2;

    /// <summary>The pipeline file (or another required file) was not found.</summary>
    public const int FileNotFound = 3;

    /// <summary>Docker was required but is not available.</summary>
    public const int DockerUnavailable = 4;

    /// <summary>The command was cancelled (Ctrl+C / SIGTERM).</summary>
    public const int Cancelled = 130;
}
