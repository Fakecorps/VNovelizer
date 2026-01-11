using System.Collections;
using UnityEngine;
using VNovelizer.Core.API;
using PrimeTween;

namespace VNovelizer.Core.Commands
{
    /// <summary>
    /// 角色淡出命令 (PrimeTween 高性能版)
    /// </summary>
    public class CharFadeOutCommand : VNCommand
    {
        public override string CommandName { get { return "charfadeout"; } }

        private float defaultDuration = 0.5f;

        // --- 运行时状态 ---
        private CanvasGroup _targetCG;
        private Tween _fadeTween;

        public override bool Execute(string args)
        {
            return true;
        }

        public override IEnumerator ExecuteAsync(string args)
        {
            if (string.IsNullOrEmpty(args)) yield break;

            // 1. 解析参数
            string[] parts = args.Split(',');
            string posCode = parts[0].Trim();
            float duration = defaultDuration;
            if (parts.Length > 1) float.TryParse(parts[1].Trim(), out duration);

            // 2. 获取目标
            RectTransform targetRect = VNAPI.GetCharRect(posCode);
            if (targetRect == null || !targetRect.gameObject.activeSelf)
            {
                // 如果本来就是隐藏的，直接结束
                yield break;
            }

            // 3. 获取组件
            _targetCG = targetRect.GetComponent<CanvasGroup>();
            if (_targetCG == null) _targetCG = targetRect.gameObject.AddComponent<CanvasGroup>();

            // 4. 【核心】使用 PrimeTween

            _fadeTween = Tween.Alpha(_targetCG, startValue: _targetCG.alpha, endValue: 0f, duration: duration)
                .OnComplete(() =>
                {
                    Finish();
                });

            // 5. 等待完成
            yield return _fadeTween.ToYieldInstruction();

            // 【Bug修复】检查对象是否仍然有效
            if (_targetCG != null && _targetCG.gameObject != null)
            {
                // 对象仍然有效，正常清理
            }
            else
            {
                Debug.LogWarning("[CharFadeOut] CanvasGroup 在动画过程中被销毁");
            }

            // 6. 清理
            _fadeTween = default;
            _targetCG = null;
        }

        public void Finish()
        {
            // 【Bug修复】检查对象是否仍然有效
            if (_targetCG != null)
            {
                try
                {
                    if (_targetCG.gameObject != null)
                    {
                        _targetCG.gameObject.SetActive(false);
                        _targetCG.alpha = 1f; // 恢复 Alpha
                    }
                }
                catch (MissingReferenceException)
                {
                    Debug.LogWarning("[CharFadeOut] 尝试完成动画时发现 CanvasGroup 已被销毁");
                }
            }
        }

        // 中断逻辑
        public override void Interrupt()
        {
            if (_fadeTween.isAlive)
            {
                _fadeTween.Complete(); // 这会触发 OnComplete 里的隐藏逻辑
                Debug.Log("[CharFadeOut] 动画被中断，已瞬间隐藏。");
            }

            // 【Bug修复】检查对象是否仍然有效
            if (_targetCG != null)
            {
                try
                {
                    if (_targetCG.gameObject != null)
                    {
                        // 对象仍然有效
                    }
                }
                catch (MissingReferenceException)
                {
                    Debug.LogWarning("[CharFadeOut] 尝试中断时发现 CanvasGroup 已被销毁");
                }
            }
            
            _targetCG = null;
        }
    }
}