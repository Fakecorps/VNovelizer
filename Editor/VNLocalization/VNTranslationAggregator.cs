using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VNovelizer.Core.Localization;

/// <summary>
/// 【本地化重构 P1】翻译表聚合器：把 TranslationFolder 下任意多张翻译 CSV 合并为
/// 单一 StringTableCollection <see cref="VNLocalizationService.CollectionName"/>。
///
/// <para>数据流（Docs/VNLocalizationRefactoring.md §6）：</para>
/// <list type="bullet">
/// <item>翻译表（含模板、UI 清单、用户自建表）：<c>ID, Text_Origin, {locale}</c>，按第 3 列表头归组；</item>
/// <item>源 locale 表自动生成：剧情条目来自剧本扫描器（原文），UI 条目来自翻译表的 Text_Origin 列；</item>
/// <item>同 locale 同 key 多值 → 冲突收集（后加载覆盖 + 报告），不中断聚合；</item>
/// <item>产物 Collection 是"构建产物"，全量重建（删除后重建），翻译真源永远是 CSV。</item>
/// </list>
/// </summary>
public static class VNTranslationAggregator
{
    /// <summary>产物目录（构建产物，建议 gitignore）。</summary>
    public const string GeneratedFolder = "Assets/Localization/Generated";

    public class Conflict
    {
        public string Locale;
        public string Key;
        public string FileA;
        public string FileB;
    }

    public class Result
    {
        public bool Success;
        public List<string> Warnings = new List<string>();
        public List<Conflict> Conflicts = new List<Conflict>();
        /// <summary>locale code -> 写入条目数</summary>
        public Dictionary<string, int> EntryCounts = new Dictionary<string, int>();
        /// <summary>参与聚合的翻译表文件</summary>
        public List<string> SourceFiles = new List<string>();
    }

    /// <summary>最近一次聚合结果（供本地化窗口显示）。</summary>
    public static Result LastResult { get; private set; }

    // 防重入：聚合写资产会触发 AssetPostprocessor 再次回调
    private static bool _aggregating;

    public static bool IsAggregating => _aggregating;

    /// <summary>全流程聚合：扫描翻译表 + 剧本 → 写单一 Collection。返回结果（含冲突/警告）。</summary>
    public static Result Aggregate()
    {
        var result = new Result();

        var config = VNProjectConfig.Instance;
        if (config == null)
        {
            result.Warnings.Add("未找到 VNProjectConfig。");
            LastResult = result;
            return result;
        }

        if (!config.EnableLocalization)
        {
            result.Warnings.Add("本地化未启用（VNProjectConfig.EnableLocalization = false），跳过聚合。");
            LastResult = result;
            return result;
        }

#if !VN_LOCALIZATION
        result.Warnings.Add("当前工程未安装 Unity Localization 包（VN_LOCALIZATION 宏未定义），无法写入 Collection。");
        LastResult = result;
        return result;
#else
        if (_aggregating)
        {
            result.Warnings.Add("聚合进行中（防重入），本次请求忽略。");
            LastResult = result;
            return result;
        }

        _aggregating = true;
        try
        {
            // 1) 收集翻译表条目：locale -> (key -> (value, sourceFile))；
            //    同时收集 Text_Origin 列（key -> 第一个非空原文，UI 清单的源语言来源）
            string translationFolder = Path.GetFullPath(config.GetTranslationFolderPath());
            var perLocale = new Dictionary<string, Dictionary<string, (string value, string file)>>(StringComparer.OrdinalIgnoreCase);
            var originByKey = new Dictionary<string, string>();

            if (Directory.Exists(translationFolder))
            {
                foreach (var csvPath in Directory.GetFiles(translationFolder, "*.csv", SearchOption.AllDirectories))
                {
                    result.SourceFiles.Add(csvPath);
                    ParseTranslationCsv(csvPath, perLocale, originByKey, result);
                }
            }
            else
            {
                result.Warnings.Add($"翻译表目录不存在：{translationFolder}（无翻译表参与聚合）。");
            }

            // 2) 源 locale 条目：剧情原文来自剧本扫描器
            string sourceLocaleCode = (config.SourceLocale ?? "zh-Hans").Trim().ToLowerInvariant();
            var sourceEntries = new Dictionary<string, string>();
            ScanScriptSourceEntries(sourceEntries, config);

            // 3) 写入单一 Collection（全量重建）：
            //    源表 = 剧本扫描原文（基线）→ 翻译表 Text_Origin 补缺 → 源语言翻译表 value 覆盖
            WriteCollection(perLocale, sourceEntries, originByKey, sourceLocaleCode, result);

            result.Success = true;
        }
        catch (Exception e)
        {
            result.Success = false;
            result.Warnings.Add($"聚合异常：{e.Message}\n{e.StackTrace}");
            Debug.LogError($"[VNTranslationAggregator] 聚合失败：{e}");
        }
        finally
        {
            _aggregating = false;
            LastResult = result;
        }

        return result;
#endif
    }



