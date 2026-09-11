using System;
using System.IO;
using System.Text;

namespace AppLauncher.Services;

internal static class DiagnosticLog
{
    private const long MaxLogBytes = 1024 * 1024;
    private static readonly object Sync = new();
    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AppLauncher");

    public static void Initialize()
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(Folder);
                RemoveOversizedLog("crash.log");
                RemoveOversizedLog("diagnostic.log");
                RemoveOversizedLog("crash.previous.log");
                RemoveOversizedLog("diagnostic.previous.log");
            }
        }
        catch
        {
            // Diagnostics must never interfere with the launcher itself.
        }
    }

    public static void Write(string context, Exception exception)
    {
        WriteCore("diagnostic.log", context, exception);
    }

    public static void WriteCrash(string context, Exception? exception)
    {
        WriteCore("crash.log", context, exception);
    }

    private static void WriteCore(string fileName, string context, Exception? exception)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(Folder);
                var path = Path.Combine(Folder, fileName);
                var message = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{context}]\n{exception}\n\n";
                message = TruncateToFit(message);

                var messageBytes = Encoding.UTF8.GetByteCount(message);
                if (File.Exists(path) && new FileInfo(path).Length + messageBytes > MaxLogBytes)
                {
                    Rotate(path);
                }

                File.AppendAllText(path, message);
            }
        }
        catch
        {
            // Diagnostics must never interfere with the launcher itself.
        }
    }

    private static string TruncateToFit(string message)
    {
        if (Encoding.UTF8.GetByteCount(message) <= MaxLogBytes)
        {
            return message;
        }

        const string suffix = "\n[Log entry truncated]\n\n";
        var maxCharacters = (int)(MaxLogBytes / 4) - suffix.Length;
        return string.Concat(message.AsSpan(0, Math.Min(message.Length, maxCharacters)), suffix);
    }

    private static void Rotate(string path)
    {
        var previousPath = Path.Combine(
            Path.GetDirectoryName(path)!,
            $"{Path.GetFileNameWithoutExtension(path)}.previous{Path.GetExtension(path)}");

        if (File.Exists(previousPath))
        {
            File.Delete(previousPath);
        }

        var currentLog = new FileInfo(path);
        if (currentLog.Length <= MaxLogBytes)
        {
            File.Move(path, previousPath);
        }
        else
        {
            File.Delete(path);
        }
    }

    private static void RemoveOversizedLog(string fileName)
    {
        var path = Path.Combine(Folder, fileName);
        if (File.Exists(path) && new FileInfo(path).Length > MaxLogBytes)
        {
            File.Delete(path);
        }
    }
}
