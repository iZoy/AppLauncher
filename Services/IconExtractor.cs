using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace AppLauncher.Services;

public static class IconExtractor
{
    private static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AppLauncher",
        "icons_clean");

    static IconExtractor()
    {
        try
        {
            if (!Directory.Exists(CacheDir))
            {
                Directory.CreateDirectory(CacheDir);
            }
        }
        catch
        {
            // Ignore
        }
    }

    public static string? GetOrExtractIcon(string rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
            return null;

        // Resolve shortcut to real exe if lnk passed, to eliminate shortcut overlay arrow
        var targetPath = rawPath;
        if (rawPath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            var resolved = NativeMethods.ResolveShortcutTarget(rawPath);
            if (!string.IsNullOrEmpty(resolved) && File.Exists(resolved))
            {
                targetPath = resolved;
            }
        }

        if (!File.Exists(targetPath))
            return null;

        try
        {
            var fileInfo = new FileInfo(targetPath);
            var cacheKey = $"{targetPath.ToLowerInvariant()}|{fileInfo.Length}|{fileInfo.LastWriteTimeUtc.Ticks}";
            var hash = ComputeHash(cacheKey);
            var cachedIconPath = Path.Combine(CacheDir, $"{hash}.png");

            if (File.Exists(cachedIconPath))
            {
                return cachedIconPath;
            }

            using var bmp = ExtractCleanBitmap(targetPath);
            if (bmp != null)
            {
                bmp.Save(cachedIconPath, ImageFormat.Png);
                return cachedIconPath;
            }
        }
        catch
        {
            // Ignore icon extraction errors
        }

        return null;
    }

    private static Bitmap? ExtractCleanBitmap(string exePath)
    {
        // 1. Primary Method: PrivateExtractIconsW directly from the EXE binary resource (Guaranteed NO shortcut arrow!)
        try
        {
            IntPtr[] phicon = new IntPtr[1];
            uint[] piconid = new uint[1];

            // Request 128x128 high-res icon from PE resource
            uint count = NativeMethods.PrivateExtractIcons(exePath, 0, 128, 128, phicon, piconid, 1, 0);
            if (count > 0 && phicon[0] != IntPtr.Zero)
            {
                using var icon = Icon.FromHandle(phicon[0]);
                var bmp = icon.ToBitmap();
                NativeMethods.DestroyIcon(phicon[0]);
                return bmp;
            }
        }
        catch
        {
            // Fallback
        }

        // 2. Secondary Method: IShellItemImageFactory on the EXE file
        NativeMethods.IShellItemImageFactory? imageFactory = null;
        try
        {
            NativeMethods.SHCreateItemFromParsingName(
                exePath,
                IntPtr.Zero,
                NativeMethods.IShellItemImageFactoryGuid,
                out imageFactory);

            if (imageFactory != null)
            {
                var size = new NativeMethods.SIZE(128, 128);
                var hr = imageFactory.GetImage(
                    size,
                    NativeMethods.SIIGBF.SIIGBF_RESIZETOFIT | NativeMethods.SIIGBF.SIIGBF_BIGGERSIZEOK | NativeMethods.SIIGBF.SIIGBF_ICONONLY,
                    out var hBitmap);

                if (hr == 0 && hBitmap != IntPtr.Zero)
                {
                    try
                    {
                        var bmp = Image.FromHbitmap(hBitmap);
                        var clonedBmp = new Bitmap(bmp);
                        bmp.Dispose();
                        return clonedBmp;
                    }
                    finally
                    {
                        NativeMethods.DeleteObject(hBitmap);
                    }
                }
            }
        }
        catch
        {
            // Fallback
        }
        finally
        {
            if (imageFactory != null)
            {
                try { Marshal.ReleaseComObject(imageFactory); } catch { }
            }
        }

        // 3. Tertiary Method: SHGetFileInfo Large Icon
        try
        {
            var shinfo = new NativeMethods.SHFILEINFO();
            var res = NativeMethods.SHGetFileInfo(
                exePath,
                0,
                ref shinfo,
                (uint)Marshal.SizeOf(shinfo),
                NativeMethods.SHGFI_ICON | NativeMethods.SHGFI_LARGEICON);

            if (res != IntPtr.Zero && shinfo.hIcon != IntPtr.Zero)
            {
                using var icon = Icon.FromHandle(shinfo.hIcon);
                var bmp = icon.ToBitmap();
                NativeMethods.DestroyIcon(shinfo.hIcon);
                return bmp;
            }
        }
        catch
        {
            // Fallback
        }

        // 4. Fallback: System.Drawing ExtractAssociatedIcon
        try
        {
            using var icon = Icon.ExtractAssociatedIcon(exePath);
            if (icon != null)
            {
                return icon.ToBitmap();
            }
        }
        catch
        {
            // Fallback
        }

        return null;
    }

    private static string ComputeHash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }
}
