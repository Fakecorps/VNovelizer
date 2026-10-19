using System.Collections.Generic;
using VNovelizer.Core.Commands;
using VNovelizer.Core.Commands.Chain;
using VNovelizer.Core.Commands.Meta;
using VNovelizer.Core.Localization;

/// <summary>
/// 【本地化重构 P1】剧本扫描器：从剧本 CSV 提取本地化条目（key + Text_Origin 原文）。
/// 供模板生成器（生成翻译表）与聚合器（源 locale 表）共用。
///
/// <para>CSV 解析复用 <c>ScriptParser.ParseFromText</c>（单一解析源，禁止第二套解析器）。</para>
///
/// <para>
/// <b>key 与序号规则必须与运行时逐节点一致</b>（运行时见
/// <c>VNCommandLocalizationContext</c> + <c>ChainExecutor</c> 执行序）：
/// <list type="bullet">
/// <item>进入段 / 出口段（@Confirm:）各自独立计数（运行时 BeginLine 各自调用）；</item>
/// <item>DFS 文本顺序遍历（= ChainExecutor 执行序）；</item>
/// <item>仅"含 <c>LocalizableText</c> 参数"的命令计数；</item>
/// <item>顶层 choice 计数并生成选项 key；选项链展开用独立计数器；</item>
/// <item>选项链内（含嵌套）的 choice 不计数（运行时 <c>SetChoiceChainExecution</c> 抑制），
///     其选项文本只支持 @loc:。</item>
/// </list>
/// </para>
/// </summary>
public static class VNLocalizationScriptScanner
{
    /// <summary>单个本地化条目：key + 原文（Text_Origin）。</summary>
    public class Entry
    {
        public string Key;
        public string TextOrigin;
    }

    /// <summary>
    /// 扫描单剧本 CSV（<paramref name="scriptName"/> 不含扩展名）全部行的本地化条目。
    /// </summary>
    public static List<Entry> ScanScriptCsv(string scriptName, string csvText)
    {
        var entries = new List<Entry>();

        var data = ScriptParser.ParseFromText(scriptName, csvText);
        if (data == null || data.Lines == null || data.Lines.Count == 0)
            return entries;

        foreach (var line in data.Lines)
        {
            if (string.IsNullOrEmpty(line.ID))
                continue;

            // 剧情文本 / 说话人
            if (!string.IsNullOrEmpty(line.Text))
            {
                entries.Add(new Entry
                {
                    Key = VNLocalizationKeyPlanner.TextKey(scriptName, line.ID),
                    TextOrigin = line.Text,
                });
            }

            if (!string.IsNullOrEmpty(line.Speaker))
            {
                entries.Add(new Entry
                {
                    Key = VNLocalizationKeyPlanner.SpeakerKey(scriptName, line.ID),
                    TextOrigin = line.Speaker,
                });
            }

            // 命令文本：进入段与出口段独立计数（与运行时 BeginLine 一致）
            if (!string.IsNullOrEmpty(line.Command))
                ScanCommandSection(entries, scriptName, line.ID, line.Command);

            if (!string.IsNullOrEmpty(line.ConfirmCommands))
                ScanCommandSection(entries, scriptName, line.ID, line.ConfirmCommands);
        }

        return entries;
    }

    /// <summary>
    /// 【Fix-ChoiceChainKey】选项链 key 上下文：null = 非选项链（命令走行内自动 key）；
    /// 非 null = 顶层 choice 的选项链（链内命令走链 key，跨链唯一无碰撞）。
    /// </summary>
    private sealed class ChoiceChainContext
    {
        public int ChoiceOrdinal;
        public int OptionIndex;
    }

    /// <summary>扫描一段命令串（进入段或出口段），用独立计数器。</summary>
    private static void ScanCommandSection(List<Entry> entries, string scriptName, string lineId, string commandText)
    {
        var parse = ChainParser.Parse(commandText);
        if (parse.Root == null)
            return;

        var counters = new Dictionary<string, int>();
        ScanNode(entries, scriptName, lineId, parse.Root, counters, null, suppressCommands: false);
    }

