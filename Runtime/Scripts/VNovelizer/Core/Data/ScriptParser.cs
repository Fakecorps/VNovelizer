using System.Collections.Generic;
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

        string[] lines = csvFile.text.Split('\n');
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

    private static string[] SplitCSV(string line)
    {
        List<string> fields = new List<string>();
        bool inQuotes = false;
        string currentField = "";

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == ',' && !inQuotes)
            {
                fields.Add(currentField);
                currentField = "";
            }
            else
            {
                currentField += c;
            }
        }
        fields.Add(currentField);
        return fields.ToArray();
    }
}