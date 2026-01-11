using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

[RequireComponent(typeof(RawImage))]
[RequireComponent(typeof(VideoPlayer))]
public class VideoModel : MonoBehaviour
{
    private VideoPlayer videoPlayer;
    private RawImage rawImage;
    private Action onComplete;

    // Unity VideoPlayer 支持的视频格式
    private static readonly string[] SupportedExtensions = { ".mp4", ".mov", ".webm", ".avi", ".asf", ".wmv" };

    private void Awake()
    {
        videoPlayer = GetComponent<VideoPlayer>();
        rawImage = GetComponent<RawImage>();

        // 设置VideoPlayer使用URL作为源
        videoPlayer.source = VideoSource.Url;

        // 绑定事件：播放结束
        videoPlayer.loopPointReached += (vp) => Close();
        videoPlayer.errorReceived += (vp, msg) => {
            Debug.LogError($"[Video] Error: {msg}");
            Close();
        };
    }

    public void Play(string videoName, Action callback)
    {
        this.onComplete = callback;

        // 1. 获取配置里的子路径 (比如 "VNovelizerRes/Videos")
        string subPath = VNProjectConfig.Instance.VideoResPath;

        // 2. 构建基础目录路径
        string baseDir = Path.Combine(Application.streamingAssetsPath, subPath);

        // 3. 查找视频文件（支持多种格式）
        string fullPath = FindVideoFile(baseDir, videoName);

        if (string.IsNullOrEmpty(fullPath))
        {
            Debug.LogError($"[Video] 无法找到视频文件: {videoName} (在目录 {baseDir} 中)");
            Close();
            return;
        }

        // 4. 统一路径分隔符为斜杠
        fullPath = fullPath.Replace("\\", "/");

        // 5. 根据平台构建正确的URL
        string url = GetVideoURL(fullPath);

        Debug.Log($"[Video] 准备播放视频: {url}");

        videoPlayer.url = url;
        StartCoroutine(PlayRoutine());
    }

    /// <summary>
    /// 查找视频文件（支持多种格式）
    /// </summary>
    private string FindVideoFile(string baseDir, string videoName)
    {
        if (!Directory.Exists(baseDir))
        {
            Debug.LogWarning($"[Video] 视频目录不存在: {baseDir}");
            return null;
        }

        // 如果文件名已经包含扩展名，直接检查
        if (Path.HasExtension(videoName))
        {
            string fullPath = Path.Combine(baseDir, videoName);
            if (File.Exists(fullPath))
            {
                return fullPath;
            }
        }

        // 如果没有扩展名，尝试所有支持的格式
        foreach (string ext in SupportedExtensions)
        {
            string fullPath = Path.Combine(baseDir, videoName + ext);
            if (File.Exists(fullPath))
            {
                Debug.Log($"[Video] 找到视频文件: {videoName + ext}");
                return fullPath;
            }
        }

        return null;
    }

    /// <summary>
    /// 根据平台获取正确的视频URL
    /// </summary>
    private string GetVideoURL(string filePath)
    {
        // 根据平台添加协议前缀
        #if UNITY_ANDROID && !UNITY_EDITOR
            // Android平台需要使用jar协议
            if (!filePath.StartsWith("jar:file://"))
            {
                filePath = "jar:file://" + filePath;
            }
        #elif UNITY_IOS && !UNITY_EDITOR
            // iOS平台需要使用file协议
            if (!filePath.StartsWith("file://"))
            {
                filePath = "file://" + filePath;
            }
        #elif UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            // Windows平台需要使用file协议（三个斜杠）
            if (!filePath.StartsWith("file://"))
            {
                filePath = "file:///" + filePath;
            }
        #elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
            // Mac平台需要使用file协议
            if (!filePath.StartsWith("file://"))
            {
                filePath = "file://" + filePath;
            }
        #else
            // 其他平台默认使用file协议
            if (!filePath.StartsWith("file://"))
            {
                filePath = "file://" + filePath;
            }
        #endif
        
        return filePath;
    }

    private IEnumerator PlayRoutine()
    {
        videoPlayer.Prepare();

        // 等待准备好
        while (!videoPlayer.isPrepared) yield return null;

        // 绑定材质并播放
        rawImage.texture = videoPlayer.texture;
        videoPlayer.Play();
    }

    private void Close()
    {
        onComplete?.Invoke();
        Destroy(gameObject); // 播放完直接自毁
    }
}