using UnityEngine;
using VNovelizer.Core.Commands.Meta;

namespace VNovelizer.Core.Commands
{
    /// <summary>
    /// 【TextActor】指定当前正文目标（粘性选择）。
    /// 格式：<c>setTextActor(posID)</c>，posID = 槽位 L/ML/M/MR/R 或 addChar 注册的自定义 posID。
    ///
    /// 多个文字角色在台上时必须用它消歧（否则正文回退 Dialogue 并告警）；
    /// 定制行放在 showDialogue 之前当行生效；普通行放在命令列则从下一行起生效
    /// （普通行正文先于命令列播放）。选择持续到再次调用或目标消失。
    /// </summary>
    [VNCommandMeta(VNCommandCategory.Performance,
        "指定正文写入的文字角色槽位（粘性；多个文字角色在台上时用于消歧）")]
    public class SetTextActorCommand : VNCommand
    {
        [VNParam(0, "posID", VNParamType.SlotCode,
            Description = "文字角色所在槽位（L/ML/M/MR/R 或自定义 posID）")]
        public override string CommandName { get { return "settextactor"; } }

        public override bool Execute(string args)
        {
            return SetFromArgs(args);
        }

        /// <summary>快进/读档预演：目标选择是数据态操作，与 Execute 一致。</summary>
        public override void Simulate(string args)
        {
            SetFromArgs(args);
        }

        private static bool SetFromArgs(string args)
        {
            string posID = args?.Trim();
            if (string.IsNullOrEmpty(posID))
            {
                Debug.LogError("[SetTextActor] 参数不能为空，格式: setTextActor(posID)");
                return false;
            }
            return VNManager.GetInstance().SetTextActorSelection(posID);
        }
    }
}
