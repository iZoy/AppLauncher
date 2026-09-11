using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace AppLauncher.Services;

public static class SmartFilter
{
    // Exclude noisy executables and helper tools
    private static readonly Regex ExcludedFileNameRegex = new(
        @"(^unins.*|uninstall|update|updater|setup|installer|patcher|crashpad|.*helper.*|.*service.*|.*daemon.*|.*agent.*|.*cleaner.*|.*deploy.*|.*upgrade.*|.*guard.*|.*usage.*|.*mdnsresponder.*|.*qtwebengine.*|.*cef.*|.*renderer.*|.*gpu_process.*|.*utility_process.*|.*broker.*|.*reporter.*|.*watcher.*|.*telemetry.*|.*framework.*|.*host.*|.*worker.*|vcredist|dxsetup|dotnet|node|npm|git|curl|ffmpeg|ffprobe|conhost|cmd|powershell|pwsh|cscript|wscript|regsvr32|rundll32|msiexec|tar|winget|skypeserver|ccxprocess|accapp|accstd|accsvc|accub|accnvme|iexplore|msoadfsb|setlang)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Exclude shortcuts that are uninstallation, help links, manuals, or configuration utilities
    private static readonly Regex ExcludedShortcutNameRegex = new(
        @"(卸载|uninstall|help|readme|manual|documentation|website|feedback|反馈|配置工具|config|setting|support|官网|链接|url)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly string[] ExcludedDirectoryPatterns = new[]
    {
        @"\$Recycle.Bin",
        @"\System Volume Information",
        @"\Windows\WinSxS",
        @"\Windows\System32",
        @"\Windows\SysWOW64",
        @"\Windows\Installer",
        @"\AppData\Local\Temp",
        @"\AppData\Local\Microsoft\WindowsApps",
        @"\node_modules",
        @"\Package Cache",
        @"\.git",
        @"\.nuget",
        @"\Cache",
        @"\Temporary"
    };

    public static bool ShouldExcludePath(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return true;

        var ext = Path.GetExtension(filePath);
        if (!string.Equals(ext, ".exe", StringComparison.OrdinalIgnoreCase))
            return true;

        var fileNameWithoutExt = Path.GetFileNameWithoutExtension(filePath);

        // Check file name blacklist
        if (ExcludedFileNameRegex.IsMatch(fileNameWithoutExt))
            return true;

        // Check path exclusions
        var fullPath = Path.GetFullPath(filePath);
        foreach (var pattern in ExcludedDirectoryPatterns)
        {
            if (fullPath.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public static bool ShouldExcludeShortcut(string shortcutName, string targetPath)
    {
        if (ExcludedShortcutNameRegex.IsMatch(shortcutName))
            return true;

        return ShouldExcludePath(targetPath);
    }

    public static string ExtractBestDisplayName(string filePath, string? shortcutTitle = null)
    {
        // 1. If we have a clean shortcut title from Start Menu / Desktop, prefer it!
        if (!string.IsNullOrWhiteSpace(shortcutTitle))
        {
            var cleanTitle = shortcutTitle.Trim();
            if (!IsGenericDescription(cleanTitle))
            {
                return cleanTitle;
            }
        }

        var defaultName = Path.GetFileNameWithoutExtension(filePath);

        // 2. Read PE metadata
        try
        {
            var versionInfo = FileVersionInfo.GetVersionInfo(filePath);

            if (!string.IsNullOrWhiteSpace(versionInfo.FileDescription))
            {
                var desc = versionInfo.FileDescription.Trim();
                if (!IsGenericDescription(desc))
                {
                    return desc;
                }
            }

            if (!string.IsNullOrWhiteSpace(versionInfo.ProductName))
            {
                var prod = versionInfo.ProductName.Trim();
                if (!IsGenericDescription(prod))
                {
                    return prod;
                }
            }
        }
        catch
        {
            // Fallback
        }

        return defaultName;
    }

    private static bool IsGenericDescription(string name)
    {
        var lower = name.ToLowerInvariant();
        return lower.Contains("setup") ||
               lower.Contains("installer") ||
               lower.Contains("uninstall") ||
               lower.Contains("update") ||
               lower.Contains("helper") ||
               lower.Contains("crash report") ||
               lower.Contains("command line") ||
               lower == "application" ||
               lower == "launcher";
    }
}
