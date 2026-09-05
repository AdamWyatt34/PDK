namespace PDK.Runners;

using System.Text;

/// <summary>
/// Quotes command-line arguments for the POSIX shell (<c>sh</c>/<c>bash</c>) and for Windows
/// (<c>cmd.exe</c> / CommandLineToArgvW conventions).
/// </summary>
public static class ShellQuote
{
    /// <summary>
    /// Quotes a single argument for a POSIX shell using single quotes. Safe for any character.
    /// Simple arguments (letters, digits, <c>_ - . / : = + @ , %</c>) are returned unchanged.
    /// </summary>
    /// <param name="value">The argument value.</param>
    /// <returns>The quoted argument.</returns>
    public static string Posix(string value)
    {
        if (value.Length == 0)
        {
            return "''";
        }

        if (value.All(IsSafePosixChar))
        {
            return value;
        }

        return "'" + value.Replace("'", "'\\''") + "'";
    }

    /// <summary>
    /// Quotes a single argument for Windows command lines using double quotes.
    /// Simple arguments are returned unchanged.
    /// </summary>
    /// <param name="value">The argument value.</param>
    /// <returns>The quoted argument.</returns>
    public static string Windows(string value)
    {
        if (value.Length == 0)
        {
            return "\"\"";
        }

        if (value.All(c => IsSafePosixChar(c) || c == '\\'))
        {
            return value;
        }

        var builder = new StringBuilder("\"");
        var backslashes = 0;
        foreach (var c in value)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }

            if (c == '"')
            {
                builder.Append('\\', backslashes * 2 + 1).Append('"');
                backslashes = 0;
                continue;
            }

            builder.Append('\\', backslashes).Append(c);
            backslashes = 0;
        }

        builder.Append('\\', backslashes * 2).Append('"');
        return builder.ToString();
    }

    /// <summary>
    /// Quotes a single argument for the shell of the given platform.
    /// </summary>
    /// <param name="value">The argument value.</param>
    /// <param name="platform">The target platform.</param>
    /// <returns>The quoted argument.</returns>
    public static string Quote(string value, OperatingSystemPlatform platform)
    {
        return platform == OperatingSystemPlatform.Windows ? Windows(value) : Posix(value);
    }

    /// <summary>
    /// Joins arguments into one command line, quoting each for the shell of the given platform.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="platform">The target platform.</param>
    /// <returns>The command line.</returns>
    public static string Join(IEnumerable<string> arguments, OperatingSystemPlatform platform)
    {
        return string.Join(' ', arguments.Select(a => Quote(a, platform)));
    }

    private static bool IsSafePosixChar(char c)
    {
        return char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.' or '/' or ':' or '=' or '+' or '@' or ',' or '%';
    }
}
