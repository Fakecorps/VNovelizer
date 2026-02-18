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