    /// <summary>
    /// DFS 遍历命令树（文本顺序）。
    /// <paramref name="chainCtx"/>：非 null = 顶层 choice 选项链内（命令生成链 key）。
    /// <paramref name="suppressCommands"/>：true = 命令不生成自动 key（嵌套 choice 的选项链内，
    /// 与运行时嵌套链「仅 @loc:」语义一致）。
    /// </summary>
    private static void ScanNode(List<Entry> entries, string scriptName, string lineId,
        ChainNode node, Dictionary<string, int> counters, ChoiceChainContext chainCtx, bool suppressCommands)
    {
        if (node == null)
            return;

        switch (node)
        {
            case SeqNode seq:
                foreach (var child in seq.Children)
                    ScanNode(entries, scriptName, lineId, child, counters, chainCtx, suppressCommands);
                break;

            case ParNode par:
                foreach (var child in par.Children)
                    ScanNode(entries, scriptName, lineId, child, counters, chainCtx, suppressCommands);
                break;

            case CommandNode cmd:
                ScanCommandNode(entries, scriptName, lineId, cmd, counters, chainCtx, suppressCommands);
                break;

            case ChoiceNode choice:
                if (chainCtx == null && !suppressCommands)
                {
                    // 顶层 choice：计数并生成选项 key
                    int ordinal = Advance(counters, "choice");
                    for (int j = 0; j < choice.Options.Count; j++)
                    {
                        var opt = choice.Options[j];
                        if (opt == null || string.IsNullOrEmpty(opt.Text))
                            continue;
                        entries.Add(new Entry
                        {
                            Key = VNLocalizationKeyPlanner.ChoiceOptionKey(scriptName, lineId, ordinal, j),
                            TextOrigin = opt.Text,
                        });
                    }

                    // 选项链展开：链内命令用链 key（独立计数器，与运行时链执行 BeginLine 重置一致）
                    for (int j = 0; j < choice.Options.Count; j++)
                    {
                        var opt = choice.Options[j];
                        if (opt == null || opt.Chain == null)
                            continue;
                        var chainCounters = new Dictionary<string, int>();
                        ScanNode(entries, scriptName, lineId, opt.Chain, chainCounters,
                            new ChoiceChainContext { ChoiceOrdinal = ordinal, OptionIndex = j },
                            suppressCommands: false);
                    }
                }
                else
                {
                    // 选项链内的嵌套 choice（或已抑制）：选项不生成 key，
                    // 其选项链内命令也不生成自动 key（仅 @loc:，与运行时嵌套链语义一致）
                    foreach (var opt in choice.Options)
                    {
                        if (opt == null || opt.Chain == null)
                            continue;
                        var chainCounters = new Dictionary<string, int>();
                        ScanNode(entries, scriptName, lineId, opt.Chain, chainCounters, null, suppressCommands: true);
                    }
                }
                break;
        }
    }

    /// <summary>命令叶子：含 LocalizableText 参数才计数，取第 0 个文本参数的值为原文。</summary>
    private static void ScanCommandNode(List<Entry> entries, string scriptName, string lineId,
        CommandNode cmd, Dictionary<string, int> counters, ChoiceChainContext chainCtx, bool suppressCommands)
    {
        if (suppressCommands)
            return; // 嵌套选项链内：仅 @loc:（运行时嵌套链不查自动 key）

        if (string.IsNullOrEmpty(cmd.Name))
            return;

        string cmdLower = cmd.Name.ToLower();
        var info = CommandMetaReader.Get(cmdLower);
        if (info == null || info.Parameters == null || info.Parameters.Count == 0)
            return;

        // 找第 0 个 LocalizableText 参数（多文本参数命令：仅第 0 个自动生成，其余用 @loc:）
        VNParamInfo textParam = null;
        foreach (var p in info.Parameters)
        {
            if (p.LocalizableText)
            {
                textParam = p;
                break;
            }
        }
        if (textParam == null)
            return;

        string value = GetParamValue(cmd.Args, textParam.Index, info.ArgSeparator);
        if (string.IsNullOrEmpty(value))
            return;

        int ordinal = Advance(counters, cmdLower);
        string key = chainCtx == null
            ? VNLocalizationKeyPlanner.CommandTextKey(scriptName, lineId, cmdLower, ordinal)
            : VNLocalizationKeyPlanner.ChoiceChainCommandKey(
                scriptName, lineId, chainCtx.ChoiceOrdinal, chainCtx.OptionIndex, cmdLower, ordinal);
        entries.Add(new Entry
        {
            Key = key,
            TextOrigin = value,
        });
    }

    /// <summary>取命令第 <paramref name="index"/> 个参数值（引号/括号感知分割 + 剥外层引号）。</summary>
    private static string GetParamValue(string args, int index, char separator)
    {
        if (string.IsNullOrEmpty(args))
            return null;

        var parts = ConditionParser.SplitTopLevel(args, separator);
        if (index < 0 || index >= parts.Count)
            return null;

        string value = (parts[index] ?? "").Trim();
        // 剥外层引号（showprompt("带逗号的文本") 场景）
        if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
            value = value.Substring(1, value.Length - 2);

        return value;
    }

    private static int Advance(Dictionary<string, int> counters, string commandName)
    {
        string key = commandName.ToLower();
        counters.TryGetValue(key, out int ordinal);
        counters[key] = ordinal + 1;
        return ordinal;
    }
}
