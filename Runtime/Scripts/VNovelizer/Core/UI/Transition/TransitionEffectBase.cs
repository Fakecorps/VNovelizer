using System;
using UnityEngine;

public abstract class TransitionEffectBase : MonoBehaviour
{
    public abstract string EffectKey { get; }
    public abstract bool IsPlaying { get; }

    /// <summary>
    /// 完整转场：进入 -> 中间逻辑 -> 退出
    /// </summary>
    public abstract void PlayTransitionAsync(
        Action<Action> middleActionAsync,
        Action onComplete = null,
        float enterDuration = -1f,
        float exitDuration = -1f
    );

    /// <summary>
    /// 只播放前半段：进入转场（异步版）
    /// </summary>
    public abstract void PlayEnterOnlyAsync(
        Action onComplete = null,
        float duration = -1f
    );

    /// <summary>
    /// 只播放后半段：退出转场（异步版）
    /// </summary>
    public abstract void PlayExitOnlyAsync(
        Action onComplete = null,
        float duration = -1f
    );

    /// <summary>
    /// 只播放前半段：同步包装版
    /// </summary>
    public virtual void PlayEnterOnly(
        Action onComplete = null,
        float duration = -1f)
    {
        PlayEnterOnlyAsync(onComplete, duration);
    }

    /// <summary>
    /// 只播放后半段：同步包装版
    /// </summary>
    public virtual void PlayExitOnly(
        Action onComplete = null,
        float duration = -1f)
    {
        PlayExitOnlyAsync(onComplete, duration);
    }
}