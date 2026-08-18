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

        /// <summary>
        /// 快进预演：写入 PendingJumpIndex，由 VNManager.FastForwardToLine 在每行模拟后消费。
        /// 【Fix P3】此前未重写 Simulate，读档/跳行快进会忽略 jump 且把被跳过的行当作已执行，导致状态错乱。
        /// </summary>
        public override void Simulate(string args)
        {
            string targetID = (args ?? "").Trim();
            if (string.IsNullOrEmpty(targetID))
            {
                Debug.LogError("[JumpCommand] Simulate 参数不能为空");
                return;
            }

            VNManager manager = VNManager.GetInstance();
            if (manager.LineIDIndexMap.TryGetValue(targetID, out int targetIndex))
            {
                manager.PendingJumpIndex = targetIndex;
            }
            else
            {
                Debug.LogError($"[JumpCommand] 快进中未找到指定的行ID: {targetID}");
            }
        }
    }
}