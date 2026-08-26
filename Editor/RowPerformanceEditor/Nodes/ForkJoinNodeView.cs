using UnityEditor.Experimental.GraphView;
using VNovelizer.Core.Commands.Chain;

namespace VNovelizer.Editor.RowPerformanceEditor
{
    /// <summary>
    /// FORK / JOIN 并行胶囊：把命令链的 <c>&amp;</c>（并行）语义**显式**画出来。
    ///
    /// <para>
    /// <b>为何要显式节点而不是隐式多连线</b>（决策 d2）：若允许一个命令节点直接连出
    /// 多条边表示并行，用户就无法看出"这些分支在哪里汇合"——而汇合点决定了
    /// 后续命令何时开始。显式 FORK/JOIN 让并行的**范围**一目了然，
    /// 也使图与命令链文本的 <c>[]</c> 分组一一对应。
    /// </para>
    /// </summary>
    public class ForkJoinNodeView : VNNodeViewBase
    {
        public bool IsFork => Data.Kind == ChainGraphNodeKind.Fork;

        public ForkJoinNodeView(ChainGraphNode data, bool isConfirmChain)
            : base(data, isConfirmChain)
        {
            AddToClassList("vn-forkjoin");
            if (isConfirmChain) AddToClassList("vn-forkjoin--confirm");
            Build();
        }

        protected override void Build()
        {
            if (IsFork)
            {
                SetTitle("FORK ∥ 分流");
                tooltip = "并行分流：以下分支同时启动。\n" +
                          "对应命令链的 & 运算符。全部分支完成后才继续 JOIN 之后的命令。";

                InputPort = CreatePort(Direction.Input, Port.Capacity.Single);
                // 出端口容量为 Multiple——一个 FORK 可分出任意多条分支
                OutputPort = CreatePort(Direction.Output, Port.Capacity.Multi, "vn-port-par");
                MultiPorts.Add(OutputPort);
            }
            else
            {
                SetTitle("JOIN ⏫ 汇合");
                tooltip = "并行汇合：等待全部分支完成后，继续执行后续命令。\n" +
                          "每个 FORK 必须有配对的 JOIN，否则命令链无法保存。";

                InputPort = CreatePort(Direction.Input, Port.Capacity.Multi, "vn-port-par");
                MultiPorts.Add(InputPort);
                OutputPort = CreatePort(Direction.Output, Port.Capacity.Single);
            }

            RefreshExpandedState();
            RefreshPorts();
        }

        public override bool IsCopiable() => false;
    }
}
