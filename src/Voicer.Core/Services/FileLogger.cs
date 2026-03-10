using System.IO;
using System.Text;

namespace Voicer.Core.Services;

/// <summary>
/// Simple rotating file logger. Writes to logs/ directory next to the executable.
/// Keeps a limited number of log files, rotating when the current file exceeds MaxFileSize.
/// Thread-safe. Can also redirect Console.Out/Error to log file.
/// </summary>
public sealed class FileLogger : TextWriter, IDisposable
{
    private const int MaxFileSize = 2 * 1024 * 1024; // 2 MB per file
    private const int MaxFiles = 5;                    // Keep last 5 files
    private const string LogFileName = "voicer.log";

    private readonly string _logDir;
    private readonly object _lock = new();
    private StreamWriter? _writer;
    private long _currentSize;
    private bool _disposed;

    public override Encoding Encoding => Encoding.UTF8;

    public FileLogger()
    {
        _logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        Directory.CreateDirectory(_logDir);

        OpenLogFile();
    }

    /// <summary>
    /// Redirects Console.Out and Console.Error to this logger (while keeping original console output too).
    /// </summary>
    public void RedirectConsole()
    {
        var originalOut = Console.Out;
        var originalErr = Console.Error;
        Console.SetOut(new TeeWriter(originalOut, this));
        Console.SetError(new TeeWriter(originalErr, this));
    }

    public void Log(string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
        lock (_lock)
        {
            if (_disposed) return;
            EnsureWriter();
            _writer!.WriteLine(line);
            _writer.Flush();
            _currentSize += Encoding.GetByteCount(line) + Environment.NewLine.Length;

            if (_currentSize >= MaxFileSize)
                Rotate();
        }
    }

    // TextWriter overrides — these are called by Console.WriteLine when redirected
    public override void WriteLine(string? value)
    {
        if (value != null) Log(value);
    }

    public override void Write(string? value)
    {
        if (value != null) Log(value);
    }

    public override void WriteLine() { }

    private void OpenLogFile()
    {
        var logPath = Path.Combine(_logDir, LogFileName);
        var fileInfo = new FileInfo(logPath);
        _currentSize = fileInfo.Exists ? fileInfo.Length : 0;

        if (_currentSize >= MaxFileSize)
            Rotate();
        else
        {
            _writer = new StreamWriter(logPath, append: true, Encoding.UTF8)
            {
                AutoFlush = true
            };
        }
    }

    private void Rotate()
    {
        _writer?.Flush();
        _writer?.Dispose();
        _writer = null;

        var logPath = Path.Combine(_logDir, LogFileName);

        // Shift existing rotated files: voicer.4.log → delete, voicer.3.log → voicer.4.log, ...
        for (int i = MaxFiles - 1; i >= 1; i--)
        {
            var src = Path.Combine(_logDir, $"voicer.{i}.log");
            var dst = Path.Combine(_logDir, $"voicer.{i + 1}.log");
            if (File.Exists(dst)) File.Delete(dst);
            if (File.Exists(src)) File.Move(src, dst);
        }

        // Current → voicer.1.log
        var first = Path.Combine(_logDir, "voicer.1.log");
        if (File.Exists(first)) File.Delete(first);
        if (File.Exists(logPath)) File.Move(logPath, first);

        // Open fresh log
        _writer = new StreamWriter(logPath, append: false, Encoding.UTF8)
        {
            AutoFlush = true
        };
        _currentSize = 0;
    }

    private void EnsureWriter()
    {
        if (_writer == null)
        {
            var logPath = Path.Combine(_logDir, LogFileName);
            _writer = new StreamWriter(logPath, append: true, Encoding.UTF8)
            {
                AutoFlush = true
            };
        }
    }

    protected override void Dispose(bool disposing)
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            _writer?.Flush();
            _writer?.Dispose();
            _writer = null;
        }
        base.Dispose(disposing);
    }

    /// <summary>
    /// Writes to both the original TextWriter and the logger.
    /// </summary>
    private class TeeWriter : TextWriter
    {
        private readonly TextWriter _original;
        private readonly FileLogger _logger;

        public TeeWriter(TextWriter original, FileLogger logger)
        {
            _original = original;
            _logger = logger;
        }

        public override Encoding Encoding => _original.Encoding;

        public override void Write(char value) => _original.Write(value);

        public override void Write(string? value)
        {
            _original.Write(value);
            if (value != null) _logger.Log(value);
        }

        public override void WriteLine(string? value)
        {
            _original.WriteLine(value);
            if (value != null) _logger.Log(value);
        }

        public override void WriteLine()
        {
            _original.WriteLine();
        }

        public override void Flush()
        {
            _original.Flush();
        }
    }
}
