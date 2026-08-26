using UnityEditor.Experimental.GraphView;
using UnityEngine.UIElements;
using VNovelizer.Core.Commands.Chain;

namespace VNovelizer.Editor.RowPerformanceEditor
{
    /// <summary>终端胶囊的四种形态。</summary>
    public enum TerminalKind
    {
        /// <summary>▷ 行开始（进入链起点，无入端口）</summary>
        LineStart,

        /// <summary>⏸ 等待确认（进入链终点 + 出口链触发点，**双端口**）</summary>
        WaitConfirm,

        /// <summary>⏵ 出口开始（出口链起点，入端口来自点击虚线）</summary>
        ConfirmStart,

        /// <summary>⏭ 链结束（出口链终点，无出端口）</summary>
        ChainEnd,
    }

    /// <summary>
    /// 终端胶囊：标示链的边界与玩家交互点。不产生命令，仅为可读性存在。
    ///
    /// <para>
    /// <b>「等待确认」是双端口</b>：入端口接进入链末尾（演出结束），
    /// 出端口经点击虚线连到「出口开始」。原规格曾记为"单侧端口"，是错误。
    /// </para>
    /// </summary>
    public class TerminalNodeView : VNNodeViewBase
    {
        public TerminalKind Kind { get; private set; }

        public TerminalNodeView(ChainGraphNode data, TerminalKind kind, bool isConfirmChain)
            : base(data, isConfirmChain)
        {
            Kind = kind;
            AddToClassList("vn-terminal");
            if (isConfirmChain) AddToClassList("vn-terminal--confirm");
            Build();
        }

        protected override void Build()
        {
            SetTitle(ResolveTitle());

            var hint = ResolveHint();
            if (!string.IsNullOrEmpty(hint))
            {
                var hintLabel = new Label(hint);
                hintLabel.AddToClassList("vn-terminal-hint");
                titleContainer.Add(hintLabel);
            }

            tooltip = ResolveTooltip();

            // 端口按形态决定——「等待确认」双端口是关键
            switch (Kind)
            {
                case TerminalKind.LineStart:
                    OutputPort = CreatePort(Direction.Output, Port.Capacity.Single);
                    break;

                case TerminalKind.WaitConfirm:
                    InputPort = CreatePort(Direction.Input, Port.Capacity.Single);
                    OutputPort = CreatePort(Direction.Output, Port.Capacity.Single, "vn-port-confirm");
                    break;

                case TerminalKind.ConfirmStart:
                    InputPort = CreatePort(Direction.Input, Port.Capacity.Single, "vn-port-confirm");
                    OutputPort = CreatePort(Direction.Output, Port.Capacity.Single);
                    break;

                case TerminalKind.ChainEnd:
                    InputPort = CreatePort(Direction.Input, Port.Capacity.Single);
                    break;
            }

            // 终端是结构性节点，禁止删除与拖出画布
            capabilities &= ~Capabilities.Deletable;
            capabilities &= ~Capabilities.Copiable;

            RefreshExpandedState();
            RefreshPorts();
        }

        private string ResolveTitle()
        {
            switch (Kind)
            {
                case TerminalKind.LineStart:    return "▷ 行开始";
                case TerminalKind.WaitConfirm:  return "⏸ 等待确认";
                case TerminalKind.ConfirmStart: return "⏵ 出口开始";
                case TerminalKind.ChainEnd:     return "⏭ 链结束";
                default:                        return "终端";
            }
        }

        private string ResolveHint()
        {
            switch (Kind)
            {
                case TerminalKind.WaitConfirm:  return "演出结束";
                case TerminalKind.ConfirmStart: return "点击后";
                default:                       return null;
            }
        }

        private string ResolveTooltip()
        {
            switch (Kind)
            {
                case TerminalKind.LineStart:
                    return "进入本行时命令链从这里开始执行。";
                case TerminalKind.WaitConfirm:
                    return "进入段演出完成，等待玩家点击或 AutoPlay。\n" +
                           "点击后执行出口段（@Confirm 链），再推进到下一行。";
                case TerminalKind.ConfirmStart:
                    return "出口段起点：玩家点击后才执行的命令从这里开始。\n" +
                           "常用于转场、跳转等「离开本行」的编排。";
                case TerminalKind.ChainEnd:
                    return "命令链结束。若链尾是 jump 等流程命令，则跳转至其目标；" +
                           "否则推进到下一行。";
                default:
                    return "";
            }
        }

        public override bool IsCopiable() => false;
    }
}
