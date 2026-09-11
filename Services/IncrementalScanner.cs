using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AppLauncher.Models;
using Microsoft.Win32;

namespace AppLauncher.Services;

public class IncrementalScanner
{
    private static readonly string AppDataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AppLauncher");

    private static readonly string CacheFilePath = Path.Combine(AppDataFolder, "app_cache.json");

    private readonly ConcurrentDictionary<string, AppItem> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _cacheSaveLock = new();

    public IncrementalScanner()
    {
        LoadCache();
    }

    private void LoadCache()
    {
        try
        {
            if (File.Exists(CacheFilePath))
            {
                var json = File.ReadAllText(CacheFilePath);
                var items = JsonSerializer.Deserialize<List<AppItem>>(json);
                if (items != null)
                {
                    foreach (var item in items)
                    {
                        if (!string.IsNullOrEmpty(item.ExePath))
                        {
                            _cache[item.ExePath] = item;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write("Scanner.LoadCache", ex);
            // Ignore corrupted cache
        }
    }

    public void SaveCache()
    {
        lock (_cacheSaveLock)
        {
            string? tempPath = null;
            try
            {
                if (!Directory.Exists(AppDataFolder))
                {
                    Directory.CreateDirectory(AppDataFolder);
                }

                var items = _cache.Values.ToList();
                var json = JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = false });
                tempPath = CacheFilePath + ".tmp";
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, CacheFilePath, true);
                tempPath = null;
            }
            catch (Exception ex)
            {
                DiagnosticLog.Write("Scanner.SaveCache", ex);
                // Ignore save errors
            }
            finally
            {
                if (tempPath != null)
                {
                    try { File.Delete(tempPath); } catch { }
                }
            }
        }
    }

    /// <summary>
    /// Instant synchronous retrieval of existing cached apps (0ms cold start for UI)
    /// </summary>
    public List<AppItem> GetCachedAppsFast(LauncherConfig config)
    {
        var rawList = new List<AppItem>();
        var seenTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in _cache.Values)
        {
            if (config.HiddenPaths != null && config.HiddenPaths.Contains(item.ExePath))
                continue;

            if (config.CustomNames != null && config.CustomNames.TryGetValue(item.ExePath, out var customName))
                item.CustomName = customName;

            var pinIndex = config.PinnedPaths != null ? config.PinnedPaths.IndexOf(item.ExePath) : -1;
            item.IsPinned = pinIndex >= 0;
            item.PinOrder = pinIndex >= 0 ? pinIndex : int.MaxValue;

            var isUserAdded = config.CustomApps != null && config.CustomApps.Contains(item.ExePath, StringComparer.OrdinalIgnoreCase);
            if (item.Status == AppStatus.Active || isUserAdded)
            {
                if (seenPaths.Add(item.ExePath))
                {
                    if (isUserAdded || seenTitles.Add(item.DisplayName))
                    {
                        rawList.Add(item);
                    }
                }
            }
        }

        return rawList
            .OrderByDescending(x => x.IsPinned)
            .ThenBy(x => x.PinOrder)
            .ThenByDescending(x => x.LaunchCount)
            .ThenBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private record DiscoveredCandidate(string ExePath, string? ShortcutTitle, string Source);

    public async Task<List<AppItem>> ScanAsync(LauncherConfig config, bool forceFullScan = false)
    {
        return await Task.Run(() =>
        {
            var candidateMap = new ConcurrentDictionary<string, DiscoveredCandidate>(StringComparer.OrdinalIgnoreCase);

            // Parallel multi-source discovery (3x faster than sequential)
            Parallel.Invoke(
                () => ScanStartMenu(candidateMap),
                () => ScanDesktop(candidateMap),
                () => ScanLocalProgramsFolder(candidateMap),
                () => ScanRegistryAppPaths(candidateMap),
                () => ScanMuiCache(candidateMap),
                () =>
                {
                    if (config.CustomScanDirectories != null)
                    {
                        foreach (var dir in config.CustomScanDirectories)
                        {
                            ScanCustomDirectory(dir, candidateMap);
                        }
                    }
                },
                () =>
                {
                    if (config.CustomApps != null)
                    {
                        foreach (var appPath in config.CustomApps)
                        {
                            if (File.Exists(appPath))
                            {
                                var fullPath = Path.GetFullPath(appPath);
                                candidateMap[fullPath] = new DiscoveredCandidate(
                                    fullPath,
                                    Path.GetFileNameWithoutExtension(fullPath),
                                    "UserAdded");
                            }
                        }
                    }
                }
            );

            // Incremental Evaluation
            var updated = false;

            Parallel.ForEach(candidateMap.Values, candidate =>
            {
                try
                {
                    var fullPath = candidate.ExePath;
                    var fileInfo = new FileInfo(fullPath);
                    if (!fileInfo.Exists) return;

                    var lastWriteTicks = fileInfo.LastWriteTimeUtc.Ticks;
                    var fileSize = fileInfo.Length;
                    _cache.TryGetValue(fullPath, out var previousItem);

                    // If already processed and file timestamp unchanged, skip heavy processing!
                    if (!forceFullScan && previousItem != null)
                    {
                        if (previousItem.LastWriteTimeUtcTicks == lastWriteTicks && previousItem.FileSize == fileSize)
                        {
                            if (!string.IsNullOrEmpty(candidate.ShortcutTitle) && 
                                previousItem.Name == Path.GetFileNameWithoutExtension(fullPath))
                            {
                                previousItem.Name = candidate.ShortcutTitle;
                            }
                            return;
                        }
                    }

                    // Newly found or modified file: run SmartFilter and metadata extraction
                    updated = true;
                    var isUserAdded = string.Equals(candidate.Source, "UserAdded", StringComparison.OrdinalIgnoreCase);
                    var isHiddenByUser = config.HiddenPaths != null && config.HiddenPaths.Contains(fullPath);

                    AppItem item;
                    if (isHiddenByUser)
                    {
                        item = new AppItem
                        {
                            Id = fullPath,
                            ExePath = fullPath,
                            Name = SmartFilter.ExtractBestDisplayName(fullPath, candidate.ShortcutTitle),
                            LastWriteTimeUtcTicks = lastWriteTicks,
                            FileSize = fileSize,
                            Status = AppStatus.Hidden,
                            Source = candidate.Source,
                            LaunchCount = previousItem?.LaunchCount ?? 0,
                            LastLaunchedUtc = previousItem?.LastLaunchedUtc,
                            CustomName = previousItem?.CustomName
                        };
                    }
                    else if (!isUserAdded && SmartFilter.ShouldExcludeShortcut(candidate.ShortcutTitle ?? string.Empty, fullPath))
                    {
                        item = new AppItem
                        {
                            Id = fullPath,
                            ExePath = fullPath,
                            Name = Path.GetFileNameWithoutExtension(fullPath),
                            LastWriteTimeUtcTicks = lastWriteTicks,
                            FileSize = fileSize,
                            Status = AppStatus.Blacklisted,
                            Source = candidate.Source,
                            LaunchCount = previousItem?.LaunchCount ?? 0,
                            LastLaunchedUtc = previousItem?.LastLaunchedUtc,
                            CustomName = previousItem?.CustomName
                        };
                    }
                    else
                    {
                        var displayName = SmartFilter.ExtractBestDisplayName(fullPath, candidate.ShortcutTitle);
                        var iconPath = IconExtractor.GetOrExtractIcon(fullPath);

                        item = new AppItem
                        {
                            Id = fullPath,
                            ExePath = fullPath,
                            Name = displayName,
                            IconCachePath = iconPath,
                            LastWriteTimeUtcTicks = lastWriteTicks,
                            FileSize = fileSize,
                            Status = AppStatus.Active,
                            Source = candidate.Source,
                            LaunchCount = previousItem?.LaunchCount ?? 0,
                            LastLaunchedUtc = previousItem?.LastLaunchedUtc,
                            CustomName = previousItem?.CustomName
                        };
                    }

                    _cache[fullPath] = item;
                }
                catch (Exception ex)
                {
                    DiagnosticLog.Write($"Scanner.Evaluate:{candidate.ExePath}", ex);
                    // Ignore per-file exceptions
                }
            });

            // Reconcile stale entries only during an explicit full refresh.
            // Incremental scans tolerate a temporarily unavailable source.
            if (forceFullScan)
            {
                var discoveredPaths = candidateMap.Keys;
                var existingKeys = _cache.Keys.ToList();
                foreach (var key in existingKeys)
                {
                    if (!discoveredPaths.Contains(key, StringComparer.OrdinalIgnoreCase))
                    {
                        _cache.TryRemove(key, out _);
                        updated = true;
                    }
                }
            }

            if (updated)
            {
                SaveCache();
            }

            return GetCachedAppsFast(config);
        });
    }

    public void AddCustomApp(string path, LauncherConfig config)
    {
        if (!File.Exists(path)) return;
        var fullPath = Path.GetFullPath(path);

        if (config.CustomApps == null) config.CustomApps = new List<string>();
        if (!config.CustomApps.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
        {
            config.CustomApps.Add(fullPath);
        }
        config.HiddenPaths?.Remove(fullPath);
        config.Save();

        var fi = new FileInfo(fullPath);
        var item = new AppItem
        {
            Id = fullPath,
            ExePath = fullPath,
            Name = SmartFilter.ExtractBestDisplayName(fullPath),
            IconCachePath = IconExtractor.GetOrExtractIcon(fullPath),
            LastWriteTimeUtcTicks = fi.LastWriteTimeUtc.Ticks,
            FileSize = fi.Length,
            Status = AppStatus.Active,
            Source = "UserAdded"
        };

        _cache[fullPath] = item;
        SaveCache();
    }

    public void AddCustomScanDirectory(string dirPath, LauncherConfig config)
    {
        if (!Directory.Exists(dirPath)) return;
        var fullDir = Path.GetFullPath(dirPath);

        if (config.CustomScanDirectories == null) config.CustomScanDirectories = new List<string>();
        if (!config.CustomScanDirectories.Contains(fullDir, StringComparer.OrdinalIgnoreCase))
        {
            config.CustomScanDirectories.Add(fullDir);
            config.Save();
        }
    }

    public void HideApp(string path, LauncherConfig config)
    {
        var fullPath = Path.GetFullPath(path);
        config.HiddenPaths?.Add(fullPath);
        config.Save();

        if (_cache.TryGetValue(fullPath, out var item))
        {
            item.Status = AppStatus.Hidden;
            SaveCache();
        }
    }

    public void PinApp(string path, LauncherConfig config, bool pin)
    {
        var fullPath = Path.GetFullPath(path);
        if (pin)
        {
            if (config.PinnedPaths != null && !config.PinnedPaths.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
            {
                config.PinnedPaths.Insert(0, fullPath);
            }
        }
        else
        {
            config.PinnedPaths?.RemoveAll(p => string.Equals(p, fullPath, StringComparison.OrdinalIgnoreCase));
        }
        config.Save();
    }

    public void RenameApp(string path, string newName, LauncherConfig config)
    {
        var fullPath = Path.GetFullPath(path);
        if (string.IsNullOrWhiteSpace(newName))
        {
            config.CustomNames?.Remove(fullPath);
        }
        else
        {
            if (config.CustomNames != null) config.CustomNames[fullPath] = newName.Trim();
        }
        config.Save();

        if (_cache.TryGetValue(fullPath, out var item))
        {
            item.CustomName = string.IsNullOrWhiteSpace(newName) ? null : newName.Trim();
        }
    }

    #region Discovery Helpers

    private void ScanStartMenu(ConcurrentDictionary<string, DiscoveredCandidate> candidateMap)
    {
        var startMenuFolders = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs")
        };

        foreach (var folder in startMenuFolders)
        {
            if (!Directory.Exists(folder)) continue;
            try
            {
                var lnkFiles = Directory.EnumerateFiles(folder, "*.lnk", SearchOption.AllDirectories);
                foreach (var lnk in lnkFiles)
                {
                    var shortcutName = Path.GetFileNameWithoutExtension(lnk);
                    var target = NativeMethods.ResolveShortcutTarget(lnk);

                    if (!string.IsNullOrEmpty(target) && target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(target))
                    {
                        var fullTarget = Path.GetFullPath(target);
                        if (!SmartFilter.ShouldExcludeShortcut(shortcutName, fullTarget))
                        {
                            candidateMap[fullTarget] = new DiscoveredCandidate(fullTarget, shortcutName, "StartMenu");
                        }
                    }
                }
            }
            catch { }
        }
    }

    private void ScanDesktop(ConcurrentDictionary<string, DiscoveredCandidate> candidateMap)
    {
        var desktopFolders = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
        };

        foreach (var folder in desktopFolders)
        {
            if (!Directory.Exists(folder)) continue;
            try
            {
                var files = Directory.EnumerateFiles(folder, "*.*", SearchOption.TopDirectoryOnly);
                foreach (var file in files)
                {
                    var ext = Path.GetExtension(file);
                    if (string.Equals(ext, ".lnk", StringComparison.OrdinalIgnoreCase))
                    {
                        var shortcutName = Path.GetFileNameWithoutExtension(file);
                        var target = NativeMethods.ResolveShortcutTarget(file);
                        if (!string.IsNullOrEmpty(target) && target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(target))
                        {
                            var fullTarget = Path.GetFullPath(target);
                            if (!SmartFilter.ShouldExcludeShortcut(shortcutName, fullTarget))
                            {
                                candidateMap[fullTarget] = new DiscoveredCandidate(fullTarget, shortcutName, "Desktop");
                            }
                        }
                    }
                    else if (string.Equals(ext, ".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        var fullExe = Path.GetFullPath(file);
                        if (!SmartFilter.ShouldExcludePath(fullExe))
                        {
                            candidateMap[fullExe] = new DiscoveredCandidate(fullExe, Path.GetFileNameWithoutExtension(fullExe), "Desktop");
                        }
                    }
                }
            }
            catch { }
        }
    }

    private void ScanLocalProgramsFolder(ConcurrentDictionary<string, DiscoveredCandidate> candidateMap)
    {
        var localPrograms = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs");

        if (!Directory.Exists(localPrograms)) return;

        try
        {
            var topDirs = Directory.EnumerateDirectories(localPrograms);
            foreach (var appDir in topDirs)
            {
                var exes = Directory.EnumerateFiles(appDir, "*.exe", SearchOption.TopDirectoryOnly);
                foreach (var exe in exes)
                {
                    var fullExe = Path.GetFullPath(exe);
                    if (!SmartFilter.ShouldExcludePath(fullExe))
                    {
                        candidateMap[fullExe] = new DiscoveredCandidate(fullExe, Path.GetFileNameWithoutExtension(fullExe), "LocalPrograms");
                    }
                }
            }
        }
        catch { }
    }

    private void ScanRegistryAppPaths(ConcurrentDictionary<string, DiscoveredCandidate> candidateMap)
    {
        ScanAppPathsInKey(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths", candidateMap);
        ScanAppPathsInKey(Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths", candidateMap);
    }

    private void ScanAppPathsInKey(RegistryKey root, string subKeyPath, ConcurrentDictionary<string, DiscoveredCandidate> candidateMap)
    {
        try
        {
            using var key = root.OpenSubKey(subKeyPath);
            if (key == null) return;

            foreach (var subName in key.GetSubKeyNames())
            {
                try
                {
                    using var subKey = key.OpenSubKey(subName);
                    var defaultVal = subKey?.GetValue(null)?.ToString();
                    if (!string.IsNullOrWhiteSpace(defaultVal))
                    {
                        var cleanPath = defaultVal.Trim('\"', ' ');
                        if (cleanPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(cleanPath))
                        {
                            var fullPath = Path.GetFullPath(cleanPath);
                            if (!SmartFilter.ShouldExcludePath(fullPath))
                            {
                                candidateMap.TryAdd(fullPath, new DiscoveredCandidate(fullPath, Path.GetFileNameWithoutExtension(fullPath), "AppPaths"));
                            }
                        }
                    }
                }
                catch { }
            }
        }
        catch { }
    }

    private void ScanMuiCache(ConcurrentDictionary<string, DiscoveredCandidate> candidateMap)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache");
            if (key == null) return;

            foreach (var valName in key.GetValueNames())
            {
                if (valName.EndsWith(".ApplicationCompany", StringComparison.OrdinalIgnoreCase) ||
                    valName.EndsWith(".FriendlyAppName", StringComparison.OrdinalIgnoreCase))
                    continue;

                var cleanPath = valName.Trim('\"', ' ');
                if (cleanPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(cleanPath))
                {
                    var fullPath = Path.GetFullPath(cleanPath);
                    if (!SmartFilter.ShouldExcludePath(fullPath))
                    {
                        var friendlyName = key.GetValue(valName)?.ToString();
                        candidateMap.TryAdd(fullPath, new DiscoveredCandidate(
                            fullPath,
                            !string.IsNullOrWhiteSpace(friendlyName) ? friendlyName : Path.GetFileNameWithoutExtension(fullPath),
                            "MuiCache"));
                    }
                }
            }
        }
        catch { }
    }

    private void ScanCustomDirectory(string dirPath, ConcurrentDictionary<string, DiscoveredCandidate> candidateMap)
    {
        if (!Directory.Exists(dirPath)) return;
        try
        {
            var exes = Directory.EnumerateFiles(dirPath, "*.exe", SearchOption.AllDirectories);
            foreach (var exe in exes)
            {
                var fullExe = Path.GetFullPath(exe);
                if (!SmartFilter.ShouldExcludePath(fullExe))
                {
                    candidateMap[fullExe] = new DiscoveredCandidate(fullExe, Path.GetFileNameWithoutExtension(fullExe), "ScanDir");
                }
            }
        }
        catch { }
    }

    #endregion
}
