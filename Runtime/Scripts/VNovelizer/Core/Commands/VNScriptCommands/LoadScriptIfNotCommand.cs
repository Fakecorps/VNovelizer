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
        [VNParam(0, "condition", VNParamType.String,
            Description = "条件表达式，如 Amy_Favor >= 80 / Met_Amy / !Met_Amy")]
        [VNParam(1, "script", VNParamType.String,
            Description = "剧本名（CSV 文件名，不含扩展名）")]
        [VNParam(2, "startId", VNParamType.String, Optional = true,
            Description = "起始行 ID（可选）")]
        public override string CommandName { get { return "loadscriptifnot"; } }

        protected override bool Invert { get { return true; } }
    }
}
