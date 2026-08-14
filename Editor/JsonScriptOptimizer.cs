using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// JSON 剧本优化工具：将剧本目录中的 JSON 剧本按行 ID 从小到大排序并重写文件。
/// 仅处理包含 lines 数组的剧本 JSON，跳过其他 JSON 文件。
/// </summary>
public static class JsonScriptOptimizer
{
    [MenuItem("VNovelizer/JSON 剧本优化（按 ID 排序）")]
    public static void OptimizeAll()
    {
        var config = VNProjectConfig.Instance;
        if (config == null || string.IsNullOrEmpty(config.VNScriptResPath))
        {
            EditorUtility.DisplayDialog("JSON 优化", "未找到 VNProjectConfig 或 VNScriptResPath 未配置。", "确定");
            return;
        }

        // 剧本 JSON 与 CSV 同目录（Assets/Resources 下），由 VNScriptResPath 决定
        string folder = Path.Combine(Application.dataPath, "Resources", config.VNScriptResPath);
        if (!Directory.Exists(folder))
        {
            EditorUtility.DisplayDialog("JSON 优化", $"剧本目录不存在：\n{folder}", "确定");
            return;
        }

        string[] files = Directory.GetFiles(folder, "*.json", SearchOption.AllDirectories);

        int sortedCount = 0, skippedCount = 0, failedCount = 0;
        foreach (string file in files)
        {
            try
            {
                string text = File.ReadAllText(file);
                List<StoryLine> lines = ScriptParser.TryParseJsonLines(text);
                if (lines == null)
                {
                    skippedCount++; // 不是剧本结构（无 lines 数组）的 JSON，跳过
                    continue;
                }

                List<StoryLine> sorted = new List<StoryLine>(lines);
                ScriptParser.SortLinesById(sorted);

                if (SameOrder(lines, sorted)) continue;

                File.WriteAllText(file, ScriptParser.SerializeJsonLines(sorted), new UTF8Encoding(false));
                sortedCount++;
            }
            catch (System.Exception e)
            {
                failedCount++;
                Debug.LogError($"[JsonScriptOptimizer] 处理失败 {file}: {e.Message}");
            }
        }

        AssetDatabase.Refresh();

        string summary = $"优化完成：排序 {sortedCount} 个，跳过 {skippedCount} 个（非剧本 JSON），失败 {failedCount} 个。";
        Debug.Log($"[JsonScriptOptimizer] {summary}");
        EditorUtility.DisplayDialog("JSON 优化", summary, "确定");
    }

    /// <summary>
    /// 判断两个列表顺序是否一致（元素引用逐个相同）。排序前后无变化时跳过重写。
    /// </summary>
    private static bool SameOrder(List<StoryLine> a, List<StoryLine> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (!ReferenceEquals(a[i], b[i])) return false;
        }
        return true;
    }
}
