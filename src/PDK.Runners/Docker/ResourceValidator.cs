namespace PDK.Runners.Docker;

/// <summary>
/// Validates Docker container resource limits (memory, CPU, timeout).
/// Ensures values are within Docker's acceptable ranges and system constraints.
/// </summary>
public static class ResourceValidator
{
    /// <summary>
    /// Minimum memory limit in bytes (6MB - Docker's minimum).
    /// </summary>
    private const long MinMemoryBytes = 6_291_456; // 6MB

    /// <summary>
    /// Maximum memory limit in bytes (16GB - reasonable upper limit).
    /// </summary>
    private const long MaxMemoryBytes = 17_179_869_184; // 16GB

    /// <summary>
    /// Maximum timeout in minutes (24 hours).
    /// </summary>
    private const int MaxTimeoutMinutes = 1440; // 24 hours

    /// <summary>
    /// Validates a memory limit value.
    /// Null values are considered valid (no limit specified).
    /// </summary>
    /// <param name="memoryBytes">The memory limit in bytes to validate.</param>
    /// <returns>
    /// A tuple containing:
    /// - isValid: true if the value is valid, false otherwise
    /// - errorMessage: a descriptive error message if invalid, null if valid
    /// </returns>
    /// <example>
    /// <code>
    /// var (isValid, error) = ResourceValidator.ValidateMemoryLimit(8_000_000_000);
    /// if (!isValid)
    /// {
    ///     Console.WriteLine(error);
    /// }
    /// </code>
    /// </example>
    public static (bool isValid, string? errorMessage) ValidateMemoryLimit(long? memoryBytes)
    {
        // Null is valid (no limit specified)
        if (!memoryBytes.HasValue)
        {
            return (true, null);
        }

        var value = memoryBytes.Value;

        // Check minimum
        if (value < MinMemoryBytes)
        {
            return (false, $"Memory limit must be at least {MinMemoryBytes:N0} bytes (6MB - Docker minimum). Provided: {value:N0} bytes.");
        }

        // Check maximum
        if (value > MaxMemoryBytes)
        {
            return (false, $"Memory limit cannot exceed {MaxMemoryBytes:N0} bytes (16GB). Provided: {value:N0} bytes.");
        }

        return (true, null);
    }

    /// <summary>
    /// Validates a CPU limit value.
    /// Null values are considered valid (no limit specified).
    /// </summary>
    /// <param name="cpuLimit">The CPU limit in cores to validate (e.g., 1.0 = 1 core, 2.5 = 2.5 cores).</param>
    /// <returns>
    /// A tuple containing:
    /// - isValid: true if the value is valid, false otherwise
    /// - errorMessage: a descriptive error message if invalid, null if valid
    /// </returns>
    /// <example>
    /// <code>
    /// var (isValid, error) = ResourceValidator.ValidateCpuLimit(2.0);
    /// if (!isValid)
    /// {
    ///     Console.WriteLine(error);
    /// }
    /// </code>
    /// </example>
    public static (bool isValid, string? errorMessage) ValidateCpuLimit(double? cpuLimit)
    {
        // Null is valid (no limit specified)
        if (!cpuLimit.HasValue)
        {
            return (true, null);
        }

        var value = cpuLimit.Value;

        // Check minimum (must be positive)
        if (value <= 0)
        {
            return (false, $"CPU limit must be greater than 0. Provided: {value}.");
        }

        // Check maximum (cannot exceed available processors)
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
    /// <returns>
    /// A tuple containing:
    /// - isValid: true if the value is valid, false otherwise
    /// - errorMessage: a descriptive error message if invalid, null if valid
    /// </returns>
    /// <example>
    /// <code>
    /// var (isValid, error) = ResourceValidator.ValidateTimeout(60);
    /// if (!isValid)
    /// {
    ///     Console.WriteLine(error);
    /// }
    /// </code>
    /// </example>
    public static (bool isValid, string? errorMessage) ValidateTimeout(int? timeoutMinutes)
    {
        // Null is valid (no timeout specified)
        if (!timeoutMinutes.HasValue)
        {
            return (true, null);
        }

        var value = timeoutMinutes.Value;

        // Check minimum (must be positive)
        if (value <= 0)
        {
            return (false, $"Timeout must be greater than 0 minutes. Provided: {value}.");
        }

        // Check maximum (24 hours)
        if (value > MaxTimeoutMinutes)
        {
            return (false, $"Timeout cannot exceed {MaxTimeoutMinutes} minutes (24 hours). Provided: {value}.");
        }

        return (true, null);
    }

    /// <summary>
    /// Validates all resource limits from a ContainerOptions object.
    /// Returns a list of all validation errors found.
    /// </summary>
    /// <param name="memoryLimit">Optional memory limit in bytes.</param>
    /// <param name="cpuLimit">Optional CPU limit in cores.</param>
    /// <param name="timeoutMinutes">Optional timeout in minutes.</param>
    /// <returns>A list of error messages for any invalid values. Empty list if all values are valid.</returns>
    /// <example>
    /// <code>
    /// var errors = ResourceValidator.ValidateAll(8_000_000_000, 2.0, 60);
    /// if (errors.Any())
    /// {
    ///     foreach (var error in errors)
    ///     {
    ///         Console.WriteLine(error);
    ///     }
    /// }
    /// </code>
    /// </example>
    public static List<string> ValidateAll(long? memoryLimit = null, double? cpuLimit = null, int? timeoutMinutes = null)
    {
        var errors = new List<string>();

        var (memoryValid, memoryError) = ValidateMemoryLimit(memoryLimit);
        if (!memoryValid && memoryError != null)
        {
            errors.Add(memoryError);
        }

        var (cpuValid, cpuError) = ValidateCpuLimit(cpuLimit);
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

    /// <summary>
    /// Converts memory from megabytes to bytes.
    /// </summary>
    /// <param name="megabytes">The memory size in megabytes.</param>
    /// <returns>The memory size in bytes.</returns>
    public static long MegabytesToBytes(long megabytes)
    {
        return megabytes * 1024 * 1024;
    }

    /// <summary>
    /// Converts memory from gigabytes to bytes.
    /// </summary>
    /// <param name="gigabytes">The memory size in gigabytes.</param>
    /// <returns>The memory size in bytes.</returns>
    public static long GigabytesToBytes(long gigabytes)
    {
        return gigabytes * 1024 * 1024 * 1024;
    }

    /// <summary>
    /// Converts memory from bytes to megabytes.
    /// </summary>
    /// <param name="bytes">The memory size in bytes.</param>
    /// <returns>The memory size in megabytes.</returns>
    public static long BytesToMegabytes(long bytes)
    {
        return bytes / 1024 / 1024;
    }

    /// <summary>
    /// Converts memory from bytes to gigabytes.
    /// </summary>
    /// <param name="bytes">The memory size in bytes.</param>
    /// <returns>The memory size in gigabytes.</returns>
    public static double BytesToGigabytes(long bytes)
    {
        return bytes / 1024.0 / 1024.0 / 1024.0;
    }
}
