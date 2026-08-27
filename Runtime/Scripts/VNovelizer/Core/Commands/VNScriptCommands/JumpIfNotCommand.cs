using UnityEngine;
using VNovelizer.Core.Commands.Meta;

namespace VNovelizer.Core.Commands
{
    /// <summary>
    /// 条件跳转命令（取反版）：jumpifnot(condition, targetId)
    /// 条件为假时等价于 jump(targetId)；为真时无操作。
    /// 比 jumpif(!flag, ...) 在 Excel 中更醒目。
    /// </summary>
    [VNCommandMeta(VNCommandCategory.Flow,
        "条件跳转（取反）：条件为假时等价 jump（仅可置于链尾）")]
    public class JumpIfNotCommand : JumpIfCommand
    {
        [VNParam(0, "condition", VNParamType.String,
            Description = "条件表达式，如 Amy_Favor >= 50 / Met_Amy / !Met_Amy")]
        [VNParam(1, "targetId", VNParamType.String,
            Description = "目标行 ID（本剧本内）")]
        public override string CommandName { get { return "jumpifnot"; } }

        protected override bool Invert { get { return true; } }
    }
}
