using PDK.Core.Models;
using PDK.Core.Validation;
using PDK.Runners.StepExecutors;

namespace PDK.Runners.Validation;

/// <summary>
/// Validates step executor availability by checking registered executors.
/// </summary>
public class ExecutorValidator : IExecutorValidator
{
    private readonly StepExecutorFactory _dockerFactory;
    private readonly HostStepExecutorFactory _hostFactory;

    public ExecutorValidator(
        StepExecutorFactory dockerFactory,
        HostStepExecutorFactory hostFactory)
    {
        _dockerFactory = dockerFactory;
        _hostFactory = hostFactory;
    }

    /// <inheritdoc/>
    public bool HasExecutor(StepType stepType, string runnerType)
    {
        if (stepType == StepType.Unknown)
        {
            return false;
        }

        var stepTypeName = ConvertStepTypeToString(stepType);
        if (stepTypeName == null)
        {
            return false;
        }

        return runnerType.ToLowerInvariant() switch
        {
            "docker" => HasDockerExecutor(stepTypeName),
            "host" => HasHostExecutor(stepTypeName),
            "auto" => HasDockerExecutor(stepTypeName) || HasHostExecutor(stepTypeName),
            _ => HasDockerExecutor(stepTypeName) || HasHostExecutor(stepTypeName)
        };
    }

    /// <inheritdoc/>
    public string? GetExecutorName(StepType stepType, string runnerType)
    {
        if (stepType == StepType.Unknown)
        {
            return null;
        }

        var stepTypeName = ConvertStepTypeToString(stepType);
        if (stepTypeName == null)
        {
            return null;
        }

        try
        {
            return runnerType.ToLowerInvariant() switch
            {
                "docker" => _dockerFactory.GetExecutor(stepTypeName).GetType().Name,
                "host" => _hostFactory.GetExecutor(stepTypeName).GetType().Name,
                "auto" => GetAutoExecutorName(stepTypeName),
                _ => GetAutoExecutorName(stepTypeName)
            };
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> GetAvailableStepTypes(string runnerType)
    {
        var types = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (runnerType.Equals("docker", StringComparison.OrdinalIgnoreCase) ||
            runnerType.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var type in GetDockerStepTypes())
            {
                types.Add(type);
            }
        }

        if (runnerType.Equals("host", StringComparison.OrdinalIgnoreCase) ||
            runnerType.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var type in _hostFactory.GetRegisteredStepTypes())
            {
                types.Add(type);
            }
        }

        return types.OrderBy(t => t).ToList();
    }

    private bool HasDockerExecutor(string stepTypeName)
    {
        try
        {
            _dockerFactory.GetExecutor(stepTypeName);
            return true;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    private bool HasHostExecutor(string stepTypeName)
    {
        return _hostFactory.HasExecutor(stepTypeName);
    }

    private string? GetAutoExecutorName(string stepTypeName)
    {
        // Prefer Docker executor if available
        try
        {
            return _dockerFactory.GetExecutor(stepTypeName).GetType().Name;
        }
        catch (NotSupportedException)
        {
            // Fall back to host executor
            try
            {
                return _hostFactory.GetExecutor(stepTypeName).GetType().Name;
            }
            catch (NotSupportedException)
            {
                return null;
            }
        }
    }

    private IEnumerable<string> GetDockerStepTypes()
    {
        // Return known Docker step types
        // The Docker factory doesn't have GetRegisteredStepTypes, so we use known types
        return new[]
        {
            "checkout", "script", "bash", "pwsh", "docker",
            "npm", "dotnet", "uploadartifact", "downloadartifact"
        };
    }

    private static string? ConvertStepTypeToString(StepType stepType)
    {
        return stepType switch
        {
            StepType.Checkout => "checkout",
            StepType.Script => "script",
            StepType.Bash => "bash",
            StepType.PowerShell => "pwsh",
            StepType.Docker => "docker",
            StepType.Npm => "npm",
            StepType.Dotnet => "dotnet",
            StepType.Python => "python",
            StepType.Maven => "maven",
            StepType.Gradle => "gradle",
            StepType.FileOperation => "fileoperation",
            StepType.UploadArtifact => "uploadartifact",
            StepType.DownloadArtifact => "downloadartifact",
            _ => null
        };
    }
}
