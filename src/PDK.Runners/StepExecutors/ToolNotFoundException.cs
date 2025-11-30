namespace PDK.Runners.StepExecutors;

/// <summary>
/// Exception thrown when a required tool is not available in the container.
/// </summary>
public class ToolNotFoundException : Exception
{
    /// <summary>
    /// Gets the name of the tool that was not found.
    /// </summary>
    public string ToolName { get; }

    /// <summary>
    /// Gets the container image name where the tool was not found.
    /// </summary>
    public string ImageName { get; }

    /// <summary>
    /// Gets a list of suggested solutions for resolving the missing tool.
    /// </summary>
    public IReadOnlyList<string> Suggestions { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ToolNotFoundException"/> class.
    /// </summary>
    /// <param name="toolName">The name of the tool that was not found.</param>
    /// <param name="imageName">The container image name where the tool was not found.</param>
    /// <param name="suggestions">A list of suggested solutions for resolving the missing tool.</param>
    public ToolNotFoundException(
        string toolName,
        string imageName,
        IReadOnlyList<string> suggestions)
        : base(BuildErrorMessage(toolName, imageName, suggestions))
    {
        ToolName = toolName;
        ImageName = imageName;
        Suggestions = suggestions;
    }

    /// <summary>
    /// Builds a formatted error message with suggestions.
    /// </summary>
    /// <param name="toolName">The name of the tool that was not found.</param>
    /// <param name="imageName">The container image name.</param>
    /// <param name="suggestions">The list of suggested solutions.</param>
    /// <returns>A formatted error message.</returns>
    private static string BuildErrorMessage(
        string toolName,
        string imageName,
        IReadOnlyList<string> suggestions)
    {
        var message = $"Tool '{toolName}' not found in container image '{imageName}'";

        if (suggestions.Count > 0)
        {
            message += "\n\nSolutions:";
            foreach (var suggestion in suggestions)
            {
                message += $"\n  • {suggestion}";
            }
        }

        return message;
    }
}
