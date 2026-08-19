using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// 剧本解析工具类
/// </summary>
public static class ScriptParser
{
    public class ScriptData
    {
        public List<StoryLine> Lines = new List<StoryLine>();
        public Dictionary<string, int> IDMap = new Dictionary<string, int>();
    }

    /// <summary>
    /// 解析剧本文件
    /// </summary>
    public static ScriptData Parse(string fileName)
    {
        ScriptData data = new ScriptData();

        // 从配置路径加载
        string configPath = VNProjectConfig.Instance.VNScriptResPath;
        string loadPath = configPath + "/" + fileName;
        Debug.Log($"[ScriptParser] 尝试加载剧本: {loadPath} (ConfigPath: {configPath}, FileName: {fileName})");

        TextAsset csvFile = Resources.Load<TextAsset>(loadPath);

        if (csvFile == null)
        {
            Debug.LogError($"[ScriptParser] 找不到剧本文件: {loadPath}");
            return null;
        }

        // 【修复】使用改进的行分割方法，正确处理引号内的换行符
        string[] lines = SplitCSVLines(csvFile.text);
        bool isFirstLine = true;

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (string.IsNullOrEmpty(line)) continue;

            // 跳过标题行
            if (isFirstLine)
            {
                isFirstLine = false;
                continue;
            }

            string[] columns = SplitCSV(line);
            if (columns.Length >= 14) // 新 14 列格式：增加了 CharMid_Left / CharMid_Right 两个槽位列
            {
                StoryLine storyLine = new StoryLine
                {
                    ID = columns[0].Trim(),
                    Speaker = columns[1].Trim(),
                    HeadProfile = columns[2].Trim(),
                    CharLeft = columns[3].Trim(),
                    CharMid_Left = columns[4].Trim(),  // 新增：中左槽位
                    CharMid = columns[5].Trim(),
                    CharMid_Right = columns[6].Trim(), // 新增：中右槽位
                    CharRight = columns[7].Trim(),
                    Text = columns[8].Trim(),
                    Background = columns[9].Trim(),
                    BGM = columns[10].Trim(),
                    Voice = columns[11].Trim(),
                    Command = columns[12].Trim(),
                    Note = columns[13].Trim()
                };
                SplitConfirmSection(storyLine, columns[12].Trim());

                data.Lines.Add(storyLine);
                // 记录ID索引
                if (!string.IsNullOrEmpty(storyLine.ID))
                {
                    data.IDMap[storyLine.ID] = data.Lines.Count - 1;
                }
            }
            else if (columns.Length >= 12) // 旧 12 列格式（向后兼容：无 CharMid_Left / CharMid_Right 列）
            {
                StoryLine storyLine = new StoryLine
                {
                    ID = columns[0].Trim(),
                    Speaker = columns[1].Trim(),
                    HeadProfile = columns[2].Trim(), // 新增：HeadProfile 列
                    CharLeft = columns[3].Trim(),
                    CharMid_Left = "",                 // 旧格式无此列，按空槽处理（隐藏）
                    CharMid = columns[4].Trim(),
                    CharMid_Right = "",                // 旧格式无此列，按空槽处理（隐藏）
                    CharRight = columns[5].Trim(),
                    Text = columns[6].Trim(),
                    Background = columns[7].Trim(),
                    BGM = columns[8].Trim(),
                    Voice = columns[9].Trim(),
                    Command = columns[10].Trim(),
                    Note = columns[11].Trim()
                };
                SplitConfirmSection(storyLine, columns[10].Trim());

                data.Lines.Add(storyLine);
                // 记录ID索引
                if (!string.IsNullOrEmpty(storyLine.ID))
                {
                    data.IDMap[storyLine.ID] = data.Lines.Count - 1;
                }
            }
        }
        return data;
    }

    /// <summary>
    /// 正确分割CSV行，处理引号内的换行符
    /// 只有在引号外遇到换行符时才分割行
    /// </summary>
    private static string[] SplitCSVLines(string csvContent)
    {
        List<string> lines = new List<string>();
        bool inQuotes = false;
        StringBuilder currentLine = new StringBuilder();

        for (int i = 0; i < csvContent.Length; i++)
        {
            char c = csvContent[i];
            char nextChar = (i + 1 < csvContent.Length) ? csvContent[i + 1] : '\0';

            if (c == '"')
            {
                // 处理转义的双引号（两个连续的双引号表示一个双引号字符）
                if (inQuotes && nextChar == '"')
                {
                    currentLine.Append('"');
                    i++; // 跳过下一个双引号
                }
                else
                {
                    inQuotes = !inQuotes;
                    currentLine.Append(c);
                }
            }
            else if ((c == '\n' || c == '\r') && !inQuotes)
            {
                // 只有在引号外遇到换行符时才分割行
                // 处理 \r\n 的情况（Windows换行符）
                if (c == '\r' && nextChar == '\n')
                {
                    i++; // 跳过 \n
                }
                
                // 如果当前行不为空，添加到列表
                if (currentLine.Length > 0)
                {
                    lines.Add(currentLine.ToString());
                    currentLine.Clear();
                }
            }
            else
            {
                // 引号内的换行符或其他字符，直接添加到当前行
                currentLine.Append(c);
            }
        }

        // 添加最后一行（如果有内容）
        if (currentLine.Length > 0)
        {
            lines.Add(currentLine.ToString());
        }

        return lines.ToArray();
    }

    /// <summary>
    /// 分割CSV行中的各个字段，处理引号内的逗号
    /// </summary>
    private static string[] SplitCSV(string line)
    {
        List<string> fields = new List<string>();
        bool inQuotes = false;
        StringBuilder currentField = new StringBuilder();

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            char nextChar = (i + 1 < line.Length) ? line[i + 1] : '\0';

            if (c == '"')
            {
                // 处理转义的双引号（两个连续的双引号表示一个双引号字符）
                if (inQuotes && nextChar == '"')
                {
                    currentField.Append('"');
                    i++; // 跳过下一个双引号
                }
                else
                {
                    inQuotes = !inQuotes;
                    // 不添加引号本身到字段内容中（CSV标准）
                }
            }
            else if (c == ',' && !inQuotes)
            {
                // 只有在引号外遇到逗号时才分割字段
                fields.Add(currentField.ToString());
                currentField.Clear();
            }
            else
            {
                currentField.Append(c);
            }
        }
        
        // 添加最后一个字段
        fields.Add(currentField.ToString());
        return fields.ToArray();
    }

    // ==================== [Confirm 出口] Command 列切分 ====================

    /// <summary>@Confirm: 标记（大小写不敏感匹配）</summary>
    private const string ConfirmToken = "@confirm:";

    /// <summary>
    /// 将 Command 列原始内容按第一个 @Confirm: 切分为进入段（line.Command）与出口段（line.ConfirmCommands）。
    /// 写法：shake(0.5)&@Confirm:jump(1010) —— @Confirm: 之前为进入行时执行的命令，之后为用户确认推进时执行的命令。
    /// 规则：
    ///   · 未出现 @Confirm: 时 ConfirmCommands 为空，行为与旧剧本完全一致；
    ///   · 出现多个 @Confirm: 时报错，仅第一个生效（容错继续解析）；
    ///   · 出口段禁止 choice 命令（出口执行后面板尚未响应即被默认推进），进入段含 choice 时警告出口不会触发。
    /// </summary>
    private static void SplitConfirmSection(StoryLine line, string rawCommand)
    {
        line.Command = "";
        line.ConfirmCommands = "";
        if (string.IsNullOrEmpty(rawCommand)) return;

        int idx = rawCommand.ToLower().IndexOf(ConfirmToken);
        if (idx < 0)
        {
            line.Command = rawCommand; // 无出口段：保持旧语义
            return;
        }

        // 进入段：去掉 @Confirm: 前残留的分隔符 & 与空白
        line.Command = rawCommand.Substring(0, idx).Trim().TrimEnd('&').Trim();

        // 出口段：第一个 @Confirm: 之后的全部内容（含后续 &）
        string rest = rawCommand.Substring(idx + ConfirmToken.Length);

        int second = rest.ToLower().IndexOf(ConfirmToken);
        if (second >= 0)
        {
            Debug.LogError($"[ScriptParser] 行 {line.ID}: Command 列包含多个 @Confirm:（仅第一个生效），请移除多余标记。");
            rest = rest.Substring(0, second);
        }
        line.ConfirmCommands = rest.Trim();

        // 语义校验：choice 与 @Confirm 的互斥关系
        if (!string.IsNullOrEmpty(line.ConfirmCommands))
        {
            if (ContainsChoiceCommand(line.ConfirmCommands))
            {
                Debug.LogError($"[ScriptParser] 行 {line.ID}: @Confirm: 出口段不允许 choice 命令（出口执行后面板尚未响应即被默认推进），请将 choice 移至进入段。");
            }
            if (ContainsChoiceCommand(line.Command))
            {
                Debug.LogWarning($"[ScriptParser] 行 {line.ID}: 进入段含 choice 命令，Choice 状态会拦截普通点击，本行 @Confirm: 出口段将不会执行（choice 选项命令优先生效）。");
            }
        }
    }

    /// <summary>粗粒度检测命令串中是否含 choice 命令（与 VNManager.ContainsChoiceCommand 同规则）</summary>
    private static bool ContainsChoiceCommand(string commandString)
    {
        return !string.IsNullOrEmpty(commandString) && commandString.ToLower().Contains("choice(");
    }
}