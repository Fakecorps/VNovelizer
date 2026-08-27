using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using VNovelizer.Core.Commands.Chain;

namespace VNovelizer.Editor
{
    /// <summary>
    /// 命令链往返一致性验证器：文本 → AST → 图 → AST → 文本 的闭环校验。
    ///
    /// <para>
    /// <b>为何需要它</b>：<see cref="GraphToAst"/> 的 SP 分解与
    /// <see cref="ChainSerializer"/> 的括号规则都是"看起来对"但极易出错的逻辑
    /// （例如 <c>&amp;</c> 优先级高于 <c>-&gt;</c> 导致 <c>a-&gt;b &amp; c-&gt;d</c>
    /// 被解析成完全不同的结构）。这类正确性必须**可执行验证**，不能靠推理。
    /// </para>
    ///
    /// <para>
    /// 用例覆盖：纯串行 / 纯并行 / 分支内串行（最易错）/ 嵌套 / 单命令 /
    /// 系统命令空参 / 引号参数 / 流程命令链尾。
    /// </para>
    /// </summary>
    public class ChainRoundTripTestWindow : EditorWindow
    {
        private Vector2 _scroll;
        private readonly List<CaseResult> _results = new List<CaseResult>();
        private bool _hasRun;

        private struct CaseResult
        {
            public string Input;
            public string TextToAstToText;   // 文本 → AST → 文本
            public string ViaGraph;          // 文本 → AST → 图 → AST → 文本
            public bool AstEqual;            // 原 AST 与序列化再解析的 AST 是否等价
            public bool GraphRoundTripEqual; // 经图往返后是否仍等价
            public string Note;
        }

        /// <summary>测试用例：覆盖各类结构，重点是易错的"分支内串行"。</summary>
        private static readonly string[] TestCases =
        {
            // 单命令（不应产生多余括号）
            "wait(0.5)",
            // 纯串行
            "showbg(Beach) -> wait(0.5) -> shake(screen,0.3)",
            // 纯并行
            "charfadein(L,Amy#uniform#Smile,1) & charfadein(R,Bob#casual#Idle,1)",
            // 【最易错】分支内串行：必须序列化为 [a->b] & [c->d]
            "[charmove(M,100,0,0.5) -> setexpression(Amy,uniform,happy)] & [wait(0.5) -> showprompt(\"注意\")]",
            // 串行中夹并行（Par 作为 Seq 子项，无需括号）
            "showbg(Room) -> charfadein(L,Amy#uniform#Smile,1) & playbgm(tension) -> wait(1)",
            // 嵌套分组
            "[[wait(0.2) & shake(screen,0.3)] -> charjump(M)] & fadeBlackOut(1.0)",
            // 系统命令空参（隐式绑定形式必须保留空括号）
            "showbg() & showchar(L) & showspeaker() & showdialogue(typewriter) & playbgm() & playvoice()",
            // 引号参数含分隔符
            "showprompt(\"a & b -> c\") -> wait(1)",
            // 流程命令置于链尾
            "shake(screen,0.3) -> jump(2001)",
            // 默认模板结构（双分支 Par，与 DefaultPerformanceTemplate 一致：无 showSpeaker）
            "showdialogue(typewriter) & [showbg() & showchar(L) & playbgm() -> shake(screen,0.3)]",
        };

        [MenuItem("VNovelizer/命令链往返一致性验证", false, 221)]
        public static void Open()
        {
            var win = GetWindow<ChainRoundTripTestWindow>("命令链往返验证");
            win.minSize = new Vector2(620, 420);
            win.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (GUILayout.Button("运行全部用例", EditorStyles.toolbarButton, GUILayout.Width(110)))
                RunAll();
            GUILayout.FlexibleSpace();
            if (_hasRun)
            {
                int pass = 0;
                foreach (var r in _results) if (r.AstEqual && r.GraphRoundTripEqual) pass++;
                GUILayout.Label($"通过 {pass} / {_results.Count}", EditorStyles.miniLabel);
            }
            EditorGUILayout.EndHorizontal();

            if (!_hasRun)
            {
                EditorGUILayout.HelpBox(
                    "验证链路：\n" +
                    "① 文本 → ChainParser → AST → ChainSerializer → 文本 → 反解析，比对 AST 结构等价\n" +
                    "② AST → 图（AstToGraph）→ GraphToAst → AST，比对结构等价\n\n" +
                    "②覆盖的是 SP 分解与括号规则的正确性——它们是图编辑器保存链路的核心。",
                    MessageType.Info);
                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (var r in _results) DrawCase(r);
            EditorGUILayout.EndScrollView();
        }

        private void DrawCase(CaseResult r)
        {
            bool ok = r.AstEqual && r.GraphRoundTripEqual;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            var prev = GUI.color;
            GUI.color = ok ? new Color(0.6f, 0.9f, 0.55f) : new Color(1f, 0.55f, 0.5f);
            EditorGUILayout.LabelField((ok ? "[OK] " : "[X] ") + r.Input, EditorStyles.boldLabel);
            GUI.color = prev;

            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField("序列化", r.TextToAstToText, EditorStyles.miniLabel);
            EditorGUILayout.LabelField("经图往返", r.ViaGraph, EditorStyles.miniLabel);
            if (!string.IsNullOrEmpty(r.Note))
                EditorGUILayout.LabelField("说明", r.Note, EditorStyles.wordWrappedMiniLabel);
            EditorGUI.indentLevel--;

            EditorGUILayout.EndVertical();
        }

        private void RunAll()
        {
            _results.Clear();
            _hasRun = true;

            foreach (string input in TestCases)
                _results.Add(RunCase(input));
        }

        private static CaseResult RunCase(string input)
        {
            var result = new CaseResult { Input = input };
            var notes = new StringBuilder();

            // ---- ① 文本 → AST → 文本 ----
            var parsed = ChainParser.Parse(input);
            if (!parsed.Success || parsed.Root == null)
            {
                result.Note = "输入无法解析：" + string.Join("; ", parsed.Errors);
                return result;
            }

            var serialized = ChainSerializer.SerializeAndVerify(parsed.Root);
            result.TextToAstToText = serialized.Text;
            result.AstEqual = serialized.Success;
            if (!serialized.Success) notes.Append(string.Join("; ", serialized.Errors)).Append(' ');

            // ---- ② AST → 图 → AST → 文本 ----
            var graph = AstToGraph.Convert(parsed.Root);
            var back = GraphToAst.Convert(graph);

            if (!back.Success)
            {
                notes.Append("图分解失败：").Append(
                    back.Errors.Count > 0 ? string.Join("; ", back.Errors) : "(未提供错误信息)");
                result.Note = notes.ToString();
                return result;
            }

            if (back.Root == null)
            {
                notes.Append($"图分解未产出 AST（图含 {graph.NodeCount} 个节点，疑似分解逻辑缺陷）");
                result.Note = notes.ToString();
                return result;
            }

            result.ViaGraph = ChainSerializer.Serialize(back.Root);
            result.GraphRoundTripEqual = ChainSerializer.AreStructurallyEqual(parsed.Root, back.Root);

            if (!result.GraphRoundTripEqual)
                notes.Append("经图往返后结构不等价（SP 分解或括号规则有误）");

            result.Note = notes.ToString();
            return result;
        }
    }
}
