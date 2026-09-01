using UnityEngine;
using VNovelizer.Core.Commands.Meta;

namespace VNovelizer.Core.Commands
{
    /// <summary>
    /// 推进到下一行。Excel / 命令链写法：<c>nextline()</c>
    ///
    /// <para>
    /// <b>2026-08-31 新增</b>：此前「本行演出完毕 → 推进下一行」是引擎隐式行为
    /// （<see cref="Core.Managers.VNManager.AdvanceToNextLine"/> 内部自动
    /// <c>CurrentLineIndex++</c>），作者在节点图上看不到这一步。现在把它显式化为
    /// 一个真实的 Flow 命令，可拖拽、可编排、可序列化。
    /// </para>
    ///
    /// <para>
    /// <b>为什么不直接调用 VNManager.NextLine()</b>：本命令在命令链执行过程中被
    /// CommandManager 调用，此时 <c>CommandManager.IsRunning == true</c>。若直接调
    /// <c>NextLine()</c>，会走进 <c>AdvanceToNextLine</c> 的「打断正在跑的演出」分支——
    /// <c>InterruptAll()</c> 把当前链（含本命令）中断，然后 return，既没有推进也毁掉了
    /// 后续命令。因此这里采用与 <c>jump</c> 相同的「待消费标志」模式：置
    /// <see cref="Core.Managers.VNManager.PendingNextLine"/>，由 VNManager 在
    /// <b>命令链执行完毕后</b>消费并推进 —— 效果仍是「链尾 nextline → 立刻推进下一行、
    /// 不等玩家点击」，但不会递归、不会自我打断、不会与引擎推进叠加。
    /// </para>
    /// </summary>
    [VNCommandMeta(VNCommandCategory.Flow,
        "推进到下一行（流程命令，必须位于链尾）")]
    public class NextLineCommand : VNCommand
    {
        public override string CommandName { get { return "nextline"; } }

        public override bool Execute(string args)
        {
            if (!string.IsNullOrEmpty(args))
            {
                Debug.LogError("[NextLineCommand] nextline 命令不接受参数，正确写法：nextline()");
                return false;
            }

            VNManager.GetInstance().RequestNextLine();
            return true;
        }

        /// <summary>
        /// 快进 / 读档预演：与 <c>jump</c> 的 Simulate 同构 —— 置待消费标志，
        /// 由 <c>FastForwardToLine</c> 在每行模拟后消费，保证跳过时流程推进正确。
        /// </summary>
        public override void Simulate(string args)
        {
            VNManager.GetInstance().PendingNextLine = true;
        }
    }
}
