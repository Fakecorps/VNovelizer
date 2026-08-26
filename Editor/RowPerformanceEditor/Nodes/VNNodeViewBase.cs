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

            // 去掉 GraphView 默认的标题文字 + 折叠箭头——我们用自定义 label
            HideDefaultTitle();

            // 立即插入自定义标题（父类构造期间 titleContainer 已可写入）
            _customTitleLabel = new Label(data.CommandName ?? "(未指定)");
            _customTitleLabel.AddToClassList("vn-node-title");
            _customTitleLabel.pickingMode = PickingMode.Ignore;
            titleContainer.Insert(0, _customTitleLabel);
        }

        /// <summary>设置节点显示标题（命令节点调用一次即可）</summary>
        protected void SetTitle(string text)
        {
            if (_customTitleLabel != null) _customTitleLabel.text = text ?? "(未指定)";
        }

        /// <summary>隐藏 GraphView 默认标题（默认 label + 折叠按钮）</summary>
        private void HideDefaultTitle()
        {
            // titleContainer 是 base Node 在 OnEnable 时才填充 #title-label，
            // 但 title-button-container 始终存在，先去掉它
            titleContainer.Q("title-button-container")?.RemoveFromHierarchy();

            // 延后一帧再清掉默认 #title-label（创建时机不确定）
            schedule.Execute(() =>
            {
                var defaultLabel = titleContainer.Q<Label>();
                if (defaultLabel != null && defaultLabel != _customTitleLabel)
                    defaultLabel.RemoveFromHierarchy();
            }).ExecuteLater(0);
        }

        /// <summary>子类在构造末尾调用，完成端口与内容的装配。</summary>
        protected abstract void Build();

        // ---------------- 端口 ----------------

        /// <summary>
        /// 创建单个端口。端口方向统一为 <see cref="Orientation.Vertical"/>——
        /// 执行流自上而下，与命令链的阅读顺序一致。
        /// </summary>
        protected Port CreatePort(Direction direction, Port.Capacity capacity, string styleClass = null)
        {
            var port = InstantiatePort(Orientation.Vertical, direction, capacity, typeof(bool));
            port.portName = "";
            if (!string.IsNullOrEmpty(styleClass)) port.AddToClassList(styleClass);
            if (IsConfirmChain) port.AddToClassList("vn-port-confirm");

            if (direction == Direction.Input) inputContainer.Add(port);
            else outputContainer.Add(port);

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
