using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// 音乐厅页面
/// </summary>
public class MusicPage : MonoBehaviour
{
    // 左侧：音乐列表
    private ScrollRect musicListScrollView;
    private Transform musicListContent;
    private GameObject musicSlotPrefab;

    // 右侧：播放控制
                     private Image musicPictureImage; // 音乐封面图片
    [SerializeField] private Button prevButton; // 上一首
    [SerializeField] private Button playPauseButton; // 播放/暂停
    [SerializeField] private Sprite PlayImage;
    [SerializeField] private Sprite PauseImage;
    [SerializeField] private Button nextButton; // 下一首
    [SerializeField] private Slider progressSlider; // 播放进度条
    [SerializeField] private Slider volumeSlider; // 音量进度条
    [SerializeField] private TextMeshProUGUI progressText; // 播放进度文本（如：2:45/3:12）
    
    // 数据
    private MusicDataContainer musicDataContainer;
    private GlobalData globalData;
    private List<VNMusic> allMusicData = new List<VNMusic>();
    private List<MusicSlot> musicSlots = new List<MusicSlot>();
    
    // 播放状态
    private AudioSource audioSource;
    private VNMusic currentMusic;
    private int currentMusicIndex = -1;
    private bool isPlaying = false;
    private float currentVolume = 1f;
    private bool isDraggingProgress = false; // 是否正在拖拽进度条
    
    private void Awake()
    {
        // 获取左侧音乐列表控件
        Transform musicListTransform = transform.Find("MusicList");
        if (musicListTransform != null)
        {
            musicListScrollView = musicListTransform.GetComponent<ScrollRect>();
            if (musicListScrollView != null)
            {
                musicListContent = musicListScrollView.content;
            }
        }
        
        // 获取右侧播放控制控件
        Transform rightPanel = transform.Find("RightPanel");
        if (rightPanel != null)
        {
            // 封面图片
            Transform pictureTransform = rightPanel.Find("MusicCover");
            if (pictureTransform != null)
            {
                musicPictureImage = pictureTransform.GetComponent<Image>();
            }
            
            // 控制按钮
            Transform controlsTransform = rightPanel.Find("Controls");
            if (controlsTransform != null)
            {
                prevButton = controlsTransform.Find("M_PrevBtn")?.GetComponent<Button>();
                playPauseButton = controlsTransform.Find("PlayPauseBtn")?.GetComponent<Button>();
                nextButton = controlsTransform.Find("M_NextBtn")?.GetComponent<Button>();
            }
            
            // 进度条
            Transform progressTransform = rightPanel.Find("Progress");
            if (progressTransform != null)
            {
                progressSlider = progressTransform.Find("ProgressSlider")?.GetComponent<Slider>();
                progressText = progressTransform.Find("ProgressText")?.GetComponent<TextMeshProUGUI>();
            }
            
            // 音量控制
            Transform volumeTransform = rightPanel.Find("Volume");
            if (volumeTransform != null)
            {
                volumeSlider = volumeTransform.Find("VolumeSlider")?.GetComponent<Slider>();
                if (volumeSlider == null)
                {
                    // 尝试直接在Volume对象上获取Slider组件
                    volumeSlider = volumeTransform.GetComponent<Slider>();
                }
            }
        }
        
        // 创建AudioSource用于播放音乐
        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.loop = true; // 循环播放
        audioSource.playOnAwake = false;
        audioSource.volume = currentVolume; // 初始化音量
        
        // 绑定事件
        if (prevButton != null) prevButton.onClick.AddListener(OnPrevButtonClick);
        if (playPauseButton != null) playPauseButton.onClick.AddListener(OnPlayPauseButtonClick);
        if (nextButton != null) nextButton.onClick.AddListener(OnNextButtonClick);
        
        if (progressSlider != null)
        {
            progressSlider.onValueChanged.AddListener(OnProgressSliderChanged);
            
            // 添加拖拽开始和结束事件
            EventTrigger trigger = progressSlider.gameObject.GetComponent<EventTrigger>();
            if (trigger == null)
            {
                trigger = progressSlider.gameObject.AddComponent<EventTrigger>();
            }
            
            // 拖拽开始
            EventTrigger.Entry pointerDown = new EventTrigger.Entry();
            pointerDown.eventID = EventTriggerType.PointerDown;
            pointerDown.callback.AddListener((data) => { isDraggingProgress = true; });
            trigger.triggers.Add(pointerDown);
            
            // 拖拽结束
            EventTrigger.Entry pointerUp = new EventTrigger.Entry();
            pointerUp.eventID = EventTriggerType.PointerUp;
            pointerUp.callback.AddListener((data) => { isDraggingProgress = false; });
            trigger.triggers.Add(pointerUp);
        }
        
        if (volumeSlider != null)
        {
            // 先设置初始值，再绑定事件（避免触发事件）
            volumeSlider.value = currentVolume;
            volumeSlider.onValueChanged.AddListener(OnVolumeSliderChanged);
        }
        
        // 监听音乐解锁事件
        EventCenter.GetInstance().AddEventListener<string>("MusicUnlocked", OnMusicUnlocked);
    }
    
