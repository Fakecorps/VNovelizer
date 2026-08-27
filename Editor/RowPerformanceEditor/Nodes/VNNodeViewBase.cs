using System.Collections.Generic;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;
using VNovelizer.Core.Commands.Chain;

namespace VNovelizer.Editor.RowPerformanceEditor
{
    /// <summary>
    /// 全部行演出编辑器节点的公共基类。
    ///
    /// <para>
    /// 承载共性：绑定的 <see cref="ChainGraphNode"/> 数据、端口创建、
    /// 分类色带、语义角标、校验状态样式。
    /// </para>
    ///
    /// <para>
    /// <b>数据与视图分离</b>：节点视图只持有 <see cref="ChainGraphNode"/> 引用，
    /// 不自行保存命令名/参数——真值永远在数据模型里，视图只负责呈现。
    /// 这样保存链路（Validator → GraphToAst → Serializer）可完全脱离 UI 运行。
    /// </para>
    ///
    /// <para>
    /// <b>标题绘制完全自管</b>：Unity GraphView 的 <c>title</c> 属性在
    /// <c>OnEnable</c> 之前或之后调用都可能不被写入 <c>#title-label</c>，
    /// 且其默认 label 不可定制字体字号。我们直接在 <c>titleContainer</c> 里
    /// 放自己的 label 并把默认的隐藏——确定性更高，样式可完全受 USS 控制。
    /// </para>
    /// </summary>
    public abstract class VNNodeViewBase : Node
    {
        /// <summary>绑定的图数据节点（真值来源）</summary>
        public ChainGraphNode Data { get; private set; }

        /// <summary>是否属于出口段（@Confirm 链）——决定配色</summary>
        public bool IsConfirmChain { get; private set; }

        public Port InputPort { get; protected set; }
        public Port OutputPort { get; protected set; }

        /// <summary>Fork/Join 的多端口列表（其余节点为空）</summary>
        public List<Port> MultiPorts { get; } = new List<Port>();

        /// <summary>自定义标题 label（隐藏在 base 之后）</summary>
        private Label _customTitleLabel;

        protected VNNodeViewBase(ChainGraphNode data, bool isConfirmChain)
        {
            Data = data;
            IsConfirmChain = isConfirmChain;

            AddToClassList("vn-node");
            if (isConfirmChain) AddToClassList("vn-node--confirm");

            // 2026-08-27：端口贴外侧（left/right:-14）必须节点不裁剪——GraphView.Node 的
            // #node-border 默认 overflow:hidden，会把溢出到节点外的端口圆裁掉看不见。
            // 设 Node 自身与 #node-border 均为 visible（inline 兜底，避免 USS 选择器覆盖不到）。
            // #node-border 可能尚未构建（构造期），挂载后再补一次 + GeometryChanged 兜底。
            style.overflow = Overflow.Visible;
            ApplyBorderOverflow();
            RegisterCallback<AttachToPanelEvent>(_ => ApplyBorderOverflow());

            // 去掉 GraphView 默认的标题文字 + 折叠箭头——我们用自定义 label
            HideDefaultTitle();

            // 立即插入自定义标题（父类构造期间 titleContainer 已可写入）
            _customTitleLabel = new Label(data.CommandName ?? "(未指定)");
            _customTitleLabel.AddToClassList("vn-node-title");
            _customTitleLabel.pickingMode = PickingMode.Ignore;
            titleContainer.Insert(0, _customTitleLabel);
        }

        /// <summary>设置节点显示标题（命令节点调用一次即可；图编辑器可对影子节点覆写标题）</summary>
        public void SetTitle(string text)
        {
            if (_customTitleLabel != null) _customTitleLabel.text = text ?? "(未指定)";
        }

        /// <summary>
        /// 强制 <c>#node-border</c> 不裁剪溢出内容——端口贴外侧的前提。
        /// 构造期 border 可能未构建，挂载后再补一次。
        /// </summary>
        private void ApplyBorderOverflow()
        {
            var border = this.Q<VisualElement>(name: "node-border");
            if (border != null) border.style.overflow = Overflow.Visible;
        }

