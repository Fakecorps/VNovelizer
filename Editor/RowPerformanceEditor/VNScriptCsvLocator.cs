using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 剧本 CSV 定位器：行演出编辑器（选择剧本下拉 / 运行时跟随 / 参数候选）与 nextline 迁移工具
/// 的剧本枚举统一入口。
///
/// <para>【Fix-ScriptPicker】此前 5 处直接 <c>AssetDatabase.FindAssets("t:TextAsset")</c>
/// 全项目扫描 .csv——翻译表（{TranslationFolder} 下的语言文件夹与自建表）同为 TextAsset，
/// 全部混入「选择剧本」下拉（如 UI.csv、VN_Localization.csv），选中后解析不出剧本行，
/// 表现为"选择剧本坏了"。现改为：</para>
/// <list type="bullet">
/// <item>权威来源 = <see cref="VNProjectConfig"/>.CsvOutputFolder 顶层 *.csv（非递归，
///     与剧本管理器 / 剧情本地化管理器的剧本枚举同源）；</item>
/// <item>未配置输出目录时回退全项目扫描，但排除本地化相关目录（翻译表 + 聚合产物）。</item>
/// </list>
/// </summary>
public static class VNScriptCsvLocator
{
    /// <summary>剧本 CSV 输出目录（Assets 相对路径）；未配置返回 null。</summary>
    public static string GetCsvFolderAssetPath()
    {
        return VNProjectConfig.TryGetInstance(out var config) &&
               !string.IsNullOrEmpty(config.GetCsvOutputPath())
            ? config.GetCsvOutputPath()
            : null;
    }

    /// <summary>全部剧本 CSV 路径（Assets 相对路径，按剧本名排序）。</summary>
    public static List<string> GetAllCsvPaths()
    {
        var result = new List<string>();

        string folder = GetCsvFolderAssetPath();
        if (folder != null && Directory.Exists(Path.GetFullPath(folder)))
        {
            // 只认输出目录顶层（非递归）：翻译表即使放在其子文件夹也不会混入
            foreach (string file in Directory.GetFiles(
                         Path.GetFullPath(folder), "*.csv", SearchOption.TopDirectoryOnly))
            {
                result.Add(VNTranslationTablePaths.ToAssetsRelativePath(file) ?? file);
            }
        }
        else
        {
            // 未配置输出目录：全项目扫描兜底，排除翻译表/产物目录（防翻译表混入）
            foreach (string guid in AssetDatabase.FindAssets("t:TextAsset"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (IsLocalizationPath(path))
                    continue;
                result.Add(path);
            }
        }

        result.Sort((a, b) => string.CompareOrdinal(
            Path.GetFileNameWithoutExtension(a), Path.GetFileNameWithoutExtension(b)));
        return result;
    }

    /// <summary>剧本名列表（去重、排序）；无剧本返回 null（候选下拉语义）。</summary>
    public static List<string> GetScriptNames()
    {
        var names = new List<string>();
        foreach (string path in GetAllCsvPaths())
        {
            string name = Path.GetFileNameWithoutExtension(path);
            if (!string.IsNullOrEmpty(name) && !names.Contains(name))
                names.Add(name);
        }
        return names.Count > 0 ? names : null;
    }

    /// <summary>按剧本名（不含扩展名）定位剧本 CSV（Assets 相对路径）；找不到返回 null。</summary>
    public static string FindCsvPath(string scriptName)
    {
        if (string.IsNullOrEmpty(scriptName))
            return null;

        string folder = GetCsvFolderAssetPath();
        if (folder != null)
        {
            string direct = $"{folder.TrimEnd('/')}/{scriptName}.csv";
            return File.Exists(Path.GetFullPath(direct)) ? direct : null;
        }

        // 未配置输出目录：全项目兜底（排除翻译表/产物目录）
        foreach (string guid in AssetDatabase.FindAssets("t:TextAsset"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                continue;
            if (IsLocalizationPath(path))
                continue;
            if (string.Equals(Path.GetFileNameWithoutExtension(path), scriptName, StringComparison.Ordinal))
                return path;
        }
        return null;
    }

    /// <summary>是否本地化相关 CSV（翻译表目录 / 聚合产物目录），不可作为剧本。</summary>
    private static bool IsLocalizationPath(string assetPath)
    {
        if (string.IsNullOrEmpty(assetPath))
            return false;
        string normalized = assetPath.Replace('\\', '/');

        if (normalized.StartsWith(VNTranslationAggregator.GeneratedFolder + "/",
                StringComparison.OrdinalIgnoreCase))
            return true;

        return VNProjectConfig.TryGetInstance(out var config) &&
               !string.IsNullOrEmpty(config.TranslationFolder) &&
               normalized.StartsWith(config.GetTranslationFolderPath() + "/",
                   StringComparison.OrdinalIgnoreCase);
    }
}