    /// <summary>
    /// 初始化音乐页面
    /// </summary>
    public void Initialize()
    {
        // 加载全局数据
        globalData = GlobalDataManager.GetInstance().GetGlobalData();
        
        // 加载音乐数据容器
        LoadMusicDataContainer();
        
        // 加载音乐列表
        LoadMusicList();
        
        // 确保音量滑块和AudioSource同步
        if (volumeSlider != null && audioSource != null)
        {
            // 如果VolumeSlider的值不是currentVolume，同步它
            if (Mathf.Abs(volumeSlider.value - currentVolume) > 0.01f)
            {
                currentVolume = volumeSlider.value;
                audioSource.volume = currentVolume;
            }
            else
            {
                // 否则，用currentVolume更新VolumeSlider
                volumeSlider.value = currentVolume;
            }
        }
    }
    
    /// <summary>
    /// 显示音乐页面
    /// </summary>
    public void Show()
    {
        gameObject.SetActive(true);
        Initialize();
    }
    
    /// <summary>
    /// 隐藏音乐页面
    /// </summary>
    public void Hide()
    {
        gameObject.SetActive(false);
        StopMusic();
        ClearMusicList();
    }
    
    private void Update()
    {
        // 更新播放进度（只有在未拖拽时才更新）
        if (isPlaying && audioSource != null && audioSource.isPlaying && !isDraggingProgress)
        {
            UpdateProgress();
        }
    }
    
    private void OnDestroy()
    {
        // 移除事件监听
        EventCenter.GetInstance().RemoveEventListener<string>("MusicUnlocked", OnMusicUnlocked);
        
        // 停止播放
        StopMusic();
    }
    
    /// <summary>
    /// 加载音乐数据容器
    /// </summary>
    private void LoadMusicDataContainer()
    {
        string path = VNProjectConfig.Instance.Music_DataPath + "/MusicDataContainer";
        musicDataContainer = ResourcesManager.GetInstance().Load<MusicDataContainer>(path);
        
        if (musicDataContainer == null)
        {
            Debug.LogWarning($"[MusicPage] 未找到音乐数据容器: {path}");
            allMusicData = new List<VNMusic>();
        }
        else
        {
            allMusicData = new List<VNMusic>(musicDataContainer.musicList);
        }
    }
    
    /// <summary>
    /// 加载音乐列表
    /// </summary>
    private void LoadMusicList()
    {
        ClearMusicList();
        
        if (musicListContent == null)
        {
            Debug.LogError("[MusicPage] 音乐列表内容容器未找到");
            return;
        }
        
        // 加载音乐槽位预制体
        if (musicSlotPrefab == null)
        {
            string loadPath = VNProjectConfig.Instance.UI_GalleryPath + "/Music";
            musicSlotPrefab = ResourcesManager.GetInstance().Load<GameObject>(loadPath + "/MusicSlot");
        }
        
        if (musicSlotPrefab == null)
        {
            Debug.LogError("[MusicPage] 音乐槽位预制体未找到");
            return;
        }
        
        // 创建音乐槽位
        for (int i = 0; i < allMusicData.Count; i++)
        {
            VNMusic music = allMusicData[i];
            if (music != null)
            {
                CreateMusicSlot(music, i);
            }
        }
    }
    
