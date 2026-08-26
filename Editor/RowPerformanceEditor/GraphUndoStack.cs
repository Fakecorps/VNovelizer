using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VNovelizer.Core.Commands.Chain;

namespace VNovelizer.Editor.RowPerformanceEditor
{
    /// <summary>
    /// 撤销栈（决策 s9）：以「命令链文本 + 节点位置 + 折叠状态」快照为单位，
    /// Ctrl+Z 反序重建整张图。
    ///
    /// <para>
    /// <b>为何不接 Unity 原生 Undo</b>：GraphView + <c>Undo.RecordObject</c> 是公认难点——
    /// 节点是 VisualElement 而非 UnityEngine.Object，需额外包一层 ScriptableObject 代理，
    /// 且容易出现"撤销后残留幽灵节点""视图与数据不同步"。自建文本快照栈虽然粒度粗
    /// （整图重建、选中态丢失），但正确性可控，实现量是前者的几分之一。
    /// </para>
    ///
    /// <para>
    /// 快照单位是"整行"，因此**切换行时清空栈**——撤销不跨行，
    /// 否则用户在 A 行按撤销却改动了 B 行，是灾难性的意外。
    /// </para>
    /// </summary>
    public class GraphUndoStack
    {
        /// <summary>一次快照。</summary>
        public class Snapshot
        {
            public string EntryChainText;
            public string ConfirmChainText;
            public Dictionary<string, Vector2> Positions;
            public bool TemplateCollapsed;

            /// <summary>操作描述（供状态栏提示"已撤销：添加 shake 节点"）</summary>
            public string Label;
        }

        private readonly List<Snapshot> _undo = new List<Snapshot>();
        private readonly List<Snapshot> _redo = new List<Snapshot>();

        /// <summary>栈深上限：行级编辑不需要无限历史，超出即丢弃最旧的。</summary>
        private const int MaxDepth = 64;

        public bool CanUndo => _undo.Count > 1; // 栈底是初始状态，不可再撤销
        public bool CanRedo => _redo.Count > 0;

        public int Depth => _undo.Count;

        /// <summary>压入一次快照（每次图变更后调用）。</summary>
        public void Push(Snapshot snapshot)
        {
            if (snapshot == null) return;

            // 与栈顶完全相同则不入栈——避免"点一下没改任何东西"也占用一次撤销
            if (_undo.Count > 0 && IsSame(_undo[_undo.Count - 1], snapshot)) return;

            _undo.Add(snapshot);
            _redo.Clear(); // 新操作使重做链失效

            while (_undo.Count > MaxDepth) _undo.RemoveAt(0);
        }

        /// <summary>撤销一步，返回应恢复到的快照（null 表示无法撤销）。</summary>
        public Snapshot Undo()
        {
            if (!CanUndo) return null;

            var current = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);
            _redo.Add(current);

            return _undo[_undo.Count - 1];
        }

        /// <summary>重做一步。</summary>
        public Snapshot Redo()
        {
            if (!CanRedo) return null;

            var snapshot = _redo[_redo.Count - 1];
            _redo.RemoveAt(_redo.Count - 1);
            _undo.Add(snapshot);

            return snapshot;
        }

        /// <summary>清空（切换行时必须调用——撤销不跨行）。</summary>
        public void Clear()
        {
            _undo.Clear();
            _redo.Clear();
        }

        /// <summary>栈顶快照的操作描述。</summary>
        public string TopLabel => _undo.Count > 0 ? _undo[_undo.Count - 1].Label : null;

        private static bool IsSame(Snapshot a, Snapshot b)
        {
            if (a.EntryChainText != b.EntryChainText) return false;
            if (a.ConfirmChainText != b.ConfirmChainText) return false;
            if (a.TemplateCollapsed != b.TemplateCollapsed) return false;

            if (a.Positions == null || b.Positions == null)
                return a.Positions == b.Positions;
            if (a.Positions.Count != b.Positions.Count) return false;

            foreach (var pair in a.Positions)
            {
                if (!b.Positions.TryGetValue(pair.Key, out var other)) return false;
                if ((pair.Value - other).sqrMagnitude > 0.01f) return false;
            }

            return true;
        }
    }

    /// <summary>
    /// 命令链的复制 / 粘贴（决策 s10）。
    ///
    /// <para>
    /// 实现即"命令链文本进系统剪贴板"——因此可跨剧本、跨 Unity 实例，
    /// 甚至可粘到聊天窗口发给同事。这个朴素做法解决了最痛的重复手画问题
    /// （同一套转场编排要用在几十行），成本却只有几行代码。
    /// 命名编排预设库排 Phase 2。
    /// </para>
    /// </summary>
    public static class ChainClipboard
    {
        /// <summary>剪贴板内容的前缀标记：避免把无关文本误判为命令链。</summary>
        private const string Marker = "#VNChain ";

        /// <summary>复制两段链到系统剪贴板。</summary>
        public static void Copy(string entryChain, string confirmChain)
        {
            string text = entryChain ?? "";
            if (!string.IsNullOrWhiteSpace(confirmChain))
                text += "@Confirm:" + confirmChain.Trim();

            EditorGUIUtility.systemCopyBuffer = Marker + text;
        }

        /// <summary>剪贴板中是否有可粘贴的命令链。</summary>
        public static bool HasContent()
        {
            string buffer = EditorGUIUtility.systemCopyBuffer;
            return !string.IsNullOrEmpty(buffer) && buffer.StartsWith(Marker);
        }

        /// <summary>
        /// 从剪贴板取出两段链。返回 false 表示剪贴板内容不是命令链。
        /// </summary>
        public static bool TryPaste(out string entryChain, out string confirmChain)
        {
            entryChain = "";
            confirmChain = "";

            string buffer = EditorGUIUtility.systemCopyBuffer;
            if (string.IsNullOrEmpty(buffer) || !buffer.StartsWith(Marker)) return false;

            string text = buffer.Substring(Marker.Length);

            int idx = text.IndexOf("@Confirm:", System.StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
            {
                entryChain = text.Trim();
                return true;
            }

            entryChain = text.Substring(0, idx).Trim().TrimEnd('&').Trim();
            confirmChain = text.Substring(idx + "@Confirm:".Length).Trim();
            return true;
        }

        /// <summary>
        /// 校验剪贴板内容能否被解析——粘贴前先验证，避免把坏文本灌进图里。
        /// </summary>
        public static bool ValidatePasteContent(out string error)
        {
            error = null;

            if (!TryPaste(out string entry, out string confirm))
            {
                error = "剪贴板中没有命令链内容。请先在某一行点击「复制链」。";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(entry))
            {
                var parsed = ChainParser.Parse(entry);
                if (!parsed.Success)
                {
                    error = "剪贴板中的进入段无法解析：" + string.Join("; ", parsed.Errors);
                    return false;
                }
            }

            if (!string.IsNullOrWhiteSpace(confirm))
            {
                var parsed = ChainParser.Parse(confirm);
                if (!parsed.Success)
                {
                    error = "剪贴板中的出口段无法解析：" + string.Join("; ", parsed.Errors);
                    return false;
                }
            }

            return true;
        }
    }
}
