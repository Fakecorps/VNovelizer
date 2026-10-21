using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// 剧本资源引用扫描器（【子文件夹支持】配套防呆功能）。
///
/// 扫描全部剧本 CSV（VNScriptCsvLocator 权威来源）中 Voice/BGM/背景列与
/// playvoice/playbgm/playsfx/bgtrans 命令参数的资源引用：
/// - 删除/重命名/移入回收站前警告受影响的行（防 Excel 引用静默失效）；
/// - 「检查缺失引用」报告引用了不存在资源的所有行。
/// </summary>
public static class ScriptReferenceScanner
{
    /// <summary>单条剧本引用记录</summary>
    public class ScriptResourceReference
    {
        public ResType Type;        // 引用的资源类别
        public string Name;         // 资源名（Excel 中填写原样，规范化 '\' → '/'）
        public string ScriptName;   // 剧本名
        public string LineId;       // 行 ID
        public int LineNo;          // CSV 行号（1 起，含标题行）
        public string Source;       // 引用来源（"Voice 列" / "playvoice 命令" 等）
    }

    /// <summary>引用资源名的命令 → 类别 与 参数序号（第 1 参为资源名）</summary>
    private static readonly (string command, ResType type)[] ResourceCommands =
    {
        ("playvoice", ResType.Voice),
        ("playbgm", ResType.BGM),
        ("playsfx", ResType.SFX),
        ("bgtrans", ResType.Background),
    };

    private static readonly Regex CommandRegex = new Regex(
        @"(?<![A-Za-z0-9_])(playvoice|playbgm|playsfx|bgtrans)\s*\(\s*(" + "\"[^\"]*\"|'[^']*'|[^,)]+" + ")",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>扫描全部剧本的全部资源引用</summary>
    public static List<ScriptResourceReference> ScanAll()
    {
        var result = new List<ScriptResourceReference>();
        foreach (string csvPath in VNScriptCsvLocator.GetAllCsvPaths())
        {
            try
            {
                string scriptName = Path.GetFileNameWithoutExtension(csvPath);
                string text = File.ReadAllText(Path.GetFullPath(csvPath));
                string[] lines = ScriptParser.SplitCSVLines(text);
                for (int i = 0; i < lines.Length; i++)
                {
                    CollectFromLine(lines[i], scriptName, i + 1, result);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[引用扫描] 读取剧本失败，跳过: {csvPath}（{e.Message}）");
            }
        }
        return result;
    }

    /// <summary>精确查找对某资源名的引用（删除/重命名单个资源用）</summary>
    public static List<ScriptResourceReference> FindReferences(ResType type, string logicalName)
    {
        string target = NormalizeName(type, logicalName);
        return ScanAll().Where(r => r.Type == type && string.Equals(NormalizeName(r.Type, r.Name), target, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>查找文件夹前缀下的全部引用（删除/重命名文件夹用：含 folder 本身与其下所有层级）</summary>
    public static List<ScriptResourceReference> FindReferencesUnderFolder(ResType type, string folderPath)
    {
        string prefix = (folderPath ?? "").Trim().Trim('/').Replace('\\', '/').TrimEnd('/') + "/";
        if (prefix == "/") return new List<ScriptResourceReference>();
        return ScanAll().Where(r => r.Type == type && NormalizeName(r.Type, r.Name).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>
    /// 检查缺失引用：引用的目标资源当前不存在（不在资源管理器该类别的任何条目中）。
    /// </summary>
    public static List<ScriptResourceReference> FindMissing()
    {
        var missing = new List<ScriptResourceReference>();
        var available = new Dictionary<ResType, HashSet<string>>();
        foreach (ResType t in Enum.GetValues(typeof(ResType)))
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in ResourceAssetService.LoadAssets(t, null))
            {
                string name = NormalizeName(t, item.LogicalName ?? item.Name);
                if (!string.IsNullOrEmpty(name)) set.Add(name);
            }
            available[t] = set;
        }

        foreach (var reference in ScanAll())
        {
            string name = NormalizeName(reference.Type, reference.Name);
            if (string.IsNullOrEmpty(name)) continue;
            if (!available.TryGetValue(reference.Type, out var set) || !set.Contains(name))
                missing.Add(reference);
        }
        return missing;
    }

    // ===================== 内部 =====================

    private static void CollectFromLine(string line, string scriptName, int lineNo, List<ScriptResourceReference> sink)
    {
        string[] columns = ScriptParser.SplitCSV(line);
        if (columns.Length < 12) return;

        // 列布局与 ScriptParser 一致：14 列新格式 / 12 列旧格式
        bool newFormat = columns.Length >= 14;
        int bgIdx = newFormat ? 9 : 7;
        int bgmIdx = newFormat ? 10 : 8;
        int voiceIdx = newFormat ? 11 : 9;
        int cmdIdx = newFormat ? 12 : 10;
        string lineId = columns[0].Trim();

        AddIfValid(sink, ResType.Background, columns[bgIdx], scriptName, lineId, lineNo, "背景列");
        AddIfValid(sink, ResType.BGM, columns[bgmIdx], scriptName, lineId, lineNo, "BGM 列");
        AddIfValid(sink, ResType.Voice, columns[voiceIdx], scriptName, lineId, lineNo, "Voice 列");

        // 命令列（含 @Confirm 出口段命令，正则全文扫描）
        string commandText = columns[cmdIdx];
        if (!string.IsNullOrEmpty(commandText))
        {
            foreach (Match m in CommandRegex.Matches(commandText))
            {
                string cmd = m.Groups[1].Value.ToLowerInvariant();
                var mapped = ResourceCommands.FirstOrDefault(c => c.command == cmd);
                if (mapped.command == null) continue;
                string arg = m.Groups[2].Value.Trim().Trim('"', '\'');
                AddIfValid(sink, mapped.type, arg, scriptName, lineId, lineNo, $"{cmd} 命令");
            }
        }
    }

    private static void AddIfValid(List<ScriptResourceReference> sink, ResType type, string raw,
        string scriptName, string lineId, int lineNo, string source)
    {
        string name = (raw ?? "").Trim();
        if (name.Length == 0) return;
        if (name.Contains("://")) return;             // URL 形式（流媒体等）
        if (type == ResType.Voice && name.Equals("false", StringComparison.OrdinalIgnoreCase)) return; // 语音开关语义
        sink.Add(new ScriptResourceReference
        {
            Type = type,
            Name = name,
            ScriptName = scriptName,
            LineId = lineId,
            LineNo = lineNo,
            Source = source,
        });
    }

    /// <summary>规范化资源名用于匹配：'\' → '/'、去首尾分隔符；Voice/BGM/SFX 剥离音频扩展名（与 VoiceManager 行为一致）</summary>
    private static string NormalizeName(ResType type, string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        string n = name.Trim().Replace('\\', '/').Trim('/');
        if (type == ResType.Voice || type == ResType.BGM || type == ResType.SFX)
        {
            foreach (string ext in new[] { ".mp3", ".wav", ".ogg" })
            {
                if (n.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                {
                    n = n.Substring(0, n.Length - ext.Length);
                    break;
                }
            }
        }
        return n;
    }
}
