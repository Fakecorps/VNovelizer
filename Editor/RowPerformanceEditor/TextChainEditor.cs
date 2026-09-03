using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;
using VNovelizer.Core.Commands;
using VNovelizer.Core.Commands.Chain;

namespace VNovelizer.Editor.RowPerformanceEditor
{
    /// <summary>
    /// 命令链文本编辑器（独立于 Inspector 的中部面板，2026-08-28 抽出）。
    ///
    /// <para><b>2026-09-03 合二为一重构</b>：原进入段 + 出口段两个独立编辑器
    /// 合并为单个文本框。出口段通过 <c>@Confirm:</c> 关键字分隔（单独成行，
    /// 大小写不敏感，与 <see cref="ScriptParser.SplitConfirmSection"/> 一致）。
    /// 合并后 UIToolkit 只有一个 IMGUIContainer，Tab 键不再切焦点到"出口段
    /// 编辑器"（原双编辑器方案的根本问题 —— 详见 <see cref="VnCodeEditorIMGUI"/>
    /// 的 OnFocusRestoreRequested 文档对四套失效方案的分析）。</para>
    ///
    /// <para><b>文本格式</b>：</para>
    /// <code>
    /// showbg(Beach)
    /// charfadein(L,Amy_Normal,1)
    /// @Confirm:
    /// nextline()
    /// </code>
    /// <para><c>@Confirm:</c> 之上是进入段，之下是出口段。无 <c>@Confirm:</c>
    /// 时整段为进入段（出口段为空，与旧剧本兼容）。</para>
    ///
    /// <para><b>视觉区分</b>：<c>@Confirm:</c> 标记行整行橙色着色，出口段
    /// （含标记行）加淡紫底色做视觉分组。词法着色（命令名蓝/字符串绿/数字金）
    /// 由 <see cref="VnCodeEditorIMGUI"/> 的 <c>Tokenize</c> 处理。</para>
    ///
    /// <para><b>拆分/合并</b>：内部按 <c>@Confirm:</c> 标记行拆分成 entry/confirm
    /// 两个字符串，分别触发 <see cref="OnChainTextChanged"/>，外部
    /// <see cref="RowPerfEditorWindow.HandleChainTextChanged"/> 接口无需改动。
    /// 合并时 entry + <c>\n@Confirm:\n</c> + confirm（confirm 为空时不加标记）。</para>
    /// </summary>
    public class TextChainEditor
    {
        /// <summary>文本被编辑（isConfirm, 新文本）——外部负责解析重建图。</summary>
        public event Action<bool, string> OnChainTextChanged;

        private readonly VisualElement _root;

        private string _entryText = "";
        private string _confirmText = "";

        private VNNodeViewBase _current;

        // ---- 防抖：避免每个 keystroke 都解析重建图 ----
        private const long DebounceMs = 200;
        private IVisualElementScheduledItem _debounce;

        /// <summary>代码编辑器的最小高度（合并后加大，容纳进入段+@Confirm:+出口段）。</summary>
        private const float EditorMinHeight = 240f;

        /// <summary>当前防抖是否已挂起（用于抑制重建后的 SetText 再次触发 change）。</summary>
        private bool _suppressEcho;

        /// <summary>编辑器持有焦点期间暂存的规范化合并文本（失焦后落回）。</summary>
        private string _pendingText;

        private VnCodeEditorIMGUI _editor;

        public TextChainEditor(VisualElement root)
        {
            _root = root;
            _root.AddToClassList("vn-textchain-panel");
            Rebuild();
        }

