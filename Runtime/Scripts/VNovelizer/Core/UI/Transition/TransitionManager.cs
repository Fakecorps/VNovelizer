using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

[DisallowMultipleComponent]
public class TransitionManager : MonoBehaviour
{
    public static TransitionManager Instance { get; private set; }

    private readonly Dictionary<string, TransitionEffectBase> effectMap = new Dictionary<string, TransitionEffectBase>();

    public bool IsTransitionPlaying { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        CacheEffects();
    }

    private void CacheEffects()
    {
        effectMap.Clear();

        TransitionEffectBase[] effects = GetComponents<TransitionEffectBase>();
        for (int i = 0; i < effects.Length; i++)
        {
            TransitionEffectBase effect = effects[i];
            if (effect == null) continue;

            string key = effect.EffectKey;
            if (string.IsNullOrEmpty(key))
            {
                Debug.LogWarning($"[TransitionManager] 检测到空 EffectKey 的转场效果组件：{effect.GetType().Name}");
                continue;
            }

            if (effectMap.ContainsKey(key))
            {
                Debug.LogWarning($"[TransitionManager] 重复的转场效果 Key：{key}，后者将覆盖前者。");
                effectMap[key] = effect;
            }
            else
            {
                effectMap.Add(key, effect);
            }
        }
    }

    public bool HasEffect(string effectKey)
    {
        if (effectMap.Count == 0)
        {
            CacheEffects();
        }

        return !string.IsNullOrEmpty(effectKey) && effectMap.ContainsKey(effectKey);
    }

    private bool TryGetEffect(string effectKey, out TransitionEffectBase effect)
    {
        if (effectMap.Count == 0)
        {
            CacheEffects();
        }

        if (string.IsNullOrEmpty(effectKey))
        {
            effect = null;
            return false;
        }

        return effectMap.TryGetValue(effectKey, out effect) && effect != null;
    }

    public bool PlayTransitionAsync(
        string effectKey,
        Action<Action> middleActionAsync,
        Action onComplete = null,
        float enterDuration = -1f,
        float exitDuration = -1f)
    {
        if (IsTransitionPlaying)
        {
            Debug.LogWarning("[TransitionManager] 当前已有转场正在播放，忽略新的请求。");
            return false;
        }

        if (!TryGetEffect(effectKey, out TransitionEffectBase effect))
        {
            Debug.LogError($"[TransitionManager] 未找到转场效果：{effectKey}");
            return false;
        }

        IsTransitionPlaying = true;
        SetAllUIInputModulesEnabled(false);

        effect.PlayTransitionAsync(
            middleActionAsync: middleActionAsync,
            onComplete: () =>
            {
                IsTransitionPlaying = false;
                SetAllUIInputModulesEnabled(true);
                onComplete?.Invoke();
            },
            enterDuration: enterDuration,
            exitDuration: exitDuration
        );

        return true;
    }

    /// <summary>
    /// 只播放前半段（异步版）
    /// </summary>
    public bool PlayEnterOnlyAsync(
        string effectKey,
        Action onComplete = null,
        float duration = -1f)
    {
        if (IsTransitionPlaying)
        {
            Debug.LogWarning("[TransitionManager] 当前已有转场正在播放，忽略新的请求。");
            return false;
        }

        if (!TryGetEffect(effectKey, out TransitionEffectBase effect))
        {
            Debug.LogError($"[TransitionManager] 未找到转场效果：{effectKey}");
            return false;
        }

        IsTransitionPlaying = true;
        SetAllUIInputModulesEnabled(false);

        effect.PlayEnterOnlyAsync(
            onComplete: () =>
            {
                IsTransitionPlaying = false;
                SetAllUIInputModulesEnabled(true);
                onComplete?.Invoke();
            },
            duration: duration
        );

        return true;
    }

    /// <summary>
    /// 只播放后半段（异步版）
    /// </summary>
    public bool PlayExitOnlyAsync(
        string effectKey,
        Action onComplete = null,
        float duration = -1f)
    {
        if (IsTransitionPlaying)
        {
            Debug.LogWarning("[TransitionManager] 当前已有转场正在播放，忽略新的请求。");
            return false;
        }

        if (!TryGetEffect(effectKey, out TransitionEffectBase effect))
        {
            Debug.LogError($"[TransitionManager] 未找到转场效果：{effectKey}");
            return false;
        }

        IsTransitionPlaying = true;
        SetAllUIInputModulesEnabled(false);

        effect.PlayExitOnlyAsync(
            onComplete: () =>
            {
                IsTransitionPlaying = false;
                SetAllUIInputModulesEnabled(true);
                onComplete?.Invoke();
            },
            duration: duration
        );

        return true;
    }

    /// <summary>
    /// 只播放前半段（同步包装版）
    /// </summary>
    public bool PlayEnterOnly(
        string effectKey,
        Action onComplete = null,
        float duration = -1f)
    {
        return PlayEnterOnlyAsync(effectKey, onComplete, duration);
    }

    /// <summary>
    /// 只播放后半段（同步包装版）
    /// </summary>
    public bool PlayExitOnly(
        string effectKey,
        Action onComplete = null,
        float duration = -1f)
    {
        return PlayExitOnlyAsync(effectKey, onComplete, duration);
    }

    public bool PlayDarkFadeTransitionAsync(
        Action<Action> middleActionAsync,
        Action onComplete = null,
        float fadeOutDuration = -1f,
        float fadeInDuration = -1f)
    {
        return PlayTransitionAsync(
            DarkFadeTransitionEffect.EffectKeyConst,
            middleActionAsync,
            onComplete,
            fadeOutDuration,
            fadeInDuration
        );
    }

    /// <summary>
    /// 只淡出到黑（异步版）
    /// </summary>
    public bool PlayDarkFadeOutOnlyAsync(
        Action onComplete = null,
        float duration = -1f)
    {
        return PlayEnterOnlyAsync(
            DarkFadeTransitionEffect.EffectKeyConst,
            onComplete,
            duration
        );
    }

    /// <summary>
    /// 只从黑淡入（异步版）
    /// </summary>
    public bool PlayDarkFadeInOnlyAsync(
        Action onComplete = null,
        float duration = -1f)
    {
        return PlayExitOnlyAsync(
            DarkFadeTransitionEffect.EffectKeyConst,
            onComplete,
            duration
        );
    }

    /// <summary>
    /// 只淡出到黑（同步包装版）
    /// </summary>
    public bool PlayDarkFadeOutOnly(
        Action onComplete = null,
        float duration = -1f)
    {
        return PlayDarkFadeOutOnlyAsync(onComplete, duration);
    }

    /// <summary>
    /// 只从黑淡入（同步包装版）
    /// </summary>
    public bool PlayDarkFadeInOnly(
        Action onComplete = null,
        float duration = -1f)
    {
        return PlayDarkFadeInOnlyAsync(onComplete, duration);
    }

    private void SetAllUIInputModulesEnabled(bool enabled)
    {
        BaseInputModule[] modules =
            FindObjectsByType<BaseInputModule>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        for (int i = 0; i < modules.Length; i++)
        {
            if (modules[i] != null)
            {
                modules[i].enabled = enabled;
            }
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            SetAllUIInputModulesEnabled(true);
            Instance = null;
        }
    }
}