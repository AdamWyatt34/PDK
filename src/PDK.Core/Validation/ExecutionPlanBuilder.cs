using System.Text.RegularExpressions;
using PDK.Core.Models;
using PDK.Core.Variables;

namespace PDK.Core.Validation;

/// <summary>
/// Builds an execution plan from a validated pipeline.
/// </summary>
public partial class ExecutionPlanBuilder
{
    private const int ScriptPreviewMaxLength = 100;

    // Matches ${{ expression }} (GitHub/Azure syntax)
    [GeneratedRegex(@"\$\{\{\s*([^}]+)\s*\}\}", RegexOptions.Compiled)]
    private static partial Regex ExpressionPattern();

    private readonly IVariableResolver? _variableResolver;
    private readonly IVariableExpander? _variableExpander;
    private readonly IExecutorValidator? _executorValidator;
    private readonly HashSet<string> _secretNames;

    public ExecutionPlanBuilder(
        IVariableResolver? variableResolver = null,
        IVariableExpander? variableExpander = null,
        IExecutorValidator? executorValidator = null,
        IEnumerable<string>? secretNames = null)
    {
        _variableResolver = variableResolver;
        _variableExpander = variableExpander;
        _executorValidator = executorValidator;
        _secretNames = secretNames?.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new HashSet<string>();
    }

    /// <summary>
    /// Builds an execution plan from a pipeline.
    /// </summary>
    public ExecutionPlan Build(
        Pipeline pipeline,
        string filePath,
        IDictionary<string, int>? jobExecutionOrder = null,
        string runnerType = "auto")
    {
        var jobs = BuildJobPlans(pipeline, jobExecutionOrder ?? new Dictionary<string, int>(), runnerType);
        var resolvedVariables = BuildResolvedVariables(pipeline);

        return new ExecutionPlan
        {
            PipelineName = pipeline.Name ?? "Unnamed Pipeline",
            FilePath = filePath,
            Provider = pipeline.Provider,
            Jobs = jobs,
            ResolvedVariables = resolvedVariables
        };
    }

    private IReadOnlyList<JobPlanNode> BuildJobPlans(
        Pipeline pipeline,
        IDictionary<string, int> executionOrder,
        string runnerType)
    {
        var jobPlans = new List<JobPlanNode>();

        foreach (var (jobId, job) in pipeline.Jobs)
        {
            var order = executionOrder.TryGetValue(jobId, out var o) ? o : 0;
            var jobPlan = BuildJobPlan(jobId, job, order, runnerType);
            jobPlans.Add(jobPlan);
        }

        // Sort by execution order
        return jobPlans.OrderBy(j => j.ExecutionOrder).ToList();
    }

    private JobPlanNode BuildJobPlan(string jobId, Job job, int executionOrder, string runnerType)
    {
        var steps = new List<StepPlanNode>();

        for (int i = 0; i < job.Steps.Count; i++)
        {
            var step = job.Steps[i];
            var stepPlan = BuildStepPlan(step, i + 1, runnerType);
            steps.Add(stepPlan);
        }

        return new JobPlanNode
        {
            JobId = jobId,
            JobName = job.Name ?? jobId,
            RunsOn = job.RunsOn ?? "unknown",
            ContainerImage = MapRunnerToImage(job.RunsOn),
            DependsOn = job.DependsOn?.ToList() ?? [],
            Steps = steps,
            Environment = MaskSecrets(ResolveEnvironment(job.Environment)),
            Condition = job.Condition?.Expression,
            ExecutionOrder = executionOrder,
            Timeout = job.Timeout
        };
    }

    private StepPlanNode BuildStepPlan(Step step, int index, string runnerType)
    {
        var executorName = _executorValidator?.GetExecutorName(step.Type, runnerType) ?? GetDefaultExecutorName(step.Type);

        return new StepPlanNode
        {
            Index = index,
            StepId = step.Id,
            StepName = step.Name ?? $"Step {index}",
            Type = step.Type,
            TypeName = GetStepTypeName(step.Type),
            ExecutorName = executorName ?? "Unknown",
            Shell = step.Shell,
            WorkingDirectory = ResolveAndMask(step.WorkingDirectory),
            Environment = MaskSecrets(ResolveEnvironment(step.Environment)),
            Inputs = MaskSecrets(ResolveInputs(step.With)),
            Condition = step.Condition?.Expression,
            ContinueOnError = step.ContinueOnError,
            Needs = step.Needs?.ToList() ?? [],
            ScriptPreview = GetScriptPreview(step.Script)
        };
    }

