using UnityEngine;
using VNovelizer.Core.Commands.Meta;
using VNovelizer.Core.Theater;

namespace VNovelizer.Core.Commands
{
    /// <summary>
    /// 【TextActor】隐藏文字角色并清空全部段落（退场销毁）。
    /// 格式：<c>hideTextActor(posID)</c>，posID = 槽位 L/ML/M/MR/R 或 addChar 注册的自定义 posID。
    ///
    /// 语义：停止接收正文、当前打字立即补全（释放正文阻塞）、面板销毁、段落清空——
    /// 下次重新登台（槽位再次填写同一角色）从空白面板开始，不保留旧文本。
    /// 与"隐藏保留"不同：本命令即"关闭并清空"。
    /// </summary>
    [VNCommandMeta(VNCommandCategory.Performance,
        "隐藏文字角色并清空段落（销毁；下次登台为空面板）")]
    public class HideTextActorCommand : VNCommand
    {
        [VNParam(0, "posID", VNParamType.SlotCode,
            Description = "文字角色所在槽位（L/ML/M/MR/R 或自定义 posID）")]
        public override string CommandName { get { return "hidetextactor"; } }

        public override bool Execute(string args)
        {
            return HideFromArgs(args);
        }

        /// <summary>快进/读档预演：移除是数据态操作（与 removeChar 对齐），与 Execute 一致。</summary>
        public override void Simulate(string args)
        {
            HideFromArgs(args);
        }

        private static bool HideFromArgs(string args)
        {
            string posID = args?.Trim();
            if (string.IsNullOrEmpty(posID))
            {
                Debug.LogError("[HideTextActor] 参数不能为空，格式: hideTextActor(posID)");
                return false;
            }

            string posCode = TheaterManager.NormalizePosCode(posID);
            if (posCode == null)
            {
                Debug.LogWarning($"[HideTextActor] 未知槽位/PosID \"{posID}\"");
                return false;
            }

            var actor = TheaterManager.GetInstance().GetTextActor(posCode);
            if (actor == null)
            {
                // 容错：槽位上是普通立绘/无演员时绝不误删（本命令只作用于文字角色）
                Debug.LogWarning($"[HideTextActor] 槽位 {posCode} 没有显示中的文字角色");
                return false;
            }

            VNManager.GetInstance().NotifyTextActorRemoved(posCode);
            // RemoveActor → Interrupt（补全当前打字并释放 isTextDisplaying）→ Dispose（销毁面板与段落）
            TheaterManager.GetInstance().RemoveActor(posCode);
            return true;
        }
    }
}
