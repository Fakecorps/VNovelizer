using UnityEngine;

namespace VNovelizer.Core.Commands
{
    /// <summary>
    /// 条件加载剧本命令（取反版）：loadscriptifnot(condition, scriptName[, startId])
    /// 条件为假时等价于 loadscript(scriptName, startId)；为真时无操作。
    /// </summary>
    public class LoadScriptIfNotCommand : LoadScriptIfCommand
    {
        public override string CommandName { get { return "loadscriptifnot"; } }

        protected override bool Invert { get { return true; } }
    }
}
