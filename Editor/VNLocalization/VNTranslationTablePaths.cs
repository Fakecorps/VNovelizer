using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 【按语言文件夹分类】翻译表路径解析与迁移工具。
///
/// <para>新结构（语言文件夹，2026-10 起）：</para>
/// <list type="bullet">
/// <item>剧本表：<c>{TranslationFolder}/{locale}/{script}.csv</c>（去掉语言后缀，语言由文件夹表达）；</item>
/// <item>UI 表：<c>{TranslationFolder}/{locale}/UI.csv</c>。</item>
/// </list>
///
/// <para>旧扁平结构（兼容读取，聚合不受影响）：</para>
/// <list type="bullet">
/// <item>剧本表：<c>{TranslationFolder}/{script}_{locale}.csv</c>；</item>
/// <item>UI 表：<c>{TranslationFolder}/UI_Locale_{locale}.csv</c>。</item>
/// </list>
///
/// <para>聚合器递归扫描翻译表目录、按 CSV 第 3 列表头识别语言（与文件名/路径无关），
/// 新旧结构可共存过渡；生成器写新路径、读旧路径兜底（译文永不丢）。</para>
/// </summary>
public static class VNTranslationTablePaths
{
    /// <summary>语言文件夹内 UI 表文件名（不含扩展名）。</summary>
    public const string UiTableName = "UI";

    /// <summary>旧扁平结构 UI 表文件名前缀：UI_Locale_{locale}.csv。</summary>
    public const string UiLegacyPrefix = "UI_Locale";

    // ==================== 路径生成 ====================

    /// <summary>语言文件夹名（= locale 代码，清理文件系统非法字符）。</summary>
    public static string SanitizeLocaleFolderName(string locale)
    {
        string name = (locale ?? "").Trim();
        if (name.Length == 0)
            return "unknown";
        char[] invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
            sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        return sb.ToString();
    }

    /// <summary>新结构剧本表路径（Assets 相对）：{TranslationFolder}/{locale}/{script}.csv。</summary>
    public static string GetTableAssetPath(VNProjectConfig config, string scriptName, string locale)
    {
        return $"{config.GetTranslationFolderPath()}/{SanitizeLocaleFolderName(locale)}/{scriptName}.csv";
    }

    /// <summary>旧扁平剧本表路径（Assets 相对）：{TranslationFolder}/{script}_{locale}.csv。</summary>
    public static string GetLegacyTableAssetPath(VNProjectConfig config, string scriptName, string locale)
    {
        return $"{config.GetTranslationFolderPath()}/{scriptName}_{locale}.csv";
    }

    /// <summary>新结构 UI 表路径（Assets 相对）：{TranslationFolder}/{locale}/UI.csv。</summary>
    public static string GetUiTableAssetPath(VNProjectConfig config, string locale)
    {
        return $"{config.GetTranslationFolderPath()}/{SanitizeLocaleFolderName(locale)}/{UiTableName}.csv";
    }

    /// <summary>旧扁平 UI 表路径（Assets 相对）：{TranslationFolder}/UI_Locale_{locale}.csv。</summary>
    public static string GetLegacyUiTableAssetPath(VNProjectConfig config, string locale)
    {
        return $"{config.GetTranslationFolderPath()}/{UiLegacyPrefix}_{locale}.csv";
    }

    /// <summary>语言文件夹完整文件系统路径。</summary>
    public static string GetLocaleFolderFullPath(VNProjectConfig config, string locale)
    {
        return Path.GetFullPath(Path.Combine(config.GetTranslationFolderPath(), SanitizeLocaleFolderName(locale)));
    }

    // ==================== 存在性查找（新路径优先，旧扁平兜底） ====================

    /// <summary>查找剧本表实际所在 Assets 相对路径（新结构优先，旧扁平兜底）。都不存在返回 null。</summary>
    public static string FindTableAssetPath(VNProjectConfig config, string scriptName, string locale)
    {
        string newPath = GetTableAssetPath(config, scriptName, locale);
        if (File.Exists(Path.GetFullPath(newPath)))
            return newPath;

        string legacyPath = GetLegacyTableAssetPath(config, scriptName, locale);
        if (File.Exists(Path.GetFullPath(legacyPath)))
            return legacyPath;

        return null;
    }

    /// <summary>剧本翻译表是否已存在（新结构或旧扁平任一）。</summary>
    public static bool TableExists(VNProjectConfig config, string scriptName, string locale)
    {
        return FindTableAssetPath(config, scriptName, locale) != null;
    }

    /// <summary>查找 UI 表实际所在 Assets 相对路径。都不存在返回 null。</summary>
    public static string FindUiTableAssetPath(VNProjectConfig config, string locale)
    {
        string newPath = GetUiTableAssetPath(config, locale);
        if (File.Exists(Path.GetFullPath(newPath)))
            return newPath;

        string legacyPath = GetLegacyUiTableAssetPath(config, locale);
        if (File.Exists(Path.GetFullPath(legacyPath)))
            return legacyPath;

        return null;
    }