        /// <summary>
        /// 由外部（节点图变化 / 切行 / 解析回填）写入规范化文本。
        ///
        /// <para>
        /// <b>关键约束（2026-08-31）</b>：目标编辑器正持有焦点时绝不直接覆写。
        /// 防抖重建图后序列化出的规范化文本与用户刚敲进去的原始输入几乎必然不同
        /// （空格、换行位置被规范化），直接回填会把用户正在输入的字符顶掉、
        /// 光标跳到别处 —— 即"打字打着就闪退"的现象。
        /// 因此：打字期间把规范化文本暂存，等失焦（<see cref="VnCodeEditorIMGUI.OnFocusLost"/>）再落回。
        /// </para>
        ///
        /// <para><b>2026-09-03 合并后</b>：entry + confirm 先拼成单文本（@Confirm: 分隔），
        /// 再整体写入唯一编辑器。</para>
        /// </summary>
        public void SetTexts(string entry, string confirm)
        {
            if (entry != null) _entryText = entry;
            if (confirm != null) _confirmText = confirm;

            if (_editor != null)
            {
                // 2026-09-03：entry 和 confirm 都为 null 时表示"不改动文本"
                // （HandleChainTextChanged 解析失败时的语义）。此时不写入编辑器，
                // 避免用缓存值覆盖用户正在输入的半成品。
                if (entry == null && confirm == null) return;

                if (_editor.HasFocus) _pendingText = MergeTexts(_entryText, _confirmText);
                else WriteTo(_editor, MergeTexts(_entryText, _confirmText), resetHistory: true);
            }
        }

        /// <summary>
        /// 2026-09-01：图重建后的语义化回填（同一逻辑单元内的修正），不清撤销栈
        /// —— 否则用户撤销就会被"立即被回填覆盖"中和掉，撤销体验失效。
        /// 2026-09-03：合并后写入单文本。
        /// </summary>
        public void EchoTexts(string entry, string confirm)
        {
            if (entry != null) _entryText = entry;
            if (confirm != null) _confirmText = confirm;

            if (_editor != null)
            {
                // 2026-09-03：同 SetTexts，entry 和 confirm 都为 null 时不动文本。
                if (entry == null && confirm == null) return;

                if (_editor.HasFocus) _pendingText = MergeTexts(_entryText, _confirmText);
                else WriteTo(_editor, MergeTexts(_entryText, _confirmText), resetHistory: false);
            }
        }

        private void WriteTo(VnCodeEditorIMGUI editor, string value, bool resetHistory)
        {
            if (editor == null) return;
            if (editor.Text == value) return;

            _suppressEcho = true;
            editor.SetTextWithoutNotify(value);
            if (resetHistory) editor.ResetHistory();
            _suppressEcho = false;
        }

        /// <summary>失去焦点时才把暂存着的规范化合并文本写回编辑器。</summary>
        private void OnEditorFocusLost()
        {
            if (_pendingText == null) return;
            string pending = _pendingText;
            _pendingText = null;

            WriteTo(_editor, pending, resetHistory: false);

            // 同步拆分后的 entry/confirm 缓存（用于外部 GetEntryText/GetConfirmText 比对）
            SplitByConfirm(pending, out string entry, out string confirm);
            _entryText = entry;
            _confirmText = confirm;
        }

        /// <summary>
        /// 在图中选中某节点后调用 —— 把该节点的命令签名下发给编辑器，
        /// 让其内部匹配的那一行整行高亮（橙色）。
        ///
        /// <para>2026-09-03 合并后：不再区分 entry/confirm 编辑器，单一编辑器
        /// 同时包含两段，签名匹配跨整文本搜索。</para>
        /// </summary>
        public void SetSelectedNode(VNNodeViewBase node)
        {
            _current = node;

            var cmdView = node as CommandNodeView;
            string sig = null;
            if (cmdView?.Data != null && !string.IsNullOrEmpty(cmdView.Data.CommandName))
            {
                sig = (cmdView.Data.CommandName ?? "") + "(" +
                      (cmdView.Data.Args ?? "") + ")";
            }

            if (_editor != null)
                _editor.SelectedSignature = sig;
        }

        public void Refresh() => Rebuild();

        /// <summary>外部读取当前进入段文本（从编辑器实时拆分）。</summary>
        public string GetEntryText()
        {
            if (_editor != null)
            {
                SplitByConfirm(_editor.Text, out string entry, out _);
                return entry;
            }
            return _entryText;
        }

        /// <summary>外部读取当前出口段文本（从编辑器实时拆分）。</summary>
        public string GetConfirmText()
        {
            if (_editor != null)
            {
                SplitByConfirm(_editor.Text, out _, out string confirm);
                return confirm;
            }
            return _confirmText;
        }

