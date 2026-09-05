using System.Text;
using Docker.DotNet;

namespace PDK.Runners.Docker;

/// <summary>
/// Reads a Docker multiplexed exec stream, decoding stdout and stderr with one UTF-8 decoder each
/// (so multi-byte characters split across read chunks are not corrupted) and emitting complete
/// lines to optional callbacks as they arrive.
/// </summary>
internal sealed class MultiplexedOutputReader
{
    private const int BufferSize = 8192;

    private readonly StreamState _stdout;
    private readonly StreamState _stderr;

    public MultiplexedOutputReader(Action<string>? onOutputLine, Action<string>? onErrorLine)
    {
        _stdout = new StreamState(onOutputLine);
        _stderr = new StreamState(onErrorLine);
    }

    /// <summary>Gets the standard output collected so far.</summary>
    public string StandardOutput => _stdout.Text.ToString();

    /// <summary>Gets the standard error collected so far.</summary>
    public string StandardError => _stderr.Text.ToString();

    /// <summary>
    /// Reads the stream until EOF. Output collected before a cancellation remains available through
    /// <see cref="StandardOutput"/> and <see cref="StandardError"/>.
    /// </summary>
    public async Task ReadToEndAsync(MultiplexedStream stream, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var buffer = new byte[BufferSize];
        var chars = new char[Encoding.UTF8.GetMaxCharCount(BufferSize)];

        while (true)
        {
            var result = await stream.ReadOutputAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
            if (result.EOF)
            {
                break;
            }

            if (result.Count <= 0)
            {
                continue;
            }

            var target = result.Target == MultiplexedStream.TargetStream.StandardError ? _stderr : _stdout;
            target.Append(buffer, result.Count, chars, flush: false);
        }

        _stdout.Append(buffer, 0, chars, flush: true);
        _stderr.Append(buffer, 0, chars, flush: true);
        _stdout.Complete();
        _stderr.Complete();
    }

    private sealed class StreamState
    {
        private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();
        private readonly StringBuilder _pendingLine = new();
        private readonly Action<string>? _onLine;

        public StreamState(Action<string>? onLine)
        {
            _onLine = onLine;
        }

        public StringBuilder Text { get; } = new();

        public void Append(byte[] bytes, int count, char[] chars, bool flush)
        {
            var charCount = _decoder.GetChars(bytes, 0, count, chars, 0, flush);
            if (charCount == 0)
            {
                return;
            }

            Text.Append(chars, 0, charCount);

            if (_onLine == null)
            {
                return;
            }

            for (var i = 0; i < charCount; i++)
            {
                var c = chars[i];
                if (c == '\n')
                {
                    EmitLine();
                }
                else
                {
                    _pendingLine.Append(c);
                }
            }
        }

        public void Complete()
        {
            if (_onLine != null && _pendingLine.Length > 0)
            {
                EmitLine();
            }
        }

        private void EmitLine()
        {
            var line = _pendingLine.ToString();
            _pendingLine.Clear();

            if (line.EndsWith('\r'))
            {
                line = line[..^1];
            }

            _onLine!(line);
        }
    }
}
