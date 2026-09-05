namespace PDK.Tests.Unit.Runners.Validation;

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PDK.Core.Models;
using PDK.Runners.StepExecutors;
using PDK.Runners.Validation;

/// <summary>
/// Unit tests for <see cref="ExecutorValidator"/>: availability is derived from the executors actually
/// registered in the Docker and host factories.
/// </summary>
public class ExecutorValidatorTests
{
    private static IStepExecutor DockerExecutor(string stepType)
    {
        var mock = new Mock<IStepExecutor>();
        mock.Setup(e => e.StepType).Returns(stepType);
        return mock.Object;
    }

    private static IHostStepExecutor HostExecutor(string stepType)
    {
        var mock = new Mock<IHostStepExecutor>();
        mock.Setup(e => e.StepType).Returns(stepType);
        return mock.Object;
    }

    private static ExecutorValidator Create(string[] dockerTypes, string[] hostTypes)
    {
        var dockerFactory = new StepExecutorFactory(dockerTypes.Select(DockerExecutor).ToList());
        var hostFactory = new HostStepExecutorFactory(hostTypes.Select(HostExecutor).ToList());
        return new ExecutorValidator(dockerFactory, hostFactory);
    }

    [Fact]
    public void Constructor_NullDockerFactory_ThrowsArgumentNullException()
    {
        var act = () => new ExecutorValidator(null!, new HostStepExecutorFactory(Enumerable.Empty<IHostStepExecutor>()));

        act.Should().Throw<ArgumentNullException>().WithParameterName("dockerFactory");
    }

    [Fact]
    public void Constructor_NullHostFactory_ThrowsArgumentNullException()
    {
        var act = () => new ExecutorValidator(new StepExecutorFactory(Enumerable.Empty<IStepExecutor>()), null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("hostFactory");
    }

    [Theory]
    [InlineData("docker", true)]
    [InlineData("Docker", true)]
    [InlineData(" docker ", true)]
    [InlineData("host", false)]
    [InlineData("auto", true)]
    [InlineData("", true)]
    [InlineData("something-else", true)]
    public void HasExecutor_UsesRegistrationsOfTheSelectedRunner(string runnerType, bool expected)
    {
        var validator = Create(new[] { "script" }, Array.Empty<string>());

        validator.HasExecutor(StepType.Script, runnerType).Should().Be(expected);
    }

    [Fact]
    public void HasExecutor_HostRunner_UsesHostRegistrations()
    {
        var validator = Create(Array.Empty<string>(), new[] { "dotnet" });

        validator.HasExecutor(StepType.Dotnet, "host").Should().BeTrue();
        validator.HasExecutor(StepType.Dotnet, "docker").Should().BeFalse();
        validator.HasExecutor(StepType.Dotnet, "auto").Should().BeTrue();
    }

    [Theory]
    [InlineData(StepType.Unknown)]
    [InlineData(StepType.Setup)]
    public void HasExecutor_StepTypesWithoutExecutor_AlwaysFalse(StepType stepType)
    {
        var validator = Create(
            new[] { "checkout", "script", "pwsh", "dotnet", "npm", "docker", "uploadartifact", "downloadartifact" },
            new[] { "checkout", "script", "dotnet", "npm", "docker", "uploadartifact", "downloadartifact" });

        validator.HasExecutor(stepType, "docker").Should().BeFalse();
        validator.HasExecutor(stepType, "host").Should().BeFalse();
        validator.HasExecutor(stepType, "auto").Should().BeFalse();
    }

    [Fact]
    public void HasExecutor_PowerShell_DockerNeedsPwshExecutorAndHostUsesScriptExecutor()
    {
        var scriptOnly = Create(new[] { "script" }, new[] { "script" });
        scriptOnly.HasExecutor(StepType.PowerShell, "docker").Should().BeFalse();
        scriptOnly.HasExecutor(StepType.PowerShell, "host").Should().BeTrue();

        var withPwsh = Create(new[] { "pwsh" }, Array.Empty<string>());
        withPwsh.HasExecutor(StepType.PowerShell, "docker").Should().BeTrue();
    }

    [Fact]
    public void HasExecutor_BashStepsAreServedByTheScriptExecutor()
    {
        var validator = Create(new[] { "script" }, new[] { "script" });

        validator.HasExecutor(StepType.Bash, "docker").Should().BeTrue();
        validator.HasExecutor(StepType.Bash, "host").Should().BeTrue();
    }

    [Fact]
    public void GetExecutorName_ReturnsTheRegisteredExecutorTypeName()
    {
        var dockerFactory = new StepExecutorFactory(new IStepExecutor[] { new ScriptStepExecutor() });
        var hostFactory = new HostStepExecutorFactory(new IHostStepExecutor[]
        {
            new HostScriptExecutor(NullLogger<HostScriptExecutor>.Instance)
        });
        var validator = new ExecutorValidator(dockerFactory, hostFactory);

        validator.GetExecutorName(StepType.Script, "docker").Should().Be(nameof(ScriptStepExecutor));
        validator.GetExecutorName(StepType.Bash, "docker").Should().Be(nameof(ScriptStepExecutor));
        validator.GetExecutorName(StepType.PowerShell, "host").Should().Be(nameof(HostScriptExecutor));
        validator.GetExecutorName(StepType.PowerShell, "docker").Should().BeNull();
        validator.GetExecutorName(StepType.PowerShell, "auto").Should().Be(nameof(HostScriptExecutor));
        validator.GetExecutorName(StepType.Dotnet, "auto").Should().BeNull();
        validator.GetExecutorName(StepType.Unknown, "auto").Should().BeNull();
        validator.GetExecutorName(StepType.Setup, "host").Should().BeNull();
    }

    [Fact]
    public void GetAvailableStepTypes_ReturnsLowercaseSortedRegistrationsPerRunner()
    {
        var validator = Create(new[] { "Script", "checkout" }, new[] { "npm" });

        validator.GetAvailableStepTypes("docker").Should().Equal("checkout", "script");
        validator.GetAvailableStepTypes("host").Should().Equal("npm");
        validator.GetAvailableStepTypes("auto").Should().Equal("checkout", "npm", "script");
        validator.GetAvailableStepTypes("").Should().Equal("checkout", "npm", "script");
    }

    [Fact]
    public void GetAvailableStepTypes_DeduplicatesTypesRegisteredForBothRunners()
    {
        var validator = Create(new[] { "script", "dotnet" }, new[] { "SCRIPT" });

        validator.GetAvailableStepTypes("auto").Should().Equal("dotnet", "script");
    }

    [Fact]
    public void GetAvailableStepTypes_NoRegistrations_ReturnsEmpty()
    {
        var validator = Create(Array.Empty<string>(), Array.Empty<string>());

        validator.GetAvailableStepTypes("docker").Should().BeEmpty();
        validator.GetAvailableStepTypes("host").Should().BeEmpty();
    }
}
