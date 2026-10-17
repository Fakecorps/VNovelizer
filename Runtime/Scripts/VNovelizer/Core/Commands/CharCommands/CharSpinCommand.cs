using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using VNovelizer.Core.Compat;
using VNovelizer.Core.Commands.Meta;
using VNovelizer.Core.Theater;

namespace VNovelizer.Core.Commands
{
    /// <summary>
    /// 角色旋转命令（剧场层实现）
    /// 格式：charspin(位置, 角度, [时长], [InEase], [OutEase])
    /// 角度为增量旋转（在当前位置上累加，支持 720 等多圈）；
    /// 正角度 = 屏幕视角顺时针（绕 Z 轴负方向，targetZ = startZ - delta）。
    /// 动画结束保持最终角度，下一行立绘重新登台时自动恢复 0°（行内瞬态，与 charmove 一致）。
    /// 实现：命令层协程逐帧驱动 theater.SetRotation（状态与视觉同步——连续 charspin
    /// 增量累加、打断瞬到终值都以 state.rotationZ 为唯一依据）。
    /// </summary>
    [VNCommandMeta(VNCommandCategory.Performance,
        "旋转角色立绘（增量角度；不继承：下一行自动回正；要求同行对应立绘列已填）")]
    public class CharSpinCommand : VNCommand
    {
        [VNParam(0, "pos", VNParamType.SlotCode,
            Description = "槽位（L/ML/M/MR/R 或全名），要求同行对应立绘列非空")]
        [VNParam(1, "angle", VNParamType.Float, Min = -1080f, Max = 1080f, Default = "90",
            Description = "旋转角度（度，增量；正=屏幕顺时针，支持多圈如 720）")]
        [VNParam(2, "duration", VNParamType.Float, Min = 0f, Max = 10f, Default = "0.5",
            Optional = true, Description = "旋转秒数")]
        [VNParam(3, "inEase", VNParamType.Enum, Options = EaseArgParser.InEaseOptions,
            Optional = true, Description = "旋转曲线（可选，默认 Linear；只填一个=进出同曲线）")]
        [VNParam(4, "outEase", VNParamType.Enum, Options = EaseArgParser.OutEaseOptions,
            Optional = true, Description = "退出曲线（本命令无离场阶段，仅保留对称写法，忽略）")]
        public override string CommandName { get { return "charspin"; } }

        private float defaultDuration = 0.5f;

        // --- 多实例并行支持（登记 posCode + 终值，中断时瞬到终值） ---
        private struct ActiveSpin
        {
            public string PosCode;
            public float TargetZ;
        }

        private readonly List<ActiveSpin> _activeSpins = new List<ActiveSpin>();

        /// <summary>中断标志：Interrupt 置位后旋转循环立即退出（循环体每帧检查）</summary>
        private bool _interrupted;

        public override bool Execute(string args)
        {
            // 快进/Simulate 语境：旋转是纯表现动画，同步路径不启动协程。
            // 终态由下一行 OnShowCharacter 重置 0°，无需在此写状态。
            return true;
        }

