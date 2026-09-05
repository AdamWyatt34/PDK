using FluentAssertions;
using PDK.Runners.Docker;

namespace PDK.Tests.Unit.Runners.Docker;

public class ContainerOwnershipTests
{
    private const string Host = "build-box";
    private static readonly DateTimeOffset Started = new(2026, 9, 5, 10, 0, 0, TimeSpan.Zero);

    private static Dictionary<string, string> Owned(int pid, DateTimeOffset? started = null, string host = Host)
    {
        var labels = new Dictionary<string, string>
        {
            ["pdk"] = "true",
            [ContainerOwnership.HostLabel] = host,
            [ContainerOwnership.ProcessLabel] = pid.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };

        if (started is { } value)
        {
            labels[ContainerOwnership.ProcessStartLabel] = value.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        }

        return labels;
    }

    [Fact]
    public void Stamp_RecordsThisProcess()
    {
        var labels = new Dictionary<string, string>();

        ContainerOwnership.Stamp(labels, keep: false);

        labels[ContainerOwnership.HostLabel].Should().Be(Environment.MachineName);
        labels[ContainerOwnership.ProcessLabel].Should().Be(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        labels.Should().NotContainKey(ContainerOwnership.KeepLabel);
        ContainerOwnership.IsOrphan(labels, "created", Environment.MachineName, ContainerOwnership.ProbeProcess).Should().BeFalse();
    }

    [Fact]
    public void IsOrphan_LiveOwner_IsNotAnOrphanInAnyState()
    {
        var labels = Owned(42, Started);
        ContainerOwnership.OwnerProcess Probe(int pid) => new(true, Started);

        ContainerOwnership.IsOrphan(labels, "created", Host, Probe).Should().BeFalse();
        ContainerOwnership.IsOrphan(labels, "running", Host, Probe).Should().BeFalse();
        ContainerOwnership.IsOrphan(labels, "exited", Host, Probe).Should().BeFalse();
    }

    [Fact]
    public void IsOrphan_DeadOwner_IsAnOrphanEvenWhenRunning()
    {
        var labels = Owned(42, Started);

        ContainerOwnership.IsOrphan(labels, "running", Host, _ => ContainerOwnership.OwnerProcess.Missing).Should().BeTrue();
    }

    [Fact]
    public void IsOrphan_ReusedProcessId_IsAnOrphan()
    {
        var labels = Owned(42, Started);

        ContainerOwnership.IsOrphan(labels, "running", Host, _ => new ContainerOwnership.OwnerProcess(true, Started.AddHours(3))).Should().BeTrue();
        ContainerOwnership.IsOrphan(labels, "running", Host, _ => new ContainerOwnership.OwnerProcess(true, Started.AddSeconds(2))).Should().BeFalse();
    }

    [Fact]
    public void IsOrphan_UnreadableStartTime_TrustsTheLiveProcess()
    {
        ContainerOwnership.IsOrphan(Owned(42, Started), "exited", Host, _ => new ContainerOwnership.OwnerProcess(true, null)).Should().BeFalse();
    }

    [Fact]
    public void IsOrphan_OtherHost_IsNeverTouched()
    {
        ContainerOwnership.IsOrphan(Owned(42, Started, host: "laptop"), "exited", Host, _ => ContainerOwnership.OwnerProcess.Missing).Should().BeFalse();
    }

    [Fact]
    public void IsOrphan_KeptContainer_IsNeverTouched()
    {
        var labels = Owned(42, Started);
        labels[ContainerOwnership.KeepLabel] = "true";

        ContainerOwnership.IsOrphan(labels, "exited", Host, _ => ContainerOwnership.OwnerProcess.Missing).Should().BeFalse();
    }

    [Theory]
    [InlineData("exited", true)]
    [InlineData("created", true)]
    [InlineData("dead", true)]
    [InlineData("running", false)]
    [InlineData("paused", false)]
    public void IsOrphan_WithoutOwnerLabels_DependsOnTheState(string state, bool expected)
    {
        var labels = new Dictionary<string, string> { ["pdk"] = "true" };

        ContainerOwnership.IsOrphan(labels, state, Host, _ => throw new InvalidOperationException("must not probe")).Should().Be(expected);
        ContainerOwnership.IsOrphan(null, state, Host, _ => throw new InvalidOperationException("must not probe")).Should().Be(expected);
    }

    [Fact]
    public void ProbeProcess_KnowsThisProcessAndNotAnImpossibleOne()
    {
        ContainerOwnership.ProbeProcess(Environment.ProcessId).Exists.Should().BeTrue();
        ContainerOwnership.ProbeProcess(2_000_000_000).Exists.Should().BeFalse();
    }
}
