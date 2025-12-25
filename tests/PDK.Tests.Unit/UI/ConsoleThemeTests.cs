namespace PDK.Tests.Unit.UI;

using Microsoft.Extensions.Logging;
using PDK.CLI.UI;
using Spectre.Console;
using Xunit;

/// <summary>
/// Unit tests for <see cref="ConsoleTheme"/>.
/// </summary>
public class ConsoleThemeTests
{
    [Theory]
    [InlineData(LogLevel.Trace)]
    [InlineData(LogLevel.Debug)]
    [InlineData(LogLevel.Information)]
    [InlineData(LogLevel.Warning)]
    [InlineData(LogLevel.Error)]
    [InlineData(LogLevel.Critical)]
    public void ForLevel_ReturnsNonNullColor_ForAllLogLevels(LogLevel level)
    {
        // Act
        var color = ConsoleTheme.ForLevel(level);

        // Assert
        Assert.NotEqual(Color.Default, color);
    }

    [Fact]
    public void ForLevel_ReturnsTraceColor_ForTracLevel()
    {
        // Act
        var color = ConsoleTheme.ForLevel(LogLevel.Trace);

        // Assert
        Assert.Equal(ConsoleTheme.Trace, color);
    }

    [Fact]
    public void ForLevel_ReturnsDebugColor_ForDebugLevel()
    {
        // Act
        var color = ConsoleTheme.ForLevel(LogLevel.Debug);

        // Assert
        Assert.Equal(ConsoleTheme.Debug, color);
    }

    [Fact]
    public void ForLevel_ReturnsInfoColor_ForInformationLevel()
    {
        // Act
        var color = ConsoleTheme.ForLevel(LogLevel.Information);

        // Assert
        Assert.Equal(ConsoleTheme.Info, color);
    }

    [Fact]
    public void ForLevel_ReturnsWarningColor_ForWarningLevel()
    {
        // Act
        var color = ConsoleTheme.ForLevel(LogLevel.Warning);

        // Assert
        Assert.Equal(ConsoleTheme.Warning, color);
    }

    [Fact]
    public void ForLevel_ReturnsErrorColor_ForErrorLevel()
    {
        // Act
        var color = ConsoleTheme.ForLevel(LogLevel.Error);

        // Assert
        Assert.Equal(ConsoleTheme.Error, color);
    }

    [Fact]
    public void ForLevel_ReturnsErrorColor_ForCriticalLevel()
    {
        // Act
        var color = ConsoleTheme.ForLevel(LogLevel.Critical);

        // Assert
        Assert.Equal(ConsoleTheme.Error, color);
    }

    [Fact]
    public void ForLevel_ReturnsInfoColor_ForNoneLevel()
    {
        // Act
        var color = ConsoleTheme.ForLevel(LogLevel.None);

        // Assert
        Assert.Equal(ConsoleTheme.Info, color);
    }

    [Fact]
    public void ForStepResult_Success_ReturnsSuccessColor()
    {
        // Act
        var color = ConsoleTheme.ForStepResult(success: true);

        // Assert
        Assert.Equal(ConsoleTheme.Success, color);
    }

    [Fact]
    public void ForStepResult_Failure_ReturnsErrorColor()
    {
        // Act
        var color = ConsoleTheme.ForStepResult(success: false);

        // Assert
        Assert.Equal(ConsoleTheme.Error, color);
    }

    [Fact]
    public void ForStepResult_Skipped_ReturnsSkippedColor()
    {
        // Act
        var color = ConsoleTheme.ForStepResult(success: true, skipped: true);

        // Assert
        Assert.Equal(ConsoleTheme.Skipped, color);
    }

    [Fact]
    public void GetResultSymbol_Success_NoColor_ReturnsPlus()
    {
        // Act
        var symbol = ConsoleTheme.GetResultSymbol(success: true, skipped: false, noColor: true);

        // Assert
        Assert.Equal("+", symbol);
    }

    [Fact]
    public void GetResultSymbol_Failure_NoColor_ReturnsX()
    {
        // Act
        var symbol = ConsoleTheme.GetResultSymbol(success: false, skipped: false, noColor: true);

        // Assert
        Assert.Equal("x", symbol);
    }

    [Fact]
    public void GetResultSymbol_Skipped_NoColor_ReturnsDash()
    {
        // Act
        var symbol = ConsoleTheme.GetResultSymbol(success: false, skipped: true, noColor: true);

        // Assert
        Assert.Equal("-", symbol);
    }

