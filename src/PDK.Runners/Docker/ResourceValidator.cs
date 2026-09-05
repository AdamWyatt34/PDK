using PDK.Runners.Models;

namespace PDK.Runners.Docker;

/// <summary>
/// Validates Docker container resource limits (memory, CPU, timeout).
/// When the daemon's resources are known (<see cref="DaemonResources"/>, from <c>docker info</c>) the limits are
/// checked against the daemon (the Docker Desktop VM on macOS/Windows); otherwise conservative host-based
/// bounds are used.
/// </summary>
public static class ResourceValidator
{
    /// <summary>
    /// Minimum memory limit in bytes (6MB - Docker's minimum).
    /// </summary>
    private const long MinMemoryBytes = 6_291_456; // 6MB

    /// <summary>
    /// Maximum memory limit in bytes used when the daemon's total memory is unknown (16GB).
    /// </summary>
    private const long FallbackMaxMemoryBytes = 17_179_869_184; // 16GB

    /// <summary>
    /// Maximum timeout in minutes (24 hours).
    /// </summary>
    private const int MaxTimeoutMinutes = 1440; // 24 hours

    /// <summary>
    /// Validates a memory limit value against the fallback bounds (6MB - 16GB).
    /// Null values are considered valid (no limit specified).
    /// </summary>
    /// <param name="memoryBytes">The memory limit in bytes to validate.</param>
    /// <returns>A tuple of (isValid, errorMessage).</returns>
    public static (bool isValid, string? errorMessage) ValidateMemoryLimit(long? memoryBytes)
    {
        return ValidateMemoryLimit(memoryBytes, null);
    }

    /// <summary>
    /// Validates a memory limit value. When <paramref name="daemon"/> is supplied the limit may not exceed the
    /// daemon's total memory; otherwise a 16GB ceiling applies.
    /// </summary>
    /// <param name="memoryBytes">The memory limit in bytes to validate.</param>
    /// <param name="daemon">Resources reported by the Docker daemon, or null when unknown.</param>
    /// <returns>A tuple of (isValid, errorMessage).</returns>
    public static (bool isValid, string? errorMessage) ValidateMemoryLimit(long? memoryBytes, DaemonResources? daemon)
    {
        if (!memoryBytes.HasValue)
        {
            return (true, null);
        }

        var value = memoryBytes.Value;

        if (value < MinMemoryBytes)
        {
            return (false, $"Memory limit must be at least {MinMemoryBytes:N0} bytes (6MB - Docker minimum). Provided: {value:N0} bytes.");
        }

        if (daemon is { TotalMemoryBytes: > 0 })
        {
            if (value > daemon.TotalMemoryBytes)
            {
                return (false,
                    $"Memory limit cannot exceed the Docker daemon's total memory of {daemon.TotalMemoryBytes:N0} bytes " +
                    $"({BytesToGigabytes(daemon.TotalMemoryBytes):F1}GB). Provided: {value:N0} bytes.");
            }

            return (true, null);
        }

        if (value > FallbackMaxMemoryBytes)
        {
            return (false, $"Memory limit cannot exceed {FallbackMaxMemoryBytes:N0} bytes (16GB). Provided: {value:N0} bytes.");
        }

        return (true, null);
    }

    /// <summary>
    /// Validates a CPU limit value against the processors of this machine.
    /// Null values are considered valid (no limit specified).
    /// </summary>
    /// <param name="cpuLimit">The CPU limit in cores to validate (e.g., 1.0 = 1 core, 2.5 = 2.5 cores).</param>
    /// <returns>A tuple of (isValid, errorMessage).</returns>
    public static (bool isValid, string? errorMessage) ValidateCpuLimit(double? cpuLimit)
    {
        return ValidateCpuLimit(cpuLimit, null);
    }

    /// <summary>
    /// Validates a CPU limit value. When <paramref name="daemon"/> is supplied the limit may not exceed the
    /// CPUs available to the daemon; otherwise the host processor count is used.
    /// </summary>
    /// <param name="cpuLimit">The CPU limit in cores to validate.</param>
    /// <param name="daemon">Resources reported by the Docker daemon, or null when unknown.</param>
    /// <returns>A tuple of (isValid, errorMessage).</returns>
    public static (bool isValid, string? errorMessage) ValidateCpuLimit(double? cpuLimit, DaemonResources? daemon)
    {
        if (!cpuLimit.HasValue)
        {
            return (true, null);
        }

        var value = cpuLimit.Value;

        if (double.IsNaN(value) || value <= 0)
        {
            return (false, $"CPU limit must be greater than 0. Provided: {value}.");
        }

        if (daemon is { CpuCount: > 0 })
        {
            if (value > daemon.CpuCount)
            {
                return (false, $"CPU limit cannot exceed {daemon.CpuCount} cores (CPUs available to the Docker daemon). Provided: {value}.");
            }

            return (true, null);
        }

        var maxCpus = Environment.ProcessorCount;
        if (value > maxCpus)
        {
            return (false, $"CPU limit cannot exceed {maxCpus} cores (available processors on this system). Provided: {value}.");
        }

        return (true, null);
    }

