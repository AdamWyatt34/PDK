using PDK.Core.Models;

namespace PDK.Core.Filtering.Filters;

/// <summary>
/// Combines multiple filters with configurable logic.
/// Filter precedence: Skip (Exclusion) > Include (Name/Index/Range) > Default (execute all).
/// </summary>
public sealed class CompositeFilter : IStepFilter
{
    private readonly IReadOnlyList<IStepFilter> _inclusionFilters;
    private readonly IStepFilter? _exclusionFilter;
    private readonly IStepFilter? _jobFilter;
    private readonly bool _hasInclusionFilters;

    /// <summary>
    /// Initializes a new instance of the <see cref="CompositeFilter"/> class.
    /// </summary>
    /// <param name="inclusionFilters">Filters that include steps (Name, Index, Range). A step must match at least one.</param>
    /// <param name="exclusionFilter">Filter that excludes steps (Skip). Takes precedence over inclusion.</param>
    /// <param name="jobFilter">Filter that selects jobs. Applied first.</param>
    public CompositeFilter(
        IEnumerable<IStepFilter>? inclusionFilters = null,
        IStepFilter? exclusionFilter = null,
        IStepFilter? jobFilter = null)
    {
        _inclusionFilters = (inclusionFilters ?? []).ToList();
        _exclusionFilter = exclusionFilter;
        _jobFilter = jobFilter;
        _hasInclusionFilters = _inclusionFilters.Count > 0;
    }

    /// <inheritdoc/>
    public FilterResult ShouldExecute(Step step, int stepIndex, Job job)
    {
        // 1. Check job filter first
        if (_jobFilter != null)
        {
            var jobResult = _jobFilter.ShouldExecute(step, stepIndex, job);
            if (!jobResult.ShouldExecute)
            {
                return jobResult;
            }
        }

        // 2. Check exclusion filter (highest precedence)
        if (_exclusionFilter != null)
        {
            var exclusionResult = _exclusionFilter.ShouldExecute(step, stepIndex, job);
            if (!exclusionResult.ShouldExecute)
            {
                return exclusionResult;
            }
        }

        // 3. Check inclusion filters (step must match at least one)
        if (_hasInclusionFilters)
        {
            foreach (var filter in _inclusionFilters)
            {
                var result = filter.ShouldExecute(step, stepIndex, job);
                if (result.ShouldExecute)
                {
                    return result;
                }
            }

            // Step didn't match any inclusion filter
            var stepName = step.Name ?? $"Step {stepIndex}";
            return FilterResult.FilteredOut($"Step '{stepName}' did not match any inclusion filters");
        }

        // 4. No inclusion filters - default to execute all
        return FilterResult.Execute("No inclusion filters applied");
    }

    /// <summary>
    /// Builder for creating composite filters.
    /// </summary>
    public class Builder
    {
        private readonly List<IStepFilter> _inclusionFilters = [];
        private IStepFilter? _exclusionFilter;
        private IStepFilter? _jobFilter;

        /// <summary>
        /// Adds an inclusion filter (step must match at least one).
        /// </summary>
        public Builder AddInclusionFilter(IStepFilter filter)
        {
            _inclusionFilters.Add(filter);
            return this;
        }

        /// <summary>
        /// Adds a step name filter.
        /// </summary>
        public Builder WithStepNames(IEnumerable<string> patterns, int fuzzyThreshold = StringMatcher.DefaultFuzzyThreshold)
        {
            var patternList = patterns.ToList();
            if (patternList.Count > 0)
            {
                _inclusionFilters.Add(new StepNameFilter(patternList, fuzzyThreshold));
            }
            return this;
        }

        /// <summary>
        /// Adds a step index filter.
        /// </summary>
        public Builder WithStepIndices(IEnumerable<int> indices)
        {
            var indexList = indices.ToList();
            if (indexList.Count > 0)
            {
                _inclusionFilters.Add(new StepIndexFilter(indexList));
            }
            return this;
        }

        /// <summary>
        /// Adds a step range filter.
        /// </summary>
        public Builder WithStepRanges(IEnumerable<StepRange> ranges)
        {
            var rangeList = ranges.ToList();
            if (rangeList.Count > 0)
            {
                _inclusionFilters.Add(new StepRangeFilter(rangeList));
            }
            return this;
        }

        /// <summary>
        /// Sets the exclusion filter (skip steps).
        /// </summary>
        public Builder WithExclusionFilter(IStepFilter filter)
        {
            _exclusionFilter = filter;
            return this;
        }

        /// <summary>
        /// Adds step exclusion patterns.
        /// </summary>
        public Builder WithSkipSteps(IEnumerable<string> patterns, int fuzzyThreshold = StringMatcher.DefaultFuzzyThreshold)
        {
            var patternList = patterns.ToList();
            if (patternList.Count > 0)
            {
                _exclusionFilter = new StepExclusionFilter(patternList, fuzzyThreshold);
            }
            return this;
        }

        /// <summary>
        /// Sets the job filter.
        /// </summary>
        public Builder WithJobFilter(IStepFilter filter)
        {
            _jobFilter = filter;
            return this;
        }

        /// <summary>
        /// Adds job name filter.
        /// </summary>
        public Builder WithJobs(IEnumerable<string> jobNames)
        {
            var nameList = jobNames.ToList();
            if (nameList.Count > 0)
            {
                _jobFilter = new JobFilter(nameList);
            }
            return this;
        }

        /// <summary>
        /// Builds the composite filter.
        /// </summary>
        public CompositeFilter Build()
            => new(_inclusionFilters, _exclusionFilter, _jobFilter);

        /// <summary>
        /// Checks if any filters have been added.
        /// </summary>
        public bool HasFilters => _inclusionFilters.Count > 0 || _exclusionFilter != null || _jobFilter != null;
    }
}
