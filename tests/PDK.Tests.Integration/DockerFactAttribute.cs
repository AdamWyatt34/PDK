namespace PDK.Tests.Integration;

/// <summary>
/// A <see cref="FactAttribute"/> for tests that need a reachable Docker daemon running Linux containers.
/// The test is reported as skipped (not failed) when no daemon can be reached.
/// Combine with <c>[Trait("Category", "RequiresDocker")]</c> so the tests can also be filtered explicitly.
/// See <see cref="DockerAvailability"/> for the probe and the <c>PDK_DOCKER_TESTS</c> override.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class DockerFactAttribute : FactAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DockerFactAttribute"/> class.
    /// </summary>
    public DockerFactAttribute()
    {
        if (!DockerAvailability.IsAvailable)
        {
            Skip = DockerAvailability.SkipReason;
        }
    }
}
