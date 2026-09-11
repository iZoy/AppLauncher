using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AppLauncher.Models;
using AppLauncher.Services;

namespace AppLauncher;

public class ProgramTest
{
    public static async Task RunTest()
    {
        Console.WriteLine("=== 正在运行 AppLauncher 增量扫描测试 ===");

        var config = new LauncherConfig();
        var scanner = new IncrementalScanner();

        var sw = Stopwatch.StartNew();
        var apps = await scanner.ScanAsync(config, true);
        sw.Stop();

        Console.WriteLine($"[首次全量扫描] 耗时: {sw.ElapsedMilliseconds} ms, 成功索引有效应用数: {apps.Count}");

        Console.WriteLine("\n[已发现的应用示例（前 15 个）]:");
        foreach (var app in apps.Take(15))
        {
            Console.WriteLine($"  • {app.DisplayName} (Icon: {(File.Exists(app.IconCachePath ?? "") ? "OK" : "None")}) -> {app.ExePath}");
        }

        // Test incremental scan
        sw.Restart();
        var incrementalApps = await scanner.ScanAsync(config, false);
        sw.Stop();

        Console.WriteLine($"\n[第二次增量扫描] 耗时: {sw.ElapsedMilliseconds} ms (几乎瞬间完成!)");

        // Verify no uninstall/update garbage
        var garbage = apps.Where(a => a.DisplayName.Contains("uninstall", StringComparison.OrdinalIgnoreCase) ||
                                      a.DisplayName.Contains("unins000", StringComparison.OrdinalIgnoreCase) ||
                                      a.DisplayName.Contains("update", StringComparison.OrdinalIgnoreCase)).ToList();

        Console.WriteLine($"\n[垃圾过滤检验] 含有 uninstall/update 关键词的异常项数量: {garbage.Count}");
        if (garbage.Count > 0)
        {
            foreach (var g in garbage)
            {
                Console.WriteLine($"    ! 误报: {g.DisplayName} -> {g.ExePath}");
            }
        }
        else
        {
            Console.WriteLine("  ✓ 智能降噪完美通过，未发现任何卸载或更新残留程序！");
        }

        Console.WriteLine("\n=== 测试完成 ===");
    }
}
