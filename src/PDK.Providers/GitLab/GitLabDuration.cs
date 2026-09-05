using System.Globalization;
using System.Text.RegularExpressions;

namespace PDK.Providers.GitLab;

/// <summary>
/// Parses the human-readable durations GitLab accepts for <c>timeout</c>, <c>expire_in</c> and <c>start_in</c>:
/// <c>1h 30m</c>, <c>90 minutes</c>, <c>2h</c>, <c>1 day</c>, <c>3 hours 20 min</c>, <c>1h30m</c>, <c>3600</c> (seconds).
/// </summary>
public static class GitLabDuration
{
    private static readonly Regex Component = new(
        @"(?<value>\d+(?:\.\d+)?)\s*(?<unit>[A-Za-z]*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Tries to parse a duration.
    /// </summary>
    /// <param name="text">The duration text.</param>
    /// <param name="duration">The parsed duration.</param>
    /// <returns>True when the text is a valid duration.</returns>
    public static bool TryParse(string? text, out TimeSpan duration)
    {
        duration = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var remaining = text.Trim().Replace(",", " ", StringComparison.Ordinal).Replace(" and ", " ", StringComparison.OrdinalIgnoreCase);
        var total = TimeSpan.Zero;
        var matched = false;
        var position = 0;

        foreach (Match match in Component.Matches(remaining))
        {
            if (match.Length == 0)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(remaining[position..match.Index]))
            {
                return false;
            }

            position = match.Index + match.Length;
            if (!double.TryParse(match.Groups["value"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                return false;
            }

            var seconds = UnitSeconds(match.Groups["unit"].Value);
            if (seconds is null)
            {
                return false;
            }

            total += TimeSpan.FromSeconds(value * seconds.Value);
            matched = true;
        }

        if (!matched || !string.IsNullOrWhiteSpace(remaining[position..]))
        {
            return false;
        }

        duration = total;
        return true;
    }

    private static double? UnitSeconds(string unit) => unit.ToLowerInvariant() switch
    {
        "" or "s" or "sec" or "secs" or "second" or "seconds" => 1,
        "m" or "min" or "mins" or "minute" or "minutes" => 60,
        "h" or "hr" or "hrs" or "hour" or "hours" => 3600,
        "d" or "day" or "days" => 86400,
        "w" or "wk" or "wks" or "week" or "weeks" => 7 * 86400,
        "mo" or "mos" or "month" or "months" => 30 * 86400,
        "y" or "yr" or "yrs" or "year" or "years" => 365 * 86400,
        _ => null
    };
}
