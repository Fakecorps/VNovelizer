using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class MusicManager : BaseManager<MusicManager>
{
    // BGM 组件（BGM 全局唯一，不需要池子）
    private AudioSource BGM = null;
    private float BGMVolume = 1f;

    // SFX 列表（用于在 Update 里检测播放是否结束）
    private List<AudioSource> SFXList = new List<AudioSource>();
    private float SFXVolume = 1f;

    public MusicManager()
    {
        MonoManager.GetInstance().AddUpdateListener(Update);
    }

    private void Update()
    {
        // 每帧检测音效是否播放完毕
        CheckSFXEnd();
    }

    #region 背景音乐 BGM 
    public void ChangeBGMVolume(float volume)
    {
        BGMVolume = volume;
        if (BGM == null) return;
        BGM.volume = BGMVolume;
    }

    public void PlayBGM(string name)
    {
        if (BGM == null)
        {
            GameObject obj = new GameObject("BGM_Player");
            BGM = obj.AddComponent<AudioSource>();
        }
        string loadPath = VNProjectConfig.Instance.BgmResPath;
        ResourcesManager.GetInstance().LoadAsync<AudioClip>(loadPath +"/" + name, (clip) =>
        {
            BGM.clip = clip;
            BGM.volume = BGMVolume;
            BGM.loop = true;
            BGM.Play();
        });
    }

    public void PauseBGM()
    {
        if (BGM != null) BGM.Pause();
    }

    public void StopBGM()
    {
        if (BGM != null) BGM.Stop();
    }
    #endregion

    #region 音效 SFX 

    public void ChangeSFXVolume(float volume)
    {
        SFXVolume = volume;
        for (int i = 0; i < SFXList.Count; ++i)
        {
            SFXList[i].volume = SFXVolume;
        }
    }

    // 播放音效
    public void PlaySFX(string name, bool isLoop, UnityAction<AudioSource> callBack = null)
    {
        string loadPath = VNProjectConfig.Instance.SFXResPath;
        ResourcesManager.GetInstance().LoadAsync<AudioClip>(loadPath +"/" + name, (clip) =>
        {
            PoolManager.GetInstance().GetObj("VNovelizerRes/VNPrefabs/Gameplay/SoundObj", (obj) =>
            {
                AudioSource source = obj.GetComponent<AudioSource>();

                if (source == null) source = obj.AddComponent<AudioSource>();

                source.clip = clip;
                source.volume = SFXVolume;
                source.loop = isLoop;
                source.Play();

                SFXList.Add(source);

                if (callBack != null)
                {
                    callBack(source);
                }
            });
        });
    }

    // 停止并回收音效
    public void StopSFX(AudioSource source)
    {
        if (source == null)
        {
            Debug.LogWarning("[MusicManager] StopSFX: source 为 null");
            return;
        }

        if (SFXList.Contains(source))
        {
            SFXList.Remove(source);
            try
            {
                source.Stop();
                source.clip = null;

               
                GameObject sourceObj = null;
                try
                {
                    sourceObj = source.gameObject;
                }
                catch (MissingReferenceException)
                {
                    Debug.LogWarning("[MusicManager] StopSFX: 音效对象已被销毁");
                    return;
                }

                if (sourceObj != null)
                {
                    PoolManager.GetInstance().PushObj("Music/SoundObj", sourceObj);
                }
            }
            catch (MissingReferenceException)
            {
                Debug.LogWarning("[MusicManager] StopSFX: 音效对象在操作过程中被销毁");
            }
        }
    }

    // 检测音效是否结束
    private void CheckSFXEnd()
    {
        for (int i = SFXList.Count - 1; i >= 0; --i)
        {
            AudioSource source = SFXList[i];
            
            //检查 AudioSource 和 GameObject 是否已被销毁
            if (source == null)
            {
                SFXList.RemoveAt(i);
                continue;
            }

            //使用 try-catch 来安全地检查 GameObject 是否有效
            GameObject sourceObj = null;
            try
            {
                sourceObj = source.gameObject;
                if (sourceObj == null)
                {
                    SFXList.RemoveAt(i);
                    continue;
                }
            }
            catch (MissingReferenceException)
            {
                // 对象已被销毁
                SFXList.RemoveAt(i);
                continue;
            }

            //检查 AudioSource 是否仍在播放
            bool isPlaying = false;
            try
            {
                isPlaying = source.isPlaying;
            }
            catch (MissingReferenceException)
            {
                // 对象已被销毁
                SFXList.RemoveAt(i);
                continue;
            }

            if (!isPlaying)
            {
                // 停止并回收
                try
                {
                    source.Stop();
                    source.clip = null; // 清理引用，防止内存泄漏

                    // 再次验证对象是否有效，防止已销毁的对象被推回对象池
                    if (source != null && sourceObj != null)
                    {
                        // 还给对象池（PushObj 内部会进行安全检查）
                        PoolManager.GetInstance().PushObj("Music/SoundObj", sourceObj);
                    }
                }
                catch (MissingReferenceException)
                {
                    // 对象在操作过程中被销毁，直接移除即可
                    Debug.LogWarning("[MusicManager] 音效对象在回收过程中被销毁");
                }

                // 从列表中移除
                SFXList.RemoveAt(i);
            }
        }
    }
    
    /// <summary>
    /// 清理所有音效（用于场景切换时）
    /// </summary>
    public void ClearAllSFX()
    {
        for (int i = SFXList.Count - 1; i >= 0; --i)
        {
            if (SFXList[i] != null && SFXList[i].gameObject != null)
            {
                SFXList[i].Stop();
                SFXList[i].clip = null;
                // 不推回对象池，因为场景切换时对象可能已被销毁
            }
        }
        SFXList.Clear();
    }
    #endregion
}