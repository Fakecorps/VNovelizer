using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// CG数据类
/// </summary>
[System.Serializable]
public class CGData
{
    [Tooltip("CG名称（用于解锁的ID）")]
    public string cgName = "";

    [Tooltip("未解锁时显示的占位图 (如果为空，使用全局默认)")]
    public Sprite lockedSprite;

    [Tooltip("是否已解锁（用于调试）")]
    public bool isUnlocked = false;
    
    [Tooltip("CG图片列表")]
    public List<Sprite> sprites;
    
    public CGData()
    {
        cgName = "";
        isUnlocked = false;
        if (sprites == null)
        {
            sprites = new List<Sprite>();
        }
    }
    
    public CGData(string name)
    {
        cgName = name;
        isUnlocked = false;
        if (sprites == null)
        {
            sprites = new List<Sprite>();
        }
    }
}


