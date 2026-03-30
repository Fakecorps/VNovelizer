using System.Collections;
using UnityEngine;
using VNovelizer.Core.Utils;

namespace VNovelizer.Core.Commands
{
    public class FadeBlackOutCommand : VNCommand
    {
        public override string CommandName => "fadeBlackOut";

        public override bool Execute(string args)
        {
            // 同步接口保留，但真正逻辑放在 ExecuteAsync 里
            return !string.IsNullOrEmpty(args);
        }

        public override IEnumerator ExecuteAsync(string args)
        {
            if (string.IsNullOrEmpty(args))
            {
                Debug.LogError("[FadeBlackOutCommand] 参数不应为空");
                yield break;
            }

            string[] parts = args.Split(',');
            if (parts.Length != 1)
            {
                Debug.LogError("[FadeBlackOutCommand] 参数格式错误，正确格式：fadeBlackOut(0.5)");
                yield break;
            }

            if (!float.TryParse(parts[0].Trim(), out float duration))
            {
                Debug.LogError($"[FadeBlackOutCommand] 无法解析时长参数: {parts[0]}");
                yield break;
            }

            if (TransitionManager.Instance == null)
            {
                Debug.LogError("[FadeBlackOutCommand] 未找到 TransitionManager");
                yield break;
            }

            bool finished = false;

            bool started = TransitionManager.Instance.PlayDarkFadeOutOnlyAsync(
                onComplete: () =>
                {
                    finished = true;
                },
                duration: duration
            );

            if (!started)
            {
                Debug.LogWarning("[FadeBlackOutCommand] 黑幕淡出启动失败");
                yield break;
            }

            // 真正等待转场完成
            yield return new WaitUntil(() => finished);

            // 不直接 NextLine，而是登记“本行命令全部执行完后自动前进”
            VNCommandBridge.AdvanceAfterCommands();
        }
    }
}