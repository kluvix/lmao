using System.IO.Pipes;
using System.Text;
using HeyJace.Core;

namespace HeyJace.Speech;

// Both directions have a fixed line limit, including lines from a damaged peer.
internal sealed class PipeConnection : IDisposable
{
    private readonly NamedPipeServerStream _pipe;
    private readonly StreamReader _reader;
    private readonly StreamWriter _writer;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly char[] _buffer = new char[1024];
    private int _bufferOffset;
    private int _bufferLength;

    internal PipeConnection(NamedPipeServerStream pipe)
    {
        _pipe = pipe;
        var utf8 = new UTF8Encoding(false, true);
        _reader = new StreamReader(pipe, utf8, false, 1024, true);
        _writer = new StreamWriter(pipe, utf8, 1024, true) { NewLine = "\n", AutoFlush = true };
    }

    internal async Task SendAsync(WireMessage message, CancellationToken cancellationToken)
    {
        string line = PipeProtocol.Encode(message);
        if (line.Length > PipeProtocol.MaxLineLength || line.Contains('\n') || line.Contains('\r'))
            throw new InvalidDataException("The outgoing pipe message exceeds the protocol limits.");
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _writer.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
        }
        finally { _writeLock.Release(); }
    }

    internal async Task<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        var line = new StringBuilder();
        while (true)
        {
            if (_bufferOffset == _bufferLength)
            {
                _bufferLength = await _reader.ReadAsync(_buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                _bufferOffset = 0;
                // EOF ends the session; a partial final message is not processed.
                if (_bufferLength == 0) return null;
            }
            char character = _buffer[_bufferOffset++];
            if (character == '\n')
            {
                if (line.Length > 0 && line[line.Length - 1] == '\r') line.Length--;
                return line.ToString();
            }
            if (line.Length >= PipeProtocol.MaxLineLength)
                throw new InvalidDataException("The incoming pipe message exceeds the protocol limits.");
            line.Append(character);
        }
    }

    public void Dispose()
    {
        _reader.Dispose();
        try { _writer.Dispose(); } catch (IOException) { }
        _pipe.Dispose();
        _writeLock.Dispose();
    }
}
