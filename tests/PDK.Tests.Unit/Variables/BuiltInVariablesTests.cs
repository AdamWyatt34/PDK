namespace PDK.Tests.Unit.Variables;

using FluentAssertions;
using PDK.Core.Variables;
using Xunit;

/// <summary>
/// Unit tests for BuiltInVariables.
/// </summary>
public class BuiltInVariablesTests
{
    #region GetValue Tests

    [Fact]
    public void GetValue_ReturnsPdkVersion()
    {
        // Arrange
        var builtIn = new BuiltInVariables();

        // Act
        var result = builtIn.GetValue("PDK_VERSION");

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().MatchRegex(@"^\d+\.\d+\.\d+$");
    }

    [Fact]
    public void GetValue_ReturnsPdkWorkspace_FromContext()
    {
        // Arrange
        var context = new VariableContext { Workspace = "/test/workspace" };
        var builtIn = new BuiltInVariables(context);

        // Act
        var result = builtIn.GetValue("PDK_WORKSPACE");

        // Assert
        result.Should().Be("/test/workspace");
    }

    [Fact]
    public void GetValue_ReturnsPdkWorkspace_DefaultsToCurrentDirectory()
    {
        // Arrange
        var builtIn = new BuiltInVariables();

        // Act
        var result = builtIn.GetValue("PDK_WORKSPACE");

        // Assert
        result.Should().Be(Environment.CurrentDirectory);
    }

    [Fact]
    public void GetValue_ReturnsPdkRunner_FromContext()
    {
        // Arrange
        var context = new VariableContext { Runner = "docker" };
        var builtIn = new BuiltInVariables(context);

        // Act
        var result = builtIn.GetValue("PDK_RUNNER");

        // Assert
        result.Should().Be("docker");
    }

    [Fact]
    public void GetValue_ReturnsPdkRunner_DefaultsToLocal()
    {
        // Arrange
        var builtIn = new BuiltInVariables();

        // Act
        var result = builtIn.GetValue("PDK_RUNNER");

        // Assert
        result.Should().Be("local");
    }

    [Fact]
    public void GetValue_ReturnsPdkJob_FromContext()
    {
        // Arrange
        var context = new VariableContext { JobName = "build" };
        var builtIn = new BuiltInVariables(context);

        // Act
        var result = builtIn.GetValue("PDK_JOB");

        // Assert
        result.Should().Be("build");
    }

