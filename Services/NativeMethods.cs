using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace AppLauncher.Services;

public static class NativeMethods
{
    #region Shell & Window Styles

    [DllImport("shell32.dll", SetLastError = true)]
    public static extern void SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string AppID);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern void SHChangeNotify(
        uint wEventId,
        uint uFlags,
        [MarshalAs(UnmanagedType.LPWStr)] string? dwItem1,
        IntPtr dwItem2);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int DWMWCP_ROUND = 2;
    private const int DWMSBT_TRANSIENTWINDOW = 3;

    [DllImport("dwmapi.dll")]
    private static extern int DwmIsCompositionEnabled(out bool pfEnabled);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int dwAttribute,
        ref int pvAttribute,
        int cbAttribute);

    public static bool IsWindows11AcrylicSupported()
    {
        return OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621);
    }

    public static bool TryEnableWindows11Acrylic(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !IsWindows11AcrylicSupported()) return false;

        try
        {
            if (DwmIsCompositionEnabled(out var compositionEnabled) != 0 || !compositionEnabled)
                return false;

            int darkMode = 1;
            int cornerPreference = DWMWCP_ROUND;
            int backdropType = DWMSBT_TRANSIENTWINDOW;

            var darkResult = DwmSetWindowAttribute(
                hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));
            var cornerResult = DwmSetWindowAttribute(
                hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPreference, sizeof(int));
            var backdropResult = DwmSetWindowAttribute(
                hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdropType, sizeof(int));

            return darkResult == 0 && cornerResult == 0 && backdropResult == 0;
        }
        catch
        {
            return false;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        public uint bmiColors;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ICONINFO
    {
        public bool fIcon;
        public int xHotspot;
        public int yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    [DllImport("gdi32.dll", SetLastError = true)]
    public static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO pbmi, uint iUsage, out IntPtr ppvBits, IntPtr hSection, uint dwOffset);

    [DllImport("gdi32.dll", SetLastError = true)]
    public static extern IntPtr CreateBitmap(int nWidth, int nHeight, uint nPlanes, uint nBitCount, IntPtr lpBits);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr CreateIconIndirect(ref ICONINFO piconinfo);

    [DllImport("gdi32.dll", SetLastError = true)]
    public static extern bool DeleteObject(IntPtr hObject);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    public static IntPtr Create32BitAlphaHicon(System.Drawing.Bitmap src)
    {
        int w = src.Width;
        int h = src.Height;

        BITMAPINFO bi = new BITMAPINFO();
        bi.bmiHeader.biSize = (uint)Marshal.SizeOf(typeof(BITMAPINFOHEADER));
        bi.bmiHeader.biWidth = w;
        bi.bmiHeader.biHeight = h;
        bi.bmiHeader.biPlanes = 1;
        bi.bmiHeader.biBitCount = 32;
        bi.bmiHeader.biCompression = 0; // BI_RGB

        IntPtr hdc = GetDC(IntPtr.Zero);
        IntPtr hBitmap = CreateDIBSection(hdc, ref bi, 0, out IntPtr ppvBits, IntPtr.Zero, 0);
        ReleaseDC(IntPtr.Zero, hdc);

        if (hBitmap == IntPtr.Zero || ppvBits == IntPtr.Zero)
            return IntPtr.Zero;

        var data = src.LockBits(
            new System.Drawing.Rectangle(0, 0, w, h),
            System.Drawing.Imaging.ImageLockMode.ReadOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);

        unsafe
        {
            byte* srcPtr = (byte*)data.Scan0;
            byte* dstPtr = (byte*)ppvBits;
            for (int y = 0; y < h; y++)
            {
                int srcRow = y * data.Stride;
                int dstRow = (h - 1 - y) * (w * 4); // bottom-up DIB
                Buffer.MemoryCopy(srcPtr + srcRow, dstPtr + dstRow, w * 4, w * 4);
            }
        }
        src.UnlockBits(data);

        // Empty monochrome mask
        IntPtr hMask = CreateBitmap(w, h, 1, 1, IntPtr.Zero);

        ICONINFO ii = new ICONINFO
        {
            fIcon = true,
            xHotspot = 0,
            yHotspot = 0,
            hbmMask = hMask,
            hbmColor = hBitmap
        };

        IntPtr hIcon = CreateIconIndirect(ref ii);

        DeleteObject(hBitmap);
        DeleteObject(hMask);

        return hIcon;
    }

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool AllowSetForegroundWindow(int dwProcessId);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_SHOWWINDOW = 0x0040;
    public const int SW_RESTORE = 9;
    public const int SW_SHOW = 5;

    public static void ForceForegroundWindow(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return;

        try
        {
            ShowWindow(hWnd, SW_RESTORE);

            var foregroundHwnd = GetForegroundWindow();
            var curThreadId = GetCurrentThreadId();
            var fgThreadId = GetWindowThreadProcessId(foregroundHwnd, out _);

            if (curThreadId != fgThreadId && fgThreadId != 0)
            {
                AttachThreadInput(curThreadId, fgThreadId, true);
                SetWindowPos(hWnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
                BringWindowToTop(hWnd);
                SetForegroundWindow(hWnd);
                AttachThreadInput(curThreadId, fgThreadId, false);
            }
            else
            {
                SetWindowPos(hWnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
                BringWindowToTop(hWnd);
                SetForegroundWindow(hWnd);
            }
        }
        catch { }
    }

    public const int GWL_STYLE = -16;
    public const int GWL_EXSTYLE = -20;
    public const int WS_MINIMIZEBOX = 0x00020000;
    public const int WS_SYSMENU = 0x00080000;

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern IntPtr GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern IntPtr SetWindowLong32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    public static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
    {
        return IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : GetWindowLong32(hWnd, nIndex);
    }

    public const int GCLP_HICON = -14;
    public const int GCLP_HICONSM = -34;

    [DllImport("user32.dll", EntryPoint = "SetClassLong")]
    private static extern IntPtr SetClassLong32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetClassLongPtr")]
    private static extern IntPtr SetClassLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    public static IntPtr SetClassLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
    {
        return IntPtr.Size == 8 ? SetClassLongPtr64(hWnd, nIndex, dwNewLong) : SetClassLong32(hWnd, nIndex, dwNewLong);
    }

    public static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
    {
        return IntPtr.Size == 8 ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong) : SetWindowLong32(hWnd, nIndex, dwNewLong);
    }

    public static void EnableTaskbarMinimize(IntPtr hWnd)
    {
        try
        {
            var style = (long)GetWindowLongPtr(hWnd, GWL_STYLE);
            style |= WS_MINIMIZEBOX | WS_SYSMENU;
            SetWindowLongPtr(hWnd, GWL_STYLE, new IntPtr(style));
        }
        catch { }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int x;
        public int y;
    }

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    public static extern IntPtr WindowFromPoint(POINT point);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);

    public const int VK_LBUTTON = 0x01;
    public const int VK_RBUTTON = 0x02;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    public static bool IsRightMouseButtonDown() => (GetAsyncKeyState(VK_RBUTTON) & 0x8000) != 0;
    public static bool IsLeftMouseButtonDown() => (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0;

    public static bool IsCursorOverTaskbar()
    {
        try
        {
            if (GetCursorPos(out var pt))
            {
                var hwnd = WindowFromPoint(pt);
                if (hwnd != IntPtr.Zero)
                {
                    var sb = new StringBuilder(256);
                    GetClassName(hwnd, sb, sb.Capacity);
                    var cls = sb.ToString();
                    if (cls.Contains("Tray", StringComparison.OrdinalIgnoreCase) ||
                        cls.Contains("Taskbar", StringComparison.OrdinalIgnoreCase) ||
                        cls.Contains("Shell_TrayWnd", StringComparison.OrdinalIgnoreCase) ||
                        cls.Contains("Shell_SecondaryTrayWnd", StringComparison.OrdinalIgnoreCase) ||
                        cls.Contains("TaskList", StringComparison.OrdinalIgnoreCase) ||
                        cls.Contains("Windows.UI.Input.InputSite", StringComparison.OrdinalIgnoreCase) ||
                        cls.Contains("Windows.UI.Composition", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                // Check primary taskbar rect
                var trayHwnd = FindWindow("Shell_TrayWnd", null);
                if (trayHwnd != IntPtr.Zero)
                {
                    GetWindowRect(trayHwnd, out RECT rc);
                    if (pt.x >= rc.Left && pt.x <= rc.Right && pt.y >= rc.Top && pt.y <= rc.Bottom)
                    {
                        return true;
                    }
                }

                // Check secondary taskbar rect
                var secTrayHwnd = FindWindow("Shell_SecondaryTrayWnd", null);
                if (secTrayHwnd != IntPtr.Zero)
                {
                    GetWindowRect(secTrayHwnd, out RECT rc);
                    if (pt.x >= rc.Left && pt.x <= rc.Right && pt.y >= rc.Top && pt.y <= rc.Bottom)
                    {
                        return true;
                    }
                }
            }
        }
        catch { }
        return false;
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern uint RegisterWindowMessage(string lpString);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    public static readonly IntPtr HWND_BROADCAST = new IntPtr(0xFFFF);

    public static bool IsShellOrTaskbarWindow(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return false;

        try
        {
            GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid != 0)
            {
                using var proc = System.Diagnostics.Process.GetProcessById((int)pid);
                var procName = proc.ProcessName;
                if (procName.Equals("explorer", StringComparison.OrdinalIgnoreCase) ||
                    procName.Equals("ShellExperienceHost", StringComparison.OrdinalIgnoreCase) ||
                    procName.Equals("StartMenuExperienceHost", StringComparison.OrdinalIgnoreCase) ||
                    procName.Equals("SearchHost", StringComparison.OrdinalIgnoreCase) ||
                    procName.Equals("TextInputHost", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            var sb = new StringBuilder(256);
            GetClassName(hWnd, sb, sb.Capacity);
            var cls = sb.ToString();
            if (cls.Contains("Tray", StringComparison.OrdinalIgnoreCase) ||
                cls.Contains("Taskbar", StringComparison.OrdinalIgnoreCase) ||
                cls.Contains("Shell_TrayWnd", StringComparison.OrdinalIgnoreCase) ||
                cls.Contains("Shell_SecondaryTrayWnd", StringComparison.OrdinalIgnoreCase) ||
                cls.Contains("TaskList", StringComparison.OrdinalIgnoreCase) ||
                cls.Contains("Windows.UI.Input.InputSite", StringComparison.OrdinalIgnoreCase) ||
                cls.Contains("Windows.UI.Composition", StringComparison.OrdinalIgnoreCase) ||
                cls.Contains("XamlExplorerHostIslandWindow", StringComparison.OrdinalIgnoreCase) ||
                cls.Contains("TopLevelWindowForOverflowXamlIsland", StringComparison.OrdinalIgnoreCase) ||
                cls.Contains("NotifyIconOverflowWindow", StringComparison.OrdinalIgnoreCase) ||
                cls.Contains("ContextMenu", StringComparison.OrdinalIgnoreCase) ||
                cls.Contains("Base_Menu", StringComparison.OrdinalIgnoreCase) ||
                cls.Contains("#32768", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        catch { }
        return false;
    }

    #endregion

    #region Memory Management

    [DllImport("kernel32.dll")]
    public static extern bool SetProcessWorkingSetSize(IntPtr hProcess, IntPtr dwMinimumWorkingSetSize, IntPtr dwMaximumWorkingSetSize);

    public static void TrimWorkingSet()
    {
        try
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            using var currentProc = System.Diagnostics.Process.GetCurrentProcess();
            SetProcessWorkingSetSize(currentProc.Handle, -1, -1);
        }
        catch
        {
            // Ignore if permission denied
        }
    }

    #endregion

    #region Keyboard Hook

    public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    public const int WH_KEYBOARD_LL = 13;
    public const int WM_KEYDOWN = 0x0100;
    public const int WM_KEYUP = 0x0101;
    public const int WM_SYSKEYDOWN = 0x0104;
    public const int WM_SYSKEYUP = 0x0105;

    public const int VK_CONTROL = 0x11;
    public const int VK_LCONTROL = 0xA2;
    public const int VK_RCONTROL = 0xA3;

    [StructLayout(LayoutKind.Sequential)]
    public struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern IntPtr GetModuleHandle(string? lpModuleName);

    #endregion

    #region Shell & Ultra-HD Icon Extraction (IShellItemImageFactory)

    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(
            [In, MarshalAs(UnmanagedType.Struct)] SIZE size,
            [In] SIIGBF flags,
            [Out] out IntPtr phbm);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SIZE
    {
        public int cx;
        public int cy;
        public SIZE(int cx, int cy) { this.cx = cx; this.cy = cy; }
    }

    [Flags]
    public enum SIIGBF
    {
        SIIGBF_RESIZETOFIT = 0x00,
        SIIGBF_BIGGERSIZEOK = 0x01,
        SIIGBF_MEMORYONLY = 0x02,
        SIIGBF_ICONONLY = 0x04,
        SIIGBF_THUMBNAILONLY = 0x08,
        SIIGBF_INCACHEONLY = 0x10,
        SIIGBF_CROPTOSQUARE = 0x20,
        SIIGBF_WIDETHUMBNAILS = 0x40,
        SIIGBF_ICONBACKGROUND = 0x80,
        SIIGBF_SCALEUP = 0x100
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    public static extern void SHCreateItemFromParsingName(
        [In, MarshalAs(UnmanagedType.LPWStr)] string pszPath,
        [In] IntPtr pbc,
        [In, MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        [Out, MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

    public static readonly Guid IShellItemImageFactoryGuid = new("bcc18b79-ba16-442f-80c4-8a59c30c463b");

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    public const uint SHGFI_ICON = 0x000000100;
    public const uint SHGFI_LARGEICON = 0x000000000;
    public const uint SHGFI_SMALLICON = 0x000000001;

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    public static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll", EntryPoint = "PrivateExtractIconsW", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern uint PrivateExtractIcons(
        string lpszFile,
        int nIconIndex,
        int cxIcon,
        int cyIcon,
        IntPtr[] phicon,
        uint[] piconid,
        uint nIcons,
        uint flags);

    #endregion

    #region IShellLink for Shortcut (.lnk) resolution

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    internal class ShellLink
    {
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    internal interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, out WIN32_FIND_DATAW pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("0000010b-0000-0000-C000-000000000046")]
    internal interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        void IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        void GetCurFile([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder ppszFileName);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    internal interface IPropertyStore
    {
        void GetCount(out uint cProps);
        void GetAt(uint iProp, out PROPERTYKEY pkey);
        void GetValue(ref PROPERTYKEY key, out PROPVARIANT pv);
        void SetValue(ref PROPERTYKEY key, ref PROPVARIANT pv);
        void Commit();
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PROPERTYKEY
    {
        public Guid FormatId;
        public uint PropertyId;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct PROPVARIANT
    {
        [FieldOffset(0)] public ushort VariantType;
        [FieldOffset(2)] public ushort Reserved1;
        [FieldOffset(4)] public ushort Reserved2;
        [FieldOffset(6)] public ushort Reserved3;
        [FieldOffset(8)] public IntPtr Value;
    }

    internal static readonly PROPERTYKEY AppUserModelIdPropertyKey = new()
    {
        FormatId = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),
        PropertyId = 5
    };

    [DllImport("ole32.dll")]
    internal static extern int PropVariantClear(ref PROPVARIANT pvar);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct WIN32_FIND_DATAW
    {
        public uint dwFileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftLastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftLastWriteTime;
        public uint nFileSizeHigh;
        public uint nFileSizeLow;
        public uint dwReserved0;
        public uint dwReserved1;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string cFileName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
        public string cAlternateFileName;
    }

    public static string? ResolveShortcutTarget(string lnkPath)
    {
        IShellLinkW? link = null;
        try
        {
            if (!File.Exists(lnkPath)) return null;
            link = (IShellLinkW)new ShellLink();
            var persistFile = (IPersistFile)link;
            persistFile.Load(lnkPath, 0);

            var sb = new StringBuilder(260);
            link.GetPath(sb, sb.Capacity, out _, 0);
            var target = sb.ToString();

            if (!string.IsNullOrWhiteSpace(target) && (File.Exists(target) || Directory.Exists(target)))
            {
                return target;
            }
        }
        catch
        {
            // Ignore shortcut parse error
        }
        finally
        {
            if (link != null)
            {
                try { Marshal.ReleaseComObject(link); } catch { }
            }
        }
        return null;
    }

    #endregion
}
