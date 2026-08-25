using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using TMPro;

/// <summary>
/// 音量弹层事件盾：拦截弹层内所有指针事件向 VolumnBtn(Button) 的冒泡。
/// 弹层是 VolumnBtn 的子物体，若不拦截，拖音量滑条时鼠标滑出窄 Handle
/// 到 Background/文本上松手，Down+Up 会被 VolumnBtn 判定为一次点击 → toggle → 弹层误关闭。
/// 挂载后弹层内任何指针事件止步于本组件，不会触发 VolumnBtn.onClick。
/// </summary>
public class VolumePanelRaycastShield : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
{
    public void OnPointerDown(PointerEventData eventData) { }
    public void OnPointerUp(PointerEventData eventData) { }
    public void OnPointerClick(PointerEventData eventData) { }
}

/// <summary>
/// 音乐厅页面
///
/// 控件引用：prevButton/playPauseButton/nextButton/progressSlider/volumeSlider/progressText
/// 由 prefab 序列化连线（GalleryPanel.prefab 中 MusicPage 组件）；
/// MusicName/VolumnBtn/Volumn 弹层/VolumnText 为运行时按名递归查找
/// （"名 - 艺术家"显示与音量弹层为后续新增，prefab 未连线）。
///
/// Volume 弹层交互契约：
/// - 初始收起；单击 VolumnBtn 切换显示/隐藏；
/// - 点击弹层外任意区域收起：Update 轮询 + RaycastAll 穿透式检测
///   （点击会穿透正常触发目标 UI——点 TabBtn 既收弹层又切页）。
///   注意不能在根节点挂全屏透明拦截 Image：MusicPage stretch 全屏且渲染在
///   TabBtns/CloseBtn 之上，会把整个 GalleryPanel 的兄弟 UI 点击全部吞掉；
/// - 弹层内挂事件盾（VolumePanelRaycastShield），拖音量滑条松手不会冒泡成 VolumnBtn 点击；
/// - 音量为音乐厅独立音量，不读写全局 BGM 音量。
/// </summary>
public class MusicPage : MonoBehaviour
{
    // ==================== prefab 序列化连线（勿改字段名） ====================
    [SerializeField] private Button prevButton; // 上一首
    [SerializeField] private Button playPauseButton; // 播放/暂停
    [SerializeField] private Sprite PlayImage;
    [SerializeField] private Sprite PauseImage;
    [SerializeField] private Button nextButton; // 下一首
    [SerializeField] private Slider progressSlider; // 播放进度条
    [SerializeField] private Slider volumeSlider; // 音量滑条（Volumn 弹层内）
    [SerializeField] private TextMeshProUGUI progressText; // 播放进度文本（如：2:45/3:12）

    // ==================== 运行时按名查找（新增控件） ====================
    private TextMeshProUGUI musicNameText;  // 当前播放曲目名（"音乐名 - 艺术家"）
    private Button volumnBtn;               // 音量按钮（切换弹层）
    private GameObject volumnPanel;         // 音量弹层（VolumnBtn 子节点 "Volume"）
    private TextMeshProUGUI volumnText;     // 音量百分比文本（如：85%）

    // 左侧：音乐列表
    private ScrollRect musicListScrollView;
    private Transform musicListContent;
    private GameObject musicSlotPrefab;

    // 右侧：播放控制
    private Image musicPictureImage; // 音乐封面图片

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
    private bool isPaused = false;          // 显式暂停标志：Stop 后 UnPause 无效，恢复时需 Play
    private float currentVolume = 1f;
    private bool isDraggingProgress = false; // 是否正在拖拽进度条（拖拽中只预览文本，松手才 seek）
    private bool isVolumePanelOpen = false;

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

        // 获取右侧播放控制控件（封面 / 曲目名）
        Transform rightPanel = transform.Find("RightPanel");
        if (rightPanel != null)
        {
            Transform pictureTransform = rightPanel.Find("MusicCover");
            if (pictureTransform != null)
            {
                musicPictureImage = pictureTransform.GetComponent<Image>();
            }
        }
        musicNameText = FindDeepText("MusicName");

