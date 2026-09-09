using UnityEngine;
using VNovelizer.Core.Commands.Meta;

namespace VNovelizer.Core.Commands
{
    /// <summary>
    /// 条件跳转命令（取反版）：jumpifnot(flag, condition, targetID)
    /// 条件为假时等价于 jump(targetId)；为真时无操作。
    /// 比 jumpif(!flag, ...) 在 Excel 中更醒目。
    /// 编辑器表单与 jumpif 相同：flag + condition + targetID 三字段，CSV 仍为旧二段格式。
    /// </summary>
    [VNCommandMeta(VNCommandCategory.Flow,
        "条件跳转（取反）：条件为假时等价 jump（仅可置于链尾）")]
    public class JumpIfNotCommand : JumpIfCommand
    {
        [VNParam(0, "flag", VNParamType.FlagName,
            Description = "被判断的标志名（候选来自 Flag 注册表；未注册时兼容模式）")]
        [VNParam(1, "condition", VNParamType.FlagCondition,
            Description = "条件：操作符（> < >= <= == != 或留空=直判、! 取反）+ 值")]
        [VNParam(2, "targetID", VNParamType.LineId,
            Description = "目标行 ID（本剧本内）")]
        public override string CommandName { get { return "jumpifnot"; } }

        protected override bool Invert { get { return true; } }
    }
}
