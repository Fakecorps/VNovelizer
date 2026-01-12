using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 语音管理器（使用Resources加载音频文件）
/// </summary>
public class VoiceManager : BaseManager<VoiceManager>
{
    private AudioSource voiceSource;
    private float voiceVolume = 1f;
    private Coroutine currentLoadCoroutine;

    // 手动标记语音是否正在播放
    private bool isVoiceRunning = false;

    /// <summary>
    /// 初始化
    /// </summary>
    public void Init()
    {
        // 创建音频源
        GameObject obj = new GameObject("Voice_Player");
        GameObject.DontDestroyOnLoad(obj); // 确保切场景不销毁
        voiceSource = obj.AddComponent<AudioSource>();
        voiceSource.playOnAwake = false;
        voiceSource.loop = false;
    }

    /// <summary>
    /// 播放语音
    /// </summary>
    /// <param name="voicePath">语音文件路径（相对于Resources/VoiceResPath/，不包含扩展名）</param>
    public void PlayVoice(string voicePath)
    {
        // 1. 检查参数有效性
        if (string.IsNullOrEmpty(voicePath))
        {
            // 空路径视为"不播放语音"，应该停止当前语音
            StopVoice();
            return;
        }

        // 2. 检查VoiceManager是否已初始化
        if (voiceSource == null)
        {
            Init();
        }

        // 3. 停止当前播放
        StopVoice();

        // 4. 规范化语音路径（移除前导/尾随空格和路径分隔符）
        voicePath = voicePath.Trim().TrimStart('/', '\\').TrimEnd('/', '\\');

        // 5. 移除扩展名（Resources.Load 不需要扩展名，会自动查找）
        // 如果路径包含扩展名，移除它
        if (voicePath.EndsWith(".mp3", System.StringComparison.OrdinalIgnoreCase) ||
            voicePath.EndsWith(".wav", System.StringComparison.OrdinalIgnoreCase) ||
            voicePath.EndsWith(".ogg", System.StringComparison.OrdinalIgnoreCase))
        {
            int lastDotIndex = voicePath.LastIndexOf('.');
            if (lastDotIndex > 0)
            {
                voicePath = voicePath.Substring(0, lastDotIndex);
            }
        }

        // 6. 构建Resources路径
        string loadPath = VNProjectConfig.Instance.VoiceResPath;
        string fullResourcePath = string.IsNullOrEmpty(loadPath) 
            ? voicePath 
            : $"{loadPath}/{voicePath}";

        // 7. 启动加载和播放流程
        isVoiceRunning = true; // 标记开始
        currentLoadCoroutine = MonoManager.GetInstance().StartCoroutine(LoadAndPlayVoice(fullResourcePath));
    }

    /// <summary>
    /// 加载并播放语音（使用Resources异步加载）
    /// </summary>
    private IEnumerator LoadAndPlayVoice(string resourcePath)
    {
        AudioClip clip = null;
        bool loadComplete = false;
        bool hasclip = false;
        // 使用 ResourcesManager 异步加载
        ResourcesManager.GetInstance().LoadAsync<AudioClip>(resourcePath, (loadedClip) =>
        {
            if (loadedClip == null)
            {
                hasclip = false;
            }
            else
            {
                hasclip = true;
               clip = loadedClip;
            }
            loadComplete = true;
        });

        // 等待加载完成
        while (!loadComplete)
        {
            yield return null;
        }

        // 检查加载结果
        if (!hasclip)
        {
            isVoiceRunning = false;
            currentLoadCoroutine = null;
        }

        if (clip == null || voiceSource == null)
        {
            Debug.LogWarning($"[VoiceManager] 语音加载失败: clip或voiceSource为空。若有意为之，请忽略该警告");
            isVoiceRunning = false;
            currentLoadCoroutine = null;
            yield break;
        }

        // 播放音频
        voiceSource.clip = clip;
        voiceSource.volume = voiceVolume;
        voiceSource.Play();
        
        Debug.Log($"[VoiceManager] 开始播放语音: {resourcePath}, 时长: {clip.length}秒");

        // 等待播放完成（使用AudioSource.isPlaying和clip.length）
        float duration = clip.length;
        float timer = 0f;

        while (timer < duration && isVoiceRunning && voiceSource != null)
        {
            // 如果AudioSource被停止，也退出
            if (!voiceSource.isPlaying && timer > 0.1f) // 0.1f 是容错时间，避免刚播放时的短暂停止
            {
                Debug.Log($"[VoiceManager] 语音播放被提前停止 (时间: {timer:F2}秒 / {duration:F2}秒)");
                break;
            }

            timer += Time.deltaTime;
            yield return null;
        }

        // 播放结束
        if (timer >= duration)
        {
            Debug.Log($"[VoiceManager] 语音播放完毕 (时长: {duration:F2}秒, 实际播放: {timer:F2}秒)");
        }
        else if (!isVoiceRunning)
        {
            Debug.Log($"[VoiceManager] 语音播放被中断 (isVoiceRunning=false, 时间: {timer:F2}秒)");
        }
        else if (voiceSource == null)
        {
            Debug.Log($"[VoiceManager] 语音播放被中断 (voiceSource=null, 时间: {timer:F2}秒)");
        }
        
        isVoiceRunning = false;
        currentLoadCoroutine = null;
        if (VNManager.GetInstance().IsAutoPlaying())// 检查自动播放条件
        {
            VNManager.GetInstance().CheckAutoPlay(); 
        }
    }

    /// <summary>
    /// 停止语音
    /// </summary>
    public void StopVoice()
    {
        // 1. 逻辑停止
        isVoiceRunning = false;

        // 2. 停止加载协程
        if (currentLoadCoroutine != null)
        {
            MonoManager.GetInstance().StopCoroutine(currentLoadCoroutine);
            currentLoadCoroutine = null;
        }

        // 3. 物理停止 AudioSource
        if (voiceSource != null)
        {
            voiceSource.Stop();
            voiceSource.clip = null; // 清空引用释放内存
        }
    }

    /// <summary>
    /// 改变语音音量
    /// </summary>
    public void ChangeVoiceVolume(float volume)
    {
        voiceVolume = volume;
        if (voiceSource != null)
        {
            voiceSource.volume = voiceVolume;
        }
    }

    /// <summary>
    /// 语音是否正在播放 (供 VNManager AutoPlay 使用)
    /// </summary>
    public bool IsVoicePlaying()
    {
        // 直接返回我们的手动标记，这比 AudioSource.isPlaying 更可靠
        return isVoiceRunning;
    }
}