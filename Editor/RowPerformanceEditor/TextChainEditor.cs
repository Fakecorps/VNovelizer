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
    /// <para>
    /// <b>职责</b>：把 <see cref="ChainSerializer"/> 反序列化得到的命令链文本以
    /// **AST 驱动的代码块预览** 呈现 —— 每行一条命令、按 <c>[]</c> 嵌套缩进、
    /// 关键字（命令名 / <c>&amp;</c> / <c>-&gt;</c> / <c>[]</c> / 字符串 /
    /// 参数）按语义着色；下方保留可编辑的多行文本框（原始语法）。
    /// </para>
    ///
    /// <para>
    /// <b>2026-08-28 改造要点</b>：
    /// </para>
    /// <list type="bullet">
    /// <item>从 Inspector 右侧 Tab 抽到独立中间列 —— 视高不再被参数表单挤占</item>
    /// <item>实时联动（debounced ValueChanged）—— 用户输入即重建图</item>
    /// <item>代码块预览（而非单行文本预览）—— 视觉对齐现代代码编辑器：
    ///      每行一条命令、按嵌套深度缩进、segment 富文本着色</item>
    /// <item>选中节点的对应命令行加底色背景高亮</item>
    /// </list>
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
        private IVisualElementScheduledItem _entryDebounce;
        private IVisualElementScheduledItem _confirmDebounce;

        /// <summary>单个代码编辑器的最小高度（进入段 / 出口段各一个）。</summary>
        private const float EditorMinHeight = 120f;

        /// <summary>当前防抖是否已挂起（用于抑制重建后的 SetText 再次触发 change）。</summary>
        private bool _suppressEcho;

        /// <summary>编辑器持有焦点期间暂存的规范化文本（失焦后落回）。</summary>
        private string _pendingEntryText;
        private string _pendingConfirmText;

        public TextChainEditor(VisualElement root)
        {
            _root = root;
            _root.AddToClassList("vn-textchain-panel");

            // 构造时立即构建 UI（标题 + 进入段 + 出口段 + 帮助）。
            Rebuild();
        }

        /// <summary>
        /// 由外部（节点图变化 / 切行 / 解析回填）写入规范化文本。
        ///
        /// <para>
        /// <b>关键约束（2026-08-31）</b>：目标编辑器<b>正持有焦点</b>时绝不直接覆写。
        /// 防抖重建图后序列化出的规范化文本与用户刚敲进去的原始输入几乎必然不同
        /// （空格、换行位置被规范化），直接回填会把用户正在输入的字符顶掉、
        /// 光标跳到别处 —— 即"打字打着就闪退"的现象。
        /// 因此：打字期间把规范化文本暂存，等失焦（<see cref="VnCodeEditorIMGUI.OnFocusLost"/>）再落回。
        /// </para>
        /// </summary>
        public void SetTexts(string entry, string confirm)
        {
            if (entry != null)
            {
                _entryText = entry;
                if (_entryEditor != null)
                {
                    if (_entryEditor.HasFocus) _pendingEntryText = entry;
                    else WriteTo(_entryEditor, entry);
                }
            }

            if (confirm != null)
            {
                _confirmText = confirm;
                if (_confirmEditor != null)
                {
                    if (_confirmEditor.HasFocus) _pendingConfirmText = confirm;
                    else WriteTo(_confirmEditor, confirm);
                }
            }
        }

        private void WriteTo(VnCodeEditorIMGUI editor, string value)
        {
            if (editor == null) return;
            if (editor.Text == value) return;

            _suppressEcho = true;
            editor.SetTextWithoutNotify(value);
            _suppressEcho = false;
        }

        /// <summary>失去焦点时才把暂存着的规范化文本写回编辑器。</summary>
        private void OnEditorFocusLost(bool isConfirm)
        {
            string pending = isConfirm ? _pendingConfirmText : _pendingEntryText;
            if (pending == null) return;

            if (isConfirm) _pendingConfirmText = null;
            else _pendingEntryText = null;

            var editor = isConfirm ? _confirmEditor : _entryEditor;
            WriteTo(editor, pending);

            if (isConfirm) _confirmText = pending;
            else _entryText = pending;
        }

        /// <summary>
        /// 在图中选中某节点后调用 —— 把该节点的命令签名下发给对应的
        /// <see cref="VnCodeEditorIMGUI"/>，让其内部匹配的那一行整行高亮（橙色）。
        ///
        /// <para>
        /// 只高亮选中节点所在泳道（进入段 / 出口段）的编辑器，另一段清空。
        /// </para>
        /// </summary>
        public void SetSelectedNode(VNNodeViewBase node)
        {
            _current = node;

            var cmdView = node as CommandNodeView;
            string entrySig = null, confirmSig = null;
            if (cmdView?.Data != null && !string.IsNullOrEmpty(cmdView.Data.CommandName))
            {
                string sig = (cmdView.Data.CommandName ?? "") + "(" +
                             (cmdView.Data.Args ?? "") + ")";
                if (cmdView.IsConfirmChain) confirmSig = sig;
                else entrySig = sig;
            }

            _entryEditor.SelectedSignature = entrySig;
            _confirmEditor.SelectedSignature = confirmSig;
        }

        public void Refresh() => Rebuild();

        /// <summary>外部读取当前文本（用于回填前比较，避免无意义覆盖）。</summary>
        public string GetEntryText() => _entryEditor?.Text ?? _entryText;
        public string GetConfirmText() => _confirmEditor?.Text ?? _confirmText;

        // ---------------- UI 构建 ----------------

        private VnCodeEditorIMGUI _entryEditor;
        private VnCodeEditorIMGUI _confirmEditor;

        private void Rebuild()
        {
            _root.Clear();

            var header = new Label("命令链文本");
            header.AddToClassList("vn-textchain-title");
            _root.Add(header);

            BuildSection("进入段（按 → 串行，& 并行，[] 分组）", isConfirm: false);
            BuildSection("出口段 @Confirm（点击确认时执行）", isConfirm: true);

            var help = new Label(
                "语法：cmd(args) · 串行分隔 -> ，并行分隔 & ，分组用 [] 嵌套，并用 @Confirm: 切到出口段。\n" +
                "文本与节点图双向实时联动 —— 编辑即重建节点图（200ms 防抖），解析失败的中间态不会破坏图。\n" +
                "选中画布节点时，编辑器里对应行会整行高亮（橙色）。");
            help.AddToClassList("vn-textchain-help");
            _root.Add(help);
        }

        /// <summary>
        /// 用 IMGUIContainer 把 IMGUI 代码编辑器嵌进 UIToolkit 面板。
        ///
        /// <para>
        /// 这是「同一窗口内节点图与文本编辑互通」的关键桥接：
        /// 节点图仍是 UIToolkit GraphView，文本编辑器是 IMGUI 自绘，
        /// 两者共处一个 EditorWindow，通过事件回调双向联动。
        /// </para>
        /// </summary>
        private IMGUIContainer BuildCodeEditor(bool isConfirm, float height)
        {
            var editor = new VnCodeEditorIMGUI();
            if (isConfirm) _confirmEditor = editor; else _entryEditor = editor;

            editor.OnTextChanged += newText =>
            {
                if (_suppressEcho) return;
                OnTextChanged(isConfirm, newText);
            };

            // 失焦 = 用户这一轮输入结束 → 此刻才把规范化文本落回编辑器
            editor.OnFocusLost += () => OnEditorFocusLost(isConfirm);

            editor.SetTextWithoutNotify(isConfirm ? _confirmText : _entryText);

            var container = new IMGUIContainer(() =>
            {
                // IMGUIContainer 内部走 GUILayout 布局。
                // 用 GetRect 声明一个撑满容器的矩形区域供编辑器绘制。
                Rect rect = GUILayoutUtility.GetRect(
                    GUIContent.none, GUIStyle.none,
                    GUILayout.ExpandWidth(true),
                    GUILayout.ExpandHeight(true));
                editor.OnGUI(rect);
            });

            // 自适应高度：最小 height，剩余空间由 flexGrow 分配。
            // 2026-08-31：不再写死固定高度 —— 面板被拉高时编辑器跟着变高，
            // 长命令链能看到更多行，不用在小窗口里挤着看。
            container.style.minHeight = height;
            container.style.flexGrow = 1;
            container.style.flexShrink = 1;
            container.AddToClassList("vn-codefield-imgui");

            return container;
        }

        private void BuildSection(string title, bool isConfirm)
        {
            var section = new VisualElement();
            section.AddToClassList("vn-textchain-section");

            var t = new Label(title);
            t.AddToClassList("vn-insp-sectitle");
            section.Add(t);

            // IMGUI 自绘代码编辑器（通过 IMGUIContainer 嵌入）——
            // 2026-08-31 重写：彻底放弃 UIToolkit TextField + 覆盖 Label 的叠层方案。
            // 该方案因 TextField 是黑盒（不暴露光标/选区/行布局）而必然产生
            // 「光标不可见、选区不可见、长行错位、滚动不同步」等致命 BUG。
            // IMGUI 自绘后：光标、选区、着色、行号全部精确，且仍在同一窗口内。
            var editorContainer = BuildCodeEditor(isConfirm, EditorMinHeight);
            section.Add(editorContainer);

            _root.Add(section);
        }

        // ---------------- 文本变更 / 防抖 ----------------

        private void OnTextChanged(bool isConfirm, string newText)
        {
            if (isConfirm) _confirmText = newText ?? "";
            else _entryText = newText ?? "";

            // IMGUI 编辑器已在 OnTextChanged 中自动重绘着色，无需额外刷新。

            var pending = isConfirm ? _confirmDebounce : _entryDebounce;
            if (pending != null) pending.Pause();

            var scheduled = _root.schedule.Execute(() =>
            {
                string current = isConfirm
                    ? (_confirmEditor?.Text ?? "")
                    : (_entryEditor?.Text ?? "");
                current = current?.Trim() ?? "";

                if (isConfirm) _confirmDebounce = null;
                else _entryDebounce = null;

                if (isConfirm && current == _confirmText && !string.IsNullOrEmpty(_confirmText))
                {
                    OnChainTextChanged?.Invoke(true, current);
                }
                else if (!isConfirm && current == _entryText && !string.IsNullOrEmpty(_entryText))
                {
                    OnChainTextChanged?.Invoke(false, current);
                }
            }).StartingIn(DebounceMs);

            if (isConfirm) _confirmDebounce = scheduled;
            else _entryDebounce = scheduled;
        }

        private void FlushIfPending(bool isConfirm)
        {
            var pending = isConfirm ? _confirmDebounce : _entryDebounce;
            if (pending == null) return;

            pending.Pause();
            if (isConfirm) _confirmDebounce = null;
            else _entryDebounce = null;

            string current = (isConfirm ? _confirmEditor?.Text : _entryEditor?.Text) ?? "";
            current = current.Trim();
            string stored = (isConfirm ? _confirmText : _entryText)?.Trim() ?? "";
            if (current == stored) return;

            OnChainTextChanged?.Invoke(isConfirm, current);
        }

        public void CancelPending()
        {
            _entryDebounce?.Pause();
            _entryDebounce = null;
            _confirmDebounce?.Pause();
            _confirmDebounce = null;
        }

        public void Flush()
        {
            if (_entryDebounce != null)
            {
                _entryDebounce.Pause();
                _entryDebounce = null;
                string current = _entryEditor?.Text ?? "";
                current = current.Trim();
                string stored = _entryText?.Trim() ?? "";
                if (current != stored)
                    OnChainTextChanged?.Invoke(false, current);
            }
            if (_confirmDebounce != null)
            {
                _confirmDebounce.Pause();
                _confirmDebounce = null;
                string current = _confirmEditor?.Text ?? "";
                current = current.Trim();
                string stored = _confirmText?.Trim() ?? "";
                if (current != stored)
                    OnChainTextChanged?.Invoke(true, current);
            }
        }

        // ---------------- 渲染已全部下放到 VnCodeEditorIMGUI ----------------
        // 2026-08-31 重写：编辑器改为 IMGUI 全自绘（见 VnCodeEditorIMGUI），
        // 本类只负责：文本状态缓存、防抖、与图的联动。
        // 旧的 AST 结构化预览块、ChainTextPrettifier、ChainCodeField 均已删除。
    }
}
