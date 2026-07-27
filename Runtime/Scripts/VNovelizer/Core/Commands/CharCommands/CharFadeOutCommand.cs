using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VNovelizer.Core.API;
using VNovelizer.Core.Compat;

namespace VNovelizer.Core.Commands
{
    /// <summary>
    /// 角色淡出命令 (PrimeTween 高性能版)
    /// </summary>
    public class CharFadeOutCommand : VNCommand
    {
        public override string CommandName { get { return "charfadeout"; } }

        private float defaultDuration = 0.5f;

        private struct ActiveFade
        {
            public int Token;
            public CanvasGroup CanvasGroup;
            public CompatTween Tween;
        }

        private readonly List<ActiveFade> _activeFades = new List<ActiveFade>();
        private int _nextFadeToken;

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

            RectTransform targetRect = VNAPI.GetCharRect(posCode);
            if (targetRect == null || !targetRect.gameObject.activeSelf)
                yield break;

            CanvasGroup targetCG = targetRect.GetComponent<CanvasGroup>();
            if (targetCG == null) targetCG = targetRect.gameObject.AddComponent<CanvasGroup>();

            CompatTween fadeTween = AnimationCompat.Alpha(targetCG, startValue: targetCG.alpha, endValue: 0f, duration: duration)
                .OnComplete(() =>
                {
                    if (targetCG != null && targetCG.gameObject != null)
                    {
                        targetCG.gameObject.SetActive(false);
                        targetCG.alpha = 1f;
                    }
                });

            int token = ++_nextFadeToken;
            _activeFades.Add(new ActiveFade { Token = token, CanvasGroup = targetCG, Tween = fadeTween });

            try
            {
                yield return fadeTween.ToYieldInstruction();

                if (targetCG != null && targetCG.gameObject != null)
                {
                }
                else
                {
                    Debug.LogWarning("[CharFadeOut] CanvasGroup 在动画过程中被销毁");
                }
            }
            finally
            {
                UnregisterFade(token);
            }
        }

        private void UnregisterFade(int token)
        {
            for (int i = _activeFades.Count - 1; i >= 0; i--)
            {
                if (_activeFades[i].Token == token)
                    _activeFades.RemoveAt(i);
            }
        }

        public override void Interrupt()
        {
            var snapshot = new List<ActiveFade>(_activeFades);
            bool anyCompleted = false;
            foreach (var af in snapshot)
            {
                if (af.Tween.isAlive)
                {
                    af.Tween.Complete();
                    anyCompleted = true;
                }
            }
            if (anyCompleted)
                Debug.Log("[CharFadeOut] 动画被中断，已瞬间隐藏。");

            _activeFades.Clear();
        }
    }
}