        /// <summary>隐藏 GraphView 默认标题（默认 label + 折叠按钮）</summary>
        private void HideDefaultTitle()
        {
            // titleContainer 是 base Node 在 OnEnable 时才填充 #title-label，
            // 但 title-button-container 始终存在，先去掉它
            titleContainer.Q("title-button-container")?.RemoveFromHierarchy();

            // 延后一帧再清掉默认 #title-label（创建时机不确定）。
            // 用 classListContains 排除我们自定义的 vn-node-title，
            // 避免误删 TerminalNodeView 的 vn-terminal-hint 等其他 Label。
            schedule.Execute(() =>
            {
                foreach (var lbl in titleContainer.Query<Label>().ToList())
                {
                    if (lbl == _customTitleLabel) continue;
                    if (lbl.ClassListContains("vn-node-title")) continue;
                    if (lbl.ClassListContains("vn-terminal-hint")) continue;
                    if (lbl.ClassListContains("vn-tpl-expander")) continue;
                    if (lbl.ClassListContains("vn-tpl-count")) continue;
                    lbl.RemoveFromHierarchy();
                }
            }).ExecuteLater(0);
        }

        /// <summary>子类在构造末尾调用，完成端口与内容的装配。</summary>
        protected abstract void Build();

        // ---------------- 端口 ----------------

        /// <summary>
        /// 创建单个端口。端口方向统一为 <see cref="Orientation.Horizontal"/>——
        /// 执行流从左到右，输入端口在左侧，输出端口在右侧，符合阅读顺序。
        /// <para>
        /// <b>V3 重构（2026-08-27）</b>：端口位置在 C# 层用 inline style 强制设置，
        /// 而不是依赖 USS。原因：Unity GraphView 的 inputContainer/outputContainer
        /// 内部 VisualElement 层级复杂，<c>.vn-node &gt; #inputContainer</c>
        /// 选择器的特异性不足以覆盖 GraphView 默认样式，导致端口仍在节点中部。
        /// inline style 优先级最高，无论 USS 加载与否都能保证端口贴外侧 + 正圆。
        /// </para>
        /// </summary>
        protected Port CreatePort(Direction direction, Port.Capacity capacity, string styleClass = null)
        {
            var port = InstantiatePort(Orientation.Horizontal, direction, capacity, typeof(bool));
            port.portName = "";
            if (!string.IsNullOrEmpty(styleClass)) port.AddToClassList(styleClass);
            if (IsConfirmChain) port.AddToClassList("vn-port-confirm");

            // ---- 强制端口容器贴边（inline style 兜底） ----
            var container = direction == Direction.Input ? inputContainer : outputContainer;
            var cs = container.style;
            cs.position = Position.Absolute;
            // 2026-08-27（用户需求 2 修复）：容器尺寸 = connector 尺寸（14×14）。
            // 之前 16×16 容器在 14×14 圆外留 1px 边——露出容器背景呈"纯色方块"包裹感。
            cs.width = 14;
            cs.height = 14;
            cs.marginLeft = 0;
            cs.marginRight = 0;
            cs.marginTop = -7;     // 14/2 = 7（垂直居中）
            cs.marginBottom = 0;
            cs.paddingLeft = 0;
            cs.paddingRight = 0;
            cs.paddingTop = 0;
            cs.paddingBottom = 0;
            cs.backgroundColor = StyleKeyword.None;  // 兜底透明，防 GraphView 内置背景
            cs.borderTopWidth = 0;
            cs.borderBottomWidth = 0;
            cs.borderLeftWidth = 0;
            cs.borderRightWidth = 0;
            cs.top = Length.Percent(50);
            // 2026-08-27（用户需求 4）：端口完全在节点内（距左/右边缘 8px）。
            // 之前 -14 贴外导致端口看似"悬浮"在节点外——用户明确要求端口属于节点内部。
            if (direction == Direction.Input)
                cs.left = 8;
            else
                cs.right = 8;

            // ---- 强制 Port 元素本身 16x16 + 清零 ----
            var ps = port.style;
            ps.width = 16;
            ps.height = 16;
            ps.minWidth = 16;
            ps.minHeight = 16;
            ps.maxWidth = 16;
            ps.maxHeight = 16;
            ps.marginLeft = 0; ps.marginRight = 0; ps.marginTop = 0; ps.marginBottom = 0;
            ps.paddingLeft = 0; ps.paddingRight = 0; ps.paddingTop = 0; ps.paddingBottom = 0;
            ps.backgroundColor = StyleKeyword.None;

            container.Add(port);

            // ---- 强制 #connector 绝对定位 + 正圆 ----
            var connector = port.Q("connector");
            if (connector != null)
            {
                connector.style.position = Position.Absolute;
                connector.style.left = 0;
                connector.style.top = 0;
                connector.style.width = 14;
                connector.style.height = 14;
                connector.style.borderTopWidth = 2;
                connector.style.borderRightWidth = 2;
                connector.style.borderBottomWidth = 2;
                connector.style.borderLeftWidth = 2;
                connector.style.borderTopLeftRadius = 7;
                connector.style.borderTopRightRadius = 7;
                connector.style.borderBottomLeftRadius = 7;
                connector.style.borderBottomRightRadius = 7;
            }

            // ---- 强制 #cap 绝对定位 + 正圆 ----
            var cap = port.Q("cap");
            if (cap != null)
            {
                cap.style.position = Position.Absolute;
                cap.style.left = 3;
                cap.style.top = 3;
                cap.style.width = 4;
                cap.style.height = 4;
                cap.style.borderTopLeftRadius = 2;
                cap.style.borderTopRightRadius = 2;
                cap.style.borderBottomLeftRadius = 2;
                cap.style.borderBottomRightRadius = 2;
            }

            // ---- 隐藏 #type 与 portName Label（防占布局） ----
            var typeEl = port.Q("type");
            if (typeEl != null)
            {
                typeEl.style.display = DisplayStyle.None;
                typeEl.style.position = Position.Absolute;
                typeEl.style.left = 0;
                typeEl.style.top = 0;
                typeEl.style.width = 0;
                typeEl.style.height = 0;
            }

            // ---- 隐藏 Port 内部所有 Label（portName 即使为空也占布局） ----
            foreach (var child in port.Children())
            {
                if (child is Label)
                {
                    child.style.display = DisplayStyle.None;
                    child.style.position = Position.Absolute;
                    child.style.left = 0;
                    child.style.top = 0;
                    child.style.width = 0;
                    child.style.height = 0;
                }
            }

            return port;
        }

