using UnityEngine;

namespace VNovelizer.Core.Commands
{
    public class JumpCommand : VNCommand
    {
        public override string CommandName { get { return "jump"; } }

        public override bool Execute(string args)
        {
            if (string.IsNullOrEmpty(args))
            {
                Debug.LogError("Jump命令参数不能为空");
                return false;
            }

            string targetID = args.Trim();
            VNManager manager = VNManager.GetInstance();

            // 直接操作 Manager 的数据
            if (manager.LineIDIndexMap.TryGetValue(targetID, out int targetIndex))
            {
                // 【修复】从 choice 选项执行 jump 时，应该强制跳转到目标行，忽略 choice 命令
                // 使用 ignoreChoice = true 参数，确保即使快进过程中遇到 choice 命令也会继续到目标行
                manager.FastForwardToLine(targetIndex, ignoreChoice: true);
                
                // 强制设置 CurrentLineIndex 为目标行（即使遇到 choice 也要跳转）
                manager.CurrentLineIndex = targetIndex;
                
                // 注意：PlayCurrentLine 会在 ExecuteActionsAndContinue 中自动调用，不需要手动调用
                
                return true;
            }
            else
            {
                Debug.LogError($"[JumpCommand] 未找到指定的行ID: {targetID}");
                return false;
            }
        }
    }
}