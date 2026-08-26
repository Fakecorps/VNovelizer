using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VNovelizer.Core.Commands;
using VNovelizer.Core.Commands.Chain;
using VNovelizer.Core.Commands.Meta;

namespace VNovelizer.Editor.RowPerformanceEditor
{
    /// <summary>
    /// 左侧命令面板：命令列表 + 泳道切换 + 拖拽建节点。
    ///
    /// <para>
    /// 数据源是 <see cref="CommandMetaReader"/>——因此**通过反射注册的第三方自定义命令
    /// 同样出现在这里**。未标注元数据的命令照常列出（拖入后为通用节点），
    /// 不隐藏：任何人写的任何命令都能上图，这是插件开放性的底线。
    /// </para>
    /// </summary>
    public class CommandPalette
    {
        /// <summary>请求在画布上创建命令节点（命令名, 是否出口段）</summary>
        public event Action<string, bool> OnRequestCreateNode;

        /// <summary>请求创建 FORK/JOIN 并行组</summary>
        public event Action<bool> OnRequestCreateForkJoin;

        private readonly VisualElement _root;
        private readonly ScrollView _list;
        private ToolbarSearchField _search;
        private Button _entryTab;
        private Button _confirmTab;

        /// <summary>当前编辑的泳道：false = 进入段，true = 出口段</summary>
        public bool TargetConfirmChain { get; private set; }

        public CommandPalette(VisualElement root)
        {
            _root = root;
            _root.AddToClassList("vn-left-panel");

            BuildHeader();

            _list = new ScrollView(ScrollViewMode.Vertical) { style = { flexGrow = 1 } };
            _root.Add(_list);

            BuildFooterNote();
            Rebuild();
        }

        private void BuildHeader()
        {
            var title = new Label("命令面板 · 元数据驱动");
            title.AddToClassList("vn-panel-title");
            _root.Add(title);

            _search = new ToolbarSearchField();
            _search.style.marginLeft = 6;
            _search.style.marginRight = 6;
            _search.style.marginTop = 5;
            _search.style.marginBottom = 3;
            _search.RegisterValueChangedCallback(_ => Rebuild());
            _root.Add(_search);

            var tabs = new VisualElement();
            tabs.AddToClassList("vn-chain-tabs");

            _entryTab = new Button(() => SetTarget(false)) { text = "进入段" };
            _entryTab.AddToClassList("vn-chain-tab");
            tabs.Add(_entryTab);

            _confirmTab = new Button(() => SetTarget(true)) { text = "出口段 @Confirm" };
            _confirmTab.AddToClassList("vn-chain-tab");
            tabs.Add(_confirmTab);

            _root.Add(tabs);
            UpdateTabVisual();

            var forkBtn = new Button(() => OnRequestCreateForkJoin?.Invoke(TargetConfirmChain))
            {
                text = "＋ 添加 FORK / JOIN 并行组"
            };
            forkBtn.style.marginLeft = 6;
            forkBtn.style.marginRight = 6;
            forkBtn.style.marginTop = 5;
            forkBtn.tooltip = "并行组让多条演出同时开始，全部完成后才继续后续命令。";
            _root.Add(forkBtn);
        }

        private void SetTarget(bool confirmChain)
        {
            TargetConfirmChain = confirmChain;
            UpdateTabVisual();
            Rebuild();
        }

        private void UpdateTabVisual()
        {
            _entryTab.RemoveFromClassList("vn-chain-tab--active");
            _confirmTab.RemoveFromClassList("vn-chain-tab--active-confirm");

            if (TargetConfirmChain) _confirmTab.AddToClassList("vn-chain-tab--active-confirm");
            else _entryTab.AddToClassList("vn-chain-tab--active");
        }

        private void BuildFooterNote()
        {
            var note = new Label(
                "签名由命令类上的 [VNParam] 特性反射生成——第三方自定义命令加特性即自动出现在此。\n" +
                "标 ⚙ 者尚无元数据，拖入后为「通用节点」（原始参数文本框，功能完整）。");
            note.AddToClassList("vn-panel-note");
            _root.Add(note);
        }