    /// <summary>
    /// 创建音乐槽位
    /// </summary>
    private void CreateMusicSlot(VNMusic music, int index)
    {
        if (musicSlotPrefab == null || musicListContent == null) return;
        
        if (music == null)
        {
            Debug.LogWarning("[MusicPage] VNMusic为null，跳过创建槽位");
            return;
        }
        
        GameObject slotObj = Instantiate(musicSlotPrefab, musicListContent);
        
        // 确保RectTransform设置正确（用于布局）
        RectTransform slotRect = slotObj.GetComponent<RectTransform>();
        if (slotRect != null)
        {
            // 重置变换属性
            slotRect.localScale = Vector3.one;
            slotRect.localRotation = Quaternion.identity;
            
            // 如果Content没有布局组件，手动设置位置
            if (musicListContent.GetComponent<VerticalLayoutGroup>() == null && 
                musicListContent.GetComponent<GridLayoutGroup>() == null &&
                musicListContent.GetComponent<HorizontalLayoutGroup>() == null)
            {
                // 没有布局组件，需要手动设置位置
                slotRect.anchoredPosition = new Vector2(0, -index * 50); // 假设每个slot高度为50
            }
        }
        
        MusicSlot slot = slotObj.GetComponent<MusicSlot>();
        if (slot == null)
        {
            slot = slotObj.AddComponent<MusicSlot>();
        }
        
        // 检查是否已解锁
        bool isUnlocked = false;
        if (globalData != null && globalData.UnlockedMusic != null && !string.IsNullOrEmpty(music.name))
        {
            isUnlocked = globalData.UnlockedMusic.Contains(music.name);
        }
        
        // 同步编辑器中的调试设置
        if (music.isUnlocked && !isUnlocked && globalData != null && globalData.UnlockedMusic != null)
        {
            if (!string.IsNullOrEmpty(music.name))
            {
                globalData.UnlockedMusic.Add(music.name);
                isUnlocked = true;
                GlobalDataManager.GetInstance().UnlockMusic(music.name); // 这会保存到文件
                Debug.Log($"[MusicPage] 同步音乐解锁状态: {music.name}");
            }
        }
        
        // 初始化音乐槽位
        slot.Init(music, isUnlocked, OnMusicSlotClick);
        
        musicSlots.Add(slot);
    }
    
    /// <summary>
    /// 音乐槽位点击事件
    /// </summary>
    private void OnMusicSlotClick(VNMusic music)
    {
        if (music == null || music.music == null)
        {
            Debug.LogWarning("[MusicPage] 音乐数据或音频文件为null");
            return;
        }
        
        // 查找音乐索引
        int index = allMusicData.IndexOf(music);
        if (index >= 0)
        {
            PlayMusic(index);
        }
    }
    
    /// <summary>
    /// 播放音乐
    /// </summary>
    private void PlayMusic(int index)
    {
        if (index < 0 || index >= allMusicData.Count) return;
        
        currentMusic = allMusicData[index];
        currentMusicIndex = index;
        
        if (currentMusic == null || currentMusic.music == null)
        {
            Debug.LogWarning("[MusicPage] 音乐数据或音频文件为null");
            return;
        }
        
        // 停止当前播放
        StopMusic();
        
        // 设置音频
        audioSource.clip = currentMusic.music;
        audioSource.volume = currentVolume;
        audioSource.Play();
        
        isPlaying = true;
        
        // 更新UI
        UpdateMusicPicture();
        UpdatePlayPauseButton();
        UpdateProgress();
    }
    
    /// <summary>
    /// 停止播放
    /// </summary>
    private void StopMusic()
    {
        if (audioSource != null && audioSource.isPlaying)
        {
            audioSource.Stop();
        }
        isPlaying = false;
        UpdatePlayPauseButton();
    }
    
    /// <summary>
    /// 上一首
    /// </summary>
    private void OnPrevButtonClick()
    {
        if (allMusicData.Count == 0) return;
        
        if (currentMusicIndex < 0)
        {
            currentMusicIndex = allMusicData.Count - 1;
        }
        else
        {
            currentMusicIndex = (currentMusicIndex - 1 + allMusicData.Count) % allMusicData.Count;
        }
        
        PlayMusic(currentMusicIndex);
    }
    
