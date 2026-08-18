using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 剧本行数据结构
/// </summary>
[System.Serializable]
public class StoryLine
{
    public string ID;
    public string Speaker;
    public string HeadProfile;
    // 五个立绘槽位（视觉顺序：左 → 中左 → 中 → 中右 → 右）
    public string CharLeft;
    public string CharMid_Left;   // 新增：中左槽位（缩写 ML）
    public string CharMid;
    public string CharMid_Right;  // 新增：中右槽位（缩写 MR）
    public string CharRight;
    public string Text;
    public string Background;
    public string BGM;
    public string Voice;
    public string Command;
    public string Note;
}