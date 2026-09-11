using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Win32;
using Windows.Foundation.Metadata;
using Windows.UI.Shell;
using WinRT;

namespace AppLauncher.Services;

public enum TaskbarPinStatus
{
    Pinned,
    AlreadyPinned,
    UserDeclined,
    NotAllowed,
    NotSupported,
    Failed
}

public readonly record struct TaskbarPinResult(TaskbarPinStatus Status)
{
    public bool IsSuccess => Status is TaskbarPinStatus.Pinned or TaskbarPinStatus.AlreadyPinned;
}

/// <summary>
/// Requests taskbar pinning through Windows' supported TaskbarManager API.
/// The API shows a system confirmation prompt and returns the actual result.
/// </summary>
public static class TaskbarPinService
{
    public const string AppUserModelId = "AppLauncher.Workspace.Productivity.AppLauncher";

    private const string TaskbarPinFeature = "com.microsoft.windows.taskbar.pin";
    private const string TaskbarPinLafSeedValue = "4096B239A7295B635C090E647E867B5707DA6AB6CB78340B01FE4E0C8F4953D4";
    private const string DesktopAppSupportInterfaceId = "cdfefd63-e879-4134-b9a7-8283f05f9480";
    private const string StartMenuShortcutFileName = "AppLauncher.lnk";
    private const ushort VariantTypeLpwstr = 31;
    private const uint ShellChangeUpdatedDirectory = 0x00001000;
    private const uint ShellChangePathW = 0x0005;

    public static async Task<TaskbarPinResult> RequestPinCurrentApplicationAsync()
    {
        try
        {
            if (!OperatingSystem.IsWindows() || !ApiInformation.IsTypePresent("Windows.UI.Shell.TaskbarManager"))
            {
                return new TaskbarPinResult(TaskbarPinStatus.NotSupported);
            }

            if (IsTaskbarPinLafRequired())
            {
                // This portable app has no Microsoft-issued Limited Access
                // Feature token. Newer Windows builds no longer require one.
                return new TaskbarPinResult(TaskbarPinStatus.NotSupported);
            }

            if (!IsDesktopAppPinningSupported())
            {
                return new TaskbarPinResult(TaskbarPinStatus.NotSupported);
            }

            var taskbarManager = TaskbarManager.GetDefault();
            if (!taskbarManager.IsSupported)
            {
                return new TaskbarPinResult(TaskbarPinStatus.NotSupported);
            }

            var executablePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
            {
                return new TaskbarPinResult(TaskbarPinStatus.Failed);
            }

            if (await taskbarManager.IsCurrentAppPinnedAsync())
            {
                return new TaskbarPinResult(TaskbarPinStatus.AlreadyPinned);
            }

            // Windows requires a Start menu entry before it reports that a
            // desktop app is allowed to request taskbar pinning.
            RemoveLegacyTaskbarShortcut(executablePath);
            EnsureStartMenuShortcut(executablePath);

            if (!taskbarManager.IsPinningAllowed)
            {
                return new TaskbarPinResult(TaskbarPinStatus.NotAllowed);
            }

            // This call must happen after explicit user interaction while the
            // app is in the foreground. Windows displays its own confirmation.
            var isPinned = await taskbarManager.RequestPinCurrentAppAsync();
            return new TaskbarPinResult(isPinned ? TaskbarPinStatus.Pinned : TaskbarPinStatus.UserDeclined);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write("TaskbarPin.Request", ex);
            return new TaskbarPinResult(TaskbarPinStatus.Failed);
        }
    }

    private static bool IsDesktopAppPinningSupported()
    {
        IntPtr desktopSupportInterface = IntPtr.Zero;
        try
        {
            // Desktop support is exposed by TaskbarManager's activation
            // factory, not by an individual TaskbarManager instance.
            using var activationFactory = ActivationFactory.Get("Windows.UI.Shell.TaskbarManager");
            return activationFactory.TryAs(
                new Guid(DesktopAppSupportInterfaceId),
                out desktopSupportInterface) >= 0;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (desktopSupportInterface != IntPtr.Zero)
            {
                try { Marshal.Release(desktopSupportInterface); } catch { }
            }
        }
    }

    private static bool IsTaskbarPinLafRequired()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                $@"SOFTWARE\Microsoft\Windows\CurrentVersion\AppModel\LimitedAccessFeatures\{TaskbarPinFeature}");
            var value = key?.GetValue(TaskbarPinLafSeedValue);
            return value switch
            {
                int intValue => intValue != 0,
                uint uintValue => uintValue != 0,
                long longValue => longValue != 0,
                _ => false
            };
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write("TaskbarPin.LafCheck", ex);
            return true;
        }
    }

    private static void EnsureStartMenuShortcut(string executablePath)
    {
        var programsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        Directory.CreateDirectory(programsDirectory);

        var shortcutPath = Path.Combine(programsDirectory, StartMenuShortcutFileName);
        NativeMethods.IShellLinkW? link = null;
        NativeMethods.IPropertyStore? propertyStore = null;
        var appIdVariant = default(NativeMethods.PROPVARIANT);
        var appIdVariantInitialized = false;

        try
        {
            link = (NativeMethods.IShellLinkW)new NativeMethods.ShellLink();
            link.SetPath(executablePath);
            link.SetWorkingDirectory(Path.GetDirectoryName(executablePath) ?? string.Empty);
            link.SetDescription("AppLauncher");

            var iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
            if (File.Exists(iconPath))
            {
                link.SetIconLocation(iconPath, 0);
            }

            propertyStore = (NativeMethods.IPropertyStore)link;
            var propertyKey = NativeMethods.AppUserModelIdPropertyKey;
            appIdVariant = new NativeMethods.PROPVARIANT
            {
                VariantType = VariantTypeLpwstr,
                Value = Marshal.StringToCoTaskMemUni(AppUserModelId)
            };
            appIdVariantInitialized = true;
            propertyStore.SetValue(ref propertyKey, ref appIdVariant);
            propertyStore.Commit();

            var persistFile = (NativeMethods.IPersistFile)link;
            persistFile.Save(shortcutPath, true);

            // Make the newly-created Start menu entry visible to Explorer
            // immediately before TaskbarManager evaluates pinning eligibility.
            NativeMethods.SHChangeNotify(
                ShellChangeUpdatedDirectory,
                ShellChangePathW,
                programsDirectory,
                IntPtr.Zero);
        }
        finally
        {
            if (appIdVariantInitialized)
            {
                try { NativeMethods.PropVariantClear(ref appIdVariant); } catch { }
            }

            ReleaseComObject(propertyStore);
            ReleaseComObject(link);
        }
    }

    private static void RemoveLegacyTaskbarShortcut(string executablePath)
    {
        var taskbarDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");
        var shortcutPath = Path.Combine(taskbarDirectory, StartMenuShortcutFileName);

        try
        {
            if (!File.Exists(shortcutPath)) return;

            var target = NativeMethods.ResolveShortcutTarget(shortcutPath);
            if (string.Equals(target, executablePath, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(shortcutPath);
            }
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write("TaskbarPin.LegacyShortcutCleanup", ex);
        }
    }

    private static void ReleaseComObject(object? value)
    {
        if (value != null)
        {
            try { Marshal.ReleaseComObject(value); } catch { }
        }
    }
}