    [Fact]
    public void GetValue_ReturnsPdkJob_NullWhenNotSet()
    {
        // Arrange
        var builtIn = new BuiltInVariables();

        // Act
        var result = builtIn.GetValue("PDK_JOB");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GetValue_ReturnsPdkStep_FromContext()
    {
        // Arrange
        var context = new VariableContext { StepName = "compile" };
        var builtIn = new BuiltInVariables(context);

        // Act
        var result = builtIn.GetValue("PDK_STEP");

        // Assert
        result.Should().Be("compile");
    }

    [Fact]
    public void GetValue_ReturnsHome()
    {
        // Arrange
        var builtIn = new BuiltInVariables();

        // Act
        var result = builtIn.GetValue("HOME");

        // Assert
        result.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GetValue_ReturnsUser()
    {
        // Arrange
        var builtIn = new BuiltInVariables();

        // Act
        var result = builtIn.GetValue("USER");

        // Assert
        result.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GetValue_ReturnsPwd()
    {
        // Arrange
        var builtIn = new BuiltInVariables();

        // Act
        var result = builtIn.GetValue("PWD");

        // Assert
        result.Should().Be(Environment.CurrentDirectory);
    }

    [Fact]
    public void GetValue_ReturnsTimestamp_InIsoFormat()
    {
        // Arrange
        var builtIn = new BuiltInVariables();

        // Act
        var result = builtIn.GetValue("TIMESTAMP");

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().MatchRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$");
    }

    [Fact]
    public void GetValue_ReturnsTimestampUnix()
    {
        // Arrange
        var builtIn = new BuiltInVariables();
        var expectedApprox = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // Act
        var result = builtIn.GetValue("TIMESTAMP_UNIX");

        // Assert
        result.Should().NotBeNullOrEmpty();
        long.Parse(result!).Should().BeCloseTo(expectedApprox, 2);
    }

    [Fact]
    public void GetValue_ReturnsNull_ForUnknownVariable()
    {
        // Arrange
        var builtIn = new BuiltInVariables();

        // Act
        var result = builtIn.GetValue("UNKNOWN_VAR");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GetValue_ReturnsNull_ForEmptyName()
    {
        // Arrange
        var builtIn = new BuiltInVariables();

        // Act
        var result = builtIn.GetValue("");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GetValue_ReturnsNull_ForNullName()
    {
        // Arrange
        var builtIn = new BuiltInVariables();

        // Act
        var result = builtIn.GetValue(null!);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region IsBuiltIn Tests

    [Theory]
    [InlineData("PDK_VERSION")]
    [InlineData("PDK_WORKSPACE")]
    [InlineData("PDK_RUNNER")]
    [InlineData("PDK_JOB")]
    [InlineData("PDK_STEP")]
    [InlineData("HOME")]
    [InlineData("USER")]
    [InlineData("PWD")]
    [InlineData("TIMESTAMP")]
    [InlineData("TIMESTAMP_UNIX")]
    public void IsBuiltIn_ReturnsTrue_ForBuiltInVariables(string name)
    {
        // Arrange
        var builtIn = new BuiltInVariables();

        // Act
        var result = builtIn.IsBuiltIn(name);

        // Assert
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData("CUSTOM_VAR")]
    [InlineData("PATH")]
    [InlineData("pdk_version")] // Case sensitive
    [InlineData("")]
    public void IsBuiltIn_ReturnsFalse_ForNonBuiltInVariables(string name)
    {
        // Arrange
        var builtIn = new BuiltInVariables();

        // Act
        var result = builtIn.IsBuiltIn(name);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsBuiltIn_ReturnsFalse_ForNullName()
    {
        // Arrange
        var builtIn = new BuiltInVariables();

        // Act
        var result = builtIn.IsBuiltIn(null!);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region GetAllNames Tests

    [Fact]
    public void GetAllNames_ReturnsAllBuiltInNames()
    {
        // Arrange
        var builtIn = new BuiltInVariables();

        // Act
        var names = builtIn.GetAllNames().ToList();

        // Assert
        names.Should().Contain("PDK_VERSION");
        names.Should().Contain("PDK_WORKSPACE");
        names.Should().Contain("PDK_RUNNER");
        names.Should().Contain("PDK_JOB");
        names.Should().Contain("PDK_STEP");
        names.Should().Contain("HOME");
        names.Should().Contain("USER");
        names.Should().Contain("PWD");
        names.Should().Contain("TIMESTAMP");
        names.Should().Contain("TIMESTAMP_UNIX");
        names.Should().HaveCount(10);
    }

    #endregion

    #region GetAll Tests

    [Fact]
    public void GetAll_ReturnsAllBuiltInVariables()
    {
        // Arrange
        var context = new VariableContext
        {
            Workspace = "/test",
            Runner = "docker",
            JobName = "build",
            StepName = "compile"
        };
        var builtIn = new BuiltInVariables(context);

        // Act
        var all = builtIn.GetAll();

        // Assert
        all.Should().ContainKey("PDK_VERSION");
        all.Should().ContainKey("PDK_WORKSPACE");
        all["PDK_WORKSPACE"].Should().Be("/test");
        all.Should().ContainKey("PDK_RUNNER");
        all["PDK_RUNNER"].Should().Be("docker");
        all.Should().ContainKey("PDK_JOB");
        all["PDK_JOB"].Should().Be("build");
        all.Should().ContainKey("PDK_STEP");
        all["PDK_STEP"].Should().Be("compile");
    }

    [Fact]
    public void GetAll_ExcludesNullValues()
    {
        // Arrange
        var builtIn = new BuiltInVariables(); // No job/step set

        // Act
        var all = builtIn.GetAll();

        // Assert
        all.Should().NotContainKey("PDK_JOB");
        all.Should().NotContainKey("PDK_STEP");
    }

    #endregion

    #region UpdateContext Tests

    [Fact]
    public void UpdateContext_UpdatesContextDependentVariables()
    {
        // Arrange
        var builtIn = new BuiltInVariables();
        var newContext = new VariableContext
        {
            Workspace = "/new/workspace",
            Runner = "kubernetes",
            JobName = "deploy"
        };

        // Act
        builtIn.UpdateContext(newContext);

        // Assert
        builtIn.GetValue("PDK_WORKSPACE").Should().Be("/new/workspace");
        builtIn.GetValue("PDK_RUNNER").Should().Be("kubernetes");
        builtIn.GetValue("PDK_JOB").Should().Be("deploy");
    }

    [Fact]
    public void UpdateContext_ThrowsOnNull()
    {
        // Arrange
        var builtIn = new BuiltInVariables();

        // Act
        var act = () => builtIn.UpdateContext(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region Constructor Tests

    [Fact]
    public void Constructor_ThrowsOnNullContext()
    {
        // Act
        var act = () => new BuiltInVariables(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_AcceptsValidContext()
    {
        // Arrange
        var context = new VariableContext { Workspace = "/test" };

        // Act
        var builtIn = new BuiltInVariables(context);

        // Assert
        builtIn.GetValue("PDK_WORKSPACE").Should().Be("/test");
    }

    #endregion

    #region VariableContext Tests

    [Fact]
    public void VariableContext_CreateDefault_SetsCurrentDirectory()
    {
        // Act
        var context = VariableContext.CreateDefault();

        // Assert
        context.Workspace.Should().Be(Environment.CurrentDirectory);
        context.Runner.Should().Be("local");
    }

    [Fact]
    public void VariableContext_WithJob_CreatesNewContextWithJob()
    {
        // Arrange
        var context = new VariableContext { Workspace = "/test" };

        // Act
        var newContext = context.WithJob("build");

        // Assert
        newContext.JobName.Should().Be("build");
        newContext.Workspace.Should().Be("/test");
        context.JobName.Should().BeNull(); // Original unchanged
    }

    [Fact]
    public void VariableContext_WithStep_CreatesNewContextWithStep()
    {
        // Arrange
        var context = new VariableContext { JobName = "build" };

        // Act
        var newContext = context.WithStep("compile");

        // Assert
        newContext.StepName.Should().Be("compile");
        newContext.JobName.Should().Be("build");
        context.StepName.Should().BeNull(); // Original unchanged
    }

    #endregion
}
