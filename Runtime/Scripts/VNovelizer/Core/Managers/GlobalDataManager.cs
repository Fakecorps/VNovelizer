using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;

/// <summary>
/// 全局数据管理器（管理不随存档槽位变化的数据）
/// </summary>
public class GlobalDataManager : BaseManager<GlobalDataManager>
{
    private GlobalData globalData;
    private const string GLOBAL_DATA_PATH = "global_data.json";
    private bool isInitialized = false;
    
    /// <summary>
    /// 初始化
    /// </summary>
    public void Init()
    {
        if (isInitialized) return;
        
        LoadGlobalData();
        ApplyDisplaySettings();
        isInitialized = true;
    }
    
    /// <summary>
    /// 确保已初始化（懒加载）
    /// </summary>
    private void EnsureInitialized()
    {
        if (!isInitialized)
        {
            Init();
        }
    }
    
    /// <summary>
    /// 应用显示设置（分辨率、全屏模式）
    /// </summary>
    private void ApplyDisplaySettings()
    {
        if (globalData != null)
        {
            Screen.SetResolution(globalData.ScreenWidth, globalData.ScreenHeight, globalData.IsFullScreen);
        }
    }
    
    /// <summary>
    /// 加载全局数据
    /// </summary>
    private void LoadGlobalData()
    {
        string path = Application.persistentDataPath + "/" + GLOBAL_DATA_PATH;
        if (File.Exists(path))
        {
            string json = File.ReadAllText(path);
            globalData = LitJson.JsonMapper.ToObject<GlobalData>(json);
        }
        else
        {
            // 创建默认全局数据
            globalData = new GlobalData();
            // 使用当前屏幕分辨率作为默认值
            globalData.ScreenWidth = Screen.currentResolution.width;
            globalData.ScreenHeight = Screen.currentResolution.height;
            globalData.IsFullScreen = Screen.fullScreen;
            SaveGlobalData();
        }
        
        // 确保分辨率数据有效
        if (globalData.ScreenWidth <= 0 || globalData.ScreenHeight <= 0)
        {
            globalData.ScreenWidth = Screen.currentResolution.width;
            globalData.ScreenHeight = Screen.currentResolution.height;
            SaveGlobalData();
        }
        
        // 确保新添加的字典已初始化（兼容旧版本数据）
        if (globalData.IntFlags == null)
        {
            globalData.IntFlags = new Dictionary<string, int>();
        }
        if (globalData.StringFlags == null)
        {
            globalData.StringFlags = new Dictionary<string, string>();
        }
        
        // 确保新添加的列表已初始化（兼容旧版本数据）
        if (globalData.UnlockedScenes == null)
        {
            globalData.UnlockedScenes = new List<string>();
        }
        if (globalData.UnlockedMusic == null)
        {
            globalData.UnlockedMusic = new List<string>();
        }
    }
    
    /// <summary>
    /// 保存全局数据
    /// </summary>
    private void SaveGlobalData()
    {
        string path = Application.persistentDataPath + "/" + GLOBAL_DATA_PATH;
        string json = LitJson.JsonMapper.ToJson(globalData);
        File.WriteAllText(path, json);
    }
    
    /// <summary>
    /// 获取全局数据（自动初始化）
    /// </summary>
    /// <returns>全局数据</returns>
    public GlobalData GetGlobalData()
    {
        EnsureInitialized();
        return globalData;
    }
    
    /// <summary>
    /// 解锁CG
    /// </summary>
    /// <param name="cgName">CG名称</param>
    public void UnlockCG(string cgName)
    {
        EnsureInitialized();
        if (!globalData.UnlockedCGs.Contains(cgName))
        {
            globalData.UnlockedCGs.Add(cgName);
            SaveGlobalData();
            EventCenter.GetInstance().EventTrigger("CGUnlocked", cgName);
        }
    }
    
    /// <summary>
    /// 检查CG是否已解锁
    /// </summary>
    /// <param name="cgName">CG名称</param>
    /// <returns>是否已解锁</returns>
    public bool IsCGUnlocked(string cgName)
    {
        EnsureInitialized();
        return globalData.UnlockedCGs.Contains(cgName);
    }
    