    /// <summary>
    /// Validates a timeout value.
    /// Null values are considered valid (no timeout specified).
    /// </summary>
    /// <param name="timeoutMinutes">The timeout in minutes to validate.</param>
    /// <returns>A tuple of (isValid, errorMessage).</returns>
    public static (bool isValid, string? errorMessage) ValidateTimeout(int? timeoutMinutes)
    {
        if (!timeoutMinutes.HasValue)
        {
            return (true, null);
        }

        var value = timeoutMinutes.Value;

        if (value <= 0)
        {
            return (false, $"Timeout must be greater than 0 minutes. Provided: {value}.");
        }

        if (value > MaxTimeoutMinutes)
        {
            return (false, $"Timeout cannot exceed {MaxTimeoutMinutes} minutes (24 hours). Provided: {value}.");
        }

        return (true, null);
    }

    /// <summary>
    /// Validates all resource limits. Returns a list of all validation errors found.
    /// </summary>
    /// <param name="memoryLimit">Optional memory limit in bytes.</param>
    /// <param name="cpuLimit">Optional CPU limit in cores.</param>
    /// <param name="timeoutMinutes">Optional timeout in minutes.</param>
    /// <returns>A list of error messages for any invalid values. Empty list if all values are valid.</returns>
    public static List<string> ValidateAll(long? memoryLimit = null, double? cpuLimit = null, int? timeoutMinutes = null)
    {
        return ValidateAll(memoryLimit, cpuLimit, timeoutMinutes, null);
    }

    /// <summary>
    /// Validates all resource limits against the daemon's resources when known.
    /// </summary>
    /// <param name="memoryLimit">Optional memory limit in bytes.</param>
    /// <param name="cpuLimit">Optional CPU limit in cores.</param>
    /// <param name="timeoutMinutes">Optional timeout in minutes.</param>
    /// <param name="daemon">Resources reported by the Docker daemon, or null when unknown.</param>
    /// <returns>A list of error messages for any invalid values. Empty list if all values are valid.</returns>
    public static List<string> ValidateAll(long? memoryLimit, double? cpuLimit, int? timeoutMinutes, DaemonResources? daemon)
    {
        var errors = new List<string>();

        var (memoryValid, memoryError) = ValidateMemoryLimit(memoryLimit, daemon);
        if (!memoryValid && memoryError != null)
        {
            errors.Add(memoryError);
        }

        var (cpuValid, cpuError) = ValidateCpuLimit(cpuLimit, daemon);
        if (!cpuValid && cpuError != null)
        {
            errors.Add(cpuError);
        }

        var (timeoutValid, timeoutError) = ValidateTimeout(timeoutMinutes);
        if (!timeoutValid && timeoutError != null)
        {
            errors.Add(timeoutError);
        }

        return errors;
    }

    /// <summary>Converts megabytes to bytes.</summary>
    /// <param name="megabytes">The memory size in megabytes.</param>
    /// <returns>The memory size in bytes.</returns>
    public static long MegabytesToBytes(long megabytes) => megabytes * 1024 * 1024;

    /// <summary>Converts gigabytes to bytes.</summary>
    /// <param name="gigabytes">The memory size in gigabytes.</param>
    /// <returns>The memory size in bytes.</returns>
    public static long GigabytesToBytes(long gigabytes) => gigabytes * 1024 * 1024 * 1024;

    /// <summary>Converts bytes to megabytes.</summary>
    /// <param name="bytes">The memory size in bytes.</param>
    /// <returns>The memory size in megabytes.</returns>
    public static long BytesToMegabytes(long bytes) => bytes / 1024 / 1024;

    /// <summary>Converts bytes to gigabytes.</summary>
    /// <param name="bytes">The memory size in bytes.</param>
    /// <returns>The memory size in gigabytes.</returns>
    public static double BytesToGigabytes(long bytes) => bytes / 1024.0 / 1024.0 / 1024.0;
}
