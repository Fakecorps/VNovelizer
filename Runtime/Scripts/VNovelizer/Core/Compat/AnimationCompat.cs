using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System;

#if PRIME_TWEEN_INSTALLED
using PrimeTween;
#endif

/// <summary>
/// 动画兼容层 — 当 PrimeTween 未安装时优雅降级（无动画，直接设置最终状态）。
/// 安装 PrimeTween 后 PRIME_TWEEN_INSTALLED 宏自动激活，内部委托至 PrimeTween 引擎。
/// API 对齐 PrimeTween 1.4.11：
///   - Ease → Easing 用隐式转换 (Easing)(PrimeTween.Ease)(int)e
///   - AnchoredPosition 系列 → UIAnchoredPosition*
///   - StopAllByTarget → Tween.StopAll(target)
///   - Custom 只支持 float / Vector2 / Vector3 / Color，无 int 重载
/// </summary>
namespace VNovelizer.Core.Compat
{
    // ===== CompatTween =====
    public struct CompatTween
    {
#if PRIME_TWEEN_INSTALLED
        internal Tween _tween;
        private CompatTween(Tween t) { _tween = t; }
        public static implicit operator CompatTween(Tween t) => new CompatTween(t);
        public bool isAlive => _tween.isAlive;
        public void Stop() => _tween.Stop();
        public void Complete() => _tween.Complete();
        public CompatTween OnComplete(Action callback) { _tween.OnComplete(callback); return this; }
        public float timeScale { set => _tween.timeScale = value; }
        public IEnumerator ToYieldInstruction() => _tween.ToYieldInstruction();
#else
        public bool isAlive => false;
        public void Stop() { }
        public void Complete() { }
        public CompatTween OnComplete(Action callback) { callback?.Invoke(); return this; }
        public float timeScale { set { } }
        public IEnumerator ToYieldInstruction() { yield break; }
#endif
    }

    // ===== CompatSequence =====
    public struct CompatSequence
    {
#if PRIME_TWEEN_INSTALLED
        private Sequence _seq;
        private CompatSequence(Sequence s) { _seq = s; }
        public static implicit operator CompatSequence(Sequence s) => new CompatSequence(s);
        public bool isAlive => _seq.isAlive;
        public void Stop() => _seq.Stop();
        public CompatSequence Group(CompatTween t) { _seq.Group(t._tween); return this; }
        public CompatSequence Chain(CompatTween t) { _seq.Chain(t._tween); return this; }
        public CompatSequence ChainDelay(float duration) { _seq.ChainDelay(duration); return this; }
        public CompatSequence OnComplete(Action callback) { _seq.OnComplete(callback); return this; }
#else
        public bool isAlive => false;
        public void Stop() { }
        public CompatSequence Group(CompatTween t) => this;
        public CompatSequence Chain(CompatTween t) => this;
        public CompatSequence ChainDelay(float duration) => this;
        public CompatSequence OnComplete(Action callback) { callback?.Invoke(); return this; }
#endif
    }

    // ===== AnimationCompat 静态工具类 =====
    public static class AnimationCompat
    {
#if PRIME_TWEEN_INSTALLED
        // Ease → Easing：PrimeTween 1.4.11 提供 Ease → Easing 隐式转换
        private static Easing E(Ease e) => (PrimeTween.Ease)(int)e;
#endif

        // ==== Alpha (CanvasGroup) ====

        public static CompatTween Alpha(CanvasGroup target, float endValue, float duration)
        {
#if PRIME_TWEEN_INSTALLED
            return Tween.Alpha(target, endValue, duration);
#else
            target.alpha = endValue; return default;
#endif
        }

        public static CompatTween Alpha(CanvasGroup target, float endValue, float duration, Ease ease)
        {
#if PRIME_TWEEN_INSTALLED
            return Tween.Alpha(target, endValue, duration, E(ease));
#else
            target.alpha = endValue; return default;
#endif
        }

        public static CompatTween Alpha(CanvasGroup target, float startValue, float endValue, float duration)
        {
#if PRIME_TWEEN_INSTALLED
            return Tween.Alpha(target, startValue, endValue, duration);
#else
            target.alpha = endValue; return default;
#endif
        }

        // ==== Alpha (Image) ====

        public static CompatTween Alpha(Image target, float endValue, float duration, Ease ease)
        {
#if PRIME_TWEEN_INSTALLED
            return Tween.Alpha(target, endValue, duration, E(ease));
#else
            var c = target.color; c.a = endValue; target.color = c; return default;
#endif
        }