    // ==================== 语言文件夹发现 ====================

    /// <summary>翻译表根目录下实际存在的语言文件夹名列表（= locale 代码）。</summary>
    public static List<string> DiscoverLocaleFolders(VNProjectConfig config)
    {
        var result = new List<string>();
        string root = Path.GetFullPath(config.GetTranslationFolderPath());
        if (!Directory.Exists(root))
            return result;

        foreach (var dir in Directory.GetDirectories(root))
            result.Add(Path.GetFileName(dir));
        return result;
    }

    // ==================== 旧扁平表检测与迁移 ====================

    /// <summary>旧扁平表信息（检测 + 迁移目标）。</summary>
    public sealed class LegacyFileInfo
    {
        /// <summary>文件名（含扩展名），如 Chapter1_en.csv / UI_Locale_en.csv</summary>
        public string FileName;
        /// <summary>剧本名（UI 表为 null，看 IsUi）</summary>
        public string ScriptName;
        /// <summary>语言代码</summary>
        public string Locale;
        /// <summary>是否 UI 表</summary>
        public bool IsUi;
        /// <summary>源文件完整路径</summary>
        public string SourceFullPath;
        /// <summary>迁移目标 Assets 相对路径</summary>
        public string DestAssetPath;
    }

    /// <summary>
    /// 检测翻译表根目录下的旧扁平表（非递归）：
    /// <c>UI_Locale_{locale}.csv</c> 与 <c>{script}_{locale}.csv</c>（语言段须在已知语言集合内）。
    /// 无法从文件名解析出已知语言的文件（用户自建表）不参与迁移，聚合照常扫描。
    /// </summary>
    public static List<LegacyFileInfo> FindLegacyFlatTables(VNProjectConfig config, IEnumerable<string> knownLocales)
    {
        var result = new List<LegacyFileInfo>();
        var known = new HashSet<string>(
            (knownLocales ?? Array.Empty<string>()).Where(l => !string.IsNullOrEmpty(l)),
            StringComparer.OrdinalIgnoreCase);

        string root = Path.GetFullPath(config.GetTranslationFolderPath());
        if (!Directory.Exists(root))
            return result;

        foreach (var file in Directory.GetFiles(root, "*.csv"))
        {
            string fileName = Path.GetFileName(file);
            string nameNoExt = Path.GetFileNameWithoutExtension(fileName);

            // 1) UI 表：UI_Locale_{locale}.csv
            if (nameNoExt.StartsWith(UiLegacyPrefix + "_", StringComparison.OrdinalIgnoreCase))
            {
                string locale = nameNoExt.Substring(UiLegacyPrefix.Length + 1);
                if (!known.Contains(locale))
                    continue;
                result.Add(new LegacyFileInfo
                {
                    FileName = fileName,
                    ScriptName = null,
                    Locale = locale,
                    IsUi = true,
                    SourceFullPath = file,
                    DestAssetPath = GetUiTableAssetPath(config, locale),
                });
                continue;
            }

            // 2) 剧本表：{script}_{locale}.csv（取最后一个下划线，语言段须为已知语言）
            int lastUnderscore = nameNoExt.LastIndexOf('_');
            if (lastUnderscore <= 0 || lastUnderscore >= nameNoExt.Length - 1)
                continue;

            string script = nameNoExt.Substring(0, lastUnderscore);
            string loc = nameNoExt.Substring(lastUnderscore + 1);
            if (string.IsNullOrEmpty(script) || !known.Contains(loc))
                continue;

            result.Add(new LegacyFileInfo
            {
                FileName = fileName,
                ScriptName = script,
                Locale = loc,
                IsUi = false,
                SourceFullPath = file,
                DestAssetPath = GetTableAssetPath(config, script, loc),
            });
        }

        return result;
    }

    /// <summary>迁移结果报告。</summary>
    public sealed class MigrationReport
    {
        public int Moved;
        public int RemovedDuplicate;
        public int SkippedConflict;
        public int Failed;
        public List<string> Details = new List<string>();

        public bool HasChanges => Moved + RemovedDuplicate + SkippedConflict + Failed > 0;
    }

