using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;

namespace UniParse.Services;

/// <summary>
/// アプリケーションの診断ログを永続化します。ログ出力の失敗が本体処理を妨げないよう、
/// すべてのファイル操作は内部で吸収します。
/// </summary>
public static class ApplicationLogger
{
    private static readonly object SyncRoot = new();
    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "UniParse",
        "Logs");
    private static readonly string LogFilePath = Path.Combine(
        LogDirectory,
        $"UniParse_{DateTime.Now:yyyyMMdd}_{Environment.ProcessId}.log");

    public static string LogPath => LogFilePath;

    public static void Initialize()
    {
        Assembly assembly = typeof(ApplicationLogger).Assembly;
        string version = assembly.GetName().Version?.ToString() ?? "unknown";

        Info("Application", $"UniParse starting. Version={version}; ProcessId={Environment.ProcessId}");
        Info("Application", $"OS={Environment.OSVersion}; Framework={Environment.Version}; Is64BitProcess={Environment.Is64BitProcess}");
        Info("Application", $"BaseDirectory={AppContext.BaseDirectory}; LogPath={LogPath}");
    }

    public static void Info(string source, string message) => Write("INFO", source, message);
    public static void Debug(string source, string message) => Write("DEBUG", source, message);
    public static void Warning(string source, string message) => Write("WARN", source, message);
    public static void Error(string source, string message, Exception? exception = null)
        => Write("ERROR", source, exception is null ? message : $"{message}{Environment.NewLine}{exception}");

    private static void Write(string level, string source, string message)
    {
        try
        {
            string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] [T{Environment.CurrentManagedThreadId}] [{source}] {message}";
            lock (SyncRoot)
            {
                Directory.CreateDirectory(LogDirectory);
                File.AppendAllText(LogFilePath, line + Environment.NewLine, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
            System.Diagnostics.Debug.WriteLine(line);
        }
        catch
        {
            // Logging must never change the result of the user operation.
        }
    }
}