        public override IEnumerator ExecuteAsync(string args)
        {
            if (string.IsNullOrEmpty(args)) yield break;

            _interrupted = false;

            string[] parts = args.Split(',');
            if (parts.Length < 2)
            {
                Debug.LogError($"[CharSpin] 参数不足，需要至少2个参数：位置, 角度, [时长]");
                yield break;
            }

            string posCode = TheaterManager.NormalizePosCode(parts[0]);
            if (posCode == null)
            {
                Debug.LogError($"[CharSpin] 未知位置: {parts[0].Trim()}（可用 L/ML/M/MR/R 或全名）");
                yield break;
            }
            if (!float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float deltaDegrees))
            {
                Debug.LogError($"[CharSpin] 无法解析角度: {parts[1]}");
                yield break;
            }
            float duration = defaultDuration;
            if (parts.Length >= 3)
                float.TryParse(parts[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out duration);

            // R13：解析 [InEase, OutEase]（只填一个 = 进出同曲线）；In 槽生效
            var easeArgs = EaseArgParser.ParseAt(parts, 3);
            Ease ease = easeArgs.In ?? easeArgs.Out ?? Ease.Linear;

            var theater = TheaterManager.GetInstance();
            var state = theater.GetState(posCode);
            if (state == null)
            {
                Debug.LogError($"[CharSpin] 找不到位置 {posCode} 的角色");
                yield break;
            }

            // 增量语义：正角度 = 屏幕顺时针 = 绕 Z 负方向
            float startZ = state.rotationZ;
            float targetZ = startZ - deltaDegrees;

            _activeSpins.Add(new ActiveSpin { PosCode = posCode, TargetZ = targetZ });
            try
            {
                if (duration <= 0f)
                {
                    theater.SetRotation(posCode, targetZ);
                    yield break;
                }

                float elapsed = 0f;
                while (elapsed < duration)
                {
                    if (_interrupted) yield break; // 中断后立即退出，终值由 Interrupt 写入
                    if (theater.GetActor(posCode) == null) yield break; // 演员被移除
                    elapsed += Time.deltaTime;
                    float t = EaseEvaluator.Evaluate(ease, Mathf.Clamp01(elapsed / duration));
                    theater.SetRotation(posCode, Mathf.Lerp(startZ, targetZ, t));
                    yield return null;
                }

                theater.SetRotation(posCode, targetZ);
            }
            finally
            {
                UnregisterSpin(posCode, targetZ);
            }

            Debug.Log($"[CharSpin] 角色 {posCode} 旋转完成: {startZ}° -> {targetZ}°（增量 {deltaDegrees}°）");
        }

        private void UnregisterSpin(string posCode, float targetZ)
        {
            for (int i = _activeSpins.Count - 1; i >= 0; i--)
            {
                if (_activeSpins[i].PosCode == posCode && Mathf.Approximately(_activeSpins[i].TargetZ, targetZ))
                    _activeSpins.RemoveAt(i);
            }
        }

        /// <summary>中断全部旋转：瞬间到终态（点击跳过语义，与 charmove 一致）</summary>
        public override void Interrupt()
        {
            _interrupted = true;

            if (_activeSpins.Count == 0) return;

            var theater = TheaterManager.GetInstance();
            var snapshot = new List<ActiveSpin>(_activeSpins);
            foreach (var sp in snapshot)
            {
                theater.SetRotation(sp.PosCode, sp.TargetZ);
            }

            Debug.Log($"[CharSpin] {snapshot.Count} 个旋转动画被玩家中断，已瞬间完成。");
            _activeSpins.Clear();
        }

        public override void Simulate(string args)
        {
            // 预演语义与 charmove 一致：旋转是行内瞬态演出，下一行登台即回正，
            // 快进后由 PlayCurrentLine 的 ExecuteAsync 重新播放，Simulate 只记录不落状态。
            if (string.IsNullOrEmpty(args)) return;

            string[] parts = args.Split(',');
            if (parts.Length < 2) return;

            string posCode = TheaterManager.NormalizePosCode(parts[0]);
            if (posCode == null) return;

            // R14：自由角色（addChar）豁免五槽立绘列检查——其数据源是剧场注册表
            bool isFreeChar = TheaterManager.GetInstance().IsFreeChar(posCode);
            string charData = VNManager.GetInstance().GetCharacterData(posCode);
            if (!isFreeChar && (string.IsNullOrEmpty(charData) || charData == "hide"))
            {
                Debug.LogWarning($"[CharSpin.Simulate] 位置 {posCode} 没有角色，跳过旋转");
                return;
            }

            Debug.Log($"[CharSpin.Simulate] 位置 {posCode} 将在运行时旋转 {parts[1].Trim()}°");
        }
    }
}
