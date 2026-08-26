using UnityEngine;
using VNovelizer.Core.Commands.Meta;

namespace VNovelizer.Core.Commands
{
    /// <summary>
    /// 无条件跳转到指定行 ID。流程命令——必须置于命令链末尾。
    /// </summary>
    [VNCommandMeta(VNCommandCategory.Flow, "跳转到指定行 ID（流程命令，必须位于链尾）")]
    public class JumpCommand : VNCommand
    {
        [VNParam(0, "targetLineId", VNParamType.LineId, Description = "目标行 ID")]
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