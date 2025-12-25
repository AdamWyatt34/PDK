namespace PDK.Tests.Unit.UI;

using FluentAssertions;
using PDK.CLI.UI;
using Spectre.Console;
using Spectre.Console.Testing;
using Xunit;

/// <summary>
/// Unit tests for <see cref="VisualHierarchy"/>.
/// </summary>
public class VisualHierarchyTests
{
    [Fact]
    public void Constructor_WithNullConsole_ThrowsArgumentNullException()
    {
        // Act
        var act = () => new VisualHierarchy(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("console");
    }

    [Fact]
    public void Header_WritesFormattedHeader()
    {
        // Arrange
        var console = new TestConsole();
        var hierarchy = new VisualHierarchy(console);

        // Act
        hierarchy.Header("Test Header");

        // Assert
        console.Output.Should().Contain("Test Header");
        console.Output.Should().Contain("===");
    }

    [Fact]
    public void Subheader_WritesFormattedSubheader()
    {
        // Arrange
        var console = new TestConsole();
        var hierarchy = new VisualHierarchy(console);

        // Act
        hierarchy.Subheader("Test Subheader");

        // Assert
        console.Output.Should().Contain("Test Subheader");
    }

    [Fact]
    public void Subheader_WithCustomIndent_AppliesIndentation()
    {
        // Arrange
        var console = new TestConsole();
        var hierarchy = new VisualHierarchy(console);

        // Act
        hierarchy.Subheader("Indented", indent: 6);

        // Assert
        console.Output.Should().Contain("      Indented");
    }

    [Fact]
    public void Body_WritesBodyText()
    {
        // Arrange
        var console = new TestConsole();
        var hierarchy = new VisualHierarchy(console);

        // Act
        hierarchy.Body("Body text content");

        // Assert
        console.Output.Should().Contain("Body text content");
    }

    [Fact]
    public void Body_AppliesDefaultIndentation()
    {
        // Arrange
        var console = new TestConsole();
        var hierarchy = new VisualHierarchy(console);

        // Act
        hierarchy.Body("Content");

        // Assert - default indent is 4 spaces
        console.Output.Should().Contain("    Content");
    }

    [Fact]
    public void Footer_WritesFormattedFooter()
    {
        // Arrange
        var console = new TestConsole();
        var hierarchy = new VisualHierarchy(console);

        // Act
        hierarchy.Footer("Footer text");

        // Assert
        console.Output.Should().Contain("---");
        console.Output.Should().Contain("Footer text");
    }

    [Fact]
    public void Separator_WritesDashedLine()
    {
        // Arrange
        var console = new TestConsole();
        var hierarchy = new VisualHierarchy(console);

        // Act
        hierarchy.Separator();

        // Assert
        console.Output.Should().Contain("---");
    }

    [Fact]
    public void Separator_WithCustomWidth_WritesCorrectLength()
    {
        // Arrange
        var console = new TestConsole();
        var hierarchy = new VisualHierarchy(console);

        // Act
        hierarchy.Separator(width: 10);

        // Assert
        console.Output.Should().Contain("----------");
    }

    [Fact]
    public void EmptyLine_WritesBlankLine()
    {
        // Arrange
        var console = new TestConsole();
        var hierarchy = new VisualHierarchy(console);

        // Act
        hierarchy.EmptyLine();
        hierarchy.Body("After empty line");

        // Assert
        console.Output.Should().Contain("\n");
    }

    [Fact]
    public void TreeItem_WritesTreePrefix()
    {
        // Arrange
        var console = new TestConsole();
        var hierarchy = new VisualHierarchy(console);

        // Act
        hierarchy.TreeItem("Item 1");

        // Assert
        console.Output.Should().Contain("├─");
        console.Output.Should().Contain("Item 1");
    }

    [Fact]
    public void TreeItem_LastItem_WritesLastItemPrefix()
    {
        // Arrange
        var console = new TestConsole();
        var hierarchy = new VisualHierarchy(console);

        // Act
        hierarchy.TreeItem("Last Item", isLast: true);

        // Assert
        console.Output.Should().Contain("└─");
        console.Output.Should().Contain("Last Item");
    }

    [Fact]
    public void KeyValue_WritesKeyAndValue()
    {
        // Arrange
        var console = new TestConsole();
        var hierarchy = new VisualHierarchy(console);

        // Act
        hierarchy.KeyValue("Name", "John");

        // Assert
        console.Output.Should().Contain("Name");
        console.Output.Should().Contain("John");
    }

    [Fact]
    public void Section_WritesTitleAndContent()
    {
        // Arrange
        var console = new TestConsole();
        var hierarchy = new VisualHierarchy(console);

        // Act
        hierarchy.Section("Section Title", "Section content here");

        // Assert
        console.Output.Should().Contain("Section Title");
        console.Output.Should().Contain("Section content here");
    }

    [Fact]
    public void PipelineHeader_WritesPipelineAndRunner()
    {
        // Arrange
        var console = new TestConsole();
        var hierarchy = new VisualHierarchy(console);

        // Act
        hierarchy.PipelineHeader("build-pipeline", "docker");

        // Assert
        console.Output.Should().Contain("Pipeline");
        console.Output.Should().Contain("build-pipeline");
        console.Output.Should().Contain("Runner");
        console.Output.Should().Contain("docker");
    }

    [Fact]
    public void JobHeader_WritesJobInfo()
    {
        // Arrange
        var console = new TestConsole();
        var hierarchy = new VisualHierarchy(console);

        // Act
        hierarchy.JobHeader("test-job", jobNumber: 1, totalJobs: 3);

        // Assert
        console.Output.Should().Contain("Job 1/3");
        console.Output.Should().Contain("test-job");
    }

    [Fact]
    public void StepResult_Success_WritesSuccessSymbol()
    {
        // Arrange
        var console = new TestConsole();
        var hierarchy = new VisualHierarchy(console);

        // Act
        hierarchy.StepResult("build step", success: true, duration: TimeSpan.FromSeconds(5));

        // Assert
        console.Output.Should().Contain("+");
        console.Output.Should().Contain("build step");
        console.Output.Should().Contain("5.");
    }

    [Fact]
    public void StepResult_Failure_WritesFailureSymbol()
    {
        // Arrange
        var console = new TestConsole();
        var hierarchy = new VisualHierarchy(console);

        // Act
        hierarchy.StepResult("test step", success: false, duration: TimeSpan.FromSeconds(2));

        // Assert
        console.Output.Should().Contain("x");
        console.Output.Should().Contain("test step");
    }

    [Fact]
    public void StepResult_LastStep_WritesLastItemPrefix()
    {
        // Arrange
        var console = new TestConsole();
        var hierarchy = new VisualHierarchy(console);

        // Act
        hierarchy.StepResult("final step", success: true, duration: TimeSpan.FromMilliseconds(500), isLast: true);

        // Assert
        console.Output.Should().Contain("└─");
    }

    [Fact]
    public void StepResult_Duration_FormatsMinutes()
    {
        // Arrange
        var console = new TestConsole();
        var hierarchy = new VisualHierarchy(console);

        // Act
        hierarchy.StepResult("long step", success: true, duration: TimeSpan.FromMinutes(2.5));

        // Assert
        console.Output.Should().Contain("m");
    }

    [Fact]
    public void StepResult_Duration_FormatsSeconds()
    {
        // Arrange
        var console = new TestConsole();
        var hierarchy = new VisualHierarchy(console);

        // Act
        hierarchy.StepResult("medium step", success: true, duration: TimeSpan.FromSeconds(30));

        // Assert
        console.Output.Should().Contain("s");
    }

    [Fact]
    public void StepResult_Duration_FormatsMilliseconds()
    {
        // Arrange
        var console = new TestConsole();
        var hierarchy = new VisualHierarchy(console);

        // Act
        hierarchy.StepResult("fast step", success: true, duration: TimeSpan.FromMilliseconds(250));

        // Assert
        console.Output.Should().Contain("ms");
    }

    [Fact]
    public void PipelineSummary_Success_WritesSuccessMessage()
    {
        // Arrange
        var console = new TestConsole();
        var hierarchy = new VisualHierarchy(console);

        // Act
        hierarchy.PipelineSummary(success: true, duration: TimeSpan.FromMinutes(1));

        // Assert
        console.Output.Should().Contain("+");
        console.Output.Should().Contain("completed successfully");
    }

    [Fact]
    public void PipelineSummary_Failure_WritesFailureMessage()
    {
        // Arrange
        var console = new TestConsole();
        var hierarchy = new VisualHierarchy(console);

        // Act
        hierarchy.PipelineSummary(success: false, duration: TimeSpan.FromSeconds(45));

        // Assert
        console.Output.Should().Contain("x");
        console.Output.Should().Contain("failed");
    }

    [Fact]
    public void EscapesMarkupCharacters_InText()
    {
        // Arrange
        var console = new TestConsole();
        var hierarchy = new VisualHierarchy(console);

        // Act
        hierarchy.Header("Test [with] markup");

        // Assert - markup should be escaped, not interpreted
        console.Output.Should().Contain("[with]");
    }
}
