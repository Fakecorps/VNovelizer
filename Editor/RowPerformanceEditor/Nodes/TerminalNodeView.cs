using UnityEditor.Experimental.GraphView;
using UnityEngine.UIElements;
using VNovelizer.Core.Commands.Chain;

namespace VNovelizer.Editor.RowPerformanceEditor
{
    /// <summary>终端胶囊的四种形态（2026-08-27 命名更新：UE 蓝图式"子弹形"）。</summary>
    public enum TerminalKind
    {
        /// <summary>行入口 LineEntry：进入链起点，无入端口（绿色子弹·左直右圆）</summary>
        LineStart,

        /// <summary>行出口 LineExit：进入链终点 + 出口链触发点，**双端口**（绿色子弹·左圆右直）</summary>
        WaitConfirm,

        /// <summary>确认入口 OnConfirmEntry：出口链起点（橙色子弹·左直右圆）</summary>
        ConfirmStart,

        /// <summary>确认出口 OnConfirmExit：出口链终点，无出端口（橙色子弹·左圆右直）</summary>
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
            // 按 Kind 加子弹形类名（决定颜色 + 方向）
            AddToClassList(ResolveTerminalShapeClass());
            Build();
        }

        /// <summary>
        /// 终端节点形状类（颜色 + 子弹方向）。
        /// Entry 类：行入口（绿）+ 确认入口（橙），形状左直右圆（入在左直、出在右圆头）。
        /// Exit 类：行出口（绿）+ 确认出口（橙），形状左圆右直（入在左圆头、出在右直边）。
        /// </summary>
        private string ResolveTerminalShapeClass()
        {
            switch (Kind)
            {
                case TerminalKind.LineStart:    return "vn-term--lineentry";
                case TerminalKind.WaitConfirm:  return "vn-term--lineexit";
                case TerminalKind.ConfirmStart: return "vn-term--confirmentry";
                case TerminalKind.ChainEnd:     return "vn-term--confirmextit";
                default:                        return "vn-term--lineentry";
            }
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

            // 端口按形态决定（2026-08-27 重构：UE 蓝图式单引脚）
            switch (Kind)
            {
                case TerminalKind.LineStart:
                    // LineEntry：行入口，引脚在右圆头（输出）
                    OutputPort = CreatePort(Direction.Output, Port.Capacity.Single);
                    break;

                case TerminalKind.WaitConfirm:
                    // LineExit：行出口，引脚在左圆头（单输入）
                    InputPort = CreatePort(Direction.Input, Port.Capacity.Single);
                    break;

                case TerminalKind.ConfirmStart:
                    // OnConfirmEntry：确认入口，引脚在右圆头（单输出）
                    OutputPort = CreatePort(Direction.Output, Port.Capacity.Single);
                    break;

                case TerminalKind.ChainEnd:
                    // OnConfirmExit：确认出口，引脚在左圆头（单输入）
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
                case TerminalKind.LineStart:    return "Line Entry";
                case TerminalKind.WaitConfirm:  return "Line Exit";
                case TerminalKind.ConfirmStart: return "OnConfirm Entry";
                case TerminalKind.ChainEnd:     return "OnConfirm Exit";
                default:                        return "Terminal";
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