        // ---------------- UI 构建 ----------------

        private void Rebuild()
        {
            _root.Clear();

            var header = new Label("命令链文本");
            header.AddToClassList("vn-textchain-title");
            _root.Add(header);

            var section = new VisualElement();
            section.AddToClassList("vn-textchain-section");

            var t = new Label("命令链（进入段 + @Confirm: + 出口段）");
            t.AddToClassList("vn-insp-sectitle");
            section.Add(t);

            // IMGUI 自绘代码编辑器（通过 IMGUIContainer 嵌入）——
            // 2026-09-03 合二为一：单编辑器同时承载进入段与出口段，@Confirm: 分隔。
            // 这从根本上消除了原双编辑器方案的 Tab 焦点切换问题
            // （UIToolkit FocusController 把 Tab 切到"下一个 focusable element"，
            //  原来下一个就是出口段 IMGUIContainer；合并后没有第二个 IMGUIContainer）。
            var editorContainer = BuildCodeEditor(EditorMinHeight);
            section.Add(editorContainer);

            _root.Add(section);

            var help = new Label(
                "语法：cmd(args) · 串行分隔 -> ，并行分隔 & ，分组用 [] 嵌套。\n" +
                "用 @Confirm: 单独成行分隔进入段与出口段，@Confirm: 之后的命令在用户确认推进时执行。\n" +
                "文本与节点图双向实时联动 —— 编辑即重建节点图（200ms 防抖），解析失败的中间态不会破坏图。\n" +
                "选中画布节点时，编辑器里对应行会整行高亮（橙色）。");
            help.AddToClassList("vn-textchain-help");
            _root.Add(help);
        }

        /// <summary>
        /// 用 IMGUIContainer 把 IMGUI 代码编辑器嵌进 UIToolkit 面板。
        /// </summary>
        private IMGUIContainer BuildCodeEditor(float height)
        {
            _editor = new VnCodeEditorIMGUI();

            _editor.OnTextChanged += newText =>
            {
                if (_suppressEcho) return;
                OnTextChanged(newText);
            };

            // 失焦 = 用户这一轮输入结束 → 此刻才把规范化文本落回编辑器
            _editor.OnFocusLost += OnEditorFocusLost;

            _editor.SetTextWithoutNotify(MergeTexts(_entryText, _confirmText));

            var container = new IMGUIContainer(() =>
            {
                // IMGUIContainer 内部走 GUILayout 布局。
                // 用 GetRect 声明一个撑满容器的矩形区域供编辑器绘制。
                Rect rect = GUILayoutUtility.GetRect(
                    GUIContent.none, GUIStyle.none,
                    GUILayout.ExpandWidth(true),
                    GUILayout.ExpandHeight(true));
                _editor.OnGUI(rect);
            });

            // 自适应高度：最小 height，剩余空间由 flexGrow 分配。
            container.style.minHeight = height;
            container.style.flexGrow = 1;
            container.style.flexShrink = 1;
            container.AddToClassList("vn-codefield-imgui");

            // 2026-09-03：合二为一后只有一个 IMGUIContainer，Tab 焦点切换问题
            // 大幅缓解（没有"出口段 IMGUIContainer"作为下一个 focusable element）。
            // 但仍保留 BlurEvent 兜底：若 Tab 切到其他 focusable element（如
            // Inspector 的 TextField 或工具栏按钮），BlurEvent 会把焦点取回。
            // 详细原理见 VnCodeEditorIMGUI.OnFocusRestoreRequested 和 PendingRefocus 文档。
            bool refocusing = false;
            _editor.OnFocusRestoreRequested += () =>
            {
                if (refocusing) return;
                refocusing = true;
                try { container.Focus(); }
                finally { refocusing = false; }
            };

            container.RegisterCallback<BlurEvent>(e =>
            {
                if (!_editor.PendingRefocus) return;
                _editor.PendingRefocus = false;
                // 在当前事件循环结束后执行（不是下一帧），避免在 BlurEvent 处理
                // 期间修改焦点状态引发重入，同时最小化延迟。
                container.schedule.Execute(() => container.Focus()).StartingIn(0);
            });

            return container;
        }

