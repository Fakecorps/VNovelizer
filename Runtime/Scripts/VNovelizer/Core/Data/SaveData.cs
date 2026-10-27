using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VNovelizer.Core.Theater;

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
    public Dictionary<string, float> FloatFlags;
    
    // 【TextActor】文字角色面板状态：累积段落快照 + 粘性正文目标（旧档缺省 null = 无文字角色）
    public string TextActorSelection;
    public List<TextActorSaveState> TextActorStates;
    public bool DialogueHidden;

    // 历史记录
    public List<HistoryEntry> HistoryLog;
    
    // 元数据
    public string SaveTime;
    /// <summary>保存时刻的高精度 Ticks（DateTime.Ticks）。SaveTime 仅秒级精度，
    /// 同秒内重复保存会导致 UI 层"内容未变"误判；以此字段做变化比对（旧档缺省 0，必然触发刷新）。</summary>
    public long SaveTick;
    public string ScreenshotPath;
}