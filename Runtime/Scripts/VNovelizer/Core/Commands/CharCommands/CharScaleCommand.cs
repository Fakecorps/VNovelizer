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
    /// 角色缩放命令（剧场层实现）
    /// 格式：charscale(位置, 缩放比例, [时长], [InEase], [OutEase])
    /// 缩放为绝对目标值（负数取绝对值，与 setchartrans 一致）；
    /// 翻转符号（scaleX）独立保持，本命令只驱动大小。
    /// 动画结束保持最终缩放，下一行立绘重新登台时自动恢复 profile 默认缩放（行内瞬态）。
    /// 实现：命令层协程逐帧驱动 theater.SetScale（状态与视觉同步，打断瞬到终值）。
    /// </summary>
    [VNCommandMeta(VNCommandCategory.Performance,
        "缩放到指定比例（绝对目标值；不继承：下一行自动恢复默认；要求同行对应立绘列已填）")]
    public class CharScaleCommand : VNCommand
    {
        [VNParam(0, "pos", VNParamType.SlotCode,
            Description = "槽位（L/ML/M/MR/R 或全名），要求同行对应立绘列非空")]
        [VNParam(1, "scale", VNParamType.Float, Min = 0.1f, Max = 5f, Default = "1",
            Description = "目标缩放（绝对值；负数取绝对值，翻转符号独立保持）")]
        [VNParam(2, "duration", VNParamType.Float, Min = 0f, Max = 10f, Default = "0.5",
            Optional = true, Description = "缩放秒数")]
        [VNParam(3, "inEase", VNParamType.Enum, Options = EaseArgParser.InEaseOptions,
            Optional = true, Description = "缩放曲线（可选，默认 Linear；只填一个=进出同曲线）")]
        [VNParam(4, "outEase", VNParamType.Enum, Options = EaseArgParser.OutEaseOptions,
            Optional = true, Description = "退出曲线（本命令无离场阶段，仅保留对称写法，忽略）")]
        public override string CommandName { get { return "charscale"; } }

        private float defaultDuration = 0.5f;

        // --- 多实例并行支持（登记 posCode + 终值，中断时瞬到终值） ---
        private struct ActiveScale
        {
            public string PosCode;
            public float TargetScale;
        }

        private readonly List<ActiveScale> _activeScales = new List<ActiveScale>();

        /// <summary>中断标志：Interrupt 置位后缩放循环立即退出（循环体每帧检查）</summary>
        private bool _interrupted;

        public override bool Execute(string args)
        {
            // 快进/Simulate 语境：缩放是纯表现动画，同步路径不启动协程。
            // 终态由下一行 OnShowCharacter 重置 profile 默认缩放，无需在此写状态。
            return true;
        }

        public override IEnumerator ExecuteAsync(string args)
        {
            if (string.IsNullOrEmpty(args)) yield break;

            _interrupted = false;

            string[] parts = args.Split(',');
            if (parts.Length < 2)
            {
                Debug.LogError($"[CharScale] 参数不足，需要至少2个参数：位置, 缩放比例, [时长]");
                yield break;
            }

            string posCode = TheaterManager.NormalizePosCode(parts[0]);
            if (posCode == null)
            {
                Debug.LogError($"[CharScale] 未知位置: {parts[0].Trim()}（可用 L/ML/M/MR/R 或全名）");
                yield break;
            }
            if (!float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float scale))
            {
                Debug.LogError($"[CharScale] 无法解析缩放比例: {parts[1]}");
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
                Debug.LogError($"[CharScale] 找不到位置 {posCode} 的角色");
                yield break;
            }

            // 绝对目标值：负数取绝对值（与 setchartrans 一致）；翻转符号不动
            float startScale = state.scale;
            float targetScale = Mathf.Abs(scale);
            if (targetScale < 0.0001f) targetScale = 0.0001f;

            _activeScales.Add(new ActiveScale { PosCode = posCode, TargetScale = targetScale });
            try
            {
                if (duration <= 0f)
                {
                    theater.SetScale(posCode, targetScale);
                    yield break;
                }

                float elapsed = 0f;
                while (elapsed < duration)
                {
                    if (_interrupted) yield break; // 中断后立即退出，终值由 Interrupt 写入
                    if (theater.GetActor(posCode) == null) yield break; // 演员被移除
                    elapsed += Time.deltaTime;
                    float t = EaseEvaluator.Evaluate(ease, Mathf.Clamp01(elapsed / duration));
                    theater.SetScale(posCode, Mathf.Lerp(startScale, targetScale, t));
                    yield return null;
                }

                theater.SetScale(posCode, targetScale);
            }
            finally
            {
                UnregisterScale(posCode, targetScale);
            }

            Debug.Log($"[CharScale] 角色 {posCode} 缩放完成: {startScale} -> {targetScale}");
        }

        private void UnregisterScale(string posCode, float targetScale)
        {
            for (int i = _activeScales.Count - 1; i >= 0; i--)
            {
                if (_activeScales[i].PosCode == posCode && Mathf.Approximately(_activeScales[i].TargetScale, targetScale))
                    _activeScales.RemoveAt(i);
            }
        }

        /// <summary>中断全部缩放：瞬间到终态（点击跳过语义，与 charmove 一致）</summary>
        public override void Interrupt()
        {
            _interrupted = true;

            if (_activeScales.Count == 0) return;

            var theater = TheaterManager.GetInstance();
            var snapshot = new List<ActiveScale>(_activeScales);
            foreach (var sc in snapshot)
            {
                theater.SetScale(sc.PosCode, sc.TargetScale);
            }

            Debug.Log($"[CharScale] {snapshot.Count} 个缩放动画被玩家中断，已瞬间完成。");
            _activeScales.Clear();
        }

        public override void Simulate(string args)
        {
            // 预演语义与 charmove 一致：缩放是行内瞬态演出，下一行登台即恢复默认，
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
                Debug.LogWarning($"[CharScale.Simulate] 位置 {posCode} 没有角色，跳过缩放");
                return;
            }

            Debug.Log($"[CharScale.Simulate] 位置 {posCode} 将在运行时缩放到 {parts[1].Trim()}");
        }
    }
}
