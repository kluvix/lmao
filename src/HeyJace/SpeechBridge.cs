using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HeyJace.Core;

namespace HeyJace;

// The .NET 8 helper owns a current-user-only pipe server. The Unity Mono side
// is a client, avoiding newer server ACL flags unsupported by older Mono BCLs.
internal sealed class SpeechBridge : IDisposable
{
    private readonly ConcurrentQueue<WireMessage> incoming = new();
    private readonly ConcurrentQueue<WireMessage> outgoing = new();
    private readonly SemaphoreSlim repliesReady = new(0);
    private readonly CancellationTokenSource stop = new();
    private NamedPipeClientStream? pipe;
    private StreamWriter? writer;
    private Process? helper;
    private volatile bool disposed;
    private volatile bool authenticated;

    internal bool Connected => !disposed && authenticated && pipe?.IsConnected == true && writer != null;
    internal bool Running
    {
        get { try { return !disposed && helper != null && !helper.HasExited; } catch { return false; } }
    }

    internal void Start()
    {
        if (Environment.OSVersion.Platform != PlatformID.Win32NT)
        { Notice("Hey Jace speech requires Windows. The game can remain minimized while listening.", false); return; }
        string directory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
        string executable = Path.Combine(directory, "Speech", "HeyJace.Speech.exe");
        if (!File.Exists(executable))
        { Notice("Speech helper missing. Install the complete HeyJace Windows package, including its Speech folder.", false); return; }
        string name = "HeyJace_" + Guid.NewGuid().ToString("N");
        try
        {
            helper = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = "--pipe " + name + " --parent " + Process.GetCurrentProcess().Id,
                    WorkingDirectory = directory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true
                },
                EnableRaisingEvents = true
            };
            helper.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) Notice(e.Data!, false); };
            helper.Exited += (_, _) => { if (!disposed) Notice("Speech helper stopped. Press F10 to restart listening.", false); };
            if (!helper.Start()) throw new InvalidOperationException("Speech helper did not start.");
            helper.BeginErrorReadLine();
            ConnectPipe(name);
            Notice("Starting offline speech. Use the Windows default microphone.");
        }
        catch (Exception e) { Notice("Could not start speech: " + e.GetBaseException().Message, false); }
    }

    internal void ConnectPipe(string name)
    {
        if (disposed || pipe != null) throw new InvalidOperationException("Speech connection already created or closed.");
        pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        _ = Task.Run(ReadLoop);
    }

    private void ReadLoop()
    {
        try
        {
            NamedPipeClientStream? connection = pipe;
            if (connection == null) return;
            connection.Connect(30000);
            if (disposed) return;
            var output = new StreamWriter(connection, new UTF8Encoding(false), 1024, true) { AutoFlush = true };
            writer = output;
            _ = Task.Run(() => WriteLoop(output));
            using var reader = new StreamReader(connection, new UTF8Encoding(false, true), false, 1024, true);
            while (!disposed && connection.IsConnected)
            {
                string? line = ReadBoundedLine(reader);
                if (line == null) break;
                if (!PipeProtocol.TryDecode(line, out WireMessage? message) || message == null) continue;
                if (!authenticated)
                {
                    if (message.Kind != WireKind.Hello || message.Text != PipeProtocol.Handshake)
                        throw new InvalidDataException("Unexpected speech helper protocol. Replace the whole HeyJace folder so both DLLs and Speech/HeyJace.Speech.exe are version 1.1.0.");
                    authenticated = true;
                    continue;
                }
                if (message.Kind is WireKind.Wake or WireKind.Command or WireKind.Status)
                {
                    if (incoming.Count < 64) incoming.Enqueue(message);
                }
            }
            if (!disposed) Notice("Speech connection closed. F10 restarts the assistant.", false);
        }
        catch (Exception e) { if (!disposed) Notice("Speech connection: " + e.GetBaseException().Message, false); }
        finally
        {
            authenticated = false;
            stop.Cancel();
            try { pipe?.Dispose(); } catch { }
            writer = null;
        }
    }

    private async Task WriteLoop(StreamWriter output)
    {
        try
        {
            while (!disposed && !stop.IsCancellationRequested)
            {
                await repliesReady.WaitAsync(stop.Token).ConfigureAwait(false);
                while (!disposed && outgoing.TryDequeue(out WireMessage? message))
                    await output.WriteLineAsync(PipeProtocol.Encode(message)).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { if (!disposed) Notice("Speech reply connection: " + e.GetBaseException().Message, false); }
        finally { try { output.Dispose(); } catch { } }
    }

    private static string? ReadBoundedLine(StreamReader reader)
    {
        var text = new StringBuilder();
        while (true)
        {
            int next = reader.Read();
            if (next < 0) return null;
            if (next == '\n') return text.ToString().TrimEnd('\r');
            if (text.Length >= PipeProtocol.MaxLineLength) throw new InvalidDataException("Speech message too long.");
            text.Append((char)next);
        }
    }

    internal bool TryRead(out WireMessage? message) => incoming.TryDequeue(out message);

    internal void SendResult(int id, ActionResult result)
    {
        try
        {
            var clean = new StringBuilder();
            foreach (char c in result.Message)
            {
                if (clean.Length >= 500) break;
                clean.Append(char.IsControl(c) || c == '\u2028' || c == '\u2029' ? ' ' : c);
            }
            if (!Connected) return;
            if (outgoing.Count >= 32) { Notice("Speech reply queue is full. F10 restarts the assistant.", false); return; }
            outgoing.Enqueue(new WireMessage(WireKind.Result, id, result.Success, clean.ToString()));
            repliesReady.Release();
        }
        catch (Exception e) { if (!disposed) Notice("Speech reply: " + e.GetBaseException().Message, false); }
    }

    internal bool TrySendVocabulary(int phase, string text)
    {
        if (!Connected || outgoing.Count >= 24) return false;
        var message = new WireMessage(WireKind.Vocabulary, phase, text: text);
        // Validate synchronously so malformed names can never kill the writer.
        PipeProtocol.Encode(message);
        outgoing.Enqueue(message);
        repliesReady.Release();
        return true;
    }

    private void Notice(string text, bool success = true)
    {
        if (disposed || incoming.Count >= 64) return;
        if (text.Length > 500) text = text.Substring(0, 500);
        incoming.Enqueue(new WireMessage(WireKind.Status, success: success, text: text));
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        stop.Cancel();
        try { pipe?.Dispose(); } catch { }
        try { if (helper != null && !helper.HasExited) helper.Kill(); } catch { }
        helper?.Dispose(); helper = null; pipe = null;
        while (incoming.TryDequeue(out _)) { }
        while (outgoing.TryDequeue(out _)) { }
    }
}
