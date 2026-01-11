using UnityEngine;

/// <summary>
/// 音乐数据类
/// </summary>
[System.Serializable]
public class VNMusic
{
    [Tooltip("音乐名称")]
    public string name = "";
    
    [Tooltip("音乐封面图片")]
    public Sprite picture;
    
    [Tooltip("音乐音频文件")]
    public AudioClip music;
    
    [Tooltip("是否已解锁（用于调试）")]
    public bool isUnlocked = false;
    
    public VNMusic()
    {
        name = "";
        picture = null;
        music = null;
        isUnlocked = false;
    }
    
    public VNMusic(string musicName)
    {
        name = musicName;
        picture = null;
        music = null;
        isUnlocked = false;
    }
}


