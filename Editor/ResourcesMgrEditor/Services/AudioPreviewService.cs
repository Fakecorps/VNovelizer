using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 音频预览服务 - 通过反射访问 Unity 内部 AudioUtil API。
///
/// 关键设计变更（修复进度条不动 bug）：
/// - 不再依赖 Unity 的 GetPreviewClipPosition（在某些版本返回 0）
/// - 改用 EditorApplication.timeSinceStartup 进行手动时间跟踪
/// - 这样进度条一定会动，倍速也能真实影响进度推进
/// </summary>
public static class AudioPreviewService
{
    // 反射方法缓存
    private static MethodInfo _playMethod;
    private static MethodInfo _stopAllMethod;
    private static MethodInfo _isPlayingMethod;
    private static MethodInfo _setPitchMethod;

    private static bool _initialized;
    private static string _initError;

    /// <summary>当前正在播放的剪辑</summary>
    public static AudioClip CurrentClip { get; private set; }

    /// <summary>当前采样率</summary>
    private static int _sampleRate = 44100;

    /// <summary>是否处于暂停状态</summary>
    public static bool IsPaused { get; private set; }

    /// <summary>是否循环播放</summary>
    public static bool Loop { get; set; }

    /// <summary>播放速度（1.0 = 正常速度）</summary>
    public static float PlaybackSpeed { get; private set; } = 1.0f;

    // ===================== 手动时间跟踪 =====================
    // 播放开始时记录的实时时间（EditorApplication.timeSinceStartup）
    private static double _realStartTime;
    // 播放开始时的位置（秒）
    private static float _positionAtStart;
    // 暂停时保存的位置
    private static float _pausedPosition;

    /// <summary>剪辑结束事件</summary>
    public static event Action<AudioClip> OnClipEnd;

    /// <summary>播放状态变化事件（用于刷新 UI）</summary>
    public static event Action OnStateChanged;

    public static bool IsAvailable => _initialized;
    public static string InitError => _initError;

    static AudioPreviewService()
    {
        try
        {
            var audioUtilType = typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");
            if (audioUtilType == null)
            {
                _initError = "未找到 UnityEditor.AudioUtil 类型";
                return;
            }

            const BindingFlags flags = BindingFlags.Static | BindingFlags.Public;
            _playMethod = audioUtilType.GetMethod("PlayPreviewClip", flags,
                null, new[] { typeof(AudioClip), typeof(int), typeof(bool) }, null);
            _stopAllMethod = audioUtilType.GetMethod("StopAllPreviewClips", flags);
            _isPlayingMethod = audioUtilType.GetMethod("IsPreviewClipPlaying", flags);

            // 尝试多种 pitch 方法名
            _setPitchMethod =
                audioUtilType.GetMethod("SetPreviewClipPitch", flags, null, new[] { typeof(float) }, null)
                ?? audioUtilType.GetMethod("SetPreviewClipPlaybackPitch", flags, null, new[] { typeof(AudioClip), typeof(float) }, null)
                ?? audioUtilType.GetMethod("SetPreviewClipPitch", flags, null, new[] { typeof(AudioClip), typeof(float) }, null)
                ?? audioUtilType.GetMethod("SetPreviewClipPlaybackPitch", flags, null, new[] { typeof(float) }, null);

            if (_playMethod == null || _stopAllMethod == null)
            {
                _initError = "AudioUtil 关键方法未找到";
                return;
            }
            _initialized = true;
        }
        catch (Exception e)
        {
            _initError = e.Message;
        }
    }

    // ===================== 播放控制 =====================

    /// <summary>从指定采样位置播放（默认从头开始）</summary>
    public static void Play(AudioClip clip, int startSample = 0, bool loop = false)
    {
        if (!_initialized || clip == null) return;

        // 停止之前的播放
        _stopAllMethod.Invoke(null, null);

        int sample = Mathf.Clamp(startSample, 0, Mathf.Max(0, clip.samples - 1));
        _playMethod.Invoke(null, new object[] { clip, sample, loop });

        CurrentClip = clip;
        _sampleRate = Mathf.Max(1, clip.frequency);
        IsPaused = false;
        Loop = loop;
        _positionAtStart = (float)sample / _sampleRate;
        _pausedPosition = _positionAtStart;
        _realStartTime = EditorApplication.timeSinceStartup;

        // 非关键步骤 - 速度应用独立 try，不影响播放
        ApplyPitchInternal();

        EditorUpdateService.StartTracking();
        OnStateChanged?.Invoke();
    }

    /// <summary>切换播放/暂停</summary>
    public static void Toggle()
    {
        if (CurrentClip == null) return;
        if (IsPaused) Resume();
        else Pause();
    }

    /// <summary>暂停</summary>
    public static void Pause()
    {
        if (!_initialized || CurrentClip == null || IsPaused) return;
        // 记录当前位置
        _pausedPosition = GetPosition();
        try
        {
            _stopAllMethod.Invoke(null, null);
            IsPaused = true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[AudioPreview] 暂停失败: {e.Message}");
        }
        OnStateChanged?.Invoke();
    }

