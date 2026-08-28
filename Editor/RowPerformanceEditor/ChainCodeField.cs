using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using VNovelizer.Core.Commands;
using VNovelizer.Core.Commands.Chain;

namespace VNovelizer.Editor.RowPerformanceEditor
{
    /// <summary>
    /// IDE 风格多行文本编辑器控件（2026-08-28 新增）。
    ///
    /// <para>
    /// <b>三层结构</b>：
    /// </para>
    /// <list type="number">
    /// <item>左侧 <b>行号 gutter</b>（等宽数字，固定宽 36px）</item>
    /// <item>底层 <b>透明 TextField</b>（捕获键盘 / IME / 光标 / 复制粘贴）</item>
    /// <item>顶层 <b>富文本 Label</b>（彩色语法高亮，与底层字符位置 1:1 对齐）</item>
    /// </list>
    ///
    /// <para>
    /// <b>不替换 Unity TextField 而叠放</b>的原因：IME（中文 / 日文输入）由 TextField 原生
    /// 处理，光标位置、Ctrl+C/Z/Y 全部走标准事件 —— 与 MonoDevelop / Rider 的 IDE
    /// 编辑行为 1:1 对齐。我们只需把 TextField 文字设为透明，让 Label 接管视觉显示。
    /// </para>
    /// </summary>
    public class ChainCodeField : VisualElement
    {
        /// <summary>值变更（用户每次 keystroke 触发）——外部通常做防抖。</summary>
        public event Action<string> OnValueChanged;

        // ---- 子元素 ----
        private readonly Label _gutter;
        private readonly TextField _input;
        private readonly Label _highlight;

        public string Value
        {
            get => _input?.value ?? "";
            set => SetValueWithoutNotify(value);
        }

        /// <summary>当前焦点行（基于最后换行符位置估算）——外部用底色高亮。</summary>
        public int CurrentLine => ComputeCurrentLine(_input?.value ?? "");

        public ChainCodeField()
        {
            AddToClassList("vn-codefield");

            _gutter = new Label("1");
            _gutter.AddToClassList("vn-codefield-gutter");
            Add(_gutter);

            var editArea = new VisualElement();
            editArea.AddToClassList("vn-codefield-edit");
            Add(editArea);

            _highlight = new Label();
            _highlight.enableRichText = true;
            _highlight.AddToClassList("vn-codefield-highlight");
            // 与 _input 同区域：full bleed absolutely
            editArea.Add(_highlight);

            _input = new TextField { multiline = true, value = "" };
            _input.AddToClassList("vn-codefield-input");
            editArea.Add(_input);

            // 同步：底层 TextField 值变 → 刷新顶层 Label 与行号
            _input.RegisterValueChangedCallback(evt =>
            {
                UpdateHighlight(evt.newValue);
                UpdateGutter(evt.newValue);
                OnValueChanged?.Invoke(evt.newValue);
            });

            // 焦点边框高亮：USS 不支持 :focus-within —— 在 C# 里切换 class
            RegisterCallback<FocusInEvent>(_ =>
                this.AddToClassList("vn-codefield--focused"));
            RegisterCallback<FocusOutEvent>(_ =>
                this.RemoveFromClassList("vn-codefield--focused"));

            // 初次构建：空文本的兜底
            UpdateHighlight("");
            UpdateGutter("");
        }

        /// <summary>
        /// 外部赋值（不触发 OnValueChanged）—— 用于把规范化文本回填。
        /// </summary>
        public void SetValueWithoutNotify(string value)
        {
            string safe = value ?? "";
            _input.SetValueWithoutNotify(safe);
            UpdateHighlight(safe);
            UpdateGutter(safe);
        }

        // ---------------- 编辑器辅助 ----------------

        /// <summary>
        /// 当前焦点行（行号 = \n 数量 + 1）。Unity TextField 不暴露 caret index 的
        /// 公开 API，估算法：取最后一个 \n 之后的内容长度，记为"当前行长度"。
        /// 对 IDE 当前行高亮只是视觉提示，不需要绝对准确。
        /// </summary>
        private static int ComputeCurrentLine(string text)
        {
            if (string.IsNullOrEmpty(text)) return 1;
            int n = 0;
            for (int i = 0; i < text.Length; i++)
                if (text[i] == '\n') n++;
            return n + 1;
        }

