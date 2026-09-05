using System.Text.RegularExpressions;
using PDK.Core.Filtering;
using PDK.Core.Logging;
using PDK.Core.Models;
using PDK.Core.Variables;

namespace PDK.Core.Validation;

/// <summary>
/// Builds an execution plan from a validated pipeline.
/// </summary>
public partial class ExecutionPlanBuilder
{
    private const int ScriptPreviewMaxLength = 100;
    private const string MaskedValue = "***MASKED***";

    // Matches ${{ expression }} (GitHub/Azure syntax)
    [GeneratedRegex(@"\$\{\{\s*([^}]+)\s*\}\}", RegexOptions.Compiled)]
    private static partial Regex ExpressionPattern();

    private readonly IVariableResolver? _variableResolver;
    private readonly IVariableExpander? _variableExpander;
    private readonly IExecutorValidator? _executorValidator;
    private readonly IImageMappingProvider? _imageMappingProvider;
    private readonly ISecretMasker? _secretMasker;
    private readonly HashSet<string> _secretNames;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExecutionPlanBuilder"/> class.
    /// </summary>
    /// <param name="variableResolver">Resolver used to list variables and their sources.</param>
    /// <param name="variableExpander">Expander used to resolve <c>${VAR}</c> references.</param>
    /// <param name="executorValidator">Validator used to name the executor of each step.</param>
    /// <param name="secretNames">Additional variable names that must be masked.</param>
    /// <param name="imageMappingProvider">Runtime image mapping; a built-in table is used when null.</param>
    /// <param name="secretMasker">Masker applied to every displayed value.</param>
    public ExecutionPlanBuilder(
        IVariableResolver? variableResolver = null,
        IVariableExpander? variableExpander = null,
        IExecutorValidator? executorValidator = null,
        IEnumerable<string>? secretNames = null,
        IImageMappingProvider? imageMappingProvider = null,
        ISecretMasker? secretMasker = null)
    {
        _variableResolver = variableResolver;
        _variableExpander = variableExpander;
        _executorValidator = executorValidator;
        _imageMappingProvider = imageMappingProvider;
        _secretMasker = secretMasker;
        _secretNames = secretNames?.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Builds an execution plan from a pipeline.
    /// </summary>
    /// <param name="pipeline">The pipeline.</param>
    /// <param name="filePath">The pipeline file path.</param>
    /// <param name="jobExecutionOrder">Execution order per job id (from the dependency phase).</param>
    /// <param name="runnerType">The runner type ("docker", "host" or "auto").</param>
    /// <param name="jobName">When set, only the job with this id or name is included.</param>
    /// <param name="stepFilter">When set, steps the filter excludes are marked <see cref="StepPlanNode.WillRun"/> = false.</param>
    public ExecutionPlan Build(
        Pipeline pipeline,
        string filePath,
        IDictionary<string, int>? jobExecutionOrder = null,
        string runnerType = "auto",
        string? jobName = null,
        IStepFilter? stepFilter = null)
    {
        ArgumentNullException.ThrowIfNull(pipeline);

        var jobs = BuildJobPlans(pipeline, jobExecutionOrder ?? new Dictionary<string, int>(), runnerType, jobName, stepFilter);
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

    /// <summary>
    /// Checks whether a job selection matches a job of the pipeline (by dictionary key, id or name, case-insensitively).
    /// </summary>
    public static bool JobMatches(string key, Job job, string? jobName)
    {
        if (string.IsNullOrWhiteSpace(jobName))
        {
            return true;
        }

        return string.Equals(key, jobName, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(job.Id, jobName, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(job.Name, jobName, StringComparison.OrdinalIgnoreCase);
    }

    private IReadOnlyList<JobPlanNode> BuildJobPlans(
        Pipeline pipeline,
        IDictionary<string, int> executionOrder,
        string runnerType,
        string? jobName,
        IStepFilter? stepFilter)
    {
        var jobPlans = new List<JobPlanNode>();

        foreach (var (jobId, job) in pipeline.Jobs)
        {
            if (!JobMatches(jobId, job, jobName))
            {
                continue;
            }

            var order = executionOrder.TryGetValue(jobId, out var o) ? o : 0;
            var jobPlan = BuildJobPlan(jobId, job, order, runnerType, stepFilter);
            jobPlans.Add(jobPlan);
        }

        // Sort by execution order
        return jobPlans.OrderBy(j => j.ExecutionOrder).ToList();
    }

    private JobPlanNode BuildJobPlan(string jobId, Job job, int executionOrder, string runnerType, IStepFilter? stepFilter)
    {
        var steps = new List<StepPlanNode>();

        for (int i = 0; i < job.Steps.Count; i++)
        {
            var step = job.Steps[i];
            var stepPlan = BuildStepPlan(step, i + 1, job, runnerType, stepFilter);
            steps.Add(stepPlan);
        }

        return new JobPlanNode
        {
            JobId = jobId,
            JobName = string.IsNullOrEmpty(job.Name) ? jobId : job.Name,
            RunsOn = job.RunsOn ?? "unknown",
            ContainerImage = ResolveContainerImage(job),
            DependsOn = job.DependsOn?.ToList() ?? [],
            Steps = steps,
            Environment = MaskSecrets(ResolveEnvironment(job.Environment)),
            Condition = job.Condition?.Expression,
            ExecutionOrder = executionOrder,
            Timeout = job.Timeout
        };
    }

    private StepPlanNode BuildStepPlan(Step step, int index, Job job, string runnerType, IStepFilter? stepFilter)
    {
        var executorName = _executorValidator?.GetExecutorName(step.Type, runnerType) ?? GetDefaultExecutorName(step.Type);

        var willRun = true;
        string? skipReason = null;

        if (!step.Enabled)
        {
            willRun = false;
            skipReason = "Step is disabled (enabled: false)";
        }
        else if (stepFilter != null)
        {
            var filterResult = stepFilter.ShouldExecute(step, index, job);
            if (!filterResult.ShouldExecute)
            {
                willRun = false;
                skipReason = string.IsNullOrWhiteSpace(filterResult.Reason)
                    ? "Excluded by step filter"
                    : filterResult.Reason;
            }
        }

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
            ScriptPreview = GetScriptPreview(step.Script),
            WillRun = willRun,
            SkipReason = skipReason
        };
    }

    /// <summary>
    /// Resolves the image a job would run in: an explicit <c>container:</c> wins, then the runtime
    /// mapping provider, then the built-in table.
    /// </summary>
    private string? ResolveContainerImage(Job job)
    {
        if (!string.IsNullOrWhiteSpace(job.Container))
        {
            return job.Container;
        }

        if (string.IsNullOrWhiteSpace(job.RunsOn))
        {
            return null;
        }

        if (_imageMappingProvider != null)
        {
            try
            {
                var mapped = _imageMappingProvider.MapRunnerToImage(job.RunsOn);
                if (!string.IsNullOrWhiteSpace(mapped))
                {
                    return mapped;
                }
            }
            catch
            {
                // Fall back to the built-in table below
            }
        }

        return MapRunnerToImage(job.RunsOn);
    }

    private IReadOnlyDictionary<string, string> BuildResolvedVariables(Pipeline pipeline)
    {
        var result = new Dictionary<string, string>();

        // Add pipeline variables
        if (pipeline.Variables != null)
        {
            foreach (var (key, value) in pipeline.Variables)
            {
                var resolved = ResolveAndMask(value) ?? value;
                result[key] = MaskIfSecret(key, resolved);
            }
        }

        // Add variables from the resolver, excluding the process environment: it is neither
        // part of the pipeline nor safe to print.
        if (_variableResolver != null)
        {
            foreach (var (name, value) in _variableResolver.GetAllVariables())
            {
                if (result.ContainsKey(name))
                {
                    continue;
                }

                if (_variableResolver.GetSource(name) == VariableSource.Environment)
                {
                    continue;
                }

                result[name] = MaskIfSecret(name, value);
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

        return Mask(value);
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

    /// <summary>
    /// Masks a value entirely when its name denotes a secret (secret source, registered secret
    /// name, or a name that looks like a secret); otherwise runs registered secret values through the masker.
    /// </summary>
    private string MaskIfSecret(string name, string value)
    {
        if (_secretNames.Contains(name) ||
            LooksLikeSecret(name) ||
            _variableResolver?.GetSource(name) == VariableSource.Secret)
        {
            return MaskedValue;
        }

        return Mask(value);
    }

    private string Mask(string value)
    {
        if (_secretMasker == null || string.IsNullOrEmpty(value))
        {
            return value;
        }

        try
        {
            return _secretMasker.MaskSecrets(value);
        }
        catch
        {
            return value;
        }
    }

    /// <summary>
    /// Name heuristic for secrets (SECRET, PASSWORD, TOKEN, API_KEY, APIKEY, PRIVATE).
    /// </summary>
    public static bool LooksLikeSecret(string name)
    {
        return name.Contains("SECRET", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("PASSWORD", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("TOKEN", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("API_KEY", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("APIKEY", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("PRIVATE", StringComparison.OrdinalIgnoreCase);
    }

    private string? GetScriptPreview(string? script)
    {
        if (string.IsNullOrEmpty(script)) return null;

        // Get first line or truncate
        var firstLine = script.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
        if (firstLine == null) return null;

        if (firstLine.Length > ScriptPreviewMaxLength)
        {
            firstLine = firstLine[..ScriptPreviewMaxLength] + "...";
        }

        return Mask(firstLine);
    }

    /// <summary>
    /// Built-in runner-to-image table used when no <see cref="IImageMappingProvider"/> is available.
    /// </summary>
    private static string? MapRunnerToImage(string? runsOn)
    {
        if (string.IsNullOrEmpty(runsOn)) return null;

        return runsOn.ToLowerInvariant() switch
        {
            "ubuntu-latest" or "ubuntu-22.04" => "buildpack-deps:jammy",
            "ubuntu-24.04" => "buildpack-deps:noble",
            "ubuntu-20.04" => "buildpack-deps:focal",
            "windows-latest" or "windows-2022" => "mcr.microsoft.com/windows/servercore:ltsc2022",
            "windows-2019" => "mcr.microsoft.com/windows/servercore:ltsc2019",
            "macos-latest" or "macos-14" or "macos-13" or "macos-12" => null, // No Docker for macOS
            _ => runsOn.Contains("ubuntu", StringComparison.OrdinalIgnoreCase) ? "buildpack-deps:jammy" : null
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
            StepType.Unknown => "unknown",
            _ => stepType.ToString().ToLowerInvariant()
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
