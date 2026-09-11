using System.Collections;
using System.Globalization;
using UnityEngine;
using VNovelizer.Core.Commands;
using VNovelizer.Core.Commands.Meta;
using VNovelizer.Core.Theater;

namespace VNovelizer.L2D
{
    /// <summary>
    /// Live2D 动作命令。
    /// 语法：l2dmotion(pos, motionID[, mode[, fade]])
    /// - mode：wait = 阻塞命令链直到动作结束；async = 立即返回（与省略同义）；
    /// - 动作播完官方多层混合自动回 Idle 待机（设计文档 §5.3）。
    /// 四参数严格位置语义：第 3 位只认 wait/async，第 4 位只认数字。
    /// </summary>
    [VNCommandMeta(VNCommandCategory.Performance,
        "Live2D 动作播放（播完自动回 Idle；mode=wait 阻塞至动作结束）")]
    public class L2DMotionCommand : VNCommand
    {
        [VNParam(0, "pos", VNParamType.SlotCode,
            Description = "槽位 L/ML/M/MR/R（或全名），也可用自由角色 posID")]
        [VNParam(1, "motionID", VNParamType.String,
            Description = "动作 ID（L2D 角色配置表内定义）；特殊值 idle = 停止动作回待机")]
        [VNParam(2, "mode", VNParamType.Enum, Options = "wait|async", Optional = true,
            Description = "wait = 阻塞命令链至动作结束；async = 立即返回（默认）")]
        [VNParam(3, "fade", VNParamType.Float, Min = 0f, Max = 10f, Optional = true,
            Description = "覆写淡入淡出秒数（P4 生效，当前用模型数据默认）")]
        public override string CommandName => "l2dmotion";

        // wait 阻塞等待目标（Interrupt 时停动作回 Idle）。
        // 命令为注册表单例、链执行串行，实例字段安全（与 CharFadeInCommand 同模式）。
        private IL2DControllable _waitingTarget;

        public override bool Execute(string args) => true;

        public override IEnumerator ExecuteAsync(string args)
        {
            _waitingTarget = null;

            if (!TryParseArgs(args, out string posCode, out string motionID, out bool wait, out string error))
            {
                Debug.LogWarning($"[L2DMotion] {error}");
                yield break;
            }

            var actor = TheaterManager.GetInstance().GetActor(posCode);
            if (actor == null)
            {
                Debug.LogWarning($"[L2DMotion] 槽位 {posCode} 没有角色");
                yield break;
            }
            if (!(actor is IL2DControllable l2d) || !l2d.IsReady)
            {
                Debug.LogWarning($"[L2DMotion] 槽位 {posCode} 不是 Live2D 角色（或模型未就绪）");
                yield break;
            }

            bool ok;
            string playError = null;
            if (string.Equals(motionID, "idle", System.StringComparison.OrdinalIgnoreCase))
                ok = l2d.TryStopMotion();
            else
                ok = l2d.TryPlayMotion(motionID, out playError);

            if (!ok)
            {
                Debug.LogWarning($"[L2DMotion] {playError ?? $"停止动作失败: {motionID}"}");
                yield break;
            }

            if (!wait) yield break;

            _waitingTarget = l2d;
            yield return l2d.WaitMotionFinished();
            _waitingTarget = null;
        }

        /// <summary>wait 阻塞被跳过（点击快进）时：停动作回 Idle（设计 §6.4）</summary>
        public override void Interrupt()
        {
            if (_waitingTarget != null)
            {
                _waitingTarget.TryStopMotion();
                _waitingTarget = null;
            }
        }

        /// <summary>严格四参数解析（无宽容重排；数值统一 InvariantCulture）</summary>
        private static bool TryParseArgs(string args,
            out string posCode, out string motionID, out bool wait, out string error)
        {
            posCode = null;
            motionID = null;
            wait = false;
            error = null;

            if (string.IsNullOrWhiteSpace(args))
            {
                error = "参数为空";
                return false;
            }

            string[] parts = args.Split(',');
            if (parts.Length > 4)
            {
                error = $"参数过多（最多 4 个）: {args}";
                return false;
            }

            posCode = TheaterManager.NormalizePosCode(parts[0]);
            if (posCode == null)
            {
                error = $"未知槽位: {parts[0].Trim()}";
                return false;
            }

            if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[1]))
            {
                error = "缺少动作 ID";
                return false;
            }
            motionID = parts[1].Trim();

            if (parts.Length > 2 && !string.IsNullOrWhiteSpace(parts[2]))
            {
                string mode = parts[2].Trim();
                if (string.Equals(mode, "wait", System.StringComparison.OrdinalIgnoreCase))
                    wait = true;
                else if (!string.Equals(mode, "async", System.StringComparison.OrdinalIgnoreCase))
                {
                    error = $"第 3 参数无法识别: {mode}（只能是 wait 或 async）";
                    return false;
                }
            }

            if (parts.Length > 3 && !string.IsNullOrWhiteSpace(parts[3]))
            {
                if (!float.TryParse(parts[3].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                {
                    error = $"第 4 参数不是数字: {parts[3].Trim()}";
                    return false;
                }
            }
            return true;
        }
    }
}
