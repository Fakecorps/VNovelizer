using UnityEngine;
using VNovelizer.Core.Commands.Meta;

namespace VNovelizer.Core.Commands
{
    /// <summary>
    /// 条件加载剧本命令（取反版）：loadscriptifnot(condition, scriptName[, startId])
    /// 条件为假时等价于 loadscript(scriptName, startId)；为真时无操作。
    /// </summary>
    [VNCommandMeta(VNCommandCategory.Flow,
        "条件加载剧本（取反）：条件为假时等价 loadscript（仅可置于链尾）")]
    public class LoadScriptIfNotCommand : LoadScriptIfCommand
    {
        [VNParam(0, "flag", VNParamType.FlagName,
            Description = "被判断的标志名（候选来自 Flag 注册表；未注册时兼容模式）")]
        [VNParam(1, "condition", VNParamType.FlagCondition,
            Description = "条件：操作符（> < >= <= == != 或留空=直判、! 取反）+ 值")]
        [VNParam(2, "script", VNParamType.ScriptName,
            Description = "剧本名（CSV 文件名，不含扩展名）")]
        [VNParam(3, "startId", VNParamType.ScriptLineId, Optional = true,
            Description = "起始行 ID（可选；候选来自所选剧本的全部行）")]
        public override string CommandName { get { return "loadscriptifnot"; } }

        protected override bool Invert { get { return true; } }
    }
}
