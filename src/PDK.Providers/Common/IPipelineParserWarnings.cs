namespace PDK.Providers;

/// <summary>
/// Optional diagnostics exposed by the parsers: non-fatal findings from the most recent parse, such as sections that
/// are ignored locally (service containers, variable groups) or constructs that are tolerated but not executed.
/// Kept outside <see cref="PDK.Core.Models.IPipelineParser"/> so the core contract stays unchanged.
/// </summary>
public interface IPipelineParserWarnings
{
    /// <summary>Gets the warnings produced by the most recent <c>Parse</c>/<c>ParseFile</c> call.</summary>
    IReadOnlyList<string> Warnings { get; }
}