    /// <summary>
    /// 播放/暂停
    /// </summary>
    private void OnPlayPauseButtonClick()
    {
        if (currentMusic == null || currentMusic.music == null)
        {
            // 如果没有选中音乐，播放第一首
            if (allMusicData.Count > 0)
            {
                PlayMusic(0);
            }
            return;
        }
        
        if (isPlaying && audioSource.isPlaying)
        {

            audioSource.Pause();
           
            isPlaying = false;
        }
        else
        {
            // 播放
            if (audioSource.clip == null)
            {
                PlayMusic(currentMusicIndex);
            }
            else
            {
                audioSource.UnPause();
                isPlaying = true;
            }
        }
        
        UpdatePlayPauseButton();
    }
    
    /// <summary>
    /// 下一首
    /// </summary>
    private void OnNextButtonClick()
    {
        if (allMusicData.Count == 0) return;
        
        if (currentMusicIndex < 0)
        {
            currentMusicIndex = 0;
        }
        else
        {
            currentMusicIndex = (currentMusicIndex + 1) % allMusicData.Count;
        }
        
        PlayMusic(currentMusicIndex);
    }
    
    /// <summary>
    /// 进度条值改变
    /// </summary>
    private void OnProgressSliderChanged(float value)
    {
        if (audioSource != null && audioSource.clip != null)
        {
            // 无论是拖拽还是点击，都更新播放位置
            audioSource.time = value * audioSource.clip.length;
            UpdateProgressText();
        }
    }
    
    /// <summary>
    /// 音量条值改变
    /// </summary>
    private void OnVolumeSliderChanged(float value)
    {
        currentVolume = Mathf.Clamp01(value); // 确保值在0-1范围内
        if (audioSource != null)
        {
            audioSource.volume = currentVolume;
        }
    }
    
    /// <summary>
    /// 更新音乐封面图片
    /// </summary>
    private void UpdateMusicPicture()
    {
        if (musicPictureImage != null && currentMusic != null)
        {
            musicPictureImage.sprite = currentMusic.picture;
            musicPictureImage.color = currentMusic.picture != null ? Color.white : new Color(0.3f, 0.3f, 0.3f, 1f);
        }
    }
    
    /// <summary>
    /// 更新播放/暂停按钮
    /// </summary>
    private void UpdatePlayPauseButton()
    {
        if (playPauseButton == null) return;

        if (isPlaying)
        {
            playPauseButton.image.sprite = PauseImage;
        }
        else
        {
            playPauseButton.image.sprite = PlayImage;
        }
        // 可以在这里更新按钮的图标或文本
        // 例如：TextMeshProUGUI buttonText = playPauseButton.GetComponentInChildren<TextMeshProUGUI>();
        // if (buttonText != null) buttonText.text = isPlaying ? "暂停" : "播放";
    }
    
    /// <summary>
    /// 更新播放进度
    /// </summary>
    private void UpdateProgress()
    {
        if (audioSource == null || audioSource.clip == null) return;
        
        // 更新进度条
        if (progressSlider != null)
        {
            float progress = audioSource.time / audioSource.clip.length;
            progressSlider.value = progress;
        }
        
        // 更新进度文本
        UpdateProgressText();
    }
    
    /// <summary>
    /// 更新进度文本
    /// </summary>
    private void UpdateProgressText()
    {
        if (progressText == null || audioSource == null || audioSource.clip == null) return;
        
        int currentSeconds = Mathf.FloorToInt(audioSource.time);
        int totalSeconds = Mathf.FloorToInt(audioSource.clip.length);
        
        string currentTime = FormatTime(currentSeconds);
        string totalTime = FormatTime(totalSeconds);
        
        progressText.text = $"{currentTime}/{totalTime}";
    }
    
    /// <summary>
    /// 格式化时间（秒转分:秒）
    /// </summary>
    private string FormatTime(int seconds)
    {
        int minutes = seconds / 60;
        int secs = seconds % 60;
        return $"{minutes}:{secs:D2}";
    }
    
    /// <summary>
    /// 清理音乐列表
    /// </summary>
    private void ClearMusicList()
    {
        if (musicListContent != null)
        {
            foreach (Transform child in musicListContent)
            {
                Destroy(child.gameObject);
            }
        }
        musicSlots.Clear();
    }
    
    /// <summary>
    /// 音乐解锁事件处理
    /// </summary>
    private void OnMusicUnlocked(string musicName)
    {
        // 更新音乐槽位状态
        foreach (MusicSlot slot in musicSlots)
        {
            if (slot != null && slot.musicData != null && slot.musicData.name == musicName)
            {
                slot.Unlock();
                break;
            }
        }
    }
}

