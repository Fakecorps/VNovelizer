using UnityEngine;

namespace VNovelizer.Core.Commands
{
    /// <summary>
    /// 条件跳转命令：jumpif(condition, targetId)
    /// 条件为真时等价于 jump(targetId)；为假时无操作，继续执行同行后续命令与下一行。
    /// 条件语法：Amy_Favor >= 50 / Met_Amy / !Met_Amy / PlayerName == "Alice"
    /// 行为与 jump 完全对齐（Execute 走 jump 逻辑；Simulate 写入 PendingJumpIndex）。
    /// </summary>
    public class JumpIfCommand : VNCommand
    {
        public override string CommandName { get { return "jumpif"; } }

        /// <summary>子类覆写为 true 即得到 jumpifnot</summary>
        protected virtual bool Invert { get { return false; } }

        public override bool Execute(string args)
        {
            var parts = ConditionParser.SplitTopLevel(args);
            if (parts.Count < 2)
            {
                Debug.LogError("[JumpIf] 参数格式错误，应为 jumpif(condition, targetId)，当前: \"" + args + "\"");
                return false;
            }

            string cond = parts[0].Trim();
            string targetID = parts[1].Trim();

            bool result;
            string error;
            if (!ConditionParser.TryEvaluate(cond, FlagService.GetInstance(), out result, out error))
            {
                Debug.LogError("[JumpIf] 条件 \"" + cond + "\" 求值失败: " + error);
                return false;
            }

            if (Invert) result = !result;
            if (!result) return true; // 条件不满足：无操作，继续

            // 条件满足：等价于 jump(targetId)，与 JumpCommand.Execute 行为完全一致
            return CommandManager.GetInstance().ExecuteCommand("jump(" + targetID + ")");
        }

        public override void Simulate(string args)
        {
            var parts = ConditionParser.SplitTopLevel(args);
            if (parts.Count < 2)
            {
                Debug.LogError("[JumpIf] 参数格式错误，应为 jumpif(condition, targetId)，当前: \"" + args + "\"");
                return;
            }

            string cond = parts[0].Trim();
            string targetID = parts[1].Trim();

            bool result;
            string error;
            if (!ConditionParser.TryEvaluate(cond, FlagService.GetInstance(), out result, out error))
            {
                Debug.LogError("[JumpIf] 条件 \"" + cond + "\" 求值失败: " + error);
                return;
            }

            if (Invert) result = !result;
            if (!result) return; // 条件不满足：不产生跳转请求，快进按原顺序继续

            VNManager manager = VNManager.GetInstance();
            if (manager.LineIDIndexMap.TryGetValue(targetID, out int targetIndex))
            {
                manager.PendingJumpIndex = targetIndex;
            }
            else
            {
                Debug.LogError("[JumpIf] 快进中未找到指定的行 ID: " + targetID);
            }
        }
    }
}
