using FluentAssertions;
using PDK.Runners.Docker;

namespace PDK.Tests.Unit.Runners.Docker;

public class ResourceValidatorTests
{
    #region ValidateMemoryLimit Tests

    [Fact]
    public void ValidateMemoryLimit_Null_ReturnsValid()
    {
        // Arrange
        long? memoryLimit = null;

        // Act
        var (isValid, errorMessage) = ResourceValidator.ValidateMemoryLimit(memoryLimit);

        // Assert
        isValid.Should().BeTrue();
        errorMessage.Should().BeNull();
    }

    [Fact]
    public void ValidateMemoryLimit_ValidValue_ReturnsValid()
    {
        // Arrange
        long memoryLimit = 1_000_000_000; // 1GB

        // Act
        var (isValid, errorMessage) = ResourceValidator.ValidateMemoryLimit(memoryLimit);

        // Assert
        isValid.Should().BeTrue();
        errorMessage.Should().BeNull();
    }

    [Fact]
    public void ValidateMemoryLimit_MinimumValue_ReturnsValid()
    {
        // Arrange
        long memoryLimit = 6_291_456; // Exactly 6MB

        // Act
        var (isValid, errorMessage) = ResourceValidator.ValidateMemoryLimit(memoryLimit);

        // Assert
        isValid.Should().BeTrue();
        errorMessage.Should().BeNull();
    }

    [Fact]
    public void ValidateMemoryLimit_MaximumValue_ReturnsValid()
    {
        // Arrange
        long memoryLimit = 17_179_869_184; // Exactly 16GB

        // Act
        var (isValid, errorMessage) = ResourceValidator.ValidateMemoryLimit(memoryLimit);

        // Assert
        isValid.Should().BeTrue();
        errorMessage.Should().BeNull();
    }

    [Fact]
    public void ValidateMemoryLimit_BelowMinimum_ReturnsInvalidWithMessage()
    {
        // Arrange
        long memoryLimit = 1_000_000; // 1MB (too small)

        // Act
        var (isValid, errorMessage) = ResourceValidator.ValidateMemoryLimit(memoryLimit);

        // Assert
        isValid.Should().BeFalse();
        errorMessage.Should().NotBeNull();
        errorMessage.Should().Contain("6MB");
        errorMessage.Should().Contain("minimum");
    }

    [Fact]
    public void ValidateMemoryLimit_AboveMaximum_ReturnsInvalidWithMessage()
    {
        // Arrange
        long memoryLimit = 20_000_000_000; // 20GB (too large)

        // Act
        var (isValid, errorMessage) = ResourceValidator.ValidateMemoryLimit(memoryLimit);

        // Assert
        isValid.Should().BeFalse();
        errorMessage.Should().NotBeNull();
        errorMessage.Should().Contain("16GB");
        errorMessage.Should().Contain("exceed");
    }

    [Fact]
    public void ValidateMemoryLimit_Zero_ReturnsInvalid()
    {
        // Arrange
        long memoryLimit = 0;

        // Act
        var (isValid, errorMessage) = ResourceValidator.ValidateMemoryLimit(memoryLimit);

        // Assert
        isValid.Should().BeFalse();
        errorMessage.Should().NotBeNull();
    }

    [Fact]
    public void ValidateMemoryLimit_Negative_ReturnsInvalid()
    {
        // Arrange
        long memoryLimit = -1000;

        // Act
        var (isValid, errorMessage) = ResourceValidator.ValidateMemoryLimit(memoryLimit);

        // Assert
        isValid.Should().BeFalse();
        errorMessage.Should().NotBeNull();
    }

    #endregion

    #region ValidateCpuLimit Tests

    [Fact]
    public void ValidateCpuLimit_Null_ReturnsValid()
    {
        // Arrange
        double? cpuLimit = null;

        // Act
        var (isValid, errorMessage) = ResourceValidator.ValidateCpuLimit(cpuLimit);

        // Assert
        isValid.Should().BeTrue();
        errorMessage.Should().BeNull();
    }

    [Fact]
    public void ValidateCpuLimit_ValidValue_ReturnsValid()
    {
        // Arrange
        double cpuLimit = 2.0;

        // Act
        var (isValid, errorMessage) = ResourceValidator.ValidateCpuLimit(cpuLimit);

        // Assert
        isValid.Should().BeTrue();
        errorMessage.Should().BeNull();
    }

    [Fact]
    public void ValidateCpuLimit_FractionalValue_ReturnsValid()
    {
        // Arrange
        double cpuLimit = 0.5;

        // Act
        var (isValid, errorMessage) = ResourceValidator.ValidateCpuLimit(cpuLimit);

        // Assert
        isValid.Should().BeTrue();
        errorMessage.Should().BeNull();
    }

