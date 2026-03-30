using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace VNovelizer.Core.Commands
{
    public class HideCommand : VNCommand
    {
        public override string CommandName { get { return "hide"; } }

        public override bool Execute(string args)
        {
            if (!string.IsNullOrEmpty(args))
            {
                Debug.LogError("hide命令参数应为空");
                return false;
            }
            
            Debug.Log("[Hidecommand] 启用了一次隐藏命令");
            // string targetID = args.Trim();
            // VNManager manager = VNManager.GetInstance();
            
            // VNovelizer.Core.API.
            // 直接操作 Manager 的数据
            // if (manager.LineIDIndexMap.TryGetValue(targetID, out int targetIndex))
            // {
            //     manager.FastForwardToLine(targetIndex, ignoreChoice: true);
            //     manager.CurrentLineIndex = targetIndex;
            //     
            //     return true;
            // }
            // else
            // {
            //     Debug.LogError($"[JumpCommand] 未找到指定的行ID: {targetID}");
            //     return false;
            // }
            var panel = UIManager.GetInstance().GetPanel<VNGameplayPanel>("VNGameplayPanel");
            panel?.OnHide(default(InputAction.CallbackContext));
            return true;
        }
    }
}