    [Fact]
    public void GetResultSymbol_Success_WithColor_ContainsGreen()
    {
        // Act
        var symbol = ConsoleTheme.GetResultSymbol(success: true, skipped: false, noColor: false);

        // Assert
        Assert.Contains("green", symbol);
        Assert.Contains("+", symbol);
    }

    [Fact]
    public void GetResultSymbol_Failure_WithColor_ContainsRed()
    {
        // Act
        var symbol = ConsoleTheme.GetResultSymbol(success: false, skipped: false, noColor: false);

        // Assert
        Assert.Contains("red", symbol);
        Assert.Contains("x", symbol);
    }

    [Theory]
    [InlineData(LogLevel.Trace, "[TRC]")]
    [InlineData(LogLevel.Debug, "[DBG]")]
    [InlineData(LogLevel.Information, "[INF]")]
    [InlineData(LogLevel.Warning, "[WRN]")]
    [InlineData(LogLevel.Error, "[ERR]")]
    [InlineData(LogLevel.Critical, "[CRT]")]
    public void GetLevelSymbol_NoColor_ReturnsPlainSymbol(LogLevel level, string expected)
    {
        // Act
        var symbol = ConsoleTheme.GetLevelSymbol(level, noColor: true);

        // Assert
        Assert.Equal(expected, symbol);
    }

    [Fact]
    public void GetLevelSymbol_WithColor_ContainsMarkup()
    {
        // Act
        var symbol = ConsoleTheme.GetLevelSymbol(LogLevel.Error, noColor: false);

        // Assert
        Assert.Contains("[/]", symbol);
        Assert.Contains("ERR", symbol);
    }

    [Fact]
    public void Format_NoColor_ReturnsPlainText()
    {
        // Arrange
        const string text = "Test message";

        // Act
        var result = ConsoleTheme.Format(text, Color.Red, noColor: true);

        // Assert
        Assert.Equal(text, result);
    }

    [Fact]
    public void Format_WithColor_ContainsMarkup()
    {
        // Arrange
        const string text = "Test message";

        // Act
        var result = ConsoleTheme.Format(text, Color.Red, noColor: false);

        // Assert
        Assert.Contains("[/]", result);
        Assert.Contains("Test message", result);
    }

    [Fact]
    public void Format_EscapesMarkupCharacters()
    {
        // Arrange
        const string text = "[red]dangerous[/]";

        // Act
        var result = ConsoleTheme.Format(text, Color.Blue, noColor: false);

        // Assert
        // The original markup characters should be escaped
        Assert.Contains("[[red]]", result);
    }

    [Fact]
    public void Bold_NoColor_ReturnsPlainText()
    {
        // Arrange
        const string text = "Bold text";

        // Act
        var result = ConsoleTheme.Bold(text, noColor: true);

        // Assert
        Assert.Equal(text, result);
    }

    [Fact]
    public void Bold_WithColor_ContainsBoldMarkup()
    {
        // Arrange
        const string text = "Bold text";

        // Act
        var result = ConsoleTheme.Bold(text, noColor: false);

        // Assert
        Assert.Contains("[bold]", result);
        Assert.Contains("[/]", result);
        Assert.Contains("Bold text", result);
    }

    [Fact]
    public void Dim_NoColor_ReturnsPlainText()
    {
        // Arrange
        const string text = "Dim text";

        // Act
        var result = ConsoleTheme.Dim(text, noColor: true);

        // Assert
        Assert.Equal(text, result);
    }

    [Fact]
    public void Dim_WithColor_ContainsDimMarkup()
    {
        // Arrange
        const string text = "Dim text";

        // Act
        var result = ConsoleTheme.Dim(text, noColor: false);

        // Assert
        Assert.Contains("[dim]", result);
        Assert.Contains("[/]", result);
        Assert.Contains("Dim text", result);
    }

    [Fact]
    public void Colors_AreDefined()
    {
        // Assert - verify key colors are defined and distinct
        Assert.NotEqual(ConsoleTheme.Error, ConsoleTheme.Success);
        Assert.NotEqual(ConsoleTheme.Warning, ConsoleTheme.Info);
        Assert.NotEqual(ConsoleTheme.Debug, ConsoleTheme.Trace);
    }
}