        // ==== Alpha (Graphic — PrimeTween 无此重载，用 Custom<Graphic, float> 实现) ====

        public static CompatTween Alpha(Graphic target, float startValue, float endValue, float duration)
        {
#if PRIME_TWEEN_INSTALLED
            return Tween.Custom(target, startValue, endValue, duration,
                (t, v) => { var c = t.color; c.a = v; t.color = c; });
#else
            var c = target.color; c.a = endValue; target.color = c; return default;
#endif
        }

        public static CompatTween Alpha(Graphic target, float startValue, float endValue, float duration, Ease ease)
        {
#if PRIME_TWEEN_INSTALLED
            return Tween.Custom(target, startValue, endValue, duration,
                (t, v) => { var c = t.color; c.a = v; t.color = c; }, E(ease));
#else
            var c = target.color; c.a = endValue; target.color = c; return default;
#endif
        }

        // ==== UIAnchoredPosition ====

        public static CompatTween AnchoredPosition(RectTransform target, Vector2 endValue, float duration, Ease ease)
        {
#if PRIME_TWEEN_INSTALLED
            return Tween.UIAnchoredPosition(target, endValue, duration, E(ease));
#else
            target.anchoredPosition = endValue; return default;
#endif
        }

        public static CompatTween AnchoredPositionY(RectTransform target, float endValue, float duration, Ease ease)
        {
#if PRIME_TWEEN_INSTALLED
            return Tween.UIAnchoredPositionY(target, endValue, duration, E(ease));
#else
            var pos = target.anchoredPosition; pos.y = endValue; target.anchoredPosition = pos; return default;
#endif
        }

        public static CompatTween AnchoredPositionX(RectTransform target, float endValue, float duration, Ease ease)
        {
#if PRIME_TWEEN_INSTALLED
            return Tween.UIAnchoredPositionX(target, endValue, duration, E(ease));
#else
            var pos = target.anchoredPosition; pos.x = endValue; target.anchoredPosition = pos; return default;
#endif
        }

        // ==== Scale ====

        public static CompatTween Scale(Transform target, Vector3 endValue, float duration, Ease ease)
        {
#if PRIME_TWEEN_INSTALLED
            return Tween.Scale(target, endValue, duration, E(ease));
#else
            target.localScale = endValue; return default;
#endif
        }

        // ==== Custom (Vector2) — CharMoveCommand 使用 ====

        public static CompatTween CustomVector2(Vector2 startValue, Vector2 endValue, float duration,
            Action<Vector2> onValueChange, Ease ease)
        {
#if PRIME_TWEEN_INSTALLED
            return Tween.Custom(startValue, endValue, duration, onValueChange, E(ease));
#else
            onValueChange?.Invoke(endValue); return default;
#endif
        }

        // ==== Custom (float) — typewriter 等使用 ====
        // PrimeTween Custom 无 int 重载，int 改用 float 回调，外部做 Mathf.FloorToInt

        public static CompatTween CustomFloat(float startValue, float endValue, float duration,
            Action<float> onValueChange)
        {
#if PRIME_TWEEN_INSTALLED
            return Tween.Custom(startValue, endValue, duration, onValueChange);
#else
            onValueChange?.Invoke(endValue); return default;
#endif
        }

        // ==== Delay ====

        public static CompatTween Delay(float duration)
        {
#if PRIME_TWEEN_INSTALLED
            return Tween.Delay(duration);
#else
            return default;
#endif
        }

        public static CompatTween Delay(float duration, Action callback)
        {
#if PRIME_TWEEN_INSTALLED
            return Tween.Delay(duration, callback);
#else
            callback?.Invoke(); return default;
#endif
        }

        // ==== 全局控制 ====

        public static void StopAll()
        {
#if PRIME_TWEEN_INSTALLED
            Tween.StopAll();
#endif
        }

        public static void StopAllByTarget(object target)
        {
#if PRIME_TWEEN_INSTALLED
            Tween.StopAll(target);
#endif
        }

        // ==== Sequence ====

        public static CompatSequence CreateSequence()
        {
#if PRIME_TWEEN_INSTALLED
            return Sequence.Create();
#else
            return default;
#endif
        }

        public static CompatSequence CreateSequence(int cycles, SequenceCycleMode cycleMode)
        {
#if PRIME_TWEEN_INSTALLED
            return Sequence.Create(cycles: cycles, cycleMode: (Sequence.SequenceCycleMode)(int)cycleMode);
#else
            return default;
#endif
        }
    }
}
