using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VNovelizer.Core.Theater;

namespace VNovelizer.Core.Commands
{
    /// <summary>
    /// 角色淡入命令（剧场层实现）
    /// 格式：charfadein(位置, [时长])
    /// 经 TheaterManager 驱动 IActor，不再接触 UGUI 类型。
    /// </summary>
    public class CharFadeInCommand : VNCommand
    {
        public override string CommandName { get { return "charfadein"; } }

        private float defaultDuration = 0.5f;

        // --- 活动淡入登记（支持多实例并行 + 点击跳过时正确归位） ---
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
            if (actor == null)
            {
                Debug.LogError($"[CharFadeIn] 找不到位置 {posCode} 的角色");
                yield break;
            }

            theater.SetVisible(posCode, true);
            theater.SetAlpha(posCode, 0f);

            _activeFades.Add(posCode);
            try
            {
                yield return actor.FadeAsync(1f, duration);
            }
            finally
            {
                _activeFades.Remove(posCode);
            }
        }

        /// <summary>中断：瞬间到终态（完全显示）</summary>
        public override void Interrupt()
        {
            var theater = TheaterManager.GetInstance();
            foreach (string posCode in _activeFades)
            {
                var actor = theater.GetActor(posCode);
                actor?.Interrupt();
                theater.SetAlpha(posCode, 1f);
                theater.SetVisible(posCode, true);
            }
            _activeFades.Clear();
        }
    }
}