    /// <summary>
    /// 解锁音乐
    /// </summary>
    /// <param name="musicName">音乐名称</param>
    public void UnlockMusic(string musicName)
    {
        EnsureInitialized();
        if (!globalData.UnlockedMusic.Contains(musicName))
        {
            globalData.UnlockedMusic.Add(musicName);
            SaveGlobalData();
            EventCenter.GetInstance().EventTrigger("MusicUnlocked", musicName);
        }
    }
    
    /// <summary>
    /// 检查音乐是否已解锁
    /// </summary>
    /// <param name="musicName">音乐名称</param>
    /// <returns>是否已解锁</returns>
    public bool IsMusicUnlocked(string musicName)
    {
        EnsureInitialized();
        return globalData.UnlockedMusic.Contains(musicName);
    }
    
    /// <summary>
    /// 解锁场景
    /// </summary>
    /// <param name="sceneID">场景ID</param>
    public void UnlockScene(string sceneID)
    {
        EnsureInitialized();
        if (!globalData.UnlockedScenes.Contains(sceneID))
        {
            globalData.UnlockedScenes.Add(sceneID);
            SaveGlobalData();
            EventCenter.GetInstance().EventTrigger("SceneUnlocked", sceneID);
        }
    }
    
    /// <summary>
    /// 检查场景是否已解锁
    /// </summary>
    /// <param name="sceneID">场景ID</param>
    /// <returns>是否已解锁</returns>
    public bool IsSceneUnlocked(string sceneID)
    {
        EnsureInitialized();
        return globalData.UnlockedScenes.Contains(sceneID);
    }
    
    /// <summary>
    /// 添加已读剧情ID
    /// </summary>
    /// <param name="lineID">剧情行ID</param>
    public void AddReadLineID(string lineID)
    {
        EnsureInitialized();
        if (!globalData.ReadLineIDs.Contains(lineID))
        {
            globalData.ReadLineIDs.Add(lineID);
            SaveGlobalData();
        }
    }
    
    /// <summary>
    /// 检查剧情是否已读
    /// </summary>
    /// <param name="lineID">剧情行ID</param>
    /// <returns>是否已读</returns>
    public bool IsLineRead(string lineID)
    {
        EnsureInitialized();
        return globalData.ReadLineIDs.Contains(lineID);
    }
    
    /// <summary>
    /// 更新音量设置
    /// </summary>
    /// <param name="masterVolume">主音量</param>
    /// <param name="bgmVolume">BGM音量</param>
    /// <param name="voiceVolume">语音音量</param>
    /// <param name="sfxVolume">音效音量</param>
    public void UpdateVolumeSettings(float masterVolume, float bgmVolume, float voiceVolume, float sfxVolume)
    {
        EnsureInitialized();
        globalData.MasterVolume = masterVolume;
        globalData.BGMVolume = bgmVolume;
        globalData.VoiceVolume = voiceVolume;
        globalData.SFXVolume = sfxVolume;
        
        // 更新系统音量
        AudioListener.volume = masterVolume;
        MusicManager.GetInstance().ChangeBGMVolume(bgmVolume);
        VoiceManager.GetInstance().ChangeVoiceVolume(voiceVolume);
        MusicManager.GetInstance().ChangeSFXVolume(sfxVolume);
        
        SaveGlobalData();
    }
    
    /// <summary>
    /// 更新文本速度
    /// </summary>
    /// <param name="textSpeed">文本速度</param>
    public void UpdateTextSpeed(float textSpeed)
    {
        EnsureInitialized();
        globalData.TextSpeed = textSpeed;
        SaveGlobalData();
        EventCenter.GetInstance().EventTrigger("TextSpeedChanged", textSpeed);
    }
    
    /// <summary>
    /// 更新自动播放速度
    /// </summary>
    /// <param name="autoSpeed">自动播放速度</param>
    public void UpdateAutoSpeed(float autoSpeed)
    {
        EnsureInitialized();
        globalData.AutoSpeed = autoSpeed;
        SaveGlobalData();
    }
    
    /// <summary>
    /// 更新显示模式
    /// </summary>
    /// <param name="isFullScreen">是否全屏</param>
    public void UpdateDisplayMode(bool isFullScreen)
    {
        EnsureInitialized();
        globalData.IsFullScreen = isFullScreen;
        Screen.fullScreen = isFullScreen;
        SaveGlobalData();
    }
    
