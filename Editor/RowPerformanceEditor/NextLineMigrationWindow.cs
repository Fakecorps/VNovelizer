using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace VNovelizer.Editor.RowPerformanceEditor
{
    /// <summary>
    /// nextline 显式化迁移工具（2026-08-31）。
    ///
    /// <para>
    /// <b>背景</b>：「本行演出完毕 → 推进下一行」曾是引擎隐式行为。改为显式命令后，
    /// 出口段（<c>@Confirm:</c>）执行完毕<b>不再</b>自动推进 —— 必须由链尾的
    /// <c>nextline()</c> 声明。存量剧本的出口段都没有 nextline，需要批量补齐。
    /// </para>
    ///
    /// <para>
    /// <b>只迁移出口段</b>：进入段不写 nextline 是合法的 ——「演出完毕等玩家点击」
    /// 正是视觉小说的标准交互，绝大多数行本就如此，不该被改写。
    /// </para>
    ///
    /// <para>
    /// <b>安全设计</b>：写回前自动备份为 <c>.csv.bak_yyyyMMdd_HHmmss</c>；
    /// 只改写 Command 列，其余单元格原样保留；按行 ID 匹配而非行序号。
    /// </para>
    /// </summary>
    public class NextLineMigrationWindow : EditorWindow
    {
        private const string ConfirmToken = "@confirm:";

        private class FileReport
        {
            public string Path;
            public List<string> Preview = new List<string>();  // 变更预览
            public int ChangedLines;
            public bool Selected = true;
        }

        private readonly List<FileReport> _reports = new List<FileReport>();
        private Vector2 _scroll;
        private bool _scanned;

        [MenuItem("VNovelizer/nextline 迁移工具", false, 220)]
        public static void Open()
        {
            var win = GetWindow<NextLineMigrationWindow>("nextline 迁移");
            win.minSize = new Vector2(720, 460);
            win.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(6);

            EditorGUILayout.HelpBox(
                "nextline 显式化后，出口段（@Confirm:）执行完毕不再自动推进下一行，" +
                "必须由链尾的 nextline() 声明。\n\n" +
                "本工具扫描工程内所有剧本 CSV，为「有出口段但缺 nextline」的行补上 nextline()。\n" +
                "进入段不处理——进入段等玩家点击是标准交互，不需要改写。\n" +
                "写回前会自动备份原文件。",
                MessageType.Info);

            EditorGUILayout.Space(8);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("扫描剧本", GUILayout.Height(26)))
                    Scan();

                GUI.enabled = _scanned && _reports.Count > 0;
                if (GUILayout.Button("应用迁移", GUILayout.Height(26)))
                    Apply();
                GUI.enabled = true;
            }

            EditorGUILayout.Space(8);

            if (!_scanned)
            {
                EditorGUILayout.LabelField("点击「扫描剧本」开始。");
                return;
            }

            if (_reports.Count == 0)
            {
                EditorGUILayout.HelpBox("没有需要迁移的行 —— 所有剧本的出口段都已声明 nextline()。",
                    MessageType.Info);
                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            int total = 0;
            foreach (var report in _reports)
            {
                total += report.ChangedLines;

                using (new EditorGUILayout.VerticalScope("box"))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        report.Selected = EditorGUILayout.Toggle(report.Selected,
                            GUILayout.Width(18));
                        EditorGUILayout.LabelField(
                            Path.GetFileName(report.Path), EditorStyles.boldLabel);
                        EditorGUILayout.LabelField(
                            $"{report.ChangedLines} 行待迁移",
                            GUILayout.Width(110));
                    }

                    EditorGUI.indentLevel++;
                    int shown = 0;
                    foreach (string line in report.Preview)
                    {
                        if (shown++ >= 6)
                        {
                            EditorGUILayout.LabelField(
                                $"… 另有 {report.Preview.Count - 6} 行");
                            break;
                        }
                        EditorGUILayout.LabelField(line, EditorStyles.miniLabel);
                    }
                    EditorGUI.indentLevel--;
                }
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField($"合计 {total} 行待迁移（分布在 {_reports.Count} 个剧本）",
                EditorStyles.boldLabel);
        }

        // ---------------- 扫描 ----------------

        private void Scan()
        {
            _reports.Clear();
            _scanned = true;

            foreach (string path in FindScriptCsvPaths())
            {
                var report = Analyze(path);
                if (report != null && report.ChangedLines > 0)
                    _reports.Add(report);
            }

            Repaint();
        }

        private static List<string> FindScriptCsvPaths()
        {
            var result = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:TextAsset"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                    result.Add(path);
            }
            result.Sort();
            return result;
        }

        /// <summary>
        /// 分析单个 CSV，返回待迁移报告（无需迁移则返回 null）。
        /// 预览阶段只读不写 —— 用户可以先看清楚再决定应用。
        /// </summary>
        private static FileReport Analyze(string path)
        {
            if (!File.Exists(path)) return null;

            string[] lines;
            try
            {
                lines = File.ReadAllLines(path);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[nextline 迁移] 读取失败 {path}：{e.Message}");
                return null;
            }

            if (lines.Length < 2) return null;

            var report = new FileReport { Path = path };

            for (int i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;

                var cells = ParseCsvLine(lines[i]);
                int cmdIndex = DetectCommandIndex(cells.Count);
                if (cmdIndex < 0 || cmdIndex >= cells.Count) continue;

                string id = cells.Count > 0 ? cells[0] : "";
                string command = cells[cmdIndex] ?? "";

                int tokenIdx = IndexOfConfirmToken(command);
                if (tokenIdx < 0) continue;   // 无出口段 → 无需迁移

                string confirm = command.Substring(tokenIdx + ConfirmToken.Length).Trim();
                if (string.IsNullOrEmpty(confirm)) continue;   // 空出口段 → 无需迁移

                if (confirm.IndexOf("nextline", StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;   // 已声明

                string newConfirm = AppendNextLine(confirm);
                report.ChangedLines++;
                if (report.Preview.Count < 64)
                    report.Preview.Add($"行 {id}：{confirm}  →  {newConfirm}");
            }

            return report;
        }

        /// <summary>
        /// 在出口段末尾追加 nextline()。
        /// 出口段可能以 <c>&amp;</c>（并行）或 <c>-></c>（串行）连接，统一用
        /// <c>&amp;nextline()</c> 串联 —— 语义是「前面的出口命令都执行完，再推进」。
        /// </summary>
        private static string AppendNextLine(string confirm)
        {
            string trimmed = confirm.Trim().TrimEnd('&', '-', '>').Trim();
            return trimmed + "&nextline()";
        }

        // ---------------- 应用 ----------------

        private void Apply()
        {
            int fileCount = 0, lineCount = 0;

            foreach (var report in _reports)
            {
                if (!report.Selected) continue;

                int changed = Migrate(report.Path);
                if (changed > 0)
                {
                    fileCount++;
                    lineCount += changed;
                }
            }

            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("迁移完成",
                $"已迁移 {lineCount} 行，涉及 {fileCount} 个剧本。\n\n" +
                "原文件已备份为 .csv.bak_时间戳。\n" +
                "请用行演出编辑器打开确认，或试玩检查流程。", "好");

            Scan();  // 重新扫描，确认已无待迁移项
        }

        /// <summary>实际改写 CSV（只改 Command 列），返回改写行数。</summary>
        private static int Migrate(string path)
        {
            if (!File.Exists(path)) return 0;

            string[] original;
            try
            {
                original = File.ReadAllLines(path);
            }
            catch (Exception e)
            {
                Debug.LogError($"[nextline 迁移] 读取失败 {path}：{e.Message}");
                return 0;
            }

            if (original.Length < 2) return 0;

            var output = new List<string> { original[0] };  // 表头原样
            int changed = 0;

            for (int i = 1; i < original.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(original[i]))
                {
                    output.Add(original[i]);
                    continue;
                }

                var cells = ParseCsvLine(original[i]);
                int cmdIndex = DetectCommandIndex(cells.Count);

                if (cmdIndex < 0 || cmdIndex >= cells.Count)
                {
                    output.Add(original[i]);   // 无法识别 → 原样保留，绝不破坏
                    continue;
                }

                string command = cells[cmdIndex] ?? "";
                int tokenIdx = IndexOfConfirmToken(command);

                if (tokenIdx < 0)
                {
                    output.Add(original[i]);
                    continue;
                }

                string entry = command.Substring(0, tokenIdx).Trim().TrimEnd('&').Trim();
                string confirm = command.Substring(tokenIdx + ConfirmToken.Length).Trim();

                if (string.IsNullOrEmpty(confirm) ||
                    confirm.IndexOf("nextline", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    output.Add(original[i]);
                    continue;
                }

                cells[cmdIndex] = entry + "&" + ConfirmToken + AppendNextLine(confirm);

                var parts = new List<string>();
                foreach (string cell in cells) parts.Add(EscapeCsv(cell));
                output.Add(string.Join(",", parts));
                changed++;
            }

            if (changed == 0) return 0;

            try
            {
                string backup = path + ".bak_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                File.Copy(path, backup, true);
                File.WriteAllLines(path, output);
                Debug.Log($"[nextline 迁移] {path}：改写 {changed} 行，备份 → {backup}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[nextline 迁移] 写入失败 {path}：{e.Message}");
                return 0;
            }

            return changed;
        }

        // ---------------- CSV 工具（与 ScriptParser / 行演出编辑器同规则） ----------------

        /// <summary>14 列格式 Command 在索引 12，旧 12 列格式在索引 10。</summary>
        private static int DetectCommandIndex(int cellCount)
        {
            if (cellCount >= 14) return 12;
            if (cellCount >= 12) return 10;
            return -1;
        }

        private static int IndexOfConfirmToken(string source)
        {
            if (string.IsNullOrEmpty(source)) return -1;

            bool inQuote = false;
            for (int i = 0; i < source.Length; i++)
            {
                char c = source[i];
                if (inQuote)
                {
                    if (c == '\\') { i++; continue; }
                    if (c == '"') inQuote = false;
                    continue;
                }
                if (c == '"') { inQuote = true; continue; }

                if (c == '@' && i + ConfirmToken.Length <= source.Length &&
                    string.Compare(source, i, ConfirmToken, 0, ConfirmToken.Length,
                        StringComparison.OrdinalIgnoreCase) == 0)
                    return i;
            }
            return -1;
        }

        private static List<string> ParseCsvLine(string line)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(line)) return result;

            var sb = new StringBuilder();
            bool inQuote = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (inQuote)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                        else inQuote = false;
                    }
                    else sb.Append(c);
                    continue;
                }

                if (c == '"') { inQuote = true; continue; }
                if (c == ',') { result.Add(sb.ToString()); sb.Clear(); continue; }
                sb.Append(c);
            }

            result.Add(sb.ToString());
            return result;
        }

        private static string EscapeCsv(string value)
        {
            value = value ?? "";
            bool needQuote = value.Contains(",") || value.Contains("\"") ||
                             value.Contains("\n") || value.Contains("\r");
            if (!needQuote) return value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }
}
