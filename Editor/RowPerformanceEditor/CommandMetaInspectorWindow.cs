using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VNovelizer.Core.Commands.Meta;

namespace VNovelizer.Editor
{
    /// <summary>
    /// 命令元数据检查器：可视化 <see cref="CommandMetaReader"/> 的反射结果。
    ///
    /// 两个用途：
    /// ① **验证节点化契约**——确认 [VNCommandMeta]/[VNParam] 被正确读取、
    ///    反射推导特征（IsAsync / HasSimulate / IsFlowCommand）与实现一致；
    /// ② **标注进度看板**——一眼看出哪些命令仍是「通用节点」形态待标注。
    ///
    /// 行演出编辑器的命令面板将复用同一份数据源。
    /// </summary>
    public class CommandMetaInspectorWindow : EditorWindow
    {
        private Vector2 _scroll;
        private string _search = "";
        private bool _onlyUnannotated;
        private readonly Dictionary<string, bool> _expanded = new Dictionary<string, bool>();

        [MenuItem("VNovelizer/命令元数据检查器", false, 220)]
        public static void Open()
        {
            var win = GetWindow<CommandMetaInspectorWindow>("命令元数据");
            win.minSize = new Vector2(520, 400);
            win.Show();
        }

        private void OnGUI()
        {
            DrawToolbar();

            var all = CommandMetaReader.GetAll();
            if (all.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "未读取到任何命令。请确认 CommandManager 可正常初始化（部分命令的构造函数可能依赖运行时管理器）。",
                    MessageType.Warning);
                return;
            }

            DrawSummary(all);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (var group in CommandMetaReader.GetGrouped()
                         .OrderBy(g => (int)g.Key))
            {
                var items = group.Value.Where(Matches).ToList();
                if (items.Count == 0) continue;

                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField($"{CategoryLabel(group.Key)}（{items.Count}）",
                    EditorStyles.boldLabel);

                foreach (var info in items) DrawCommand(info);
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            _search = EditorGUILayout.TextField(_search, EditorStyles.toolbarSearchField);
            _onlyUnannotated = GUILayout.Toggle(_onlyUnannotated, "仅未标注",
                EditorStyles.toolbarButton, GUILayout.Width(72));
            if (GUILayout.Button("刷新", EditorStyles.toolbarButton, GUILayout.Width(48)))
            {
                CommandMetaReader.Invalidate();
                Repaint();
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawSummary(IReadOnlyDictionary<string, VNCommandInfo> all)
        {
            int total = all.Count;
            int annotated = all.Values.Count(i => i.HasMeta);
            int noSimulate = all.Values.Count(i => !i.HasSimulate);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            float pct = total == 0 ? 0f : (float)annotated / total;
            Rect r = GUILayoutUtility.GetRect(18, 18, GUILayout.ExpandWidth(true));
            EditorGUI.ProgressBar(r, pct, $"节点化标注进度 {annotated} / {total}（{pct:P0}）");
            EditorGUILayout.LabelField(
                $"未标注 {total - annotated} 个 → 图编辑器渲染为「通用节点」（功能完整，可连线/拖拽/序列化）",
                EditorStyles.miniLabel);
            EditorGUILayout.LabelField(
                $"无 Simulate 实现 {noSimulate} 个 → 纯演出/流程命令属正常；若含状态变更命令需补 Simulate",
                EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
        }

        private bool Matches(VNCommandInfo info)
        {
            if (_onlyUnannotated && info.HasMeta) return false;
            if (string.IsNullOrEmpty(_search)) return true;
            return info.Name.IndexOf(_search, System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void DrawCommand(VNCommandInfo info)
        {
            if (!_expanded.ContainsKey(info.Name)) _expanded[info.Name] = false;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();
            _expanded[info.Name] = EditorGUILayout.Foldout(
                _expanded[info.Name], info.Signature, true, EditorStyles.foldout);

            GUILayout.FlexibleSpace();
            if (!info.HasMeta) Tag("⚙ 通用节点", new Color(0.85f, 0.78f, 0.55f));
            if (info.Planned) Tag("⏳ 未实现", new Color(0.7f, 0.7f, 0.75f));
            if (info.IsFlowCommand) Tag("链尾", new Color(0.9f, 0.78f, 0.5f));
            if (info.IsAsync) Tag("async", new Color(0.6f, 0.78f, 0.9f));
            if (info.HasSimulate) Tag("Sim", new Color(0.6f, 0.85f, 0.55f));
            if (info.HasInterrupt) Tag("Intr", new Color(0.75f, 0.7f, 0.9f));
            EditorGUILayout.EndHorizontal();

            if (_expanded[info.Name])
            {
                EditorGUI.indentLevel++;
                if (!string.IsNullOrEmpty(info.Description))
                    EditorGUILayout.LabelField(info.Description, EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.LabelField("实现类型", info.ImplType.Name, EditorStyles.miniLabel);

                if (info.Parameters.Count > 0)
                {
                    EditorGUILayout.LabelField("参数", EditorStyles.miniBoldLabel);
                    foreach (var p in info.Parameters) DrawParam(p);
                }
                else if (info.HasMeta)
                {
                    EditorGUILayout.LabelField("（无参数）", EditorStyles.miniLabel);
                }
                else
                {
                    EditorGUILayout.LabelField(
                        "未标注 [VNParam] → Inspector 退回单行原始参数文本框",
                        EditorStyles.miniLabel);
                }
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawParam(VNParamInfo p)
        {
            var parts = new List<string> { p.Type.ToString() };
            if (p.HasRange) parts.Add($"[{p.Min}~{p.Max}]");
            if (p.Options.Length > 0) parts.Add(string.Join("|", p.Options));
            if (!string.IsNullOrEmpty(p.Default)) parts.Add($"默认 {p.Default}");
            if (p.Optional) parts.Add("可选");
            if (p.ImplicitBinding) parts.Add($"📎 引用 {p.BoundColumn} 列");
            if (p.InlineForbidden) parts.Add("禁止内联");

            EditorGUILayout.LabelField($"  {p.Index}. {p.Name}",
                string.Join(" · ", parts), EditorStyles.miniLabel);
            if (!string.IsNullOrEmpty(p.Description))
                EditorGUILayout.LabelField("    " + p.Description, EditorStyles.centeredGreyMiniLabel);
        }

        private static void Tag(string text, Color color)
        {
            var prev = GUI.color;
            GUI.color = color;
            GUILayout.Label(text, EditorStyles.miniButton, GUILayout.Width(
                EditorStyles.miniButton.CalcSize(new GUIContent(text)).x + 6));
            GUI.color = prev;
        }

        private static string CategoryLabel(VNCommandCategory c)
        {
            switch (c)
            {
                case VNCommandCategory.System:      return "系统命令（数据列隐式驱动）";
                case VNCommandCategory.Performance: return "演出命令";
                case VNCommandCategory.Flow:        return "流程命令（仅可置于链尾）";
                case VNCommandCategory.Logic:       return "逻辑与变量";
                case VNCommandCategory.Audio:       return "音频";
                default:                            return "其他 / 未分类";
            }
        }
    }
}
