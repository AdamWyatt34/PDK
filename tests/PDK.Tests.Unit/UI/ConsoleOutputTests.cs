namespace PDK.Tests.Unit.UI;

using FluentAssertions;
using PDK.CLI.UI;
using Spectre.Console;
using Spectre.Console.Testing;
using Xunit;

public class ConsoleOutputTests
{
    [Fact]
    public void WriteSuccess_WritesSuccessSymbolAndMessage()
    {
        // Arrange
        var console = new TestConsole();
        var output = new ConsoleOutput(console);

        // Act
        output.WriteSuccess("Operation completed");

        // Assert
        console.Output.Should().Contain("+");
        console.Output.Should().Contain("Operation completed");
    }

    [Fact]
    public void WriteError_WritesErrorSymbolAndMessage()
    {
        // Arrange
        var console = new TestConsole();
        var output = new ConsoleOutput(console);

        // Act
        output.WriteError("Something went wrong");

        // Assert
        console.Output.Should().Contain("x");
        console.Output.Should().Contain("Error:");
        console.Output.Should().Contain("Something went wrong");
    }

    [Fact]
    public void WriteWarning_WritesWarningSymbolAndMessage()
    {
        // Arrange
        var console = new TestConsole();
        var output = new ConsoleOutput(console);

        // Act
        output.WriteWarning("This might be a problem");

        // Assert
        console.Output.Should().Contain("!");
        console.Output.Should().Contain("Warning:");
        console.Output.Should().Contain("This might be a problem");
    }

    [Fact]
    public void WriteInfo_WritesInfoSymbolAndMessage()
    {
        // Arrange
        var console = new TestConsole();
        var output = new ConsoleOutput(console);

        // Act
        output.WriteInfo("Some information");

        // Assert
        console.Output.Should().Contain("i");
        console.Output.Should().Contain("Some information");
    }

    [Fact]
    public void WriteDebug_WhenNotVerbose_WritesNothing()
    {
        // Arrange
        var console = new TestConsole();
        var output = new ConsoleOutput(console, verbose: false);

        // Act
        output.WriteDebug("Debug message");

        // Assert
        console.Output.Should().BeEmpty();
    }

    [Fact]
    public void WriteDebug_WhenVerbose_WritesDebugMessage()
    {
        // Arrange
        var console = new TestConsole();
        var output = new ConsoleOutput(console, verbose: true);

        // Act
        output.WriteDebug("Debug message");

        // Assert
        console.Output.Should().Contain("[DEBUG]");
        console.Output.Should().Contain("Debug message");
    }

    [Fact]
    public void WriteInfo_EscapesMarkupCharacters()
    {
        // Arrange
        var console = new TestConsole();
        var output = new ConsoleOutput(console);
        var messageWithMarkup = "Test [red]markup[/] characters";

        // Act
        output.WriteInfo(messageWithMarkup);

        // Assert
        // The message should be escaped, so the literal text should appear
        console.Output.Should().Contain("[red]");
        console.Output.Should().Contain("[/]");
    }

    [Fact]
    public void WriteSuccess_EscapesMarkupCharacters()
    {
        // Arrange
        var console = new TestConsole();
        var output = new ConsoleOutput(console);
        var messageWithMarkup = "Success with [bold]markup[/]";

        // Act
        output.WriteSuccess(messageWithMarkup);

        // Assert
        console.Output.Should().Contain("[bold]");
    }

    [Fact]
    public void WriteError_EscapesMarkupCharacters()
    {
        // Arrange
        var console = new TestConsole();
        var output = new ConsoleOutput(console);
        var messageWithMarkup = "Error: [invalid] tag";

        // Act
        output.WriteError(messageWithMarkup);

        // Assert
        console.Output.Should().Contain("[invalid]");
    }

    [Fact]
    public void WriteLine_WritesMessage()
    {
        // Arrange
        var console = new TestConsole();
        var output = new ConsoleOutput(console);

        // Act
        output.WriteLine("Plain message");

        // Assert
        console.Output.Should().Contain("Plain message");
    }

    [Fact]
    public void WriteLine_WithoutMessage_WritesEmptyLine()
    {
        // Arrange
        var console = new TestConsole();
        var output = new ConsoleOutput(console);

        // Act
        output.WriteLine();
        output.WriteLine("After empty line");

        // Assert
        // Check that there's content after the empty line
        console.Output.Should().Contain("After empty line");
        // The output should start with a newline character
        console.Output.Should().StartWith("\n");
    }