        /// <summary>创建标准的单入单出端口。</summary>
        protected void CreateStandardPorts(bool withInput = true, bool withOutput = true)
        {
            if (withInput) InputPort = CreatePort(Direction.Input, Port.Capacity.Single);
            if (withOutput) OutputPort = CreatePort(Direction.Output, Port.Capacity.Single);
        }

        // ---------------- 装饰 ----------------

        /// <summary>添加左侧分类色带。</summary>
        protected void AddAccentBar(string accentClass)
        {
            var accent = new VisualElement();
            accent.AddToClassList("vn-node-accent");
            if (!string.IsNullOrEmpty(accentClass)) accent.AddToClassList(accentClass);
            accent.pickingMode = PickingMode.Ignore;
            Insert(0, accent);
        }

        /// <summary>
        /// 在标题右侧添加语义角标。
        /// </summary>
        /// <param name="text">角标文字（含符号，如 "📎 Text"）</param>
        /// <param name="styleClass">配色样式类（vn-badge-ref / vn-badge-flow / …）</param>
        /// <param name="tooltip">悬停说明——角标是缩写，必须有完整解释</param>
        protected void AddBadge(string text, string styleClass, string tooltip = null)
        {
            var badge = new Label(text);
            badge.AddToClassList("vn-badge");
            if (!string.IsNullOrEmpty(styleClass)) badge.AddToClassList(styleClass);
            if (!string.IsNullOrEmpty(tooltip)) badge.tooltip = tooltip;
            titleContainer.Add(badge);
        }

        // ---------------- 校验状态 ----------------

        /// <summary>应用校验结果的视觉状态（标红 / 高亮 / 恢复）。</summary>
        public void ApplyValidationState(ChainGraphIssueLevel? level, string message = null)
        {
            RemoveFromClassList("vn-node--error");
            RemoveFromClassList("vn-node--warning");
            tooltip = "";

            if (level == ChainGraphIssueLevel.Fatal)
            {
                AddToClassList("vn-node--error");
                tooltip = message ?? "";
            }
            else if (level == ChainGraphIssueLevel.Warning)
            {
                AddToClassList("vn-node--warning");
                tooltip = message ?? "";
            }
        }

        /// <summary>标记为模板影子节点（半透明虚线，未持久化到 Command 列）。</summary>
        public void MarkAsTemplateGhost()
        {
            AddToClassList("vn-node--template");
        }

        // ---------------- GraphView 行为 ----------------

        /// <summary>
        /// 节点是否可删除。模板影子节点的"删除"应触发按需提升确认流程，
        /// 而不是直接从图上移除，故由 GraphView 层拦截。
        /// </summary>
        public override bool IsCopiable() => Data != null && Data.Kind == ChainGraphNodeKind.Command;
    }
}