    /// <summary>扫描 CsvOutputFolder 全部剧本 CSV，提取源 locale 条目（key -> 原文）。</summary>
    private static void ScanScriptSourceEntries(Dictionary<string, string> sourceEntries, VNProjectConfig config)
    {
        string csvFolder = config.GetCsvOutputPath();
        if (string.IsNullOrEmpty(csvFolder))
            return;

        string fullFolder = Path.GetFullPath(csvFolder);
        if (!Directory.Exists(fullFolder))
            return;

        foreach (var csvPath in Directory.GetFiles(fullFolder, "*.csv"))
        {
            string scriptName = Path.GetFileNameWithoutExtension(csvPath);
            if (string.IsNullOrEmpty(scriptName))
                continue;

            string text;
            try
            {
                text = File.ReadAllText(csvPath);
            }
            catch (Exception)
            {
                continue;
            }

            foreach (var entry in VNLocalizationScriptScanner.ScanScriptCsv(scriptName, text))
            {
                if (string.IsNullOrEmpty(entry.Key))
                    continue;
                if (!sourceEntries.ContainsKey(entry.Key))
                    sourceEntries[entry.Key] = entry.TextOrigin ?? "";
            }
        }
    }

#if VN_LOCALIZATION
    /// <summary>
    /// 解析单张翻译表（表头：ID, Text_Origin, {locale}）。
    /// 同时把 Text_Origin 列收集进 <paramref name="originByKey"/>（仅补缺，先到先得）。
    /// </summary>
    private static void ParseTranslationCsv(string csvPath,
        Dictionary<string, Dictionary<string, (string value, string file)>> perLocale,
        Dictionary<string, string> originByKey,
        Result result)
    {
        string text;
        try
        {
            text = File.ReadAllText(csvPath);
        }
        catch (Exception e)
        {
            result.Warnings.Add($"读取翻译表失败：{csvPath}（{e.Message}）");
            return;
        }

        var lines = ScriptParser.SplitCSVLines(text);
        if (lines.Length < 2)
            return;

        var header = ScriptParser.SplitCSV(lines[0]);
        if (header.Length < 3)
        {
            result.Warnings.Add($"翻译表格式错误（需表头 ID,Text_Origin,{{locale}}）：{csvPath}");
            return;
        }

        string localeCode = (header[2] ?? "").Trim();
        if (string.IsNullOrEmpty(localeCode))
        {
            result.Warnings.Add($"翻译表第 3 列表头（locale 代码）为空：{csvPath}");
            return;
        }

        if (!perLocale.TryGetValue(localeCode, out var table))
        {
            table = new Dictionary<string, (string value, string file)>();
            perLocale[localeCode] = table;
        }

        for (int i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrEmpty(lines[i]))
                continue;

            var cols = ScriptParser.SplitCSV(lines[i]);
            if (cols.Length < 3)
                continue;

            string key = (cols[0] ?? "").Trim();
            string origin = (cols[1] ?? "").Trim();
            string value = (cols[2] ?? "").Trim();
            if (string.IsNullOrEmpty(key))
                continue;

            // Text_Origin 补缺（先到先得，作为源 locale 的补充来源）
            if (!string.IsNullOrEmpty(origin) && !originByKey.ContainsKey(key))
                originByKey[key] = origin;

            if (string.IsNullOrEmpty(value))
                continue; // 空译文行：跳过（运行时走 fallback）

            if (table.TryGetValue(key, out var existing))
            {
                if (!string.Equals(existing.value, value, StringComparison.Ordinal))
                {
                    result.Conflicts.Add(new Conflict
                    {
                        Locale = localeCode,
                        Key = key,
                        FileA = existing.file,
                        FileB = csvPath,
                    });
                }
                // 后加载覆盖（确定性：Directory.GetFiles 按路径排序）
            }
            table[key] = (value, csvPath);
        }
    }

    /// <summary>全量重建单一 Collection 并写入各 locale 表。</summary>
    private static void WriteCollection(
        Dictionary<string, Dictionary<string, (string value, string file)>> perLocale,
        Dictionary<string, string> sourceEntries,
        Dictionary<string, string> originByKey,
        string sourceLocaleCode,
        Result result)
    {
        // 1) 删除旧产物 Collection（全量重建语义）
        DeleteCollectionByName(VNLocalizationService.CollectionName);
        AssetDatabase.Refresh(); // 删除后刷新，避免同路径重建残留

        // 2) 新建 Collection
        var collection = UnityEditor.Localization.LocalizationEditorSettings
            .CreateStringTableCollection(VNLocalizationService.CollectionName, GeneratedFolder);
        if (collection == null)
        {
            result.Warnings.Add("创建 StringTableCollection 失败。");
            return;
        }

        var projectLocales = UnityEditor.Localization.LocalizationEditorSettings.GetLocales();

        // 3) 写源 locale 表：剧本扫描原文 + 翻译表 Text_Origin 补充 + 源语言翻译表 value 覆盖
        var sourceTableValues = new Dictionary<string, string>(sourceEntries);
        foreach (var kv in originByKey)
        {
            if (!sourceTableValues.ContainsKey(kv.Key))
                sourceTableValues[kv.Key] = kv.Value;
        }
        if (perLocale.TryGetValue(sourceLocaleCode, out var sourceOverride))
        {
            foreach (var kv in sourceOverride)
                sourceTableValues[kv.Key] = kv.Value.value;
        }

        int sourceCount = WriteTable(collection, projectLocales, sourceLocaleCode, sourceTableValues, result, isSource: true);

        // 4) 写各目标 locale 表
        foreach (var kv in perLocale)
        {
            if (string.Equals(kv.Key, sourceLocaleCode, StringComparison.OrdinalIgnoreCase))
                continue; // 源 locale 已写

            var values = kv.Value.ToDictionary(p => p.Key, p => p.Value.value);
            WriteTable(collection, projectLocales, kv.Key, values, result, isSource: false);
        }

        EditorUtility.SetDirty(collection);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[VNTranslationAggregator] 聚合完成：源语言 {sourceLocaleCode} 写入 {sourceCount} 条，" +
                  $"目标语言 {perLocale.Count(p => !string.Equals(p.Key, sourceLocaleCode, StringComparison.OrdinalIgnoreCase))} 种，" +
                  $"冲突 {result.Conflicts.Count} 处。");
    }

    /// <summary>
    /// locale 代码归一化：BCP-47 用连字符（zh-Hans），用户可能误写 POSIX 下划线（zh_Hans），
    /// 匹配前统一转连字符（配合忽略大小写比较），使旧配置/手误写法仍能命中 Locale 资产。
    /// </summary>
    private static string NormalizeLocaleCode(string code) => (code ?? "").Replace('_', '-');

    /// <summary>写单个 locale 的 StringTable（项目未配置该 Locale 时警告跳过）。返回写入条目数。</summary>
    private static int WriteTable(UnityEditor.Localization.StringTableCollection collection,
        ReadOnlyCollection<UnityEngine.Localization.Locale> projectLocales, string localeCode,
        Dictionary<string, string> values, Result result, bool isSource)
    {
        var locale = projectLocales?.FirstOrDefault(l =>
            string.Equals(NormalizeLocaleCode(l.Identifier.Code), NormalizeLocaleCode(localeCode),
                StringComparison.OrdinalIgnoreCase));
        if (locale == null)
        {
            result.Warnings.Add($"项目未配置 Locale：{localeCode}" +
                $"（{(isSource ? "源语言" : "目标语言")}）。" +
                "VNovelizer 设置页里填写的只是 locale 代码；还需要到 Edit → Project Settings → Localization " +
                "创建同名 Locale 资产后重新导入。");
            return 0;
        }

        var tableObj = collection.GetTable(locale.Identifier);
        var table = tableObj as UnityEngine.Localization.Tables.StringTable;
        if (table == null)
        {
            result.Warnings.Add($"获取 StringTable 失败：{localeCode}");
            return 0;
        }

        int count = 0;
        foreach (var kv in values)
        {
            if (string.IsNullOrEmpty(kv.Key))
                continue;

            var entry = table.GetEntry(kv.Key);
            if (entry == null)
            {
                table.AddEntry(kv.Key, kv.Value ?? "");
            }
            else
            {
                entry.Value = kv.Value ?? "";
            }
            count++;
        }

        EditorUtility.SetDirty(table);

        // SharedData 反射读取（与旧实现一致，避免不同 Localization 版本 API 差异）
        try
        {
            var sharedDataProp = table.GetType().GetProperty("SharedData");
            var sharedData = sharedDataProp?.GetValue(table) as UnityEngine.Object;
            if (sharedData != null)
                EditorUtility.SetDirty(sharedData);
        }
        catch { }

        result.EntryCounts[localeCode] = count;
        return count;
    }

    /// <summary>删除指定名称的 StringTableCollection（含 SharedData 与全部 StringTable 资产）。</summary>
    private static void DeleteCollectionByName(string collectionName)
    {
        var collections = UnityEditor.Localization.LocalizationEditorSettings.GetStringTableCollections();
        var collection = collections?.FirstOrDefault(c => c != null && c.TableCollectionName == collectionName);
        if (collection == null)
            return;

        var pathsToDelete = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        string collectionPath = AssetDatabase.GetAssetPath(collection);
        if (!string.IsNullOrEmpty(collectionPath))
            pathsToDelete.Add(collectionPath);

        try
        {
            var sharedDataProp = collection.GetType().GetProperty("SharedData");
            if (sharedDataProp != null)
            {
                var sharedData = sharedDataProp.GetValue(collection) as UnityEngine.Object;
                if (sharedData != null)
                {
                    string p = AssetDatabase.GetAssetPath(sharedData);
                    if (!string.IsNullOrEmpty(p))
                        pathsToDelete.Add(p);
                }
            }
        }
        catch { }

        try
        {
            var tablesProp = collection.GetType().GetProperty("StringTables");
            if (tablesProp != null)
            {
                var enumerable = tablesProp.GetValue(collection) as System.Collections.IEnumerable;
                if (enumerable != null)
                {
                    foreach (var t in enumerable)
                    {
                        var obj = t as UnityEngine.Object;
                        if (obj == null) continue;
                        string p = AssetDatabase.GetAssetPath(obj);
                        if (!string.IsNullOrEmpty(p))
                            pathsToDelete.Add(p);
                    }
                }
            }
        }
        catch { }

        foreach (var path in pathsToDelete)
            AssetDatabase.DeleteAsset(path);
    }
#endif
}
