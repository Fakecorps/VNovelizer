using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine;
using VNovelizer.Core.Commands;
using VNovelizer.Core.Commands.Chain;

/// <summary>一次只读扫描的引用与完整性；不完整结果不能视为资源未被引用。</summary>
public class ResourceReferenceScanResult
{
    public List<ScriptReferenceScanner.ScriptResourceReference> References = new List<ScriptReferenceScanner.ScriptResourceReference>();
    public List<string> Errors = new List<string>();
    public bool IsComplete => Errors.Count == 0;
}

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
        public string CsvPath;
        public string AssetPath;
        public string ResourceKey;
        public bool Potential;      // 空 Voice 列按 ID 推导的可选依赖
        public bool IsLoadable;      // 注册地址 / Resources 键与可加载资产类型均匹配
    }

    private static readonly ResType[] ScannedTypes =
    {
        ResType.Background, ResType.BGM, ResType.SFX, ResType.Voice,
    };

    /// <summary>一次扫描全部 CSV 与真实资源源；失败保留部分引用并标记不完整。</summary>
    public static ResourceReferenceScanResult ScanAll()
    {
        var result = new ResourceReferenceScanResult();
        try
        {
            string folder = VNScriptCsvLocator.GetCsvFolderAssetPath();
            if (!string.IsNullOrEmpty(folder) && !Directory.Exists(Path.GetFullPath(folder)))
                result.Errors.Add($"剧本输出目录不存在：{folder}；无法保证枚举完整。");
            foreach (string csvPath in VNScriptCsvLocator.GetAllCsvPaths())
            {
                try { CollectFromCsv(csvPath, result); }
                catch (Exception e) { result.Errors.Add($"读取或解析剧本失败：{csvPath}（{e.Message}）"); }
            }
        }
        catch (Exception e) { result.Errors.Add($"枚举剧本失败：{e.Message}"); }

        try { ResolveLoadableResources(result); }
        catch (Exception e) { result.Errors.Add($"检查可加载资源失败：{e.Message}"); }
        return result;
    }

    public static List<ScriptResourceReference> FindReferences(ResType type, string logicalName)
    {
        var scan = ScanAll();
        RequireComplete(scan);
        return FindReferences(scan, type, logicalName);
    }

    public static List<ScriptResourceReference> FindReferences(ResourceReferenceScanResult scan, ResType type, string logicalName)
    {
        if (scan == null) throw new ArgumentNullException(nameof(scan));
        string target = NormalizeName(type, logicalName);
        return scan.References.Where(r => r.Type == type &&
            string.Equals(NormalizeName(r.Type, r.Name), target, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    public static List<ScriptResourceReference> FindReferencesUnderFolder(ResType type, string folderPath)
    {
        var scan = ScanAll();
        RequireComplete(scan);
        return FindReferencesUnderFolder(scan, type, folderPath);
    }

    public static List<ScriptResourceReference> FindReferencesUnderFolder(ResourceReferenceScanResult scan, ResType type, string folderPath)
    {
        if (scan == null) throw new ArgumentNullException(nameof(scan));
        string folder = (folderPath ?? "").Trim().Replace('\\', '/').Trim('/');
        if (folder.Length == 0) return scan.References.Where(r => r.Type == type).ToList();
        return scan.References.Where(r => r.Type == type &&
            NormalizeName(r.Type, r.Name).StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>自动配音 ID 默认不算必需资源；调用方可显式包含潜在依赖。</summary>
    public static List<ScriptResourceReference> FindMissing()
    {
        var scan = ScanAll();
        RequireComplete(scan);
        return FindMissing(scan);
    }

    public static List<ScriptResourceReference> FindMissing(ResourceReferenceScanResult scan, bool includePotential = false)
    {
        if (scan == null) throw new ArgumentNullException(nameof(scan));
        return scan.References.Where(r => !r.IsLoadable && (includePotential || !r.Potential)).ToList();
    }

    private static void RequireComplete(ResourceReferenceScanResult scan)
    {
        if (!scan.IsComplete)
            throw new InvalidOperationException("引用扫描不完整，不能判定安全：\n" + string.Join("\n", scan.Errors));
    }

    // ===================== 内部 =====================

    private static void CollectFromCsv(string csvPath, ResourceReferenceScanResult result)
    {
        CollectFromText(File.ReadAllText(Path.GetFullPath(csvPath)), csvPath, result);
    }

    private static void CollectFromText(string text, string csvPath, ResourceReferenceScanResult result)
    {
        string scriptName = Path.GetFileNameWithoutExtension(csvPath);
        var lineNumbers = GetRecordLineNumbers(text, csvPath, result);
        string[] records = ScriptParser.SplitCSVLines(text);
        var parsed = ScriptParser.ParseFromText(scriptName, text);
        bool header = true;
        bool voiceEnabled = true;
        int rowIndex = 0;
        for (int i = 0; i < records.Length; i++)
        {
            string record = records[i].Trim();
            if (record.Length == 0) continue;
            if (header) { header = false; continue; }
            int lineNo = i < lineNumbers.Count ? lineNumbers[i] : i + 1;
            string[] columns = ScriptParser.SplitCSV(record);
            if (columns.Length < 12)
            {
                result.Errors.Add($"{csvPath}:{lineNo} 列数不足（{columns.Length}，至少 12 列），未能完整扫描该行。");
                continue;
            }
            var line = parsed.Lines[rowIndex++];
            string rawCommand = columns[columns.Length >= 14 ? 12 : 10];
            if (CountConfirmTokens(rawCommand) > 1)
                result.Errors.Add($"{csvPath}:{lineNo} 含多个 @Confirm:；运行时截断后的命令未扫描。");
            AddIfValid(result, ResType.Background, line.Background, scriptName, line.ID, csvPath, lineNo, "背景列");
            AddIfValid(result, ResType.BGM, line.BGM, scriptName, line.ID, csvPath, lineNo, "BGM 列");
            if (string.Equals(line.Voice, "false", StringComparison.OrdinalIgnoreCase)) voiceEnabled = false;
            else if (!string.IsNullOrWhiteSpace(line.Voice))
            {
                voiceEnabled = true;
                AddIfValid(result, ResType.Voice, line.Voice, scriptName, line.ID, csvPath, lineNo, "Voice 列");
            }
            else if (voiceEnabled && !string.IsNullOrWhiteSpace(line.ID))
                AddIfValid(result, ResType.Voice, line.ID + ".mp3", scriptName, line.ID, csvPath, lineNo, "自动配音 ID（可选）", true);
            CollectCommandText(line.Command, scriptName, line.ID, csvPath, lineNo, "进入段", result);
            CollectCommandText(line.ConfirmCommands, scriptName, line.ID, csvPath, lineNo, "出口段", result);
        }
        if (header) result.Errors.Add($"{csvPath} 为空，未找到剧本标题行。");
    }

    private static void CollectCommandText(string text, string script, string id, string csv, int lineNo,
        string source, ResourceReferenceScanResult result)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var parsed = ChainParser.Parse(text);
        foreach (var error in parsed.Errors)
            result.Errors.Add($"{csv}:{lineNo} [{id}] {source} 命令解析失败：{error}");
        CollectNode(parsed.Root, script, id, csv, lineNo, source, result);
    }

    private static void CollectNode(ChainNode node, string script, string id, string csv, int lineNo,
        string source, ResourceReferenceScanResult result)
    {
        if (node == null) return;
        if (node is SeqNode seq)
        {
            foreach (var child in seq.Children) CollectNode(child, script, id, csv, lineNo, source, result);
            return;
        }
        if (node is ParNode par)
        {
            foreach (var child in par.Children) CollectNode(child, script, id, csv, lineNo, source, result);
            return;
        }
        if (node is ChoiceNode choice)
        {
            foreach (var option in choice.Options)
                CollectNode(option.Chain, script, id, csv, lineNo, source + " / 选项链", result);
            return;
        }
        if (!(node is CommandNode command)) return;
        string name = (command.Name ?? "").ToLowerInvariant();
        if (name == "choice")
        {
            string args = command.Args ?? "";
            int pipe = args.IndexOf('|');
            if (pipe >= 0)
                CollectCommandText(args.Substring(pipe + 1), script, id, csv, lineNo, source + " / 旧选项链", result);
            return;
        }
        ResType type;
        switch (name)
        {
            case "showbg": case "bgtrans": type = ResType.Background; break;
            case "playvoice": type = ResType.Voice; break;
            case "playbgm": type = ResType.BGM; break;
            case "playsfx": type = ResType.SFX; break;
            default: return;
        }
        var parameters = ConditionParser.SplitTopLevel(command.Args ?? "", ',');
        string first = parameters.Count == 0 ? "" : parameters[0].Trim();
        if (first.Length == 0)
        {
            if (name == "playsfx" || name == "bgtrans")
                result.Errors.Add($"{csv}:{lineNo} [{id}] {name} 缺少必需资源参数。");
            return;
        }
        AddIfValid(result, type, first, script, id, csv, lineNo, source + " / " + name + " 命令");
    }

    private static void AddIfValid(ResourceReferenceScanResult result, ResType type, string raw,
        string script, string id, string csv, int lineNo, string source, bool potential = false)
    {
        string name = NormalizeName(type, raw);
        if (name.Length == 0 || name.Contains("://")) return;
        if (type == ResType.Voice && name.Equals("false", StringComparison.OrdinalIgnoreCase)) return;
        if (type == ResType.Background && (name.Equals("black", StringComparison.OrdinalIgnoreCase) || name.Equals("hide", StringComparison.OrdinalIgnoreCase))) return;
        if (type == ResType.BGM && (name.Equals("stop", StringComparison.OrdinalIgnoreCase) || name.Equals("pause", StringComparison.OrdinalIgnoreCase) || name.Equals("resume", StringComparison.OrdinalIgnoreCase))) return;
        result.References.Add(new ScriptResourceReference
        {
            Type = type, Name = name, ScriptName = script, LineId = id, CsvPath = csv,
            LineNo = lineNo, Source = source, Potential = potential,
        });
    }

    private static string NormalizeName(ResType type, string name)
    {
        string n = (name ?? "").Trim().Replace('\\', '/');
        if (type != ResType.Voice) return n;
        n = n.Trim('/');
        foreach (string ext in new[] { ".mp3", ".wav", ".ogg" })
        {
            if (!n.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) continue;
            n = n.Substring(0, n.Length - ext.Length);
            break;
        }
        return n;
    }

    private static List<int> GetRecordLineNumbers(string text, string csv, ResourceReferenceScanResult result)
    {
        var numbers = new List<int>();
        bool inQuotes = false, started = false;
        int physicalLine = 1;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            bool newline = c == '\r' || c == '\n';
            if (!started && !newline) { numbers.Add(physicalLine); started = true; }
            if (c == '"')
            {
                if (inQuotes && i + 1 < text.Length && text[i + 1] == '"') i++;
                else inQuotes = !inQuotes;
            }
            if (!newline) continue;
            if (!inQuotes) started = false;
            if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
            physicalLine++;
        }
        if (inQuotes) result.Errors.Add($"{csv} CSV 引号未闭合；后续行可能未被完整解析。");
        return numbers;
    }

    private static int CountConfirmTokens(string text)
    {
        bool quote = false;
        int count = 0;
        const string token = "@confirm:";
        for (int i = 0; i < text.Length; i++)
        {
            if (quote && text[i] == '\\') { i++; continue; }
            if (text[i] == '"') { quote = !quote; continue; }
            if (!quote && i + token.Length <= text.Length &&
                string.Compare(text, i, token, 0, token.Length, StringComparison.OrdinalIgnoreCase) == 0)
                count++;
        }
        return count;
    }

    private static void ResolveLoadableResources(ResourceReferenceScanResult result)
    {
        if (!VNProjectConfig.TryGetInstance(out var config) || config == null)
        {
            result.Errors.Add("未找到 VNProjectConfig，无法确定资源加载键。");
            return;
        }
        var roots = new Dictionary<ResType, string>
        {
            { ResType.Background, config.BackgroundResPath }, { ResType.BGM, config.BgmResPath },
            { ResType.SFX, config.SFXResPath }, { ResType.Voice, config.VoiceResPath },
        };
        var available = new Dictionary<ResType, Dictionary<string, string>>();
        foreach (var type in ScannedTypes) available[type] = new Dictionary<string, string>(StringComparer.Ordinal);
        var checkedTypes = new Dictionary<string, HashSet<ResType>>(StringComparer.Ordinal);
        void AddAsset(string key, string assetPath)
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(assetPath)) return;
            if (!checkedTypes.TryGetValue(assetPath, out var types))
            {
                types = new HashSet<ResType>();
                try
                {
                    if (AssetDatabase.LoadAssetAtPath<AudioClip>(assetPath) != null)
                        types.UnionWith(new[] { ResType.BGM, ResType.SFX, ResType.Voice });
                    if (AssetDatabase.LoadAssetAtPath<Sprite>(assetPath) != null || AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath) != null)
                        types.Add(ResType.Background);
                }
                catch (Exception e) { result.Errors.Add($"资源类型检查失败：{assetPath}（{e.Message}）"); }
                checkedTypes[assetPath] = types;
            }
            foreach (var type in types)
                if (!available[type].ContainsKey(key)) available[type][key] = assetPath;
        }
#if VN_ADDRESSABLES
        try
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings != null)
                foreach (var group in settings.groups)
                {
                    if (group == null) continue;
                    foreach (var entry in group.entries)
                        if (entry != null && !AssetDatabase.IsValidFolder(entry.AssetPath))
                            AddAsset(entry.address, entry.AssetPath);
                }
        }
        catch (Exception e) { result.Errors.Add($"Addressables 注册数据检查失败：{e.Message}"); }
#endif
        foreach (string assetPath in AssetDatabase.GetAllAssetPaths())
        {
            if (assetPath.IndexOf("/Editor/", StringComparison.OrdinalIgnoreCase) >= 0) continue;
            int index = assetPath.IndexOf("/Resources/", StringComparison.Ordinal);
            if (index < 0 || AssetDatabase.IsValidFolder(assetPath)) continue;
            string key = assetPath.Substring(index + "/Resources/".Length);
            string extension = Path.GetExtension(key);
            if (extension.Length > 0) key = key.Substring(0, key.Length - extension.Length);
            AddAsset(key, assetPath);
        }
        foreach (var reference in result.References)
        {
            string root = roots[reference.Type] ?? "";
            string key = root.Length == 0 ? reference.Name : root + "/" + reference.Name;
            reference.ResourceKey = key;
            if (!available[reference.Type].TryGetValue(key, out var path) && reference.Type == ResType.Background)
            {
                string fallback = "Backgrounds/" + reference.Name;
                if (available[reference.Type].TryGetValue(fallback, out path)) reference.ResourceKey = fallback;
            }
            reference.IsLoadable = !string.IsNullOrEmpty(path);
            reference.AssetPath = path;
        }
    }
}