    [Fact]
    public void WriteTable_RendersTable()
    {
        // Arrange
        var console = new TestConsole();
        var output = new ConsoleOutput(console);
        var table = new Table();
        table.AddColumn("Name");
        table.AddColumn("Value");
        table.AddRow("Item1", "Value1");

        // Act
        output.WriteTable(table);

        // Assert
        console.Output.Should().Contain("Name");
        console.Output.Should().Contain("Value");
        console.Output.Should().Contain("Item1");
        console.Output.Should().Contain("Value1");
    }

    [Fact]
    public void WritePanel_RendersPanel()
    {
        // Arrange
        var console = new TestConsole();
        var output = new ConsoleOutput(console);
        var panel = new Panel("Panel content");
        panel.Header = new PanelHeader("Test Panel");

        // Act
        output.WritePanel(panel);

        // Assert
        console.Output.Should().Contain("Panel content");
        console.Output.Should().Contain("Test Panel");
    }

    [Fact]
    public void Write_RendersAnyRenderable()
    {
        // Arrange
        var console = new TestConsole();
        var output = new ConsoleOutput(console);
        var rule = new Rule("Section Title");

        // Act
        output.Write(rule);

        // Assert
        console.Output.Should().Contain("Section Title");
    }

    [Fact]
    public void ColorEnabled_ReturnsTrue_WhenConsoleSupportsColor()
    {
        // Arrange
        var console = new TestConsole();
        var output = new ConsoleOutput(console);

        // Act & Assert
        // TestConsole supports color by default
        output.ColorEnabled.Should().BeTrue();
    }

    [Fact]
    public void IsInteractive_ReflectsConsoleCapabilities()
    {
        // Arrange
        var console = new TestConsole();
        var output = new ConsoleOutput(console);

        // Act & Assert
        output.IsInteractive.Should().Be(console.Profile.Capabilities.Interactive);
    }

    [Fact]
    public void TerminalWidth_ReturnsConsoleWidth()
    {
        // Arrange
        var console = new TestConsole();
        console.Profile.Width = 120;
        var output = new ConsoleOutput(console);

        // Act & Assert
        output.TerminalWidth.Should().Be(120);
    }

    [Fact]
    public void Constructor_WithNullConsole_ThrowsArgumentNullException()
    {
        // Act
        var act = () => new ConsoleOutput(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("console");
    }

    [Fact]
    public void WriteSuccess_MultipleMessages_WritesAll()
    {
        // Arrange
        var console = new TestConsole();
        var output = new ConsoleOutput(console);

        // Act
        output.WriteSuccess("First success");
        output.WriteSuccess("Second success");
        output.WriteSuccess("Third success");

        // Assert
        console.Output.Should().Contain("First success");
        console.Output.Should().Contain("Second success");
        console.Output.Should().Contain("Third success");
    }

    [Fact]
    public void MixedOutput_WritesInCorrectOrder()
    {
        // Arrange
        var console = new TestConsole();
        var output = new ConsoleOutput(console);

        // Act
        output.WriteInfo("Starting process");
        output.WriteWarning("Something to note");
        output.WriteSuccess("Step completed");
        output.WriteError("Final error");

        // Assert
        var outputText = console.Output;
        var infoIndex = outputText.IndexOf("Starting process", StringComparison.Ordinal);
        var warningIndex = outputText.IndexOf("Something to note", StringComparison.Ordinal);
        var successIndex = outputText.IndexOf("Step completed", StringComparison.Ordinal);
        var errorIndex = outputText.IndexOf("Final error", StringComparison.Ordinal);

        infoIndex.Should().BeLessThan(warningIndex);
        warningIndex.Should().BeLessThan(successIndex);
        successIndex.Should().BeLessThan(errorIndex);
    }

    [Fact]
    public void WriteDebug_WhenVerbose_EscapesMarkupCharacters()
    {
        // Arrange
        var console = new TestConsole();
        var output = new ConsoleOutput(console, verbose: true);
        var messageWithMarkup = "Debug: [value] = 42";

        // Act
        output.WriteDebug(messageWithMarkup);

        // Assert
        console.Output.Should().Contain("[value]");
    }
}
