using System.Globalization;

namespace PdfUnlocker;

/// <summary>
/// Create-on-start / delete-on-success log (spec §4.5). The log file only survives a run
/// that recorded an error; a clean run leaves no log behind.
/// </summary>
/// <remarks>
/// Robustness rule: logging must never turn an otherwise-successful run into a failure. Any
/// exception raised while creating, writing, or deleting the log is swallowed.
/// </remarks>
internal sealed class RunLogger : IDisposable
{
    private readonly string _logPath;
    private readonly StreamWriter? _writer;
    private bool _errorOccurred;

    public RunLogger(string logFolder)
    {
        _logPath = Path.Combine(logFolder, Constants.LogFileName);
        try
        {
            // Overwrite any stale log from a previous run.
            _writer = new StreamWriter(_logPath, append: false) { AutoFlush = true };
            WriteLine($"PdfUnlocker run started at {Timestamp()}");
        }
        catch
        {
            // Read-only folder etc. — run without a log rather than crashing.
            _writer = null;
        }
    }

    /// <summary>Records a handled or unexpected error and marks the run as failed.</summary>
    public void LogError(string message, Exception? exception, string? inputPath)
    {
        _errorOccurred = true;
        try
        {
            WriteLine($"[{Timestamp()}] ERROR: {message}");
            if (inputPath is not null)
                WriteLine($"  Input: {inputPath}");
            if (exception is not null)
            {
                WriteLine($"  Exception: {exception.GetType().FullName}: {exception.Message}");
                if (exception.StackTrace is { } stack)
                    WriteLine(stack);
            }
        }
        catch
        {
            // Swallow — see remarks.
        }
    }

    private void WriteLine(string text) => _writer?.WriteLine(text);

    private static string Timestamp() =>
        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    /// <summary>Flushes and closes the log; deletes it if no error was recorded.</summary>
    public void Dispose()
    {
        try
        {
            _writer?.Dispose();
            if (!_errorOccurred && File.Exists(_logPath))
                File.Delete(_logPath);
        }
        catch
        {
            // Swallow — see remarks.
        }
    }
}