    private IReadOnlyDictionary<string, string> BuildResolvedVariables(Pipeline pipeline)
    {
        var result = new Dictionary<string, string>();

        // Add pipeline variables
        if (pipeline.Variables != null)
        {
            foreach (var (key, value) in pipeline.Variables)
            {
                var resolved = ResolveAndMask(value);
                result[key] = resolved ?? value;
            }
        }

        // Add resolved variables from resolver
        if (_variableResolver != null)
        {
            foreach (var (name, value) in _variableResolver.GetAllVariables())
            {
                if (!result.ContainsKey(name))
                {
                    result[name] = MaskIfSecret(name, value);
                }
            }
        }

        return result;
    }

    private Dictionary<string, string> ResolveEnvironment(IDictionary<string, string>? env)
    {
        if (env == null) return new Dictionary<string, string>();

        var result = new Dictionary<string, string>();
        foreach (var (key, value) in env)
        {
            result[key] = ResolveAndMask(value) ?? value;
        }
        return result;
    }

    private Dictionary<string, string> ResolveInputs(IDictionary<string, string>? inputs)
    {
        if (inputs == null) return new Dictionary<string, string>();

        var result = new Dictionary<string, string>();
        foreach (var (key, value) in inputs)
        {
            result[key] = ResolveAndMask(value) ?? value;
        }
        return result;
    }

    private string? ResolveAndMask(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value;

        // Replace ${{ expr }} with <runtime:expr> placeholder
        value = ExpressionPattern().Replace(value, match =>
        {
            var expr = match.Groups[1].Value.Trim();
            return $"<runtime:{expr}>";
        });

        // Expand ${VAR} style variables if we have an expander
        if (_variableExpander != null && _variableResolver != null)
        {
            try
            {
                value = _variableExpander.Expand(value, _variableResolver);
            }
            catch
            {
                // Keep original value if expansion fails
            }
        }

        return value;
    }

    private IReadOnlyDictionary<string, string> MaskSecrets(IDictionary<string, string> dict)
    {
        var result = new Dictionary<string, string>();
        foreach (var (key, value) in dict)
        {
            result[key] = MaskIfSecret(key, value);
        }
        return result;
    }

    private string MaskIfSecret(string name, string value)
    {
        // Check if the key looks like a secret
        if (_secretNames.Contains(name) ||
            name.Contains("SECRET", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("PASSWORD", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("TOKEN", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("API_KEY", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("APIKEY", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("PRIVATE", StringComparison.OrdinalIgnoreCase))
        {
            return "***MASKED***";
        }

        return value;
    }

    private string? GetScriptPreview(string? script)
    {
        if (string.IsNullOrEmpty(script)) return null;

        // Get first line or truncate
        var firstLine = script.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
        if (firstLine == null) return null;

        if (firstLine.Length > ScriptPreviewMaxLength)
        {
            return firstLine[..ScriptPreviewMaxLength] + "...";
        }

        return firstLine;
    }

    private static string? MapRunnerToImage(string? runsOn)
    {
        if (string.IsNullOrEmpty(runsOn)) return null;

        return runsOn.ToLowerInvariant() switch
        {
            "ubuntu-latest" or "ubuntu-22.04" => "ubuntu:22.04",
            "ubuntu-20.04" => "ubuntu:20.04",
            "windows-latest" or "windows-2022" => "mcr.microsoft.com/windows/servercore:ltsc2022",
            "windows-2019" => "mcr.microsoft.com/windows/servercore:ltsc2019",
            "macos-latest" or "macos-14" or "macos-13" or "macos-12" => null, // No Docker for macOS
            _ => runsOn.Contains("ubuntu") ? "ubuntu:22.04" : null
        };
    }

    private static string GetStepTypeName(StepType stepType)
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
            _ => "unknown"
        };
    }

    private static string? GetDefaultExecutorName(StepType stepType)
    {
        return stepType switch
        {
            StepType.Checkout => "CheckoutStepExecutor",
            StepType.Script => "ScriptStepExecutor",
            StepType.Bash => "ScriptStepExecutor",
            StepType.PowerShell => "PowerShellStepExecutor",
            StepType.Docker => "DockerStepExecutor",
            StepType.Npm => "NpmStepExecutor",
            StepType.Dotnet => "DotnetStepExecutor",
            StepType.UploadArtifact => "UploadArtifactExecutor",
            StepType.DownloadArtifact => "DownloadArtifactExecutor",
            _ => null
        };
    }
}
