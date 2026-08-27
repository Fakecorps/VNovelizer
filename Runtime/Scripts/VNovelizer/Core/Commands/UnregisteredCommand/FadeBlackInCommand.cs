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