        // 音量控件（VolumnBtn 及其子节点 "Volume" 弹层）
        volumnBtn = FindDeep("VolumnBtn")?.GetComponent<Button>();
        if (volumnBtn != null)
        {
            volumnPanel = volumnBtn.transform.Find("Volume")?.gameObject;
            if (volumnPanel != null && volumnPanel.GetComponent<VolumePanelRaycastShield>() == null)
            {
                // 事件盾：拦截弹层内指针事件向 VolumnBtn 冒泡，
                // 否则拖音量滑条松手（鼠标滑到 Background/文本上）会被判定为 VolumnBtn 点击而误关弹层
                volumnPanel.AddComponent<VolumePanelRaycastShield>();
            }
        }
        volumnText = FindDeepText("VolumnText");

        // 图标连线校验：两槽连同一张图或漏连时，播放/暂停状态将无法区分
        if (PlayImage == null || PauseImage == null || PlayImage == PauseImage)
        {
            Debug.LogWarning("[MusicPage] PlayImage/PauseImage 未正确连线（为空或相同），播放/暂停图标将无法切换，请检查 GalleryPanel 预制体上的 MusicPage 组件");
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
        if (volumnBtn != null) volumnBtn.onClick.AddListener(OnVolumnButtonClick);

        if (progressSlider != null)
        {
            // 拖拽中仅预览文本，松手（PointerUp）时一次性 seek，
            // 避免旧实现"拖拽期间每帧 audioSource.time = ..."造成的卡顿/滴答声
            progressSlider.onValueChanged.AddListener(OnProgressSliderChanged);

            EventTrigger trigger = progressSlider.gameObject.GetComponent<EventTrigger>();
            if (trigger == null)
            {
                trigger = progressSlider.gameObject.AddComponent<EventTrigger>();
            }

            EventTrigger.Entry pointerDown = new EventTrigger.Entry();
            pointerDown.eventID = EventTriggerType.PointerDown;
            pointerDown.callback.AddListener((data) => { isDraggingProgress = true; });
            trigger.triggers.Add(pointerDown);

            EventTrigger.Entry pointerUp = new EventTrigger.Entry();
            pointerUp.eventID = EventTriggerType.PointerUp;
            pointerUp.callback.AddListener((data) =>
            {
                if (isDraggingProgress)
                {
                    isDraggingProgress = false;
                    SeekToSliderValue();
                }
            });
            trigger.triggers.Add(pointerUp);
        }

        if (volumeSlider != null)
        {
            // 先设置初始值，再绑定事件（避免触发事件）
            volumeSlider.value = currentVolume;
            volumeSlider.onValueChanged.AddListener(OnVolumeSliderChanged);
        }

        // 音量弹层初始收起
        SetVolumePanelOpen(false);

        // 监听音乐解锁事件
        EventCenter.GetInstance().AddEventListener<string>("MusicUnlocked", OnMusicUnlocked);
    }

    /// <summary>按名递归查找后代 Transform（prefab 层级较深时 Find("a/b/c") 不可靠）</summary>
    private Transform FindDeep(string childName)
    {
        return FindDeepRecursive(transform, childName);
    }

    private static Transform FindDeepRecursive(Transform parent, string childName)
    {
        foreach (Transform child in parent)
        {
            if (child.name == childName) return child;
            Transform result = FindDeepRecursive(child, childName);
            if (result != null) return result;
        }
        return null;
    }

