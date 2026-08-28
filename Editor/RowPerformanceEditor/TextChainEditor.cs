using System;
using System.Collections.Generic;
using System.Text;
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

        /// <summary>当前防抖是否已挂起（用于抑制重建后的 SetText 再次触发 change）。</summary>
        private bool _suppressEcho;

        public TextChainEditor(VisualElement root)
        {
            _root = root;
            _root.AddToClassList("vn-textchain-panel");

            // 构造时立即构建 UI（标题 + 进入段 + 出口段 + 帮助）。
            Rebuild();
        }

        public void SetTexts(string entry, string confirm)
        {
            bool changed = false;

            if (entry != null && entry != _entryText)
            {
                _entryText = entry;
                if (_entryField != null)
                {
                    _suppressEcho = true;
                    _entryField.SetValueWithoutNotify(entry);
                    _suppressEcho = false;
                }
                changed = true;
            }

            if (confirm != null && confirm != _confirmText)
            {
                _confirmText = confirm;
                if (_confirmField != null)
                {
                    _suppressEcho = true;
                    _confirmField.SetValueWithoutNotify(confirm);
                    _suppressEcho = false;
                }
                changed = true;
            }

            if (changed) RefreshAll();
        }

        public void SetSelectedNode(VNNodeViewBase node)
        {
            _current = node;
            ApplySelectionHighlight();
        }

        public void Refresh() => Rebuild();

        // ---------------- UI 构建 ----------------

        private ChainCodeField _entryField;
        private ChainCodeField _confirmField;
        private VisualElement _entryPreviewBlock;
        private VisualElement _confirmPreviewBlock;

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
                "文本与节点图双向实时联动 —— 编辑即重建节点图，输入半成品（解析失败的中间态）会被忽略。\n" +
                "提示：选中画布上的节点可在预览栏里看到它对应的那一行（蓝底高亮）。");
            help.AddToClassList("vn-textchain-help");
            _root.Add(help);
        }

        private void BuildSection(string title, bool isConfirm)
        {
            var section = new VisualElement();
            section.AddToClassList("vn-textchain-section");

            var t = new Label(title);
            t.AddToClassList("vn-insp-sectitle");
            section.Add(t);

            // 上方：AST 结构化预览块（多行，每行一条命令 + 缩进 + 段落着色）
            var prev = new VisualElement();
            prev.AddToClassList("vn-textchain-prevblock");
            if (isConfirm) _confirmPreviewBlock = prev; else _entryPreviewBlock = prev;
            section.Add(prev);

            // 下方：IDE 风格多行代码编辑器（行号 + 着色 + 当前行高亮）
            var field = new ChainCodeField();
            field.AddToClassList("vn-textchain-field");
            field.tooltip = "IDE 风格命令链编辑器：行号、关键字着色、Tab/Enter 智能缩进。Ctrl+Z / Ctrl+Y 撤销重做。";

            field.OnValueChanged += newText =>
            {
                if (_suppressEcho) return;
                OnTextChanged(isConfirm, newText);
            };

            // 初次填充（不会触发 OnValueChanged —— OnValueChanged 只在 Set 后才挂）
            field.SetValueWithoutNotify(isConfirm ? _confirmText : _entryText);

            if (isConfirm) _confirmField = field; else _entryField = field;
            section.Add(field);

            _root.Add(section);

            RenderPreviewBlock(prev, isConfirm ? _confirmText : _entryText, isConfirm);
        }

        // ---------------- 文本变更 / 防抖 ----------------

        private void OnTextChanged(bool isConfirm, string newText)
        {
            if (isConfirm) _confirmText = newText ?? "";
            else _entryText = newText ?? "";

            RenderPreviewBlock(
                isConfirm ? _confirmPreviewBlock : _entryPreviewBlock,
                isConfirm ? _confirmText : _entryText,
                isConfirm);

            var pending = isConfirm ? _confirmDebounce : _entryDebounce;
            if (pending != null) pending.Pause();

            var scheduled = _root.schedule.Execute(() =>
            {
                string current = isConfirm
                    ? (_confirmField?.Value ?? "")
                    : (_entryField?.Value ?? "");
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

            string current = (isConfirm ? _confirmField?.Value : _entryField?.Value) ?? "";
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
                string current = _entryField?.Value ?? "";
                current = current.Trim();
                string stored = _entryText?.Trim() ?? "";
                if (current != stored)
                    OnChainTextChanged?.Invoke(false, current);
            }
            if (_confirmDebounce != null)
            {
                _confirmDebounce.Pause();
                _confirmDebounce = null;
                string current = _confirmField?.Value ?? "";
                current = current.Trim();
                string stored = _confirmText?.Trim() ?? "";
                if (current != stored)
                    OnChainTextChanged?.Invoke(true, current);
            }
        }

        // ---------------- 预览渲染（AST → 富文本块） ----------------

        private void RenderPreviewBlock(VisualElement block, string chainText, bool isConfirm)
        {
            if (block == null) return;
            block.Clear();

            if (string.IsNullOrWhiteSpace(chainText))
            {
                var empty = new Label("（空 · 待补全）");
                empty.AddToClassList("vn-textchain-prevempty");
                block.Add(empty);
                return;
            }

            var lines = ChainTextPrettifier.Build(chainText);

            string selectedSig = GetSelectedSignature(isConfirm);

            if (lines.Count == 0)
            {
                var errRow = new VisualElement();
                errRow.AddToClassList("vn-textchain-prevrow");
                var err = new Label(EscapeRichText(chainText));
                err.AddToClassList("vn-textchain-prevbody");
                errRow.Add(err);
                block.Add(errRow);
                return;
            }

            foreach (var line in lines)
            {
                var row = new VisualElement();
                row.AddToClassList("vn-textchain-prevrow");
                row.style.paddingLeft = line.Depth * 14;

                if (!string.IsNullOrEmpty(line.LeadPrefix))
                {
                    var lead = new Label(line.LeadPrefix);
                    lead.AddToClassList("vn-textchain-prevlead");
                    row.Add(lead);
                }

                var sb = new StringBuilder();
                foreach (var seg in line.Segments)
                    sb.Append(SegmentToRich(seg));

                var main = new Label(sb.ToString());
                main.enableRichText = true;
                main.AddToClassList("vn-textchain-prevbody");
                row.Add(main);

                // 行级高亮：纯文本签名包含选中命令的全签名 → 加底色
                if (selectedSig != null)
                {
                    var plainSb = new StringBuilder();
                    plainSb.Append(line.LeadPrefix ?? "");
                    foreach (var seg in line.Segments) plainSb.Append(seg.Text);
                    string plain = plainSb.ToString();
                    if (plain.Contains(selectedSig, StringComparison.Ordinal))
                        row.AddToClassList("vn-textchain-prevrow--selected");
                }

                block.Add(row);
            }
        }

        private string GetSelectedSignature(bool isConfirm)
        {
            var cmdView = _current as CommandNodeView;
            if (cmdView == null || cmdView.IsConfirmChain != isConfirm ||
                cmdView.Data == null || string.IsNullOrEmpty(cmdView.Data.CommandName))
                return null;
            return (cmdView.Data.CommandName ?? "") + "(" + (cmdView.Data.Args ?? "") + ")";
        }

        private void ApplySelectionHighlight()
        {
            ApplySelectionHighlightOn(_entryPreviewBlock, isConfirm: false);
            ApplySelectionHighlightOn(_confirmPreviewBlock, isConfirm: true);
        }

        private void ApplySelectionHighlightOn(VisualElement block, bool isConfirm)
        {
            if (block == null) return;
            string selectedSig = GetSelectedSignature(isConfirm);

            foreach (var child in block.Children())
            {
                if (!(child is VisualElement v)) continue;
                v.RemoveFromClassList("vn-textchain-prevrow--selected");

                if (selectedSig == null) continue;

                var sb = new StringBuilder();
                foreach (var inner in v.Children())
                    if (inner is Label l) sb.Append(l.text);
                if (sb.ToString().Contains(selectedSig, StringComparison.Ordinal))
                    v.AddToClassList("vn-textchain-prevrow--selected");
            }
        }

        private void RefreshAll()
        {
            RenderPreviewBlock(_entryPreviewBlock, _entryText, false);
            RenderPreviewBlock(_confirmPreviewBlock, _confirmText, true);
            ApplySelectionHighlight();
        }

        private static string SegmentToRich(PreviewSegment seg)
        {
            string esc = EscapeRichText(seg.Text);
            switch (seg.Kind)
            {
                case PreviewSegmentKind.CommandName:
                    return "<color=#6FB8E8><b>" + esc + "</b></color>";
                case PreviewSegmentKind.Bracket:
                    return "<color=#6E6E6E>" + esc + "</color>";
                case PreviewSegmentKind.Separator:
                    return "<color=#7A7A7A>" + esc + "</color>";
                case PreviewSegmentKind.StringArg:
                    return "<color=#A8C887>" + esc + "</color>";
                case PreviewSegmentKind.NumberArg:
                    return "<color=#E8C87F>" + esc + "</color>";
                case PreviewSegmentKind.Error:
                    return "<color=#C85A50><i>" + esc + "</i></color>";
                default:
                    return "<color=#D8D8D8>" + esc + "</color>";
            }
        }

        private static string EscapeRichText(string s)
        {
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        }
    }

    /// <summary>单条预览行：深度（缩进级数）+ 前导符号 + 主体片段序列。</summary>
    internal class PreviewLine
    {
        public int Depth = 0;
        public string LeadPrefix = "";
        public List<PreviewSegment> Segments = new List<PreviewSegment>();
    }

    internal class PreviewSegment
    {
        public PreviewSegmentKind Kind;
        public string Text;
    }

    internal enum PreviewSegmentKind
    {
        CommandName, // 命令名 · 蓝色加粗
        Arg,         // 普通参数 · 浅色
        StringArg,   // 引号字符串 · 绿
        NumberArg,   // 数字 · 暖色
        Separator,   // 逗号 / 分号 · 灰
        Bracket,     // 括号 / 方括号 · 暗灰
        Error,       // 解析错误 · 红
    }

    /// <summary>
    /// 命令链文本 → 预览行序列的转换器（AST 驱动 + 字符串渲染 fallback）。
    /// 把 <c>SeqNode / ParNode / CommandNode</c> 树展开为 "一行一条命令" 的扁平列表。
    /// </summary>
    internal static class ChainTextPrettifier
    {
        public static List<PreviewLine> Build(string chainText)
        {
            var lines = new List<PreviewLine>();
            if (string.IsNullOrWhiteSpace(chainText))
                return lines;

            var parsed = ChainParser.Parse(chainText);
            if (parsed.Root != null)
            {
                Render(parsed.Root, lines, depth: 0, lead: LeadKind.None);
                return lines;
            }

            // 解析失败 fallback：单行原样 + 错误信息
            var fallback = new PreviewLine { Depth = 0, LeadPrefix = "" };
            foreach (var err in parsed.Errors)
            {
                fallback.Segments.Add(new PreviewSegment
                {
                    Kind = PreviewSegmentKind.Error,
                    Text = "⚠ [位置 " + err.Position + "] " + err.Message + "  ",
                });
            }
            fallback.Segments.Add(new PreviewSegment
            {
                Kind = PreviewSegmentKind.Arg,
                Text = chainText.Trim(),
            });
            lines.Add(fallback);
            return lines;
        }

        private enum LeadKind { None, Arrow, Amp }

        private static void Render(ChainNode node, List<PreviewLine> lines, int depth, LeadKind lead)
        {
            if (node is SeqNode seq)
            {
                for (int i = 0; i < seq.Children.Count; i++)
                {
                    var childLead = i == 0 ? lead : LeadKind.Arrow;
                    Render(seq.Children[i], lines, depth, childLead);
                }
            }
            else if (node is ParNode par)
            {
                for (int i = 0; i < par.Children.Count; i++)
                {
                    var childLead = i == 0 ? lead : LeadKind.Amp;
                    Render(par.Children[i], lines, depth, childLead);
                }
            }
            else if (node is CommandNode cmd)
            {
                lines.Add(BuildCommandLine(cmd, depth, lead));
            }
        }

        private static PreviewLine BuildCommandLine(CommandNode cmd, int depth, LeadKind lead)
        {
            var line = new PreviewLine
            {
                Depth = depth,
                LeadPrefix = lead switch
                {
                    LeadKind.Arrow => "→ ",
                    LeadKind.Amp   => "& ",
                    _               => "",
                },
            };
            line.Segments.Add(new PreviewSegment
            {
                Kind = PreviewSegmentKind.CommandName,
                Text = string.IsNullOrEmpty(cmd.Name) ? "?" : cmd.Name,
            });
            line.Segments.Add(new PreviewSegment
            {
                Kind = PreviewSegmentKind.Bracket,
                Text = "(",
            });

            // 按顶部逗号切（引号内不切）
            var args = ConditionParser.SplitTopLevel(cmd.Args ?? "", ',');
            for (int i = 0; i < args.Count; i++)
            {
                if (i > 0)
                    line.Segments.Add(new PreviewSegment
                    {
                        Kind = PreviewSegmentKind.Separator,
                        Text = ", ",
                    });

                string a = args[i]?.Trim() ?? "";
                line.Segments.Add(new PreviewSegment
                {
                    Kind = ClassifyArg(a),
                    Text = a,
                });
            }

            line.Segments.Add(new PreviewSegment
            {
                Kind = PreviewSegmentKind.Bracket,
                Text = ")",
            });
            return line;
        }

        private static PreviewSegmentKind ClassifyArg(string a)
        {
            if (string.IsNullOrEmpty(a))
                return PreviewSegmentKind.Arg;

            // 引号字符串（首末有 "）
            if (a.Length >= 2 && a.StartsWith("\"", StringComparison.Ordinal) &&
                a.EndsWith("\"", StringComparison.Ordinal))
                return PreviewSegmentKind.StringArg;

            // 数字（含小数 / 负号）
            int first = (a.Length > 0 && a[0] == '-') ? 1 : 0;
            bool isNumeric = first < a.Length;
            for (int i = first; i < a.Length && isNumeric; i++)
                if (!char.IsDigit(a[i]) && a[i] != '.') { isNumeric = false; break; }
            if (isNumeric) return PreviewSegmentKind.NumberArg;

            return PreviewSegmentKind.Arg;
        }
    }
}
