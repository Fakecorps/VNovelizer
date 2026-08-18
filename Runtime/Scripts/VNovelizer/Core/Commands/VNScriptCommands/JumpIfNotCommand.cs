using UnityEngine;

namespace VNovelizer.Core.Commands
{
    /// <summary>
    /// 条件跳转命令（取反版）：jumpifnot(condition, targetId)
    /// 条件为假时等价于 jump(targetId)；为真时无操作。
    /// 比 jumpif(!flag, ...) 在 Excel 中更醒目。
    /// </summary>
    public class JumpIfNotCommand : JumpIfCommand
    {
        public override string CommandName { get { return "jumpifnot"; } }

        protected override bool Invert { get { return true; } }
    }
}