    [Fact]
    public void ValidateCpuLimit_ProcessorCount_ReturnsValid()
    {
        // Arrange
        double cpuLimit = Environment.ProcessorCount;

        // Act
        var (isValid, errorMessage) = ResourceValidator.ValidateCpuLimit(cpuLimit);

        // Assert
        isValid.Should().BeTrue();
        errorMessage.Should().BeNull();
    }

    [Fact]
    public void ValidateCpuLimit_Zero_ReturnsInvalid()
    {
        // Arrange
        double cpuLimit = 0;

        // Act
        var (isValid, errorMessage) = ResourceValidator.ValidateCpuLimit(cpuLimit);

        // Assert
        isValid.Should().BeFalse();
        errorMessage.Should().NotBeNull();
        errorMessage.Should().Contain("greater than 0");
    }

    [Fact]
    public void ValidateCpuLimit_Negative_ReturnsInvalid()
    {
        // Arrange
        double cpuLimit = -1.0;

        // Act
        var (isValid, errorMessage) = ResourceValidator.ValidateCpuLimit(cpuLimit);

        // Assert
        isValid.Should().BeFalse();
        errorMessage.Should().NotBeNull();
        errorMessage.Should().Contain("greater than 0");
    }

    [Fact]
    public void ValidateCpuLimit_ExceedsProcessorCount_ReturnsInvalid()
    {
        // Arrange
        double cpuLimit = Environment.ProcessorCount + 1.0;

        // Act
        var (isValid, errorMessage) = ResourceValidator.ValidateCpuLimit(cpuLimit);

        // Assert
        isValid.Should().BeFalse();
        errorMessage.Should().NotBeNull();
        errorMessage.Should().Contain($"{Environment.ProcessorCount}");
        errorMessage.Should().Contain("available processors");
    }

    #endregion

    #region ValidateTimeout Tests

    [Fact]
    public void ValidateTimeout_Null_ReturnsValid()
    {
        // Arrange
        int? timeout = null;

        // Act
        var (isValid, errorMessage) = ResourceValidator.ValidateTimeout(timeout);

        // Assert
        isValid.Should().BeTrue();
        errorMessage.Should().BeNull();
    }

    [Fact]
    public void ValidateTimeout_ValidValue_ReturnsValid()
    {
        // Arrange
        int timeout = 60; // 1 hour

        // Act
        var (isValid, errorMessage) = ResourceValidator.ValidateTimeout(timeout);

        // Assert
        isValid.Should().BeTrue();
        errorMessage.Should().BeNull();
    }

    [Fact]
    public void ValidateTimeout_OneMinute_ReturnsValid()
    {
        // Arrange
        int timeout = 1;

        // Act
        var (isValid, errorMessage) = ResourceValidator.ValidateTimeout(timeout);

        // Assert
        isValid.Should().BeTrue();
        errorMessage.Should().BeNull();
    }

    [Fact]
    public void ValidateTimeout_MaximumValue_ReturnsValid()
    {
        // Arrange
        int timeout = 1440; // 24 hours

        // Act
        var (isValid, errorMessage) = ResourceValidator.ValidateTimeout(timeout);

        // Assert
        isValid.Should().BeTrue();
        errorMessage.Should().BeNull();
    }

    [Fact]
    public void ValidateTimeout_Zero_ReturnsInvalid()
    {
        // Arrange
        int timeout = 0;

        // Act
        var (isValid, errorMessage) = ResourceValidator.ValidateTimeout(timeout);

        // Assert
        isValid.Should().BeFalse();
        errorMessage.Should().NotBeNull();
        errorMessage.Should().Contain("greater than 0");
    }

    [Fact]
    public void ValidateTimeout_Negative_ReturnsInvalid()
    {
        // Arrange
        int timeout = -10;

        // Act
        var (isValid, errorMessage) = ResourceValidator.ValidateTimeout(timeout);

        // Assert
        isValid.Should().BeFalse();
        errorMessage.Should().NotBeNull();
        errorMessage.Should().Contain("greater than 0");
    }

    [Fact]
    public void ValidateTimeout_ExceedsMaximum_ReturnsInvalid()
    {
        // Arrange
        int timeout = 2000; // More than 24 hours

        // Act
        var (isValid, errorMessage) = ResourceValidator.ValidateTimeout(timeout);

        // Assert
        isValid.Should().BeFalse();
        errorMessage.Should().NotBeNull();
        errorMessage.Should().Contain("1440");
        errorMessage.Should().Contain("24 hours");
    }

    #endregion

    #region ValidateAll Tests

