namespace PDK.Tests.Unit.Logging;

using PDK.Core.Logging;
using Xunit;

/// <summary>
/// Unit tests for <see cref="CorrelationContext"/>.
/// </summary>
public class CorrelationContextTests
{
    [Fact]
    public void CurrentId_WhenNoScopeSet_CreatesNewId()
    {
        // Arrange
        CorrelationContext.Clear();

        // Act
        var id = CorrelationContext.CurrentId;

        // Assert
        Assert.NotNull(id);
        Assert.StartsWith("pdk-", id);
        Assert.Matches(@"pdk-\d{8}-[a-f0-9]{16}", id);
    }

    [Fact]
    public void CurrentIdOrNull_WhenNoScopeSet_ReturnsNull()
    {
        // Arrange
        CorrelationContext.Clear();

        // Act
        var id = CorrelationContext.CurrentIdOrNull;

        // Assert
        Assert.Null(id);
    }

    [Fact]
    public void CreateScope_WithNoId_GeneratesNewId()
    {
        // Arrange
        CorrelationContext.Clear();

        // Act
        using var scope = CorrelationContext.CreateScope();
        var id = CorrelationContext.CurrentId;

        // Assert
        Assert.NotNull(id);
        Assert.StartsWith("pdk-", id);
    }

    [Fact]
    public void CreateScope_WithCustomId_UsesProvidedId()
    {
        // Arrange
        CorrelationContext.Clear();
        const string customId = "test-correlation-id-123";

        // Act
        using var scope = CorrelationContext.CreateScope(customId);
        var id = CorrelationContext.CurrentId;

        // Assert
        Assert.Equal(customId, id);
    }

    [Fact]
    public void CreateScope_WhenDisposed_RestoresPreviousId()
    {
        // Arrange
        CorrelationContext.Clear();
        const string originalId = "original-id";
        const string nestedId = "nested-id";

        // Act & Assert
        using (var outerScope = CorrelationContext.CreateScope(originalId))
        {
            Assert.Equal(originalId, CorrelationContext.CurrentId);

            using (var innerScope = CorrelationContext.CreateScope(nestedId))
            {
                Assert.Equal(nestedId, CorrelationContext.CurrentId);
            }

            // After inner scope disposal, should be back to original
            Assert.Equal(originalId, CorrelationContext.CurrentId);
        }

        // After outer scope disposal, should be null
        Assert.Null(CorrelationContext.CurrentIdOrNull);
    }

    [Fact]
    public void SetCurrentId_SetsIdDirectly()
    {
        // Arrange
        CorrelationContext.Clear();
        const string testId = "direct-set-id";

        // Act
        CorrelationContext.SetCurrentId(testId);
        var id = CorrelationContext.CurrentId;

        // Assert
        Assert.Equal(testId, id);

        // Cleanup
        CorrelationContext.Clear();
    }

    [Fact]
    public void Clear_RemovesCurrentId()
    {
        // Arrange
        using var scope = CorrelationContext.CreateScope("test-id");

        // Act
        CorrelationContext.Clear();

        // Assert
        Assert.Null(CorrelationContext.CurrentIdOrNull);
    }

    [Fact]
    public async Task CreateScope_PreservesIdAcrossAsyncOperations()
    {
        // Arrange
        CorrelationContext.Clear();
        const string testId = "async-test-id";
        string? capturedId = null;

        // Act
        using (var scope = CorrelationContext.CreateScope(testId))
        {
            await Task.Delay(10);
            capturedId = CorrelationContext.CurrentId;
        }

        // Assert
        Assert.Equal(testId, capturedId);
    }

    [Fact]
    public void CreateScope_GeneratesUniqueIdsForEachScope()
    {
        // Arrange
        CorrelationContext.Clear();
        var ids = new HashSet<string>();

        // Act
        for (int i = 0; i < 100; i++)
        {
            using var scope = CorrelationContext.CreateScope();
            ids.Add(CorrelationContext.CurrentId);
        }

        // Assert - all IDs should be unique
        Assert.Equal(100, ids.Count);
    }

    [Fact]
    public void CorrelationId_Format_IsCorrect()
    {
        // Arrange
        CorrelationContext.Clear();

        // Act
        using var scope = CorrelationContext.CreateScope();
        var id = CorrelationContext.CurrentId;

        // Assert - format: pdk-YYYYMMDD-16hexchars
        Assert.Matches(@"^pdk-\d{8}-[a-f0-9]{16}$", id);
    }
}
