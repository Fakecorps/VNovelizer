using UnityEngine;
using VNovelizer.Core.Commands.Meta;
using VNovelizer.Core.Theater;

namespace VNovelizer.Core.Commands
{
    /// <summary>
    /// 【TextActor】清空文字角色的累积段落（保留面板与显隐状态）。
    /// 格式：<c>clearTextActor(posID)</c>，posID = 槽位 L/ML/M/MR/R 或 addChar 注册的自定义 posID。
    ///
    /// 语义：删除面板上已有段落（当前打字先补全），角色继续存在并接收后续正文。
    /// 剧本回跳/重播产生的重复累积由本命令手动清理（文字段落不自动去重）。
    /// </summary>
    [VNCommandMeta(VNCommandCategory.Performance,
        "清空文字角色的累积段落（保留面板，继续接收后续正文）")]
    public class ClearTextActorCommand : VNCommand
    {
        [VNParam(0, "posID", VNParamType.SlotCode,
            Description = "文字角色所在槽位（L/ML/M/MR/R 或自定义 posID）")]
        public override string CommandName { get { return "cleartextactor"; } }

        public override bool Execute(string args)
        {
            return ClearFromArgs(args);
        }

        /// <summary>快进/读档预演：清空是数据态操作，与 Execute 一致。</summary>
        public override void Simulate(string args)
        {
            ClearFromArgs(args);
        }

        private static bool ClearFromArgs(string args)
        {
            string posID = args?.Trim();
            if (string.IsNullOrEmpty(posID))
            {
                Debug.LogError("[ClearTextActor] 参数不能为空，格式: clearTextActor(posID)");
                return false;
            }

            string posCode = TheaterManager.NormalizePosCode(posID);
            if (posCode == null)
            {
                Debug.LogWarning($"[ClearTextActor] 未知槽位/PosID \"{posID}\"");
                return false;
            }

            var actor = TheaterManager.GetInstance().GetTextActor(posCode);
            if (actor == null)
            {
                Debug.LogWarning($"[ClearTextActor] 槽位 {posCode} 没有文字角色");
                return false;
            }

            actor.ClearParagraphs();
            return true;
        }
    }
}
