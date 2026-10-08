using LaunchStage.Core.Storage;

namespace LaunchStage.Core.Logging;

/// <summary>Writes every step to %AppData%\LaunchStage\Logs\debug_log.txt so problems are easy to trace.</summary>
public static class Log
{
    private const long MaxBytes = 2_000_000;
    private static readonly object Gate = new();
    private static bool _prepared;

    public static void Debug(string message) => Write("DEBUG", message);
    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message) => Write("ERROR", message);

    private static void Write(string level, string message)
    {
        lock (Gate)
        {
            try
            {
                if (!_prepared)
                {
                    AppPaths.EnsureFolders();
                    RotateIfLarge();
                    _prepared = true;
                }

                string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} - {level} - {message}{Environment.NewLine}";
                File.AppendAllText(AppPaths.LogFile, line);
            }
            catch
            {
                // Logging must never crash the app.
            }
        }
    }

    private static void RotateIfLarge()
    {
        var file = new FileInfo(AppPaths.LogFile);
        if (file.Exists && file.Length > MaxBytes)
        {
            File.Move(file.FullName, Path.Combine(AppPaths.LogsFolder, "debug_log.old.txt"), overwrite: true);
        }
    }
}
