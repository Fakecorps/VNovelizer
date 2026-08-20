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

            Debug.Log("[HideCommand] 启用了一次隐藏命令");
            var panel = UIManager.GetInstance().Get<VNGameplayPanel>();
            panel?.OnHide(default(InputAction.CallbackContext));
            return true;
        }
    }
}