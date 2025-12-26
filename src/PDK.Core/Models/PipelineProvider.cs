namespace PDK.Core.Models;

/// <summary>
/// Identifies the CI/CD platform that a pipeline originates from.
/// </summary>
public enum PipelineProvider
{
    /// <summary>
    /// The pipeline provider could not be determined.
    /// </summary>
    Unknown,

    /// <summary>
    /// GitHub Actions workflow.
    /// </summary>
    GitHub,

    /// <summary>
    /// Azure DevOps pipeline.
    /// </summary>
    AzureDevOps,

    /// <summary>
    /// GitLab CI/CD pipeline.
    /// </summary>
    GitLab
}