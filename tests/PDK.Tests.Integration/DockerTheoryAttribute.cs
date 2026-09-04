namespace PDK.Tests.Integration;

/// <summary>
/// A <see cref="TheoryAttribute"/> for data-driven tests that need a reachable Docker daemon running
/// Linux containers. Every case is reported as skipped (not failed) when no daemon can be reached.
/// See <see cref="DockerAvailability"/> for the probe and the <c>PDK_DOCKER_TESTS</c> override.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class DockerTheoryAttribute : TheoryAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DockerTheoryAttribute"/> class.
    /// </summary>
    public DockerTheoryAttribute()
    {
        if (!DockerAvailability.IsAvailable)
        {
            Skip = DockerAvailability.SkipReason;
        }
    }
}
