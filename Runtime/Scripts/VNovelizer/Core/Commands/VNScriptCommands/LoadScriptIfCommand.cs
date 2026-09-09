using UnityEngine;
using VNovelizer.Core.Commands.Meta;

namespace VNovelizer.Core.Commands
{
    /// <summary>
    /// 条件加载剧本命令：loadscriptif(condition, scriptName[, startId])
    /// 条件为真时等价于 loadscript(scriptName, startId)；为假时无操作。
    /// 典型用法：章节末尾按好感度分流——
    ///   loadscriptif(Amy_Favor >= 80, Chapter2A, Scene_000)
    ///   loadscriptif(Amy_Favor >= 50, Chapter2B, Scene_000)
    ///   loadscript(Chapter2C)   ← 兜底
    /// </summary>
    [VNCommandMeta(VNCommandCategory.Flow,
        "条件加载剧本：条件为真时等价 loadscript（仅可置于链尾）")]
    public class LoadScriptIfCommand : VNCommand
    {
        [VNParam(0, "flag", VNParamType.FlagName,
            Description = "被判断的标志名（候选来自 Flag 注册表；未注册时兼容模式）")]
        [VNParam(1, "condition", VNParamType.FlagCondition,
            Description = "条件：操作符（> < >= <= == != 或留空=直判、! 取反）+ 值")]
        [VNParam(2, "script", VNParamType.ScriptName,
            Description = "剧本名（CSV 文件名，不含扩展名）")]
        [VNParam(3, "startId", VNParamType.ScriptLineId, Optional = true,
            Description = "起始行 ID（可选；候选来自所选剧本的全部行）")]
        public override string CommandName { get { return "loadscriptif"; } }

        /// <summary>子类覆写为 true 即得到 loadscriptifnot</summary>
        protected virtual bool Invert { get { return false; } }

        public override bool Execute(string args)
        {
            var parts = ConditionParser.SplitTopLevel(args);
            if (parts.Count < 2)
            {
                Debug.LogError("[LoadScriptIf] 参数格式错误，应为 loadscriptif(condition, scriptName[, startId])，当前: \"" + args + "\"");
                return false;
            }

            string cond = parts[0].Trim();
            string scriptName = parts[1].Trim();
            string startID = parts.Count >= 3 && !string.IsNullOrEmpty(parts[2].Trim()) ? parts[2].Trim() : null;

            bool result;
            string error;
            if (!ConditionParser.TryEvaluate(cond, FlagService.GetInstance(), out result, out error))
            {
                Debug.LogError("[LoadScriptIf] 条件 \"" + cond + "\" 求值失败: " + error);
                return false;
            }

            if (Invert) result = !result;
            if (!result) return true; // 条件不满足：无操作，继续

            // 条件满足：等价于 loadscript(scriptName[, startId])，与 LoadScriptCommand.Execute 行为完全一致
            string loadArgs = string.IsNullOrEmpty(startID) ? scriptName : scriptName + "," + startID;
            return CommandManager.GetInstance().ExecuteCommand("loadscript(" + loadArgs + ")");
        }

        public override void Simulate(string args)
        {
            var parts = ConditionParser.SplitTopLevel(args);
            if (parts.Count < 2)
            {
                Debug.LogError("[LoadScriptIf] 参数格式错误，应为 loadscriptif(condition, scriptName[, startId])，当前: \"" + args + "\"");
                return;
            }

            string cond = parts[0].Trim();
            string scriptName = parts[1].Trim();
            string startID = parts.Count >= 3 && !string.IsNullOrEmpty(parts[2].Trim()) ? parts[2].Trim() : null;

            bool result;
            string error;
            if (!ConditionParser.TryEvaluate(cond, FlagService.GetInstance(), out result, out error))
            {
                Debug.LogError("[LoadScriptIf] 条件 \"" + cond + "\" 求值失败: " + error);
                return;
            }

            if (Invert) result = !result;
            if (!result) return; // 条件不满足：不产生切换请求

            VNManager.GetInstance().PendingScriptSwitch = (scriptName, startID);
        }
    }
}
