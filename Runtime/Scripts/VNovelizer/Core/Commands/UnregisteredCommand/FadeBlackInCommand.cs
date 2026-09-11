using System.Globalization;
using UnityEngine;
using VNovelizer.Core.Commands.Meta;

namespace VNovelizer.Core.Commands
{
    [VNCommandMeta(VNCommandCategory.Performance,
        "黑幕淡入（阻塞至淡入完成；配合 fadeBlackOut 使用）")]
    public class FadeBlackInCommand : VNCommand
    {
        [VNParam(0, "duration", VNParamType.Float, Min = 0.05f, Max = 10f, Default = "0.5",
            Description = "淡入秒数")]
        public override string CommandName => "fadeBlackIn";

        public override bool Execute(string args)
        {
            if (string.IsNullOrEmpty(args))
            {
                Debug.LogError("[FadeBlackInCommand] 参数不应为空");
                return false;
            }

            string[] parts = args.Split(',');
            if (parts.Length != 1)
            {
                Debug.LogError("[FadeBlackInCommand] 参数格式错误，正确格式：fadeBlackIn(0.5)");
                return false;
            }

            // 【Fix-62】InvariantCulture：小数点为逗号的系统上 "0.5" 必须按 '.' 解析
            if (!float.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float duration))
            {
                Debug.LogError($"[FadeBlackInCommand] 无法解析时长参数: {parts[0]}");
                return false;
            }

            if (TransitionManager.Instance == null)
            {
                Debug.LogError("[FadeBlackInCommand] 未找到 TransitionManager");
                return false;
            }

            TransitionManager.Instance.PlayDarkFadeInOnly(
                onComplete: () =>
                {
                    Debug.Log("[FadeBlackInCommand] 黑幕淡入结束");
                },
                duration: duration
            );

            return true;
        }

        /// <summary>
        /// 【Fix-23】快进预演兜底：Simulate 默认为空实现，快进时会静默跳过本命令——
        /// 但前置的 fadeBlackOut 已把屏幕拉黑（黑幕 Image + raycast 拦截），
        /// 跳过淡入会导致黑幕永久残留、UI 完全卡死。此处立即解除黑幕与拦截。
        /// </summary>
        public override void Simulate(string args)
        {
            if (TransitionManager.Instance != null)
                TransitionManager.Instance.PlayDarkFadeInOnlyAsync(duration: 0f);
        }

        /// <summary>
        /// 【Fix-23】中断兜底：与 Simulate 同理，保证中断路径也必然解除黑幕。
        /// </summary>
        public override void Interrupt()
        {
            if (TransitionManager.Instance != null)
            {
                TransitionManager.Instance.ForceReset();
                TransitionManager.Instance.PlayDarkFadeInOnlyAsync(duration: 0f);
            }
        }
    }
}