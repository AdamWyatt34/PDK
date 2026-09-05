namespace PDK.Core.Progress;

/// <summary>
/// Wraps a progress reporter so every step and output line carries the job it belongs to.
/// Used when jobs run concurrently and their output interleaves.
/// </summary>
public sealed class PrefixedProgressReporter : IProgressReporter
{
    private readonly IProgressReporter _inner;
    private readonly string _prefix;

    /// <summary>Creates a prefixed reporter.</summary>
    /// <param name="inner">The reporter that renders the output.</param>
    /// <param name="jobName">The job whose events are reported.</param>
    public PrefixedProgressReporter(IProgressReporter inner, string jobName)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _prefix = string.IsNullOrWhiteSpace(jobName) ? "job" : jobName.Trim();
    }

    /// <summary>Gets the job name used as the prefix.</summary>
    public string JobName => _prefix;

    /// <inheritdoc/>
    public Task ReportJobStartAsync(string jobName, int currentJob, int totalJobs, CancellationToken cancellationToken = default)
        => _inner.ReportJobStartAsync(jobName, currentJob, totalJobs, cancellationToken);

    /// <inheritdoc/>
    public Task ReportJobCompleteAsync(string jobName, bool success, TimeSpan duration, CancellationToken cancellationToken = default)
        => _inner.ReportJobCompleteAsync(jobName, success, duration, cancellationToken);

    /// <inheritdoc/>
    public Task ReportStepStartAsync(string stepName, int currentStep, int totalSteps, CancellationToken cancellationToken = default)
        => _inner.ReportStepStartAsync(Decorate(stepName), currentStep, totalSteps, cancellationToken);

    /// <inheritdoc/>
    public Task ReportStepCompleteAsync(string stepName, bool success, TimeSpan duration, CancellationToken cancellationToken = default)
        => _inner.ReportStepCompleteAsync(Decorate(stepName), success, duration, cancellationToken);

    /// <inheritdoc/>
    public Task ReportStepSkippedAsync(string stepName, int currentStep, int totalSteps, string? reason, CancellationToken cancellationToken = default)
        => _inner.ReportStepSkippedAsync(Decorate(stepName), currentStep, totalSteps, reason, cancellationToken);

    /// <inheritdoc/>
    public Task ReportOutputAsync(string line, CancellationToken cancellationToken = default)
        => _inner.ReportOutputAsync($"[{_prefix}] {line}", cancellationToken);

    /// <inheritdoc/>
    public Task ReportProgressAsync(double percentage, string message, CancellationToken cancellationToken = default)
        => _inner.ReportProgressAsync(percentage, $"[{_prefix}] {message}", cancellationToken);

    private string Decorate(string stepName) => $"{_prefix} › {stepName}";
}