        /// <summary>重建命令列表（搜索 / 泳道切换 / 元数据刷新后调用）。</summary>
        public void Rebuild()
        {
            _list.Clear();

            string filter = _search?.value ?? "";
            var grouped = CommandMetaReader.GetGrouped();

            var order = new[]
            {
                VNCommandCategory.System,
                VNCommandCategory.Performance,
                VNCommandCategory.Audio,
                VNCommandCategory.Logic,
                VNCommandCategory.Flow,
                VNCommandCategory.Misc,
            };

            foreach (var category in order)
            {
                if (!grouped.TryGetValue(category, out var items)) continue;

                var visible = new List<VNCommandInfo>();
                foreach (var info in items)
                {
                    if (!Matches(info, filter)) continue;
                    // 出口段禁止 choice（执行后立即推进，选项无法响应）
                    if (TargetConfirmChain && info.Name == "choice") continue;
                    visible.Add(info);
                }

                if (visible.Count == 0) continue;

                _list.Add(BuildGroupHeader(category, visible.Count));
                foreach (var info in visible) _list.Add(BuildItem(info));
            }

            if (_list.childCount == 0)
            {
                var empty = new Label("没有匹配的命令。");
                empty.AddToClassList("vn-panel-note");
                _list.Add(empty);
            }
        }

        private static bool Matches(VNCommandInfo info, string filter)
        {
            if (string.IsNullOrEmpty(filter)) return true;
            return info.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   (info.Description ?? "").IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private VisualElement BuildGroupHeader(VNCommandCategory category, int count)
        {
            var header = new VisualElement();
            header.AddToClassList("vn-cmd-group");

            var dot = new VisualElement();
            dot.AddToClassList("vn-cmd-group-dot");
            dot.style.backgroundColor = CategoryColor(category);
            header.Add(dot);

            header.Add(new Label(CategoryName(category) + $"（{count}）"));
            return header;
        }

        private VisualElement BuildItem(VNCommandInfo info)
        {
            var item = new VisualElement();
            item.AddToClassList("vn-cmd-item");
            if (!info.HasMeta) item.AddToClassList("vn-cmd-item--generic");

            var name = new Label(info.Name);
            name.AddToClassList("vn-cmd-item-name");
            item.Add(name);

            var sig = new Label(info.HasMeta ? BuildSignatureTail(info) : "⚙ 无元数据");
            sig.AddToClassList("vn-cmd-item-sig");
            item.Add(sig);

            item.tooltip = BuildItemTooltip(info);

            // 单击即在画布上创建节点。
            // 说明：GraphView 的原生 DragAndDrop 在 UIElements 面板间传递体验不稳定，
            // 单击创建 + 随后自由拖拽定位，交互更可靠且更快。
            item.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                OnRequestCreateNode?.Invoke(info.Name, TargetConfirmChain);
                evt.StopPropagation();
            });

            return item;
        }

        private static string BuildSignatureTail(VNCommandInfo info)
        {
            if (info.Parameters.Count == 0) return "()";

            var parts = new List<string>();
            foreach (var p in info.Parameters) parts.Add(p.ToSignatureToken());
            return "(" + string.Join(",", parts) + ")";
        }

        private static string BuildItemTooltip(VNCommandInfo info)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(info.Signature);

            if (!string.IsNullOrEmpty(info.Description))
                sb.Append('\n').Append(info.Description);

            if (!info.HasMeta)
                sb.Append("\n\n该命令尚未标注元数据，将以「通用节点」形态添加（原始参数文本框）。");

            if (info.IsFlowCommand)
                sb.Append("\n\n⚠ 流程命令：必须置于命令链末尾。");

            if (info.IsAsync)
                sb.Append("\n⏳ 异步命令：所在分支会等待它完成。");

            sb.Append("\n\n单击添加到画布。");
            return sb.ToString();
        }

        private static Color CategoryColor(VNCommandCategory category)
        {
            switch (category)
            {
                case VNCommandCategory.System:      return new Color(0.29f, 0.56f, 0.85f);
                case VNCommandCategory.Performance: return new Color(0.61f, 0.58f, 0.70f);
                case VNCommandCategory.Flow:        return new Color(0.80f, 0.48f, 0.16f);
                case VNCommandCategory.Logic:       return new Color(0.37f, 0.66f, 0.54f);
                case VNCommandCategory.Audio:       return new Color(0.78f, 0.50f, 0.66f);
                default:                            return new Color(0.54f, 0.48f, 0.35f);
            }
        }

        private static string CategoryName(VNCommandCategory category)
        {
            switch (category)
            {
                case VNCommandCategory.System:      return "系统命令";
                case VNCommandCategory.Performance: return "演出命令";
                case VNCommandCategory.Flow:        return "流程命令（仅链尾）";
                case VNCommandCategory.Logic:       return "逻辑与变量";
                case VNCommandCategory.Audio:       return "音频";
                default:                            return "其他";
            }
        }
    }
}
