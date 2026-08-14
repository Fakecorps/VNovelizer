using System.Collections.Generic;
using System.Text;
using UnityEngine;
using LitJson;

/// <summary>
/// 剧本解析工具类：支持 CSV 与 JSON 两种剧本格式，根据内容自动判断。
/// </summary>
public static class ScriptParser
{
    public class ScriptData
    {
        public List<StoryLine> Lines = new List<StoryLine>();
        public Dictionary<string, int> IDMap = new Dictionary<string, int>();
    }

    /// <summary>
    /// JSON 剧本容器。标准结构为 {"lines":[...]}。
    /// 每行字段名与 StoryLine 一致（大小写敏感）：
    /// ID, Speaker, HeadProfile, CharLeft, CharMid, CharRight,
    /// Text, Background, BGM, Voice, Command, Note
    /// 示例：
    /// { "lines": [ { "ID":"1001", "Speaker":"Amy", "CharLeft":"Amy_Normal",
    ///   "Text":"你好", "Background":"School_Day", "BGM":"Theme" } ] }
    /// </summary>
    [System.Serializable]
    public class JsonScriptContainer
    {
        public List<StoryLine> lines;
    }

    /// <summary>
    /// 解析剧本文件（根据内容自动判断 CSV / JSON）
    /// </summary>
    public static ScriptData Parse(string fileName)
    {
        // 从配置路径加载
        string configPath = VNProjectConfig.Instance.VNScriptResPath;
        string loadPath = configPath + "/" + fileName;
        Debug.Log($"[ScriptParser] 尝试加载剧本: {loadPath} (ConfigPath: {configPath}, FileName: {fileName})");

        TextAsset textAsset = Resources.Load<TextAsset>(loadPath);

        if (textAsset == null)
        {
            Debug.LogError($"[ScriptParser] 找不到剧本文件: {loadPath}");
            return null;
        }

        // JSON 以 { 或 [ 开头，其余按 CSV 解析
        string trimmed = textAsset.text.TrimStart();
        bool isJson = trimmed.StartsWith("{") || trimmed.StartsWith("[");
        return isJson ? ParseJson(textAsset.text) : ParseCsv(textAsset.text);
    }

    /// <summary>
    /// 解析 CSV 剧本
    /// </summary>
    private static ScriptData ParseCsv(string csvContent)
    {
        ScriptData data = new ScriptData();

        // 【修复】使用改进的行分割方法，正确处理引号内的换行符
        string[] lines = SplitCSVLines(csvContent);
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
            if (columns.Length >= 12) // 增加了 HeadProfile 列，现在需要 12 列
            {
                StoryLine storyLine = new StoryLine
                {
                    ID = columns[0].Trim(),
                    Speaker = columns[1].Trim(),
                    HeadProfile = columns[2].Trim(), // 新增：HeadProfile 列
                    CharLeft = columns[3].Trim(),
                    CharMid = columns[4].Trim(),
                    CharRight = columns[5].Trim(),
                    Text = columns[6].Trim(),
                    Background = columns[7].Trim(),
                    BGM = columns[8].Trim(),
                    Voice = columns[9].Trim(),
                    Command = columns[10].Trim(),
                    Note = columns[11].Trim()
                };

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
    /// 解析 JSON 剧本
    /// </summary>
    private static ScriptData ParseJson(string jsonContent)
    {
        ScriptData data = new ScriptData();

        List<StoryLine> lines = TryParseJsonLines(jsonContent);
        if (lines == null)
        {
            Debug.LogError("[ScriptParser] JSON 剧本缺少 lines 数组，请使用 {\"lines\":[...]} 结构");
            return null;
        }

        foreach (StoryLine line in lines)
        {
            if (line == null) continue;
            data.Lines.Add(line);
            if (!string.IsNullOrEmpty(line.ID))
            {
                data.IDMap[line.ID] = data.Lines.Count - 1;
            }
        }
        return data;
    }

    /// <summary>
    /// 将 JSON 文本解析为剧本行列表；解析失败或不是剧本结构（无 lines 数组）时返回 null。
    /// 供运行时解析与编辑器优化工具复用。
    /// </summary>
    public static List<StoryLine> TryParseJsonLines(string jsonText)
    {
        if (string.IsNullOrEmpty(jsonText)) return null;

        JsonScriptContainer container;
        try
        {
            container = JsonMapper.ToObject<JsonScriptContainer>(jsonText);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[ScriptParser] JSON 剧本解析失败: {e.Message}");
            return null;
        }

        if (container == null || container.lines == null) return null;
        return container.lines;
    }

    /// <summary>
    /// 将剧本行序列化为带缩进的 JSON 文本（{"lines":[...]}）。
    /// </summary>
    public static string SerializeJsonLines(List<StoryLine> lines)
    {
        var container = new JsonScriptContainer { lines = lines };
        var writer = new JsonWriter { PrettyPrint = true };
        JsonMapper.ToJson(container, writer);
        return writer.ToString();
    }

    /// <summary>
    /// 按 ID 从小到大排序剧本行（数值优先比较，非数值回退字符串比较）。
    /// </summary>
    public static void SortLinesById(List<StoryLine> lines)
    {
        lines.Sort(CompareLineById);
    }

    private static int CompareLineById(StoryLine a, StoryLine b)
    {
        string idA = a != null ? a.ID ?? "" : "";
        string idB = b != null ? b.ID ?? "" : "";

        bool numA = long.TryParse(idA, out long valA);
        bool numB = long.TryParse(idB, out long valB);

        if (numA && numB) return valA.CompareTo(valB);
        if (numA != numB) return numA ? -1 : 1; // 数字排在非数字前
        return string.CompareOrdinal(idA, idB);
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
}
