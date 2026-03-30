using UnityEngine;

namespace VNovelizer.Core.Commands
{
    public class FadeBlackInCommand : VNCommand
    {
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

            if (!float.TryParse(parts[0].Trim(), out float duration))
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
    }
}