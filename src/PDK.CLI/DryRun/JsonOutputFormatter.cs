using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using PDK.Core.Validation;

namespace PDK.CLI.DryRun;

/// <summary>
/// Formats dry-run results as JSON for machine consumption.
/// </summary>
public class JsonOutputFormatter
{
    private readonly ILogger<JsonOutputFormatter> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public JsonOutputFormatter(ILogger<JsonOutputFormatter> logger)
    {
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters =
            {
                new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
            }
        };
    }

    /// <summary>
    /// Serializes the dry-run result to JSON string.
    /// </summary>
    public string Serialize(DryRunResult result)
    {
        var output = CreateOutputModel(result);
        return JsonSerializer.Serialize(output, _jsonOptions);
    }

    /// <summary>
    /// Writes the dry-run result to a JSON file.
    /// </summary>
    public async Task WriteToFileAsync(
        DryRunResult result,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        var output = CreateOutputModel(result);
        var json = JsonSerializer.Serialize(output, _jsonOptions);

        await File.WriteAllTextAsync(filePath, json, cancellationToken);
        _logger.LogInformation("Dry-run results written to {FilePath}", filePath);
    }

    private static DryRunJsonOutput CreateOutputModel(DryRunResult result)
    {
        return new DryRunJsonOutput
        {
            IsValid = result.IsValid,
            ValidationDurationMs = (int)result.ValidationDuration.TotalMilliseconds,
            Pipeline = result.ExecutionPlan != null
                ? new PipelineInfo
                {
                    Name = result.ExecutionPlan.PipelineName,
                    FilePath = result.ExecutionPlan.FilePath,
                    Provider = result.ExecutionPlan.Provider.ToString()
                }
                : null,
            Summary = new ValidationSummary
            {
                TotalJobs = result.ExecutionPlan?.TotalJobs ?? 0,
                TotalSteps = result.ExecutionPlan?.TotalSteps ?? 0,
                ErrorCount = result.Errors.Count,
                WarningCount = result.Warnings.Count
            },
            Errors = result.Errors.Select(e => CreateErrorModel(e)).ToList(),
            Warnings = result.Warnings.Select(e => CreateErrorModel(e)).ToList(),
            ExecutionPlan = result.ExecutionPlan != null
                ? CreateExecutionPlanModel(result.ExecutionPlan)
                : null,
            PhaseResults = result.PhaseResults.Values.Select(p => new PhaseResultModel
            {
                Name = p.PhaseName,
                Passed = p.Passed,
                DurationMs = (int)p.Duration.TotalMilliseconds,
                ErrorCount = p.ErrorCount,
                WarningCount = p.WarningCount
            }).ToList()
        };
    }

    private static ValidationErrorModel CreateErrorModel(DryRunValidationError error)
    {
        return new ValidationErrorModel
        {
            Code = error.ErrorCode,
            Message = error.Message,
            Severity = error.Severity.ToString().ToLowerInvariant(),
            Category = error.Category.ToString(),
            JobId = error.JobId,
            StepName = error.StepName,
            StepIndex = error.StepIndex,
            LineNumber = error.LineNumber,
            Suggestions = error.Suggestions.ToList()
        };
    }

    private static ExecutionPlanModel CreateExecutionPlanModel(ExecutionPlan plan)
    {
        return new ExecutionPlanModel
        {
            Jobs = plan.Jobs.Select(j => new JobModel
            {
                JobId = j.JobId,
                JobName = j.JobName,
                RunsOn = j.RunsOn,
                ContainerImage = j.ContainerImage,
                DependsOn = j.DependsOn.ToList(),
                ExecutionOrder = j.ExecutionOrder,
                Condition = j.Condition,
                TimeoutMinutes = j.Timeout?.TotalMinutes,
                Environment = j.Environment.ToDictionary(kv => kv.Key, kv => kv.Value),
                Steps = j.Steps.Select(s => new StepModel
                {
                    Index = s.Index,
                    StepId = s.StepId,
                    StepName = s.StepName,
                    Type = s.TypeName,
                    ExecutorName = s.ExecutorName,
                    Shell = s.Shell,
                    WorkingDirectory = s.WorkingDirectory,
                    Condition = s.Condition,
                    ContinueOnError = s.ContinueOnError,
                    Needs = s.Needs.ToList(),
                    ScriptPreview = s.ScriptPreview,
                    Environment = s.Environment.ToDictionary(kv => kv.Key, kv => kv.Value),
                    Inputs = s.Inputs.ToDictionary(kv => kv.Key, kv => kv.Value)
                }).ToList()
            }).ToList(),
            Variables = plan.ResolvedVariables.ToDictionary(kv => kv.Key, kv => kv.Value)
        };
    }

    #region JSON Output Models

    private class DryRunJsonOutput
    {
        public bool IsValid { get; set; }
        public int ValidationDurationMs { get; set; }
        public PipelineInfo? Pipeline { get; set; }
        public ValidationSummary? Summary { get; set; }
        public List<ValidationErrorModel>? Errors { get; set; }
        public List<ValidationErrorModel>? Warnings { get; set; }
        public ExecutionPlanModel? ExecutionPlan { get; set; }
        public List<PhaseResultModel>? PhaseResults { get; set; }
    }

    private class PipelineInfo
    {
        public string? Name { get; set; }
        public string? FilePath { get; set; }
        public string? Provider { get; set; }
    }

    private class ValidationSummary
    {
        public int TotalJobs { get; set; }
        public int TotalSteps { get; set; }
        public int ErrorCount { get; set; }
        public int WarningCount { get; set; }
    }

    private class ValidationErrorModel
    {
        public string? Code { get; set; }
        public string? Message { get; set; }
        public string? Severity { get; set; }
        public string? Category { get; set; }
        public string? JobId { get; set; }
        public string? StepName { get; set; }
        public int? StepIndex { get; set; }
        public int? LineNumber { get; set; }
        public List<string>? Suggestions { get; set; }
    }

    private class PhaseResultModel
    {
        public string? Name { get; set; }
        public bool Passed { get; set; }
        public int DurationMs { get; set; }
        public int ErrorCount { get; set; }
        public int WarningCount { get; set; }
    }

    private class ExecutionPlanModel
    {
        public List<JobModel>? Jobs { get; set; }
        public Dictionary<string, string>? Variables { get; set; }
    }

    private class JobModel
    {
        public string? JobId { get; set; }
        public string? JobName { get; set; }
        public string? RunsOn { get; set; }
        public string? ContainerImage { get; set; }
        public List<string>? DependsOn { get; set; }
        public int ExecutionOrder { get; set; }
        public string? Condition { get; set; }
        public double? TimeoutMinutes { get; set; }
        public Dictionary<string, string>? Environment { get; set; }
        public List<StepModel>? Steps { get; set; }
    }

    private class StepModel
    {
        public int Index { get; set; }
        public string? StepId { get; set; }
        public string? StepName { get; set; }
        public string? Type { get; set; }
        public string? ExecutorName { get; set; }
        public string? Shell { get; set; }
        public string? WorkingDirectory { get; set; }
        public string? Condition { get; set; }
        public bool ContinueOnError { get; set; }
        public List<string>? Needs { get; set; }
        public string? ScriptPreview { get; set; }
        public Dictionary<string, string>? Environment { get; set; }
        public Dictionary<string, string>? Inputs { get; set; }
    }

    #endregion
}
