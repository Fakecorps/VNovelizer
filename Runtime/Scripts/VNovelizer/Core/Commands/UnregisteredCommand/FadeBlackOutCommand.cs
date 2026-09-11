using System.Collections;
using System.Globalization;
using UnityEngine;
using VNovelizer.Core.Utils;
using VNovelizer.Core.Commands.Meta;

namespace VNovelizer.Core.Commands
{
    [VNCommandMeta(VNCommandCategory.Performance,
        "黑幕淡出（阻塞至淡出完成；配合 fadeBlackIn 使用）")]
    public class FadeBlackOutCommand : VNCommand
    {
        [VNParam(0, "duration", VNParamType.Float, Min = 0.05f, Max = 10f, Default = "0.5",
            Description = "淡出秒数")]
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

            // 【Fix-62】InvariantCulture：小数点为逗号的系统上 "0.5" 必须按 '.' 解析
            if (!float.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float duration))
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
            // 【Fix-23】超时兜底：转场协程被外部停止（StopRunningTransition）或 tween 被 StopAll 时
            // onComplete 可能永不触发，WaitUntil 会永久挂起卡住命令链。
            float waited = 0f;
            while (!finished && waited < duration + 10f)
            {
                waited += Time.deltaTime;
                yield return null;
            }

            // 不直接 NextLine，而是登记"本行命令全部执行完后自动前进"
            VNCommandBridge.AdvanceAfterCommands();
        }

        /// <summary>
        /// 【Fix-23】中断兜底：玩家跳过时黑幕可能停在半程（全屏黑 + raycast 拦截残留），
        /// 强制复位转场状态并立即解除黑幕，保证 UI 永远可点击。
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