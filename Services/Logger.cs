using System.IO;

namespace Echo.Services;

/// <summary>
/// Simple file & console logger for diagnostic tracing.
/// Writes to %LOCALAPPDATA%\Echo\logs\ECHO.log
/// </summary>
public static class Logger
{
    private static readonly string LogDir;
    private static readonly string LogFilePath;
    private static readonly object LockObj = new();

    // Every transcript is logged, so an unbounded file grows without limit on a machine
    // that is never restarted. Roll over at 4 MB, keeping one previous file.
    private const long MaxLogBytes = 4L * 1024 * 1024;

    static Logger()
    {
        LogDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Echo", "logs");
        Directory.CreateDirectory(LogDir);
        LogFilePath = Path.Combine(LogDir, "ECHO.log");
    }

    public static string LogPath => LogFilePath;

    public static void Info(string message) => Log("INFO", message);
    public static void Warn(string message) => Log("WARN", message);
    public static void Error(string message, Exception? ex = null)
    {
        string fullMessage = ex != null ? $"{message} | Exception: {ex}" : message;
        Log("ERROR", fullMessage);
    }

    private static void Log(string level, string message)
    {
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        string line = $"[{timestamp}] [{level}] {message}";

        Console.WriteLine(line);
        System.Diagnostics.Debug.WriteLine(line);

        lock (LockObj)
        {
            try
            {
                RollIfTooLarge();
                File.AppendAllText(LogFilePath, line + Environment.NewLine);
            }
            catch
            {
                // Ignore file write errors
            }
        }
    }

    /// <summary>
    /// Caller must hold LockObj.
    /// </summary>
    private static void RollIfTooLarge()
    {
        var info = new FileInfo(LogFilePath);
        if (!info.Exists || info.Length < MaxLogBytes) return;

        string archive = LogFilePath + ".1";
        if (File.Exists(archive)) File.Delete(archive);
        File.Move(LogFilePath, archive);
    }
}
