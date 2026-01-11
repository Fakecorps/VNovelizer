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
                // 【修复】先调用 FastForwardToLine，如果遇到 choice 命令它会设置 CurrentLineIndex
                bool encounteredChoice = manager.FastForwardToLine(targetIndex);
                
                // 只有在没有遇到 choice 时才设置 CurrentLineIndex
                if (!encounteredChoice)
                {
                    manager.CurrentLineIndex = targetIndex;
                }
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