        private void UpdateGutter(string text)
        {
            int lineCount = 1;
            if (!string.IsNullOrEmpty(text))
            {
                for (int i = 0; i < text.Length; i++)
                    if (text[i] == '\n') lineCount++;
            }
            // 行号右对齐，前面填空格让宽度对齐
            var sb = new System.Text.StringBuilder();
            int width = lineCount.ToString().Length;
            for (int i = 1; i <= lineCount; i++)
            {
                if (i > 1) sb.Append('\n');
                sb.Append(i.ToString().PadLeft(width, ' '));
            }
            _gutter.text = sb.ToString();
        }

        /// <summary>
        /// 把文本用 <see cref="ChainTextPrettyInline"/> 解析 → 生成单行富文本字符串。
        /// 单条串渲染（不是多行结构）—— 因为底层 TextField 已经负责分行 + 缩进 + 行高，
        /// Label 只需把它们染色即可。
        /// </summary>
        private void UpdateHighlight(string text)
        {
            if (_highlight == null) return;
            _highlight.text = ChainTextPrettyInline.Render(text);
        }
    }

    /// <summary>
    /// 单条字符串的 inline 渲染器（与 <see cref="ChainTextPrettifier"/> 共享 segment 分类，
    /// 但输出格式不同：这里输出一行 continuous rich-text 字符串，依赖 TextField 的
    /// pre-wrap 自然换行；Prettifier 输出多行 PreviewLine 列表）。
    /// </summary>
    internal static class ChainTextPrettyInline
    {
        public static string Render(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";

            // 走一遍 parser（保留 \n 与原始空白）
            var parsed = ChainParser.Parse(text);
            if (parsed.Root == null)
                return EscapeRichText(text);

            var sb = new System.Text.StringBuilder();
            RenderNode(parsed.Root, sb);
            return sb.ToString();
        }

        private enum LeadKind { None, Arrow, Amp }

        private static void RenderNode(ChainNode node, System.Text.StringBuilder sb)
        {
            if (node is SeqNode seq)
            {
                for (int i = 0; i < seq.Children.Count; i++)
                {
                    if (i > 0) sb.Append("<color=#7A7A7A> -> </color>");
                    RenderNode(seq.Children[i], sb);
                }
            }
            else if (node is ParNode par)
            {
                for (int i = 0; i < par.Children.Count; i++)
                {
                    if (i > 0) sb.Append("<color=#7A7A7A> &amp; </color>");
                    RenderNode(par.Children[i], sb);
                }
            }
            else if (node is CommandNode cmd)
            {
                sb.Append("<color=#6FB8E8><b>").Append(EscapeRichText(cmd.Name ?? "?")).Append("</b></color>");
                sb.Append("<color=#6E6E6E>(</color>");
                var args = ConditionParser.SplitTopLevel(cmd.Args ?? "", ',');
                for (int i = 0; i < args.Count; i++)
                {
                    if (i > 0) sb.Append("<color=#7A7A7A>, </color>");
                    string a = args[i]?.Trim() ?? "";
                    sb.Append(ClassifyArgSpan(a));
                }
                sb.Append("<color=#6E6E6E>)</color>");
            }
        }

        private static string ClassifyArgSpan(string a)
        {
            if (string.IsNullOrEmpty(a))
                return "<color=#D8D8D8></color>";

            string esc = EscapeRichText(a);
            if (a.Length >= 2 && a.StartsWith("\"", StringComparison.Ordinal) &&
                a.EndsWith("\"", StringComparison.Ordinal))
                return "<color=#A8C887>" + esc + "</color>";

            int first = (a.Length > 0 && a[0] == '-') ? 1 : 0;
            bool isNumeric = first < a.Length;
            for (int i = first; i < a.Length && isNumeric; i++)
                if (!char.IsDigit(a[i]) && a[i] != '.') { isNumeric = false; break; }
            if (isNumeric) return "<color=#E8C87F>" + esc + "</color>";

            return "<color=#D8D8D8>" + esc + "</color>";
        }

        private static string EscapeRichText(string s)
        {
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        }
    }
}