    [Fact]
    public void ValidateAll_AllValid_ReturnsEmptyList()
    {
        // Arrange
        long memoryLimit = 8_000_000_000;
        double cpuLimit = 2.0;
        int timeout = 60;

        // Act
        var errors = ResourceValidator.ValidateAll(memoryLimit, cpuLimit, timeout);

        // Assert
        errors.Should().BeEmpty();
    }

    [Fact]
    public void ValidateAll_AllNull_ReturnsEmptyList()
    {
        // Arrange, Act
        var errors = ResourceValidator.ValidateAll(null, null, null);

        // Assert
        errors.Should().BeEmpty();
    }

    [Fact]
    public void ValidateAll_OneInvalid_ReturnsOneError()
    {
        // Arrange
        long memoryLimit = 1000; // Too small
        double cpuLimit = 2.0;
        int timeout = 60;

        // Act
        var errors = ResourceValidator.ValidateAll(memoryLimit, cpuLimit, timeout);

        // Assert
        errors.Should().HaveCount(1);
        errors[0].Should().Contain("Memory");
    }

    [Fact]
    public void ValidateAll_MultipleInvalid_ReturnsMultipleErrors()
    {
        // Arrange
        long memoryLimit = 1000; // Too small
        double cpuLimit = -1; // Negative
        int timeout = 0; // Zero

        // Act
        var errors = ResourceValidator.ValidateAll(memoryLimit, cpuLimit, timeout);

        // Assert
        errors.Should().HaveCount(3);
        errors.Should().Contain(e => e.Contains("Memory"));
        errors.Should().Contain(e => e.Contains("CPU"));
        errors.Should().Contain(e => e.Contains("Timeout"));
    }

    #endregion

    #region Unit Conversion Tests

    [Fact]
    public void MegabytesToBytes_ValidValue_ConvertsCorrectly()
    {
        // Arrange
        long megabytes = 100;

        // Act
        var bytes = ResourceValidator.MegabytesToBytes(megabytes);

        // Assert
        bytes.Should().Be(104_857_600); // 100 * 1024 * 1024
    }

    [Fact]
    public void GigabytesToBytes_ValidValue_ConvertsCorrectly()
    {
        // Arrange
        long gigabytes = 4;

        // Act
        var bytes = ResourceValidator.GigabytesToBytes(gigabytes);

        // Assert
        bytes.Should().Be(4_294_967_296); // 4 * 1024 * 1024 * 1024
    }

    [Fact]
    public void BytesToMegabytes_ValidValue_ConvertsCorrectly()
    {
        // Arrange
        long bytes = 104_857_600; // 100MB in bytes

        // Act
        var megabytes = ResourceValidator.BytesToMegabytes(bytes);

        // Assert
        megabytes.Should().Be(100);
    }

    [Fact]
    public void BytesToGigabytes_ValidValue_ConvertsCorrectly()
    {
        // Arrange
        long bytes = 4_294_967_296; // 4GB in bytes

        // Act
        var gigabytes = ResourceValidator.BytesToGigabytes(bytes);

        // Assert
        gigabytes.Should().BeApproximately(4.0, 0.01);
    }

    [Fact]
    public void GigabytesToBytes_4GB_ReturnsCorrectValue()
    {
        // Arrange
        long gigabytes = 4;

        // Act
        var bytes = ResourceValidator.GigabytesToBytes(gigabytes);

        // Assert
        bytes.Should().Be(4_294_967_296);
    }

    [Fact]
    public void BytesToGigabytes_4GB_Returns4()
    {
        // Arrange
        long bytes = 4_294_967_296;

        // Act
        var gigabytes = ResourceValidator.BytesToGigabytes(bytes);

        // Assert
        gigabytes.Should().BeApproximately(4.0, 0.01);
    }

    [Fact]
    public void RoundTripConversion_MegabytesToBytesAndBack_PreservesValue()
    {
        // Arrange
        long originalMegabytes = 256;

        // Act
        var bytes = ResourceValidator.MegabytesToBytes(originalMegabytes);
        var backToMegabytes = ResourceValidator.BytesToMegabytes(bytes);

        // Assert
        backToMegabytes.Should().Be(originalMegabytes);
    }

    [Fact]
    public void RoundTripConversion_GigabytesToBytesAndBack_PreservesValue()
    {
        // Arrange
        long originalGigabytes = 8;

        // Act
        var bytes = ResourceValidator.GigabytesToBytes(originalGigabytes);
        var backToGigabytes = ResourceValidator.BytesToGigabytes(bytes);

        // Assert
        backToGigabytes.Should().BeApproximately(originalGigabytes, 0.01);
    }

    #endregion
}
