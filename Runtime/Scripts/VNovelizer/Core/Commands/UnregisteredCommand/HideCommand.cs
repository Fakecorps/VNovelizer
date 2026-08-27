using UnityEngine;
using UnityEngine.InputSystem;
using VNovelizer.Core.Commands.Meta;

namespace VNovelizer.Core.Commands
{
    [VNCommandMeta(VNCommandCategory.System,
        "手动隐藏对话框（等同玩家按隐藏键；无参数）")]
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