    /// <summary>
    /// 一键迁移旧扁平表到语言文件夹（MoveAsset 保留 .meta/GUID，版本控制友好）。
    /// 目标已存在时：源表全部非空译文已包含在目标表（值相同）→ 删除源；否则跳过待人工处理。
    /// </summary>
    public static MigrationReport MigrateLegacyTables(VNProjectConfig config, List<LegacyFileInfo> files)
    {
        var report = new MigrationReport();
        if (files == null || files.Count == 0)
            return report;

        // 先刷新资产数据库：文件可能刚写入尚未索引（MoveAsset 需要已知资产）
        AssetDatabase.Refresh();

        foreach (var f in files)
        {
            try
            {
                string srcAssetPath = ToAssetsRelativePath(f.SourceFullPath);
                if (srcAssetPath == null)
                {
                    report.Failed++;
                    report.Details.Add($"✗ {f.FileName}：不在 Assets 下，无法迁移");
                    continue;
                }

                string destFullPath = Path.GetFullPath(f.DestAssetPath);

                if (File.Exists(destFullPath))
                {
                    // 目标已存在：源的全部非空译文已被目标包含（值相同）→ 源冗余可删
                    if (IsTranslationSubset(f.SourceFullPath, destFullPath))
                    {
                        AssetDatabase.DeleteAsset(srcAssetPath);
                        report.RemovedDuplicate++;
                        report.Details.Add($"✓ {f.FileName} → 已有等价表，删除冗余源文件");
                    }
                    else
                    {
                        report.SkippedConflict++;
                        report.Details.Add($"⚠ {f.FileName} → {f.DestAssetPath}：目标已存在且译文有差异，请人工合并后处理");
                    }
                    continue;
                }

                EnsureFolderAssetPath(Path.GetDirectoryName(f.DestAssetPath).Replace('\\', '/'));

                string err = AssetDatabase.MoveAsset(srcAssetPath, f.DestAssetPath);
                if (!string.IsNullOrEmpty(err))
                {
                    // 兜底：文件系统级移动（连同 .meta），最后统一 Refresh
                    File.Move(f.SourceFullPath, destFullPath);
                    string srcMeta = f.SourceFullPath + ".meta";
                    if (File.Exists(srcMeta))
                        File.Move(srcMeta, destFullPath + ".meta");
                }
                report.Moved++;
                report.Details.Add($"✓ {f.FileName} → {f.DestAssetPath}");
            }
            catch (Exception e)
            {
                report.Failed++;
                report.Details.Add($"✗ {f.FileName}：{e.Message}");
            }
        }

        if (report.HasChanges)
            AssetDatabase.Refresh();

        return report;
    }

    /// <summary>绝对路径 → "Assets/..."；不在工程 Assets 下返回 null。</summary>
    public static string ToAssetsRelativePath(string absolutePath)
    {
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."))
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string full = Path.GetFullPath(absolutePath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (!full.StartsWith(projectRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
            !full.StartsWith(projectRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return null;

        string rel = full.Substring(projectRoot.Length + 1).Replace('\\', '/');
        return rel.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ? rel : null;
    }

    /// <summary>逐级创建 Assets 下的文件夹资产（Assets/A/B/C）。</summary>
    public static void EnsureFolderAssetPath(string assetDir)
    {
        if (string.IsNullOrEmpty(assetDir))
            return;
        string[] parts = assetDir.Replace('\\', '/').Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }

    // ==================== 翻译表解析（进度统计 / 迁移比较共用） ====================

    /// <summary>
    /// 读取翻译表第 3 列（译文列）非空条目：key → 译文（跳过表头；解析失败返回空表）。
    /// </summary>
    public static Dictionary<string, string> ReadTranslationColumn(string fullPath)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath))
            return map;

        string text;
        try
        {
            text = File.ReadAllText(fullPath);
        }
        catch (Exception)
        {
            return map;
        }

        var lines = ScriptParser.SplitCSVLines(text);
        for (int i = 1; i < lines.Length; i++) // 跳过表头
        {
            if (string.IsNullOrEmpty(lines[i]))
                continue;
            var cols = ScriptParser.SplitCSV(lines[i]);
            if (cols.Length < 3)
                continue;

            string key = (cols[0] ?? "").Trim();
            string value = (cols[2] ?? "").Trim();
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(value))
                continue;
            map[key] = value;
        }
        return map;
    }

    /// <summary>翻译表统计：数据行总数与已译条目数（第 3 列非空）。</summary>
    public static void ReadTableStats(string fullPath, out int totalRows, out int translatedRows)
    {
        totalRows = 0;
        translatedRows = 0;
        if (string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath))
            return;

        string text;
        try
        {
            text = File.ReadAllText(fullPath);
        }
        catch (Exception)
        {
            return;
        }

        var lines = ScriptParser.SplitCSVLines(text);
        for (int i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrEmpty(lines[i]))
                continue;
            var cols = ScriptParser.SplitCSV(lines[i]);
            if (cols.Length < 3)
                continue;

            totalRows++;
            if (!string.IsNullOrEmpty((cols[2] ?? "").Trim()))
                translatedRows++;
        }
    }

    /// <summary>源表的全部非空译文均已在目标表中且值相同（源可安全删除）。</summary>
    private static bool IsTranslationSubset(string sourceFullPath, string destFullPath)
    {
        var source = ReadTranslationColumn(sourceFullPath);
        if (source.Count == 0)
            return true; // 源无译文：纯模板，目标存在即冗余

        var dest = ReadTranslationColumn(destFullPath);
        foreach (var kv in source)
        {
            if (!dest.TryGetValue(kv.Key, out var value) ||
                !string.Equals(value, kv.Value, StringComparison.Ordinal))
                return false;
        }
        return true;
    }
}
