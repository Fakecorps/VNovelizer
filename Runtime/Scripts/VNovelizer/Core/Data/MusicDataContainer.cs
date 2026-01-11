using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 音乐数据容器（ScriptableObject）
/// </summary>
[CreateAssetMenu(fileName = "MusicDataContainer", menuName = "VNovelizer/Music Data Container")]
public class MusicDataContainer : ScriptableObject
{
    [Tooltip("音乐列表")]
    public List<VNMusic> musicList = new List<VNMusic>();
    
    /// <summary>
    /// 根据名称获取音乐数据
    /// </summary>
    public VNMusic GetMusicByName(string musicName)
    {
        if (musicList == null) return null;
        
        foreach (VNMusic music in musicList)
        {
            if (music != null && music.name == musicName)
            {
                return music;
            }
        }
        return null;
    }
    
    /// <summary>
    /// 添加音乐
    /// </summary>
    public void AddMusic(VNMusic music)
    {
        if (musicList == null)
        {
            musicList = new List<VNMusic>();
        }
        musicList.Add(music);
    }
    
    /// <summary>
    /// 移除音乐
    /// </summary>
    public void RemoveMusic(VNMusic music)
    {
        if (musicList != null)
        {
            musicList.Remove(music);
        }
    }
}