    /// <summary>恢复播放 - 从暂停位置继续</summary>
    public static void Resume()
    {
        if (!_initialized || CurrentClip == null || !IsPaused) return;
        try
        {
            int sample = Mathf.Clamp(Mathf.RoundToInt(_pausedPosition * _sampleRate), 0, Mathf.Max(0, CurrentClip.samples - 1));
            _playMethod.Invoke(null, new object[] { CurrentClip, sample, Loop });
            IsPaused = false;
            _positionAtStart = _pausedPosition;
            _realStartTime = EditorApplication.timeSinceStartup;

            // 重新应用速度
            ApplyPitchInternal();
        }
        catch (Exception e)
        {
            Debug.LogError($"[AudioPreview] 恢复失败: {e.Message}");
        }
        OnStateChanged?.Invoke();
    }

    /// <summary>停止播放</summary>
    public static void Stop()
    {
        if (!_initialized) return;
        try
        {
            _stopAllMethod.Invoke(null, null);
        }
        catch (Exception e)
        {
            Debug.LogError($"[AudioPreview] 停止失败: {e.Message}");
        }
        CurrentClip = null;
        IsPaused = false;
        _pausedPosition = 0f;
        _positionAtStart = 0f;
        OnStateChanged?.Invoke();
    }

    /// <summary>跳转到指定位置（秒）</summary>
    public static void Seek(float seconds)
    {
        if (CurrentClip == null) return;
        seconds = Mathf.Clamp(seconds, 0, GetLength());

        if (IsPaused)
        {
            _pausedPosition = seconds;
        }
        else
        {
            // 重新启动播放，从新位置
            try
            {
                int sample = Mathf.RoundToInt(seconds * _sampleRate);
                _stopAllMethod.Invoke(null, null);
                _playMethod.Invoke(null, new object[] { CurrentClip, sample, Loop });
                ApplyPitchInternal();

                _positionAtStart = seconds;
                _realStartTime = EditorApplication.timeSinceStartup;
            }
            catch (Exception e)
            {
                Debug.LogError($"[AudioPreview] 跳转失败: {e.Message}");
            }
        }
        OnStateChanged?.Invoke();
    }

    // ===================== 状态查询 =====================

    /// <summary>是否正在播放（不包括暂停）</summary>
    public static bool IsPlaying()
    {
        if (!_initialized || CurrentClip == null || IsPaused) return false;
        try
        {
            return (bool)(_isPlayingMethod?.Invoke(null, null) ?? false);
        }
        catch { return false; }
    }

    /// <summary>当前播放位置（秒） - 基于手动时间跟踪</summary>
    public static float GetPosition()
    {
        if (CurrentClip == null) return 0f;
        if (IsPaused) return _pausedPosition;
        // 使用 EditorApplication.timeSinceStartup 计算
        double elapsed = EditorApplication.timeSinceStartup - _realStartTime;
        float pos = _positionAtStart + (float)elapsed * PlaybackSpeed;
        return Mathf.Clamp(pos, 0, GetLength());
    }

    /// <summary>当前剪辑总长度（秒）</summary>
    public static float GetLength()
    {
        return CurrentClip != null ? CurrentClip.length : 0f;
    }

    // ===================== 音量和速度 =====================

    /// <summary>设置播放速度（0.25 - 2.0）</summary>
    public static void SetPlaybackSpeed(float speed)
    {
        PlaybackSpeed = Mathf.Clamp(speed, 0.25f, 2.0f);
        ApplyPitchInternal();
        OnStateChanged?.Invoke();
    }

    private static void ApplyPitchInternal()
    {
        if (!_initialized || _setPitchMethod == null || CurrentClip == null) return;
        try
        {
            var parameters = _setPitchMethod.GetParameters();
            if (parameters.Length == 1)
                _setPitchMethod.Invoke(null, new object[] { PlaybackSpeed });
            else if (parameters.Length == 2)
                _setPitchMethod.Invoke(null, new object[] { CurrentClip, PlaybackSpeed });
        }
        catch { /* 版本不支持时静默忽略 */ }
    }

    /// <summary>每帧检测剪辑是否结束</summary>
    internal static void Tick()
    {
        // 使用手动时间检测播放结束（更可靠）
        if (CurrentClip != null && !IsPaused && !Loop)
        {
            if (GetPosition() >= GetLength() - 0.05f)
            {
                var ended = CurrentClip;
                CurrentClip = null;
                IsPaused = false;
                OnClipEnd?.Invoke(ended);
                OnStateChanged?.Invoke();
            }
        }
    }
}

/// <summary>
/// Editor 帧更新服务 - 提供每帧回调。
/// 用于驱动音频播放进度条等需要持续更新的 UI。
/// </summary>
public static class EditorUpdateService
{
    private static bool _tracking;
    private static Action _callback;

    public static void StartTracking()
    {
        if (_tracking) return;
        _tracking = true;
        EditorApplication.update += OnUpdate;
    }

    public static void StopTracking()
    {
        _tracking = false;
        EditorApplication.update -= OnUpdate;
    }

    public static void RegisterCallback(Action callback) => _callback = callback;
    public static void UnregisterCallback() => _callback = null;

    private static void OnUpdate()
    {
        try
        {
            AudioPreviewService.Tick();
            _callback?.Invoke();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[EditorUpdate] 回调异常: {e.Message}");
        }
    }
}
