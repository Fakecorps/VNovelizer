using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// 【本地化重构 P1】翻译表模板生成器：从剧本 CSV 为每个目标语言生成/更新模板 CSV。
///
/// <para>格式（Docs/VNLocalizationRefactoring.md §4）：<c>ID, Text_Origin, {locale}</c></para>
///
/// <para>增量语义（译文安全第一）：</para>
/// <list type="bullet">
/// <item>新条目：追加（译文列留空）；</item>
/// <item>已有条目：仅更新 Text_Origin（原文变化时），<b>译文列永不动</b>；</item>
/// <item>消失条目（行 ID 删除/改动）：保留原行并追加在末尾（译文保留，作者手删）。</item>
/// </list>
///
/// <para>挂点：ScriptManager 的 Excel → CSV 转换流程尾部（见 ScriptManager）。</para>
/// </summary>
public static class VNTranslationTemplateGenerator
{
    /// <summary>为剧本生成全部目标语言的模板（转换流程调用）。返回生成的模板文件路径列表。</summary>
    public static List<string> GenerateTemplatesForScript(string scriptName, string csvText)
    {
        var generated = new List<string>();
        var config = VNProjectConfig.Instance;
        if (config == null || !config.EnableLocalization)
            return generated;

        foreach (var locale in config.GetTargetLocales())
        {
            string path = GenerateTemplate(scriptName, locale, csvText);
            if (!string.IsNullOrEmpty(path))
                generated.Add(path);
        }
        return generated;
    }

    /// <summary>为剧本 + 目标语言生成/更新模板。返回模板文件路径（失败返回 null）。</summary>
    public static string GenerateTemplate(string scriptName, string locale, string csvText)
    {
        var config = VNProjectConfig.Instance;
        if (config == null || string.IsNullOrEmpty(scriptName) || string.IsNullOrEmpty(locale))
            return null;

        // 【按语言文件夹分类】模板写入 {TranslationFolder}/{locale}/{script}.csv；
        // 旧扁平 {script}_{locale}.csv 的译文兜底合并（译文永不丢），旧文件本身由迁移工具/迁移视图处理。
        string path = Path.GetFullPath(VNTranslationTablePaths.GetTableAssetPath(config, scriptName, locale));

        // 1) 扫描剧本条目（key + 原文，有序）
        var entries = VNLocalizationScriptScanner.ScanScriptCsv(scriptName, csvText);
        if (entries.Count == 0)
        {
            Debug.LogWarning($"[VNTranslationTemplateGenerator] 剧本 {scriptName} 无可本地化条目，跳过模板生成。");
            return null;
        }

        // 2) 读旧模板的译文（key -> 译文）：新结构路径优先，旧扁平路径兜底合并
        var oldTranslations = ReadOldTemplate(path, locale);
        string legacyPath = Path.GetFullPath(VNTranslationTablePaths.GetLegacyTableAssetPath(config, scriptName, locale));
        foreach (var kv in ReadOldTemplate(legacyPath, locale))
        {
            if (!oldTranslations.ContainsKey(kv.Key))
                oldTranslations[kv.Key] = kv.Value;
        }

        // 3) 重建模板：新条目按序 + 孤儿译文行追加
        var sb = new StringBuilder();
        sb.AppendLine($"ID,Text_Origin,{locale}");

        var seenKeys = new HashSet<string>();
        foreach (var entry in entries)
        {
            if (string.IsNullOrEmpty(entry.Key))
                continue;
            seenKeys.Add(entry.Key);
            oldTranslations.TryGetValue(entry.Key, out string oldValue);
            sb.AppendLine(string.Join(",",
                EscapeCsvField(entry.Key),
                EscapeCsvField(entry.TextOrigin ?? ""),
                EscapeCsvField(oldValue ?? "")));
        }

        // 消失行（有译文的孤儿 key）追加在末尾，等待作者手动清理
        foreach (var kv in oldTranslations)
        {
            if (seenKeys.Contains(kv.Key))
                continue;
            if (string.IsNullOrEmpty(kv.Value))
                continue;
            sb.AppendLine(string.Join(",",
                EscapeCsvField(kv.Key),
                "", // Text_Origin 已无从考证（原文行已删），留空
                EscapeCsvField(kv.Value)));
        }

        // 4) 写文件（UTF-8 with BOM：Excel 打开不乱码；语言文件夹不存在时自动创建）
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        return path;
    }

    /// <summary>读旧模板：解析第三列（译文列），返回 key -> 译文（仅非空）。</summary>
    private static Dictionary<string, string> ReadOldTemplate(string path, string locale)
    {
        var map = new Dictionary<string, string>();
        if (!File.Exists(path))
            return map;

        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (System.Exception)
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
            if (string.IsNullOrEmpty(key))
                continue;
            if (!string.IsNullOrEmpty(value))
                map[key] = value;
        }
        return map;
    }

    /// <summary>CSV 字段转义（与 ScriptParser.SplitCSV 的引号感知解析兼容）。</summary>
    public static string EscapeCsvField(string field)
    {
        if (field == null)
            return "";
        if (field.IndexOf(',') >= 0 || field.IndexOf('"') >= 0 ||
            field.IndexOf('\n') >= 0 || field.IndexOf('\r') >= 0)
        {
            return "\"" + field.Replace("\"", "\"\"") + "\"";
        }
        return field;
    }
}
