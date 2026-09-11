using System;
using System.Collections.Generic;
using AppLauncher.Models;

namespace AppLauncher.Services;

public static class LocalizationService
{
    public const string LanguageChinese = "zh-CN";
    public const string LanguageEnglish = "en-US";

    public static string CurrentLanguage { get; private set; } = LanguageChinese;

    public static event Action? LanguageChanged;

    private static readonly Dictionary<string, string> ZhStrings = new()
    {
        { "Tray_Language", "🌐 语言 / Language" },
        { "Tray_Lang_Zh", "中文 (Chinese)" },
        { "Tray_Lang_En", "English" },
        { "Tray_PinTaskbar", "📌 固定到任务栏" },
        { "Tray_PinTaskbarSuccess", "AppLauncher 已固定到任务栏。" },
        { "Tray_PinTaskbarAlreadyPinned", "AppLauncher 已经固定到任务栏。" },
        { "Tray_PinTaskbarDeclined", "你取消了固定操作，AppLauncher 未固定到任务栏。" },
        { "Tray_PinTaskbarNotAllowed", "当前系统不允许固定到任务栏，可能是系统策略或当前桌面状态导致。" },
        { "Tray_PinTaskbarNotSupported", "当前 Windows 版本不支持此固定方式。" },
        { "Tray_PinTaskbarFailure", "固定到任务栏失败，请稍后重试。" },
        { "Tray_Exit", "退出" },

        { "Blank_AddApp", "➕ 添加软件 (选择 EXE / 快捷方式)..." },
        { "Blank_AddFolder", "📁 扫描并添加软件文件夹..." },
        { "Blank_Refresh", "🔄 刷新应用列表" },

        { "Menu_Launch", "🚀 启动应用" },
        { "Menu_LaunchAdmin", "🛡️ 以管理员身份运行" },
        { "Menu_OpenFolder", "📂 打开所在文件夹" },
        { "Menu_Pin", "📌 置顶" },
        { "Menu_Unpin", "📌 取消置顶" },
        { "Menu_Rename", "✏️ 重命名显示名称" },
        { "Menu_Hide", "🚫 从启动器中隐藏" },

        { "Empty_NoApp", "未找到匹配的应用" },
        { "Empty_Hint", "右键空白处可手动添加软件" },

        { "Dialog_AddAppTitle", "选择要添加到启动器的应用程序" },
        { "Dialog_AddAppFilter", "可执行文件与快捷方式 (*.exe;*.lnk)|*.exe;*.lnk|所有文件 (*.*)|*.*" },
        { "Dialog_AddFolderTitle", "选择包含便携/绿色软件的文件夹（将自动扫描其中的所有 exe）" },

        { "Rename_Title", "重命名应用" },
        { "Rename_Header", "✏️ 重命名应用显示名称" },
        { "Rename_Cancel", "取消" },
        { "Rename_Confirm", "确定" },

        { "Search_Placeholder", "应用程序" }
    };

    private static readonly Dictionary<string, string> EnStrings = new()
    {
        { "Tray_Language", "🌐 Language / 语言" },
        { "Tray_Lang_Zh", "中文 (Chinese)" },
        { "Tray_Lang_En", "English" },
        { "Tray_PinTaskbar", "📌 Pin to taskbar" },
        { "Tray_PinTaskbarSuccess", "AppLauncher has been pinned to the taskbar." },
        { "Tray_PinTaskbarAlreadyPinned", "AppLauncher is already pinned to the taskbar." },
        { "Tray_PinTaskbarDeclined", "Pinning was cancelled. AppLauncher was not pinned to the taskbar." },
        { "Tray_PinTaskbarNotAllowed", "Windows currently does not allow taskbar pinning, possibly due to policy or desktop state." },
        { "Tray_PinTaskbarNotSupported", "This Windows version does not support this pinning method." },
        { "Tray_PinTaskbarFailure", "Could not pin AppLauncher to the taskbar. Please try again." },
        { "Tray_Exit", "Exit" },

        { "Blank_AddApp", "➕ Add App (Select EXE / Shortcut)..." },
        { "Blank_AddFolder", "📁 Scan & Add Software Folder..." },
        { "Blank_Refresh", "🔄 Refresh Applications" },

        { "Menu_Launch", "🚀 Launch App" },
        { "Menu_LaunchAdmin", "🛡️ Run as Administrator" },
        { "Menu_OpenFolder", "📂 Open File Location" },
        { "Menu_Pin", "📌 Pin" },
        { "Menu_Unpin", "📌 Unpin" },
        { "Menu_Rename", "✏️ Rename App" },
        { "Menu_Hide", "🚫 Hide from Launcher" },

        { "Empty_NoApp", "No matching applications found" },
        { "Empty_Hint", "Right-click empty space to add apps" },

        { "Dialog_AddAppTitle", "Select applications to add to Launcher" },
        { "Dialog_AddAppFilter", "Executables and Shortcuts (*.exe;*.lnk)|*.exe;*.lnk|All Files (*.*)|*.*" },
        { "Dialog_AddFolderTitle", "Select folder containing portable software (will scan all EXEs)" },

        { "Rename_Title", "Rename Application" },
        { "Rename_Header", "✏️ Rename Display Name" },
        { "Rename_Cancel", "Cancel" },
        { "Rename_Confirm", "Confirm" },

        { "Search_Placeholder", "Applications" }
    };

    public static void Initialize(string? langCode)
    {
        if (string.Equals(langCode, LanguageEnglish, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(langCode, "en", StringComparison.OrdinalIgnoreCase))
        {
            CurrentLanguage = LanguageEnglish;
        }
        else
        {
            CurrentLanguage = LanguageChinese;
        }
    }

    public static void SetLanguage(string langCode, LauncherConfig? config = null)
    {
        if (CurrentLanguage == langCode) return;

        CurrentLanguage = langCode;
        if (config != null)
        {
            config.Language = langCode;
            config.Save();
        }

        LanguageChanged?.Invoke();
    }

    public static string Get(string key)
    {
        var dict = CurrentLanguage == LanguageEnglish ? EnStrings : ZhStrings;
        if (dict.TryGetValue(key, out var val))
        {
            return val;
        }

        if (ZhStrings.TryGetValue(key, out var fallback))
        {
            return fallback;
        }

        return key;
    }
}
