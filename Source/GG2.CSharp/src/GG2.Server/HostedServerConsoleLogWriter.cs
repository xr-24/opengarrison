using System;
using System.IO;
using System.Text;

namespace GG2.Server;

internal sealed class HostedServerConsoleLogWriter : TextWriter, IDisposable
{
    private readonly object _sync = new();
    private readonly TextWriter _primary;
    private readonly StreamWriter _logWriter;

    public HostedServerConsoleLogWriter(TextWriter primary, string logPath, bool reset)
    {
        _primary = primary;
        Directory.CreateDirectory(Path.GetDirectoryName(logPath) ?? Directory.GetCurrentDirectory());
        var fileMode = reset ? FileMode.Create : FileMode.Append;
        var stream = new FileStream(logPath, fileMode, FileAccess.Write, FileShare.ReadWrite);
        _logWriter = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            AutoFlush = true,
        };
    }

    public override Encoding Encoding => _primary.Encoding;

    public override void Write(char value)
    {
        lock (_sync)
        {
            _primary.Write(value);
            _logWriter.Write(value);
        }
    }

    public override void Write(string? value)
    {
        lock (_sync)
        {
            _primary.Write(value);
            _logWriter.Write(value);
        }
    }

    public override void WriteLine(string? value)
    {
        lock (_sync)
        {
            _primary.WriteLine(value);
            _logWriter.WriteLine(value);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            lock (_sync)
            {
                _logWriter.Dispose();
            }
        }

        base.Dispose(disposing);
    }
}
