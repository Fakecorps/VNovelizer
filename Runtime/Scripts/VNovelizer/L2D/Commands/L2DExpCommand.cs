using System;
using UnityEngine;
using VNovelizer.Core.Commands;
using VNovelizer.Core.Commands.Meta;
using VNovelizer.Core.Theater;

namespace VNovelizer.L2D
{
    /// <summary>
    /// Live2D 表情命令。
    /// 语法：l2dexp(pos, expID[, fade])
    /// - expID：表情 ID（配置表内定义）；特殊值 none = 清除表情；
    /// - 保持型语义（官方 CurrentExpressionIndex）：表情持续挂在模型上，
    ///   下一行立绘列情绪会自动覆盖（行边界规则，设计 §6.4）。
    /// 同步命令（无异步阶段）；Simulate 空操作（表情持久状态由立绘列承载）。
    /// </summary>
    [VNCommandMeta(VNCommandCategory.Performance,
        "Live2D 表情切换（保持型；下一行立绘列情绪会覆盖；expID 填 none 清除）")]
    public class L2DExpCommand : VNCommand
    {
        [VNParam(0, "pos", VNParamType.SlotCode,
            Description = "槽位 L/ML/M/MR/R（或全名），也可用自由角色 posID")]
        [VNParam(1, "expID", VNParamType.String,
            Description = "表情 ID（L2D 角色配置表内定义）；特殊值 none = 清除表情")]
        [VNParam(2, "fade", VNParamType.Float, Min = 0f, Max = 10f, Optional = true,
            Description = "覆写淡入秒数（P4 生效，当前用 exp3 自带）")]
        public override string CommandName => "l2dexp";

        public override bool Execute(string args)
        {
            if (string.IsNullOrWhiteSpace(args)) return true;

            string[] parts = args.Split(',');
            if (parts.Length > 3)
            {
                Debug.LogWarning($"[L2DExp] 参数过多（最多 3 个）: {args}");
                return true;
            }

            string posCode = TheaterManager.NormalizePosCode(parts[0]);
            if (posCode == null)
            {
                Debug.LogWarning($"[L2DExp] 未知槽位: {parts[0].Trim()}");
                return true;
            }

            if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[1]))
            {
                Debug.LogWarning("[L2DExp] 缺少表情 ID");
                return true;
            }
            string expID = parts[1].Trim();

            var actor = TheaterManager.GetInstance().GetActor(posCode);
            if (actor == null)
            {
                Debug.LogWarning($"[L2DExp] 槽位 {posCode} 没有角色");
                return true;
            }
            if (!(actor is IL2DControllable l2d) || !l2d.IsReady)
            {
                Debug.LogWarning($"[L2DExp] 槽位 {posCode} 不是 Live2D 角色（或模型未就绪）");
                return true;
            }

            if (string.Equals(expID, "none", StringComparison.OrdinalIgnoreCase))
            {
                l2d.TryClearExpression();
            }
            else
            {
                if (!l2d.TrySetExpression(expID, null, out string error))
                    Debug.LogWarning($"[L2DExp] {error}");
            }
            return true;
        }
    }
}
