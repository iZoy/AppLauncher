using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using AppLauncher.Services;

namespace AppLauncher.Models;

public class LauncherConfig
{
    public const int DefaultBackgroundOpacityPercent = 52;
    public const int DefaultBackgroundGrayTone = 18;
    public const int DefaultBlurIntensity = 2;

    public List<string> CustomApps { get; set; } = new();
    public HashSet<string> HiddenPaths { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> PinnedPaths { get; set; } = new();
    public Dictionary<string, string> CustomNames { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> CustomScanDirectories { get; set; } = new();
    public int GridColumns { get; set; } = 5;
    public string Language { get; set; } = "zh-CN";
    public int BackgroundOpacityPercent { get; set; } = DefaultBackgroundOpacityPercent;
    public int BackgroundGrayTone { get; set; } = DefaultBackgroundGrayTone;
    public int BlurIntensity { get; set; } = DefaultBlurIntensity;

    private static readonly string AppDataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AppLauncher");

    private static readonly string ConfigFilePath = Path.Combine(AppDataFolder, "config.json");
    private static readonly object SaveLock = new();

    public static LauncherConfig Load()
    {
        try
        {
            if (File.Exists(ConfigFilePath))
            {
                var json = File.ReadAllText(ConfigFilePath);
                var config = JsonSerializer.Deserialize<LauncherConfig>(json);
                if (config != null)
                {
                    // JsonSerializer does not preserve collection comparers.
                    config.CustomApps ??= new List<string>();
                    config.HiddenPaths = new HashSet<string>(
                        config.HiddenPaths ?? new HashSet<string>(),
                        StringComparer.OrdinalIgnoreCase);
                    config.PinnedPaths ??= new List<string>();
                    config.CustomNames = new Dictionary<string, string>(
                        config.CustomNames ?? new Dictionary<string, string>(),
                        StringComparer.OrdinalIgnoreCase);
                    config.CustomScanDirectories ??= new List<string>();
                    return config;
                }
            }
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write("Config.Load", ex);
            // Fallback to default
        }
        return new LauncherConfig();
    }

    public void Save()
    {
        lock (SaveLock)
        {
            string? tempPath = null;
            try
            {
                if (!Directory.Exists(AppDataFolder))
                {
                    Directory.CreateDirectory(AppDataFolder);
                }

                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(this, options);
                tempPath = ConfigFilePath + ".tmp";
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, ConfigFilePath, true);
                tempPath = null;
            }
            catch (Exception ex)
            {
                DiagnosticLog.Write("Config.Save", ex);
                // Ignore write errors
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
}
