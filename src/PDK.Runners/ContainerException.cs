namespace PDK.Runners;

/// <summary>
/// Exception thrown when Docker container operations fail.
/// </summary>
public class ContainerException : Exception
{
    /// <summary>
    /// Gets the ID of the container that caused the exception, if available.
    /// </summary>
    public string? ContainerId { get; init; }

    /// <summary>
    /// Gets the Docker image name associated with the exception, if available.
    /// </summary>
    public string? Image { get; init; }

    /// <summary>
    /// Gets the command that was being executed when the exception occurred, if available.
    /// </summary>
    public string? Command { get; init; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ContainerException"/> class with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public ContainerException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ContainerException"/> class with a specified error message
    /// and a reference to the inner exception that is the cause of this exception.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public ContainerException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
