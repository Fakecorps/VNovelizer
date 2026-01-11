using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using LitJson;

/// <summary>
/// 全局数据结构（不随存档槽位变化的数据）
/// </summary>
[System.Serializable]
public class GlobalData
{
    // 音频设置
    public float MasterVolume = 1f;
    public float BGMVolume = 1f;
    public float VoiceVolume = 1f;
    public float SFXVolume = 1f;
    
    // 文本设置
    public float TextSpeed = 0.05f;
    public float AutoSpeed = 1.0f;
    
    // 显示设置
    public bool IsFullScreen = true;
    public int ScreenWidth = 1920;
    public int ScreenHeight = 1080;
    
    // 游戏进度数据
    public List<string> UnlockedCGs = new List<string>();
    public List<string> UnlockedScenes = new List<string>();
    public List<string> UnlockedMusic = new List<string>();
    public List<string> ReadLineIDs = new List<string>();
    
    // 游戏标志（用于剧情分支等）
    public Dictionary<string, bool> Flags = new Dictionary<string, bool>();
    public Dictionary<string, int> IntFlags = new Dictionary<string, int>();
    public Dictionary<string, string> StringFlags = new Dictionary<string, string>();
    
    /// <summary>
    /// 设置游戏标志（bool类型）
    /// </summary>
    /// <param name="flagName">标志名称</param>
    /// <param name="value">标志值</param>
    public void SetFlag(string flagName, bool value)
    {
        if (Flags.ContainsKey(flagName))
        {
            Flags[flagName] = value;
        }
        else
        {
            Flags.Add(flagName, value);
        }
    }
    
    /// <summary>
    /// 获取游戏标志（bool类型）
    /// </summary>
    /// <param name="flagName">标志名称</param>
    /// <returns>标志值，如果不存在则返回false</returns>
    public bool GetFlag(string flagName)
    {
        return Flags.ContainsKey(flagName) ? Flags[flagName] : false;
    }
    
    /// <summary>
    /// 设置游戏标志（int类型）
    /// </summary>
    /// <param name="flagName">标志名称</param>
    /// <param name="value">标志值</param>
    public void SetIntFlag(string flagName, int value)
    {
        if (IntFlags.ContainsKey(flagName))
        {
            IntFlags[flagName] = value;
        }
        else
        {
            IntFlags.Add(flagName, value);
        }
    }
    
    /// <summary>
    /// 获取游戏标志（int类型）
    /// </summary>
    /// <param name="flagName">标志名称</param>
    /// <returns>标志值，如果不存在则返回0</returns>
    public int GetIntFlag(string flagName)
    {
        return IntFlags.ContainsKey(flagName) ? IntFlags[flagName] : 0;
    }
    
    /// <summary>
    /// 设置游戏标志（string类型）
    /// </summary>
    /// <param name="flagName">标志名称</param>
    /// <param name="value">标志值</param>
    public void SetStringFlag(string flagName, string value)
    {
        if (StringFlags.ContainsKey(flagName))
        {
            StringFlags[flagName] = value;
        }
        else
        {
            StringFlags.Add(flagName, value);
        }
    }
    
    /// <summary>
    /// 获取游戏标志（string类型）
    /// </summary>
    /// <param name="flagName">标志名称</param>
    /// <returns>标志值，如果不存在则返回空字符串</returns>
    public string GetStringFlag(string flagName)
    {
        return StringFlags.ContainsKey(flagName) ? StringFlags[flagName] : "";
    }

    public List<HistoryEntry> HistoryLog = new List<HistoryEntry>();
}