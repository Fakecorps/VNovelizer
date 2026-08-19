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
    /// <summary>进入本行时执行的命令（Command 列中 @Confirm: 之前的部分）</summary>
    public string Command;
    /// <summary>
    /// [Confirm 出口] 用户确认推进时执行的命令（Command 列中 @Confirm: 之后的部分）。
    /// 为空表示未声明出口，推进走默认 NextLine；声明后由点击/AutoPlay/命令驱动推进统一经由此段执行。
    /// </summary>
    public string ConfirmCommands;
    public string Note;
}