        // ---------------- 文本变更 / 防抖 ----------------

        private void OnTextChanged(string newText)
        {
            // 实时拆分，缓存当前 entry/confirm（供外部 GetEntryText/GetConfirmText 比对）
            SplitByConfirm(newText, out string entry, out string confirm);
            _entryText = entry ?? "";
            _confirmText = confirm ?? "";

            _debounce?.Pause();

            var scheduled = _root.schedule.Execute(() =>
            {
                _debounce = null;

                string current = _editor?.Text ?? "";
                SplitByConfirm(current, out string curEntry, out string curConfirm);

                // 分别触发进入段和出口段的图重建。
                // 两次触发都走 HandleChainTextChanged，各自独立解析重建对应泳道。
                // 2026-09-02：去掉 Trim 比较逻辑，每次防抖稳定触发，
                // 由 HandleChainTextChanged 决定是否重建图（解析失败的中间态会自动跳过）。
                if (!string.IsNullOrEmpty(curEntry))
                    OnChainTextChanged?.Invoke(false, curEntry);
                if (!string.IsNullOrEmpty(curConfirm))
                    OnChainTextChanged?.Invoke(true, curConfirm);
            }).StartingIn(DebounceMs);

            _debounce = scheduled;
        }

        public void CancelPending()
        {
            _debounce?.Pause();
            _debounce = null;
        }

        public void Flush()
        {
            if (_debounce == null) return;
            _debounce.Pause();
            _debounce = null;

            string current = _editor?.Text ?? "";
            SplitByConfirm(current, out string entry, out string confirm);

            if (!string.IsNullOrEmpty(entry))
                OnChainTextChanged?.Invoke(false, entry);
            if (!string.IsNullOrEmpty(confirm))
                OnChainTextChanged?.Invoke(true, confirm);
        }

        // ---------------- @Confirm: 拆分 / 合并 ----------------

        /// <summary>
        /// 把 entry + confirm 合并为单文本（@Confirm: 分隔）。
        /// confirm 为空时不加 @Confirm: 标记（纯进入段，与旧剧本兼容）。
        /// </summary>
        private static string MergeTexts(string entry, string confirm)
        {
            string e = entry ?? "";
            string c = confirm ?? "";
            if (string.IsNullOrEmpty(c)) return e;
            if (string.IsNullOrEmpty(e)) return "@Confirm:\n" + c;
            return e + "\n@Confirm:\n" + c;
        }

        /// <summary>
        /// 按 @Confirm: 标记行拆分文本。@Confirm: 必须独占一行（整行 trim 后
        /// 等于 @Confirm:，大小写不敏感，与用户确认的分隔规则一致）。
        /// 该行之上是进入段，之下是出口段。无 @Confirm: 标记时整段为进入段。
        ///
        /// <para><b>引号安全</b>：@Confirm: 必须独占一行才算标记。命令参数中的
        /// @Confirm:（如 <c>showprompt("@Confirm: 是标记")</c>）不会独占一行，
        /// 不会被误识别为分隔标记。这与 <see cref="ScriptParser.IndexOfConfirmToken"/>
        /// 的引号感知切分在语义上一致（虽然实现不同）。</para>
        /// </summary>
        private static void SplitByConfirm(string text, out string entry, out string confirm)
        {
            entry = "";
            confirm = "";

            if (string.IsNullOrEmpty(text)) return;

            string[] lines = text.Split('\n');
            int confirmLine = -1;
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Trim().Equals("@Confirm:",
                        StringComparison.OrdinalIgnoreCase))
                {
                    confirmLine = i;
                    break;
                }
            }

            if (confirmLine < 0)
            {
                // 无 @Confirm: 标记行：整段为进入段
                entry = text;
                return;
            }

            // 进入段：@Confirm: 行之前的所有行
            if (confirmLine > 0)
            {
                entry = string.Join("\n", lines, 0, confirmLine);
            }

            // 出口段：@Confirm: 行之后的所有行
            if (confirmLine + 1 < lines.Length)
            {
                confirm = string.Join("\n", lines, confirmLine + 1,
                    lines.Length - confirmLine - 1);
            }
        }
    }
}
