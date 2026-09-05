using System.Text;
using Docker.DotNet;

namespace PDK.Tests.Unit.Runners.Docker;

/// <summary>
/// Builds Docker multiplexed stream frames (8-byte header: stream type, 3 zero bytes, big-endian length).
/// </summary>
internal static class MultiplexedFrames
{
    public const byte Stdout = 1;
    public const byte Stderr = 2;

    public static byte[] Frame(byte stream, byte[] payload)
    {
        var frame = new byte[8 + payload.Length];
        frame[0] = stream;
        frame[4] = (byte)(payload.Length >> 24);
        frame[5] = (byte)(payload.Length >> 16);
        frame[6] = (byte)(payload.Length >> 8);
        frame[7] = (byte)payload.Length;
        Buffer.BlockCopy(payload, 0, frame, 8, payload.Length);
        return frame;
    }

    public static byte[] Frame(byte stream, string text) => Frame(stream, Encoding.UTF8.GetBytes(text));

    public static MultiplexedStream Build(params byte[][] frames)
    {
        var memory = new MemoryStream();
        foreach (var frame in frames)
        {
            memory.Write(frame, 0, frame.Length);
        }

        memory.Position = 0;
        return new MultiplexedStream(memory, true);
    }
}

/// <summary>
/// A stream whose reads block until the cancellation token fires (simulates a command that never ends).
/// </summary>
internal sealed class BlockingStream : Stream
{
    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken);
        return 0;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken);
        return 0;
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
