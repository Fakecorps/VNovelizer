using UnityEngine;
using VNovelizer.Core.Commands.Meta;

namespace VNovelizer.Core.Commands.SystemCommands
{
    [VNCommandMeta(VNCommandCategory.System,
        "隐藏原对话框、姓名框和头像；保留输入、文字角色及其他功能 UI")]
    public class HideDialogueCommand : VNCommand
    {
        public override string CommandName => "hidedialogue";

        public override bool Execute(string args)
        {
            if (!string.IsNullOrWhiteSpace(args))
            {
                Debug.LogWarning("[HideDialogue] 不接受参数，请使用 hideDialogue()");
                return false;
            }
            VNManager.GetInstance().SysHideDialogue();
            return true;
        }

        public override void Simulate(string args)
        {
            Execute(args);
        }
    }
}
