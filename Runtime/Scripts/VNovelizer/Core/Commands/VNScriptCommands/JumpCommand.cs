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
                manager.FastForwardToLine(targetIndex, ignoreChoice: true);
                manager.CurrentLineIndex = targetIndex;
                
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