using System.Collections.Concurrent;
using System.Text.Json;

namespace UltimatePCOptimizerPro.Core.Services;

public enum LogLevel { Debug, Info, Warning, Error }

public sealed record LogEntry(DateTimeOffset Timestamp, LogLevel Level, string Source, string Message, string? Exception);

/// <summary>Structured JSON-lines log in %LOCALAPPDATA%\UltimatePCOptimizerPro\Logs.</summary>
public sealed class LoggingService
{
    private readonly object _fileLock = new();
    private readonly ConcurrentQueue<LogEntry> _recent = new();
    public bool DetailedLogging { get; set; }

    public event Action<LogEntry>? EntryWritten;

    public IReadOnlyCollection<LogEntry> Recent => _recent.ToArray();

    public string CurrentLogFile => Path.Combine(AppPaths.Logs, $"app-{DateTime.Now:yyyyMMdd}.jsonl");

    public void Debug(string source, string message) { if (DetailedLogging) Write(LogLevel.Debug, source, message); }
    public void Info(string source, string message) => Write(LogLevel.Info, source, message);
    public void Warn(string source, string message, Exception? ex = null) => Write(LogLevel.Warning, source, message, ex);
    public void Error(string source, string message, Exception? ex = null) => Write(LogLevel.Error, source, message, ex);

    public void Write(LogLevel level, string source, string message, Exception? ex = null)
    {
        var entry = new LogEntry(DateTimeOffset.Now, level, source, message, ex?.ToString());
        _recent.Enqueue(entry);
        while (_recent.Count > 500 && _recent.TryDequeue(out _)) { }
        try
        {
            var line = JsonSerializer.Serialize(entry, JsonDefaults.Compact);
            lock (_fileLock) File.AppendAllText(CurrentLogFile, line + Environment.NewLine);
        }
        catch
        {
            // Logging must never take the application down.
        }
        EntryWritten?.Invoke(entry);
    }
}
