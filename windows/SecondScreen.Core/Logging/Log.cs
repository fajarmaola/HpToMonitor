namespace SecondScreen.Core;

public enum LogLevel { Debug, Info, Warn, Error }

// Minimal thread-safe logger: raises an event (UI subscribes) and appends to a rolling file
// under %LOCALAPPDATA%\SecondScreenLocal\logs. No external logging dependency.
public static class Log
{
    public static event EventHandler<(LogLevel level, string message)>? OnLog;
    public static LogLevel MinLevel = LogLevel.Debug;

    private static readonly object _lock = new();
    private static readonly string _file = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SecondScreenLocal", "logs", $"ssl-{DateTime.Now:yyyyMMdd}.log");

    public static void Debug(string m) => Write(LogLevel.Debug, m);
    public static void Info(string m) => Write(LogLevel.Info, m);
    public static void Warn(string m) => Write(LogLevel.Warn, m);
    public static void Error(string m) => Write(LogLevel.Error, m);
    public static void Error(string m, Exception ex) => Write(LogLevel.Error, $"{m}: {ex}");

    private static void Write(LogLevel level, string message)
    {
        if (level < MinLevel) return;
        var line = $"{DateTime.Now:HH:mm:ss.fff} [{level}] {message}";
        try
        {
            lock (_lock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
                File.AppendAllText(_file, line + Environment.NewLine);
            }
        }
        catch { /* logging must never crash the app */ }
        OnLog?.Invoke(null, (level, message));
    }
}