    /// <summary>
    /// 更新分辨率
    /// </summary>
    /// <param name="width">宽度</param>
    /// <param name="height">高度</param>
    /// <param name="isFullScreen">是否全屏</param>
    public void UpdateResolution(int width, int height, bool isFullScreen)
    {
        EnsureInitialized();
        globalData.ScreenWidth = width;
        globalData.ScreenHeight = height;
        globalData.IsFullScreen = isFullScreen;
        Screen.SetResolution(width, height, isFullScreen);
        SaveGlobalData();
    }

    public void AddHistoryLog(string speaker, string text, string voiceID)
    {
        EnsureInitialized();
        if (globalData == null) return;

        // 创建新条目
        HistoryEntry entry = new HistoryEntry(speaker, text, voiceID);
        globalData.HistoryLog.Add(entry);

        // 限制上限 (比如100条)
        if (globalData.HistoryLog.Count > 100)
        {
            globalData.HistoryLog.RemoveAt(0);
        }
    }

    public List<HistoryEntry> GetHistoryLog()
    {
        EnsureInitialized();
        return globalData != null ? globalData.HistoryLog : new List<HistoryEntry>();
    }
    
    /// <summary>
    /// 清空历史记录
    /// </summary>
    public void ClearHistoryLog()
    {
        EnsureInitialized();
        if (globalData != null)
        {
            globalData.HistoryLog.Clear();
            SaveGlobalData();
            Debug.Log("[GlobalDataManager] 历史记录已清空");
        }
    }
    
    /// <summary>
    /// 恢复历史记录（从存档加载）
    /// </summary>
    public void RestoreHistoryLog(List<HistoryEntry> historyLog)
    {
        EnsureInitialized();
        if (globalData != null && historyLog != null)
        {
            globalData.HistoryLog = new List<HistoryEntry>(historyLog);
            SaveGlobalData();
            Debug.Log($"[GlobalDataManager] 已恢复 {historyLog.Count} 条历史记录");
        }
    }
    
    /// <summary>
    /// 设置游戏标志（bool类型）
    /// </summary>
    /// <param name="flagName">标志名称</param>
    /// <param name="value">标志值</param>
    public void SetBoolFlag(string flagName, bool value)
    {
        EnsureInitialized();
        if (globalData != null)
        {
            globalData.SetFlag(flagName, value);
            SaveGlobalData();
        }
    }
    
    /// <summary>
    /// 获取游戏标志（bool类型）
    /// </summary>
    /// <param name="flagName">标志名称</param>
    /// <returns>标志值，如果不存在则返回false</returns>
    public bool GetBoolFlag(string flagName)
    {
        EnsureInitialized();
        return globalData != null ? globalData.GetFlag(flagName) : false;
    }
    
    /// <summary>
    /// 设置游戏标志（int类型）
    /// </summary>
    /// <param name="flagName">标志名称</param>
    /// <param name="value">标志值</param>
    public void SetIntFlag(string flagName, int value)
    {
        EnsureInitialized();
        if (globalData != null)
        {
            globalData.SetIntFlag(flagName, value);
            SaveGlobalData();
        }
    }
    
    /// <summary>
    /// 获取游戏标志（int类型）
    /// </summary>
    /// <param name="flagName">标志名称</param>
    /// <returns>标志值，如果不存在则返回0</returns>
    public int GetIntFlag(string flagName)
    {
        EnsureInitialized();
        return globalData != null ? globalData.GetIntFlag(flagName) : 0;
    }
    
    /// <summary>
    /// 设置游戏标志（string类型）
    /// </summary>
    /// <param name="flagName">标志名称</param>
    /// <param name="value">标志值</param>
    public void SetStringFlag(string flagName, string value)
    {
        EnsureInitialized();
        if (globalData != null)
        {
            globalData.SetStringFlag(flagName, value);
            SaveGlobalData();
        }
    }
    
    /// <summary>
    /// 获取游戏标志（string类型）
    /// </summary>
    /// <param name="flagName">标志名称</param>
    /// <returns>标志值，如果不存在则返回空字符串</returns>
    public string GetStringFlag(string flagName)
    {
        EnsureInitialized();
        return globalData != null ? globalData.GetStringFlag(flagName) : "";
    }
}