    private TextMeshProUGUI FindDeepText(string childName)
    {
        Transform t = FindDeep(childName);
        return t != null ? t.GetComponent<TextMeshProUGUI>() : null;
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
            if (Mathf.Abs(volumeSlider.value - currentVolume) > 0.01f)
            {
                currentVolume = volumeSlider.value;
                audioSource.volume = currentVolume;
            }
            else
            {
                volumeSlider.value = currentVolume;
            }
        }
        UpdateVolumnText();
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
        SetVolumePanelOpen(false);
        gameObject.SetActive(false);
        StopMusic();
        ClearMusicList();
    }

    private void Update()
    {
        // 播放中更新进度（未拖拽时；拖拽中由 OnProgressSliderChanged 预览）
        if (isPlaying && audioSource != null && audioSource.isPlaying && !isDraggingProgress)
        {
            UpdateProgress();
        }

        // 图标每帧跟随音频实际状态：即使 isPlaying 标志与 AudioSource 状态脱节
        // （平台帧延迟、外部暂停等），按钮也能自动纠正，不会出现"音乐在响却显示播放图标"
        SyncPlayPauseButton();

        // 音量弹层打开时检测"点击外部"收起（穿透式，不拦截任何 UI）
        CheckVolumeOutsideClick();
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
        // 模板覆写优先（八、UI 模板覆写），fallback 经资源服务链；键即默认地址
        string path = VNUIPrefabKeys.MusicDataContainer;
        musicDataContainer = VNUIPrefabs.LoadAsset<MusicDataContainer>(VNUIPrefabKeys.MusicDataContainer, VNUIPrefabKeys.MusicDataContainer);

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

        // 加载音乐槽位预制体（模板覆写优先，fallback 经资源服务链；键即默认地址）
        if (musicSlotPrefab == null)
        {
            musicSlotPrefab = VNUIPrefabs.Load(VNUIPrefabKeys.MusicSlot, VNUIPrefabKeys.MusicSlot);
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
        isPaused = false;

        // 更新UI
        UpdateMusicName();
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
        isPaused = false;
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
            isPaused = true;
        }
        else
        {
            // 播放
            if (audioSource.clip == null)
            {
                PlayMusic(currentMusicIndex);
            }
            else if (isPaused)
            {
                // Pause 过 → 恢复
                audioSource.UnPause();
                isPlaying = true;
                isPaused = false;
            }
            else
            {
                // Stop 过（如面板 Hide→Show 后）→ UnPause 对 stopped 源无效，需整体重播
                audioSource.Play();
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

    // ==================== 音量弹层 ====================

    /// <summary>
    /// VolumnBtn 单击：切换音量弹层显示/隐藏
    /// </summary>
    private void OnVolumnButtonClick()
    {
        SetVolumePanelOpen(!isVolumePanelOpen);
    }

    /// <summary>
    /// 点击弹层外部收起（穿透式检测，不拦截、不影响任何 UI 的正常点击）。
    /// 本帧指针按下时 RaycastAll 命中链：命中弹层/VolumnBtn 子树内对象则保持，
    /// 否则收起（点击本身照常穿透触发目标 UI——点 TabBtn 既收弹层又切页）。
    /// </summary>
    private static readonly List<RaycastResult> s_volumeRaycastResults = new List<RaycastResult>();
    private void CheckVolumeOutsideClick()
    {
        if (!isVolumePanelOpen || volumnPanel == null || volumnBtn == null) return;

        Pointer pointer = Pointer.current; // 兼容鼠标与触摸
        if (pointer == null || !pointer.press.wasPressedThisFrame) return;

        EventSystem es = EventSystem.current;
        if (es == null) { SetVolumePanelOpen(false); return; }

        PointerEventData data = new PointerEventData(es);
        data.position = pointer.position.ReadValue();

        s_volumeRaycastResults.Clear();
        es.RaycastAll(data, s_volumeRaycastResults);
        if (s_volumeRaycastResults.Count == 0)
        {
            SetVolumePanelOpen(false); // 点在无 UI 区域
            return;
        }

        Transform panelRoot = volumnPanel.transform;
        Transform btnRoot = volumnBtn.transform;
        for (int i = 0; i < s_volumeRaycastResults.Count; i++)
        {
            Transform hit = s_volumeRaycastResults[i].gameObject.transform;
            if (hit.IsChildOf(panelRoot) || hit.IsChildOf(btnRoot))
            {
                return; // 按在弹层或音量按钮内：保持打开
            }
        }

        SetVolumePanelOpen(false);
    }

    private void SetVolumePanelOpen(bool open)
    {
        isVolumePanelOpen = open;
        if (volumnPanel != null && volumnPanel.activeSelf != open)
        {
            volumnPanel.SetActive(open);
        }
        if (open)
        {
            // 打开时同步滑块与文本到当前音量
            if (volumeSlider != null) volumeSlider.value = currentVolume;
            UpdateVolumnText();
        }
    }

    /// <summary>
    /// 音量条值改变（音乐厅独立音量，不读写全局 BGM 音量）
    /// </summary>
    private void OnVolumeSliderChanged(float value)
    {
        currentVolume = Mathf.Clamp01(value);
        if (audioSource != null)
        {
            audioSource.volume = currentVolume;
        }
        UpdateVolumnText();
    }

    /// <summary>
    /// 更新音量百分比文本（如：85%）
    /// </summary>
    private void UpdateVolumnText()
    {
        if (volumnText != null)
        {
            volumnText.text = $"{Mathf.RoundToInt(currentVolume * 100)}%";
        }
    }

    // ==================== 进度条 ====================

    /// <summary>
    /// 进度条值改变：拖拽中仅预览时间文本，不执行 seek
    /// （实际 seek 在 PointerUp 的 SeekToSliderValue 中一次性完成）
    /// </summary>
    private void OnProgressSliderChanged(float value)
    {
        if (!isDraggingProgress) return; // 非用户拖拽（代码回写）忽略，避免与 Update 循环打架

        if (audioSource != null && audioSource.clip != null)
        {
            PreviewProgressText(value * audioSource.clip.length, audioSource.clip.length);
        }
    }

    /// <summary>
    /// 拖拽结束：按滑块当前值一次性 seek
    /// </summary>
    private void SeekToSliderValue()
    {
        if (audioSource == null || audioSource.clip == null || progressSlider == null) return;
        audioSource.time = progressSlider.value * audioSource.clip.length;
        UpdateProgress();
    }

    // ==================== 显示更新 ====================

    /// <summary>
    /// 更新当前曲目名（"音乐名 - 艺术家"；艺术家留空只显示音乐名）
    /// </summary>
    private void UpdateMusicName()
    {
        if (musicNameText != null && currentMusic != null)
        {
            musicNameText.text = currentMusic.DisplayName;
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
    /// 更新播放/暂停按钮（依据 isPlaying 标志，事件回调时立即刷新）
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
    }

    /// <summary>
    /// 每帧按 AudioSource 实际播放状态同步按钮图标（带缓存，状态未变不重复赋值）。
    /// 兜底 isPlaying 标志与音频实际状态的任何脱节（平台帧延迟、外部干预等）。
    /// </summary>
    private bool lastSyncedPlayingState = false;
    private void SyncPlayPauseButton()
    {
        if (playPauseButton == null || audioSource == null) return;

        bool actuallyPlaying = audioSource.isPlaying;
        if (actuallyPlaying != lastSyncedPlayingState)
        {
            lastSyncedPlayingState = actuallyPlaying;
            isPlaying = actuallyPlaying;
            if (!actuallyPlaying) isPaused = audioSource.clip != null; // 有 clip 且未在播 → 处于暂停可恢复态
            UpdatePlayPauseButton();
        }
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
        PreviewProgressText(audioSource.time, audioSource.clip.length);
    }

    /// <summary>按给定时间对（秒）刷新进度文本</summary>
    private void PreviewProgressText(float currentSec, float totalSec)
    {
        if (progressText == null) return;
        progressText.text = $"{FormatTime(Mathf.FloorToInt(currentSec))}/{FormatTime(Mathf.FloorToInt(totalSec))}";
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
