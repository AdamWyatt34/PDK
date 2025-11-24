namespace PDK.Tests.Unit.Runners;

using FluentAssertions;
using PDK.Runners;

/// <summary>
/// Unit tests for the ExecutionContext record.
/// </summary>
public class ExecutionContextTests : RunnerTestBase
{
    #region Constructor Tests

    [Fact]
    public void Constructor_ValidValues_CreatesInstance()
    {
        // Arrange & Act
        var context = CreateTestContext();

        // Assert
        context.Should().NotBeNull();
        context.ContainerId.Should().Be("test-container-123");
        context.WorkspacePath.Should().Be("/tmp/workspace");
        context.ContainerWorkspacePath.Should().Be("/workspace");
        context.WorkingDirectory.Should().Be(".");
    }

    [Fact]
    public void Constructor_WithEnvironment_SetsReadOnlyDictionary()
    {
        // Arrange & Act
        var context = CreateTestContext();

        // Assert
        context.Environment.Should().NotBeNull();
        context.Environment.Should().ContainKey("TEST_VAR");
        context.Environment["TEST_VAR"].Should().Be("test-value");
    }

    #endregion

    #region Property Tests

    [Fact]
    public void Environment_IsReadOnlyDictionary_CannotModifyDirectly()
    {
        // Arrange
        var context = CreateTestContext();

        // Act & Assert
        context.Environment.Should().BeAssignableTo<IReadOnlyDictionary<string, string>>();
    }

    [Fact]
    public void ContainerId_NotNullOrEmpty_HasValue()
    {
        // Arrange & Act
        var context = CreateTestContext();

        // Assert
        context.ContainerId.Should().NotBeNullOrEmpty();
        context.ContainerId.Should().Be("test-container-123");
    }

    [Fact]
    public void ContainerManager_NotNull_IsSet()
    {
        // Arrange & Act
        var context = CreateTestContext();

        // Assert
        context.ContainerManager.Should().NotBeNull();
        context.ContainerManager.Should().Be(MockContainerManager.Object);
    }

    #endregion

    #region Default Values Tests

    [Fact]
    public void ContainerWorkspacePath_DefaultValue_IsWorkspace()
    {
        // Arrange & Act
        var context = new ExecutionContext
        {
            ContainerId = "test",
            ContainerManager = MockContainerManager.Object,
            WorkspacePath = "/tmp/test",
            JobInfo = new JobMetadata
            {
                JobName = "Test",
                JobId = "123",
                Runner = "ubuntu"
            }
        };

        // Assert
        context.ContainerWorkspacePath.Should().Be("/workspace");
        context.WorkingDirectory.Should().Be(".");
        context.Environment.Should().NotBeNull();
        context.Environment.Should().BeEmpty();
    }

    #endregion
}
