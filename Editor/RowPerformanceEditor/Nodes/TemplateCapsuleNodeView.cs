using System;
using System.Collections.Generic;
using UnityEditor.Experimental.GraphView;
using UnityEngine.UIElements;
using VNovelizer.Core.Commands.Chain;
using VNovelizer.Core.Commands.Meta;

namespace VNovelizer.Editor.RowPerformanceEditor
{
    /// <summary>
    /// 「默认演出」折叠胶囊（决策 s8）：把 9 个系统命令影子节点收成一个胶囊。
    ///
    /// <para>
    /// <b>为何默认折叠</b>：展开态是 9 分支并行，按节点间距横向要 2000px 以上——
    /// 用户每次打开行编辑器都得先滚过一屏模板才能看到自己的命令。
    /// 折叠后一眼看清"这行用了哪些数据列"，双击才展开细节。
    /// </para>
    ///
    /// <para>
    /// <b>胶囊内的徽章是"数据列 → 值"的实时映射</b>：有值的列亮蓝色，
    /// 空列灰显划线——用户不必打开 Excel 就知道这行哪些槽位是空的。
    /// </para>
    /// </summary>
    public class TemplateCapsuleNodeView : VNNodeViewBase
    {
        /// <summary>双击胶囊时触发（请求展开为完整影子链）</summary>
        public event Action OnRequestExpand;

        /// <summary>徽章点击时触发（参数 = 数据列名，用于跳转表格对应单元格）</summary>
        public event Action<string> OnColumnClicked;

        private readonly VNLineContext _lineContext;

        /// <param name="lineContext">当前行的解析后上下文——决定哪些列有值</param>
        public TemplateCapsuleNodeView(ChainGraphNode data, VNLineContext lineContext)
            : base(data, isConfirmChain: false)
        {
            _lineContext = lineContext;
            AddToClassList("vn-tplcapsule");
            Build();
        }

        protected override void Build()
        {
            SetTitle("[默认演出]");
            tooltip = "本行使用引擎默认演出（数据列驱动），未占用 Command 列。\n\n" +
                      "双击展开查看完整结构。修改任一节点会把整行「提升」为定制行——" +
                      "届时完整命令链将写入 Command 列，由你完全掌控。";

            CreateStandardPorts();

            BuildHeaderExtras();
            BuildColumnChips();
            BuildNote();

            // 影子节点不可删除（删除应走"提升"确认流程）
            capabilities &= ~Capabilities.Deletable;
            capabilities &= ~Capabilities.Copiable;

            RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.clickCount == 2)
                {
                    OnRequestExpand?.Invoke();
                    evt.StopPropagation();
                }
            });

            RefreshExpandedState();
            RefreshPorts();
        }

        private void BuildHeaderExtras()
        {
            var expander = new Label("[+]");
            expander.AddToClassList("vn-tpl-expander");
            titleContainer.Insert(0, expander);

            var count = new Label($"{DefaultPerformanceTemplate.SystemNodeCount} 节点 · 已折叠");
            count.AddToClassList("vn-tpl-count");
            titleContainer.Add(count);
        }

        /// <summary>
        /// 数据列徽章。顺序按演出层次排（对话 → 背景 → 立绘 → 音频），
        /// 与作者思考剧本的顺序一致，而非按内部字段顺序。
        /// </summary>
        private void BuildColumnChips()
        {
            var chips = new VisualElement();
            chips.AddToClassList("vn-tpl-chips");

            foreach (var entry in BuildDisplayColumns())
            {
                bool hasValue = !string.IsNullOrWhiteSpace(entry.Value);

                var chip = new Label(">> " + entry.Label + (hasValue ? "" : " (空)"));
                chip.AddToClassList("vn-refchip");
                if (!hasValue) chip.AddToClassList("vn-refchip--empty");

                chip.tooltip = hasValue
                    ? $"{entry.Column} 列 = {Truncate(entry.Value, 60)}\n（点击跳转表格对应单元格）"
                    : $"{entry.Column} 列为空——该项不会呈现。\n（点击跳转表格对应单元格）";

                string column = entry.Column;
                chip.RegisterCallback<MouseDownEvent>(evt =>
                {
                    if (evt.button == 0)
                    {
                        OnColumnClicked?.Invoke(column);
                        evt.StopPropagation();
                    }
                });

                chips.Add(chip);
            }

            mainContainer.Add(chips);
        }

        private struct ColumnEntry
        {
            public string Label;
            public string Column;
            public string Value;
        }

        private List<ColumnEntry> BuildDisplayColumns()
        {
            var list = new List<ColumnEntry>();
            if (_lineContext == null) return list;

            list.Add(new ColumnEntry { Label = "对话", Column = "Text",       Value = _lineContext.Text });
            list.Add(new ColumnEntry { Label = "说话人", Column = "Speaker",  Value = _lineContext.Speaker });
            list.Add(new ColumnEntry { Label = "背景", Column = "Background", Value = _lineContext.Background });

            list.Add(new ColumnEntry { Label = "立绘L",  Column = "CharLeft",       Value = _lineContext.CharLeft });
            list.Add(new ColumnEntry { Label = "立绘ML", Column = "CharMid_Left",   Value = _lineContext.CharMidLeft });
            list.Add(new ColumnEntry { Label = "立绘M",  Column = "CharMid",        Value = _lineContext.CharMid });
            list.Add(new ColumnEntry { Label = "立绘MR", Column = "CharMid_Right",  Value = _lineContext.CharMidRight });
            list.Add(new ColumnEntry { Label = "立绘R",  Column = "CharRight",      Value = _lineContext.CharRight });

            list.Add(new ColumnEntry { Label = "BGM",  Column = "BGM",   Value = _lineContext.BGM });
            list.Add(new ColumnEntry { Label = "语音", Column = "Voice", Value = _lineContext.Voice });

            return list;
        }

        private void BuildNote()
        {
            var note = new Label(
                "对话独立并行 · 其余系统命令同帧启动 —— 与引擎隐式演出逐帧等价");
            note.AddToClassList("vn-tpl-note");
            mainContainer.Add(note);
        }

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= max) return s;
            return s.Substring(0, max) + "…";
        }

        public override bool IsCopiable() => false;
    }
}
