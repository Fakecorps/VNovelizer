using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VNovelizer.Core.Theater;

namespace VNovelizer.Core.Commands
{
    /// <summary>
    /// 角色淡出命令（剧场层实现）
    /// 格式：charfadeout(位置, [时长])
    /// 淡出到 0 后隐藏并将透明度复位为 1（与旧行为一致：隐藏时归位 alpha）。
    /// </summary>
    public class CharFadeOutCommand : VNCommand
    {
        public override string CommandName { get { return "charfadeout"; } }

        private float defaultDuration = 0.5f;

        // --- 活动淡出登记（支持多实例并行 + 点击跳过时正确归位） ---
        private readonly List<string> _activeFades = new List<string>();

        public override bool Execute(string args)
        {
            return true;
        }

        public override IEnumerator ExecuteAsync(string args)
        {
            if (string.IsNullOrEmpty(args)) yield break;

            string[] parts = args.Split(',');
            string posCode = parts[0].Trim();
            float duration = defaultDuration;
            if (parts.Length > 1) float.TryParse(parts[1].Trim(), out duration);

            var theater = TheaterManager.GetInstance();
            var actor = theater.GetActor(posCode);
            if (actor == null) yield break;

            var state = theater.GetState(posCode);
            if (state != null && !state.visible) yield break; // 已隐藏，无需淡出

            _activeFades.Add(posCode);
            try
            {
                yield return actor.FadeAsync(0f, duration);
            }
            finally
            {
                _activeFades.Remove(posCode);
            }

            // 淡出完成：隐藏并归位透明度（后续 charfadein 从 1 → 淡入逻辑不受影响）
            theater.SetVisible(posCode, false);
            theater.SetAlpha(posCode, 1f);
        }

        /// <summary>中断：瞬间到终态（隐藏）</summary>
        public override void Interrupt()
        {
            var theater = TheaterManager.GetInstance();
            foreach (string posCode in _activeFades)
            {
                var actor = theater.GetActor(posCode);
                actor?.Interrupt();
                theater.SetVisible(posCode, false);
                theater.SetAlpha(posCode, 1f);
            }
            _activeFades.Clear();
        }
    }
}
