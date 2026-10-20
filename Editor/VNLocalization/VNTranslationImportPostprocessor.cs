using System.IO;
using System.Linq;
using UnityEditor;

/// <summary>
/// 【本地化重构 P1】翻译表全自动导入：监听翻译表目录与剧本 CSV 目录的 .csv 变化，
/// 变化后防抖触发 <see cref="VNTranslationAggregator.Aggregate"/>（保存即聚合，开发期零操作）。
///
/// <para>防重入与防抖：</para>
/// <list type="bullet">
/// <item>聚合写出的产物在 Generated 目录，不在监听目录内 → 不触发自身再聚合；</item>
/// <item>聚合器内部另有 _aggregating 标志兜底；</item>
/// <item>0.5s 防抖合并批量导入（一次保存触发多次 postprocess）。</item>
/// </list>
/// </summary>
public class VNTranslationImportPostprocessor : AssetPostprocessor
{
    private const double DebounceSeconds = 0.5;

    private static bool _scheduled;
    private static double _pendingTime;

    private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets,
        string[] movedAssets, string[] movedFromAssetPaths)
    {
        if (!VNProjectConfig.TryGetInstance(out var config))
            return;
        if (!config.EnableLocalization)
            return;

        string translationFolder = config.GetTranslationFolderPath().TrimEnd('/', '\\');
        string csvFolder = (config.GetCsvOutputPath() ?? "").TrimEnd('/', '\\');

        var all = importedAssets
            .Concat(deletedAssets ?? new string[0])
            .Concat(movedAssets ?? new string[0]);

        bool relevant = false;
        foreach (var path in all)
        {
            if (path == null || !path.EndsWith(".csv", System.StringComparison.OrdinalIgnoreCase))
                continue;

            if ((translationFolder.Length > 0 &&
                 path.StartsWith(translationFolder, System.StringComparison.OrdinalIgnoreCase)) ||
                (csvFolder.Length > 0 &&
                 path.StartsWith(csvFolder, System.StringComparison.OrdinalIgnoreCase)))
            {
                relevant = true;
                break;
            }
        }

        if (!relevant)
            return;

        ScheduleAggregate();
    }

    private static void ScheduleAggregate()
    {
        _pendingTime = EditorApplication.timeSinceStartup + DebounceSeconds;
        if (_scheduled)
            return;

        _scheduled = true;
        EditorApplication.update += DebouncedTick;
    }

    private static void DebouncedTick()
    {
        if (EditorApplication.timeSinceStartup < _pendingTime)
            return;

        _scheduled = false;
        EditorApplication.update -= DebouncedTick;
        VNTranslationAggregator.Aggregate();
    }

    /// <summary>手动导入入口（菜单入口统一在 VNLocalizationMenu，未启用本地化时灰显）。</summary>
    public static void ImportNow()
    {
        var result = VNTranslationAggregator.Aggregate();
        if (result == null)
            return;

        string message = result.Success
            ? $"导入完成。冲突 {result.Conflicts.Count} 处。"
            : "导入失败。";
        if (result.Warnings.Count > 0)
        {
            message += "\n\n警告：\n" + string.Join("\n", result.Warnings.Take(5));
            if (result.Warnings.Count > 5)
                message += $"\n…（共 {result.Warnings.Count} 条，详见 Console）";
        }
        EditorUtility.DisplayDialog("导入翻译表", message, "确定");
    }
}
