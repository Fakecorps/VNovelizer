using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 存档数据结构
/// </summary>
[System.Serializable]
public class SaveData
{
    // 剧本进度
    public string ScriptFileName;
    public string LineID;
    
    // 视觉状态
    public string CurrentBG;
    public string CurrentBGM;
    public Dictionary<string, string> Characters; // Pos -> CharacterID_Emotion
    public Dictionary<string, float> CharacterScaleX;//character的朝向

    public List<string> ActiveEffects;
    // 逻辑变量
    public Dictionary<string, bool> Flags;
    public Dictionary<string, int> IntFlags;
    public Dictionary<string, string> StringFlags;
    
    // 历史记录
    public List<HistoryEntry> HistoryLog;
    
    // 元数据
    public string SaveTime;
    public string ScreenshotPath;
}