using System.Text;
using FluentAssertions;
using PDK.Runners.Docker;

namespace PDK.Tests.Unit.Runners.Docker;

public class MultiplexedOutputReaderTests
{
    [Fact]
    public async Task ReadToEndAsync_SeparatesStdoutAndStderr()
    {
        var stream = MultiplexedFrames.Build(
            MultiplexedFrames.Frame(MultiplexedFrames.Stdout, "out1\n"),
            MultiplexedFrames.Frame(MultiplexedFrames.Stderr, "err1\n"),
            MultiplexedFrames.Frame(MultiplexedFrames.Stdout, "out2\n"));
        var reader = new MultiplexedOutputReader(null, null);

        await reader.ReadToEndAsync(stream, CancellationToken.None);

        reader.StandardOutput.Should().Be("out1\nout2\n");
        reader.StandardError.Should().Be("err1\n");
    }

    [Fact]
    public async Task ReadToEndAsync_UsesOneDecoderPerStream()
    {
        var snowman = Encoding.UTF8.GetBytes("☃"); // E2 98 83
        var stream = MultiplexedFrames.Build(
            MultiplexedFrames.Frame(MultiplexedFrames.Stdout, new[] { snowman[0] }),
            MultiplexedFrames.Frame(MultiplexedFrames.Stderr, new[] { snowman[0], snowman[1] }),
            MultiplexedFrames.Frame(MultiplexedFrames.Stdout, new[] { snowman[1], snowman[2] }),
            MultiplexedFrames.Frame(MultiplexedFrames.Stderr, new[] { snowman[2] }));
        var reader = new MultiplexedOutputReader(null, null);

        await reader.ReadToEndAsync(stream, CancellationToken.None);

        reader.StandardOutput.Should().Be("☃");
        reader.StandardError.Should().Be("☃");
    }

    [Fact]
    public async Task ReadToEndAsync_EmitsLinesAcrossFramesAndTrailingPartialLine()
    {
        var stream = MultiplexedFrames.Build(
            MultiplexedFrames.Frame(MultiplexedFrames.Stdout, "first\r\nsec"),
            MultiplexedFrames.Frame(MultiplexedFrames.Stdout, "ond\nlast"));
        var lines = new List<string>();
        var reader = new MultiplexedOutputReader(lines.Add, null);

        await reader.ReadToEndAsync(stream, CancellationToken.None);

        lines.Should().Equal("first", "second", "last");
    }

    [Fact]
    public async Task ReadToEndAsync_EmptyStream_ProducesNothing()
    {
        var stream = MultiplexedFrames.Build();
        var lines = new List<string>();
        var reader = new MultiplexedOutputReader(lines.Add, lines.Add);

        await reader.ReadToEndAsync(stream, CancellationToken.None);

        lines.Should().BeEmpty();
        reader.StandardOutput.Should().BeEmpty();
        reader.StandardError.Should().BeEmpty();
    }

    [Fact]
    public async Task ReadToEndAsync_LargePayload_IsReadCompletely()
    {
        var text = new string('x', 50_000) + "\n";
        var stream = MultiplexedFrames.Build(MultiplexedFrames.Frame(MultiplexedFrames.Stdout, text));
        var lines = new List<string>();
        var reader = new MultiplexedOutputReader(lines.Add, null);

        await reader.ReadToEndAsync(stream, CancellationToken.None);

        reader.StandardOutput.Should().HaveLength(text.Length);
        lines.Should().ContainSingle().Which.Should().HaveLength(50_000);
    }
}
