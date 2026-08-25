using UnityEngine;

/// <summary>
/// 音乐数据类
/// </summary>
[System.Serializable]
public class VNMusic
{
    [Tooltip("音乐名称")]
    public string name = "";

    [Tooltip("艺术家名称（显示格式：音乐名 - 艺术家；留空则只显示音乐名）")]
    public string artist = "";

    [Tooltip("音乐封面图片")]
    public Sprite picture;

    [Tooltip("音乐音频文件")]
    public AudioClip music;

    [Tooltip("是否已解锁（用于调试）")]
    public bool isUnlocked = false;

    /// <summary>
    /// 展示名：艺术家留空时只显示音乐名，否则显示 "音乐名 - 艺术家"
    /// </summary>
    public string DisplayName
    {
        get { return string.IsNullOrEmpty(artist) ? name : $"{name} - {artist}"; }
    }

    public VNMusic()
    {
        name = "";
        artist = "";
        picture = null;
        music = null;
        isUnlocked = false;
    }

    public VNMusic(string musicName)
    {
        name = musicName;
        artist = "";
        picture = null;
        music = null;
        isUnlocked = false;
    }
}
