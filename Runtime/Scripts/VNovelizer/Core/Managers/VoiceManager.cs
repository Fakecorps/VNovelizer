using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// 语音管理器（使用UnityWebRequest流式加载StreamingAssets音频）
/// </summary>
public class VoiceManager : BaseManager<VoiceManager>
{
    private AudioSource voiceSource;
    private float voiceVolume = 1f;
    private UnityWebRequest currentRequest;
    
    /// <summary>
    /// 初始化
    /// </summary>
    public void Init()
    {
        // 创建音频源
        GameObject obj = new GameObject("Voice_Player");
        voiceSource = obj.AddComponent<AudioSource>();
        voiceSource.playOnAwake = false;
        voiceSource.loop = false;
    }
    
    /// <summary>
    /// 播放语音
    /// </summary>
    /// <param name="voicePath">语音文件路径（相对于StreamingAssets/Voice/）</param>
    public void PlayVoice(string voicePath)
    {
        // 【Bug修复】检查参数有效性
        if (string.IsNullOrEmpty(voicePath))
        {
            Debug.LogWarning("[VoiceManager] 语音路径为空，跳过播放");
            return;
        }
        
        // 检查VoiceManager是否已初始化
        if (voiceSource == null)
        {
            Debug.LogWarning("[VoiceManager] VoiceSource未初始化，尝试初始化...");
            Init();
        }
        
        // 停止当前播放
        StopVoice();
        
        // 构建完整路径
        string fullPath = Path.Combine(Application.streamingAssetsPath, "Voice", voicePath);
        
        // 【Bug修复】检查文件是否存在（仅编辑器下，运行时无法直接检查StreamingAssets）
        #if UNITY_EDITOR
        if (!System.IO.File.Exists(fullPath))
        {
            Debug.LogWarning($"[VoiceManager] 语音文件不存在: {fullPath}");
            return;
        }
        #endif
        
        // 开始流式加载
        MonoManager.GetInstance().StartCoroutine(LoadAndPlayVoice(fullPath));
    }
    
    /// <summary>
    /// 流式加载并播放语音
    /// </summary>
    /// <param name="fullPath">完整路径</param>
    private IEnumerator LoadAndPlayVoice(string fullPath)
    {
        // 根据平台构建正确的URL
        string url = fullPath;
        if (Application.platform == RuntimePlatform.Android)
        {
            url = "jar:file://" + url;
        }
        else if (Application.platform == RuntimePlatform.IPhonePlayer)
        {
            url = "file://" + url;
        }
        
        currentRequest = UnityWebRequestMultimedia.GetAudioClip(url, AudioType.MPEG);
        currentRequest.SendWebRequest();
        
        // 等待加载完成
        while (!currentRequest.isDone)
        {
            yield return null;
        }
        
        if (currentRequest.result != UnityWebRequest.Result.Success)
        {
            // 【Bug修复】优化错误信息，区分不同类型的错误
            string errorMsg = currentRequest.error;
            if (errorMsg.Contains("Cannot connect") || errorMsg.Contains("destination host"))
            {
                Debug.LogWarning($"[VoiceManager] 语音文件可能不存在或路径错误: {fullPath}");
            }
            else
            {
                Debug.LogError($"[VoiceManager] 语音加载错误: {errorMsg} (路径: {fullPath})");
            }
            
            // 清理请求
            if (currentRequest != null)
            {
                currentRequest.Dispose();
                currentRequest = null;
            }
        }
        else
        {
            // 播放音频
            AudioClip audioClip = DownloadHandlerAudioClip.GetContent(currentRequest);
            voiceSource.clip = audioClip;
            voiceSource.volume = voiceVolume;
            voiceSource.Play();
        }
    }
    
    /// <summary>
    /// 停止语音
    /// </summary>
    public void StopVoice()
    {
        if (voiceSource != null)
        {
            voiceSource.Stop();
        }
        
        if (currentRequest != null && !currentRequest.isDone)
        {
            currentRequest.Abort();
            currentRequest.Dispose();
        }
    }
    
    /// <summary>
    /// 改变语音音量
    /// </summary>
    /// <param name="volume">音量值（0-1）</param>
    public void ChangeVoiceVolume(float volume)
    {
        voiceVolume = volume;
        if (voiceSource != null)
        {
            voiceSource.volume = voiceVolume;
        }
    }
    
    /// <summary>
    /// 语音是否正在播放
    /// </summary>
    /// <returns>是否正在播放</returns>
    public bool IsVoicePlaying()
    {
        return voiceSource != null && voiceSource.isPlaying;
    }
}