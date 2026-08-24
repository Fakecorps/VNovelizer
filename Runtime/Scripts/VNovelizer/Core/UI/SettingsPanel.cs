using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 游戏设置面板
/// </summary>
public class SettingsPanel : BasePanel
{
    [Header("设置项开关（可在Inspector中配置）")]
    [Tooltip("是否启用主音量设置")]
    public bool enableMasterVolume = true;
    [Tooltip("是否启用BGM音量设置")]
    public bool enableBGMVolume = true;
    [Tooltip("是否启用音效音量设置")]
    public bool enableSFXVolume = true;
    [Tooltip("是否启用语音音量设置")]
    public bool enableVoiceVolume = true;
    [Tooltip("是否启用文字显示速度设置")]
    public bool enableTextSpeed = true;
    [Tooltip("是否启用自动播放间隔设置")]
    public bool enableAutoSpeed = true;
    [Tooltip("是否启用显示模式设置")]
    public bool enableDisplayMode = true;
    [Tooltip("是否启用分辨率设置")]
    public bool enableResolution = true;

    [Header("UI控件引用")]
    // （已移除分页 TabsBtn：所有设置项平铺在同一页；
    //   VolumePage/TextPage/DisplayPage 容器若在 prefab 中保留则由 ResolveGroupRoot 强制显示，删除则全局查找）

    // 自动播放间隔范围与精度：0.1s ~ 5s，步进 0.1s（显示保留一位小数）
    private const float AutoSpeedMin = 0.1f;
    private const float AutoSpeedMax = 5f;
    private const float AutoSpeedStep = 0.1f;

    // 音频设置（在VolumePage内）
    [SerializeField] private GameObject masterVolumeGroup;
    private Slider masterVolumeSlider;
    private TextMeshProUGUI masterVolumeText;

    [SerializeField] private GameObject bgmVolumeGroup;
    private Slider bgmVolumeSlider;
    private TextMeshProUGUI bgmVolumeText;

    [SerializeField] private GameObject voiceVolumeGroup;
    private Slider voiceVolumeSlider;
    private TextMeshProUGUI voiceVolumeText;

    [SerializeField] private GameObject sfxVolumeGroup;
    private Slider sfxVolumeSlider;
    private TextMeshProUGUI sfxVolumeText;

    // 文本设置（在TextPage内）
    [SerializeField] private GameObject textSpeedGroup;
    private Slider textSpeedSlider;
    private TextMeshProUGUI textSpeedText;

    [SerializeField] private GameObject autoSpeedGroup;
    private Slider autoSpeedSlider;
    private TextMeshProUGUI autoSpeedText;

    // 显示设置（在DisplayPage内）
    [SerializeField] private GameObject displayModeGroup;
    private TMP_Dropdown displayModeDropdown;

    [SerializeField] private GameObject resolutionGroup;
    private TMP_Dropdown resolutionDropdown;
    
    // 按钮
    private Button closeBtn;
    
    private GlobalData globalData;
    private List<Resolution> availableResolutions = new List<Resolution>();
    
    protected override void Awake()
    {
        base.Awake();
        
        // 初始化控件（使用GetControl获取，如果不存在则返回null）
        InitializeControls();
        
        // 获取可用分辨率
        GetAvailableResolutions();
        
        // 绑定事件
        BindEvents();
    }

    /// <summary>
    /// 初始化控件
    /// </summary>
    private void InitializeControls()
    {
        // （已移除分页 TabsBtn 查找）

        // 查找页面/分组容器（单页布局：容器存在则强制显示；prefab 已删除容器时返回面板根，group 直接全局查找）
        Transform volumeRoot = ResolveGroupRoot("VolumePage");
        Transform textRoot = ResolveGroupRoot("TextPage");
        Transform displayRoot = ResolveGroupRoot("DisplayPage");

        // 音频设置组
        {
            masterVolumeGroup = FindControlGroupInParent(volumeRoot, "MasterVolumeGroup");
            if (masterVolumeGroup != null)
            {
                masterVolumeSlider = masterVolumeGroup.GetComponentInChildren<Slider>();
                masterVolumeText = FindValueText(masterVolumeGroup.transform);
            }

            bgmVolumeGroup = FindControlGroupInParent(volumeRoot, "BGMVolumeGroup");
            if (bgmVolumeGroup != null)
            {
                bgmVolumeSlider = bgmVolumeGroup.GetComponentInChildren<Slider>();
                bgmVolumeText = FindValueText(bgmVolumeGroup.transform);
            }

            voiceVolumeGroup = FindControlGroupInParent(volumeRoot, "VoiceVolumeGroup");
            if (voiceVolumeGroup != null)
            {
                voiceVolumeSlider = voiceVolumeGroup.GetComponentInChildren<Slider>();
                voiceVolumeText = FindValueText(voiceVolumeGroup.transform);
            }

            sfxVolumeGroup = FindControlGroupInParent(volumeRoot, "SFXVolumeGroup");
            if (sfxVolumeGroup != null)
            {
                sfxVolumeSlider = sfxVolumeGroup.GetComponentInChildren<Slider>();
                sfxVolumeText = FindValueText(sfxVolumeGroup.transform);
            }
        }

        // 文本设置组
        {
            textSpeedGroup = FindControlGroupInParent(textRoot, "TextSpeedGroup");
            if (textSpeedGroup != null)
            {
                textSpeedSlider = textSpeedGroup.GetComponentInChildren<Slider>();
                textSpeedText = FindValueText(textSpeedGroup.transform);
            }

            autoSpeedGroup = FindControlGroupInParent(textRoot, "AutoSpeedGroup");
            if (autoSpeedGroup != null)
            {
                autoSpeedSlider = autoSpeedGroup.GetComponentInChildren<Slider>();
                autoSpeedText = FindValueText(autoSpeedGroup.transform);

                // 强制约束范围 0.1~5s（覆盖 prefab 序列化值，保证与需求一致）
                if (autoSpeedSlider != null)
                {
                    autoSpeedSlider.minValue = AutoSpeedMin;
                    autoSpeedSlider.maxValue = AutoSpeedMax;
                    autoSpeedSlider.wholeNumbers = false;
                }
            }
        }

        // 显示设置组
        {
            displayModeGroup = FindControlGroupInParent(displayRoot, "DisplayModeGroup");
            if (displayModeGroup != null)
            {
                displayModeDropdown = displayModeGroup.GetComponentInChildren<TMP_Dropdown>();
            }

            resolutionGroup = FindControlGroupInParent(displayRoot, "ResolutionGroup");
            if (resolutionGroup != null)
            {
                resolutionDropdown = resolutionGroup.GetComponentInChildren<TMP_Dropdown>();
            }
        }

        // 关闭按钮
        closeBtn = GetControl<Button>("CloseBtn");
    }

    /// <summary>
    /// 解析分组查找根节点：
    /// 单页布局下 Page 容器若仍存在则作为查找根并强制显示（保持层级整洁）；
    /// 若 prefab 中已删除 Page 容器（内容直接平铺），则回落到面板根做全局递归查找。
    /// </summary>
    private Transform ResolveGroupRoot(string pageName)
    {
        GameObject page = FindControlGroup(pageName);
        if (page == null) return transform;
        page.SetActive(true);
        return page.transform;
    }
    
    /// <summary>
    /// 在指定父对象内查找控件组
    /// </summary>
    private GameObject FindControlGroupInParent(Transform parent, string name)
    {
        Transform found = parent.Find(name);
        if (found != null) return found.gameObject;
        
        found = FindInChildren(parent, name);
        if (found != null) return found.gameObject;
        
        return null;
    }
    
    /// <summary>
    /// 查找ValueText组件（优先查找名为ValueText的对象，否则查找非Label的TextMeshProUGUI）
    /// </summary>
    private TextMeshProUGUI FindValueText(Transform parent)
    {
        // 方式1：优先查找名为"ValueText"的对象
        Transform valueTextTransform = parent.Find("ValueText");
        if (valueTextTransform != null)
        {
            TextMeshProUGUI text = valueTextTransform.GetComponent<TextMeshProUGUI>();
            if (text != null) return text;
        }
        
        // 方式2：查找所有TextMeshProUGUI，排除名为"Label"的，返回第一个非Label的
        TextMeshProUGUI[] allTexts = parent.GetComponentsInChildren<TextMeshProUGUI>();
        if (allTexts != null && allTexts.Length > 0)
        {
            foreach (TextMeshProUGUI text in allTexts)
            {
                // 排除名为"Label"的TextMeshProUGUI
                if (text != null && text.name != "Label" && !text.name.ToLower().Contains("label"))
                {
                    return text;
                }
            }
            // 如果所有都是Label，返回最后一个
            return allTexts[allTexts.Length - 1];
        }
        
        return null;
    }
    
    /// <summary>
    /// 查找控件组（支持多种查找方式）
    /// </summary>
    private GameObject FindControlGroup(string name)
    {
        // 方式1：直接查找子对象
        Transform found = transform.Find(name);
        if (found != null) return found.gameObject;
        
        // 方式2：递归查找
        found = FindInChildren(transform, name);
        if (found != null) return found.gameObject;
        
        return null;
    }
    
    /// <summary>
    /// 递归查找子对象
    /// </summary>
    private Transform FindInChildren(Transform parent, string name)
    {
        foreach (Transform child in parent)
        {
            if (child.name == name) return child;
            Transform found = FindInChildren(child, name);
            if (found != null) return found;
        }
        return null;
    }
    
    /// <summary>
    /// 获取可用分辨率
    /// </summary>
    private void GetAvailableResolutions()
    {
        availableResolutions.Clear();
        Resolution[] resolutions = Screen.resolutions;
        
        // 去重（相同宽高的分辨率只保留一个）
        Dictionary<string, Resolution> uniqueResolutions = new Dictionary<string, Resolution>();
        foreach (Resolution res in resolutions)
        {
            string key = $"{res.width}x{res.height}";
            if (!uniqueResolutions.ContainsKey(key))
            {
                uniqueResolutions[key] = res;
                availableResolutions.Add(res);
            }
        }
        
        // 按宽度和高度排序（从大到小）
        availableResolutions.Sort((a, b) => 
        {
            if (a.width != b.width) return b.width.CompareTo(a.width);
            return b.height.CompareTo(a.height);
        });
    }
    
    /// <summary>
    /// 绑定事件
    /// </summary>
    private void BindEvents()
    {
        // （已移除分页按钮事件）

        // 设置项事件
        if (masterVolumeSlider != null)
            masterVolumeSlider.onValueChanged.AddListener(OnMasterVolumeChanged);
        if (bgmVolumeSlider != null)
            bgmVolumeSlider.onValueChanged.AddListener(OnBGMVolumeChanged);
        if (voiceVolumeSlider != null)
            voiceVolumeSlider.onValueChanged.AddListener(OnVoiceVolumeChanged);
        if (sfxVolumeSlider != null)
            sfxVolumeSlider.onValueChanged.AddListener(OnSFXVolumeChanged);
        if (textSpeedSlider != null)
            textSpeedSlider.onValueChanged.AddListener(OnTextSpeedChanged);
        if (autoSpeedSlider != null)
            autoSpeedSlider.onValueChanged.AddListener(OnAutoSpeedChanged);
        if (displayModeDropdown != null)
            displayModeDropdown.onValueChanged.AddListener(OnDisplayModeChanged);
        if (resolutionDropdown != null)
            resolutionDropdown.onValueChanged.AddListener(OnResolutionChanged);
        if (closeBtn != null)
            closeBtn.onClick.AddListener(OnCloseBtnClick);
    }
    
    /// <summary>
    /// 解绑事件（用于在更新UI值时避免触发事件）
    /// </summary>
    private void UnbindEvents()
    {
        // 分页按钮不需要解绑（它们不会在设置值时触发）
        
        // 设置控件事件
        if (masterVolumeSlider != null)
            masterVolumeSlider.onValueChanged.RemoveListener(OnMasterVolumeChanged);
        if (bgmVolumeSlider != null)
            bgmVolumeSlider.onValueChanged.RemoveListener(OnBGMVolumeChanged);
        if (voiceVolumeSlider != null)
            voiceVolumeSlider.onValueChanged.RemoveListener(OnVoiceVolumeChanged);
        if (sfxVolumeSlider != null)
            sfxVolumeSlider.onValueChanged.RemoveListener(OnSFXVolumeChanged);
        if (textSpeedSlider != null)
            textSpeedSlider.onValueChanged.RemoveListener(OnTextSpeedChanged);
        if (autoSpeedSlider != null)
            autoSpeedSlider.onValueChanged.RemoveListener(OnAutoSpeedChanged);
        if (displayModeDropdown != null)
            displayModeDropdown.onValueChanged.RemoveListener(OnDisplayModeChanged);
        if (resolutionDropdown != null)
            resolutionDropdown.onValueChanged.RemoveListener(OnResolutionChanged);
        // closeBtn不需要解绑（它不会在设置值时触发）
    }
    
    protected override void OnEnable()
    {
        base.OnEnable();
        
        // 确保globalData已初始化
        if (globalData == null)
        {
            globalData = GlobalDataManager.GetInstance().GetGlobalData();
        }
        
        // 每次打开面板时设置状态
        // 检查是否可以打开（如果当前状态是其他面板状态，则不允许）
        // 【修改】允许从Pause状态打开（包括从Choice状态通过Pause打开的情况）
        GameState currentState = GameStateManager.GetInstance().CurrentState;
        if (currentState == GameState.History || currentState == GameState.SaveLoad || 
            currentState == GameState.System)
        {
            Debug.LogWarning($"[SettingsPanel] 当前状态 {currentState} 不允许打开设置面板，已关闭");
            gameObject.SetActive(false);
            return;
        }
        
        // 如果已经在Settings状态，不需要重复设置
        if (currentState != GameState.Settings)
        {
            // 如果当前状态是Pause，使用PushState（嵌套状态）
            // 否则使用SetState（普通状态切换）
            if (currentState == GameState.Pause)
            {
                GameStateManager.GetInstance().PushState(GameState.Settings);
            }
            else
            {
                GameStateManager.GetInstance().SetState(GameState.Settings);
            }
        }
        
        // 确保UI值已更新（如果ShowMe没有被调用，这里会更新）
        // 使用协程延迟一帧，确保所有组件都已初始化
        StartCoroutine(DelayedUpdateUI());
    }
    
    /// <summary>
    /// 延迟更新UI（确保所有组件都已初始化）
    /// </summary>
    private System.Collections.IEnumerator DelayedUpdateUI()
    {
        yield return null; // 等待一帧
        
        // 如果globalData已加载，更新UI
        if (globalData != null)
        {
            UpdateUIFromGlobalData();
        }
    }

    public override void ShowMe()
    {
        gameObject.SetActive(true);

        // 加载全局数据（确保从保存的文件中加载）
        globalData = GlobalDataManager.GetInstance().GetGlobalData();
        if (globalData == null)
        {
            Debug.LogError("[SettingsPanel] 无法获取GlobalData，请确保GlobalDataManager已初始化");
            return;
        }

        // （单页布局：无页面切换）

        // 更新UI显示状态（根据bool开关）
        UpdateUIVisibility();

        // 更新UI值（在设置值前临时移除事件监听，避免触发事件）
        UpdateUIFromGlobalData();
    }
    
    public override void HideMe()
    {
        gameObject.SetActive(false);
        
        // 保存设置（已经在每次修改时即时保存，这里可以再次确保保存）
        if (globalData != null)
        {
            GlobalDataManager.GetInstance().UpdateVolumeSettings(
                globalData.MasterVolume,
                globalData.BGMVolume,
                globalData.VoiceVolume,
                globalData.SFXVolume
            );
        }
    }
    
    /// <summary>
    /// 根据bool开关更新UI显示状态（单页布局：统一处理所有分组）
    /// </summary>
    private void UpdateUIVisibility()
    {
        // 音频
        if (masterVolumeGroup != null)
            masterVolumeGroup.SetActive(enableMasterVolume);
        if (bgmVolumeGroup != null)
            bgmVolumeGroup.SetActive(enableBGMVolume);
        if (voiceVolumeGroup != null)
            voiceVolumeGroup.SetActive(enableVoiceVolume);
        if (sfxVolumeGroup != null)
            sfxVolumeGroup.SetActive(enableSFXVolume);

        // 文本
        if (textSpeedGroup != null)
            textSpeedGroup.SetActive(enableTextSpeed);
        if (autoSpeedGroup != null)
            autoSpeedGroup.SetActive(enableAutoSpeed);

        // 显示
        if (displayModeGroup != null)
            displayModeGroup.SetActive(enableDisplayMode);
        if (resolutionGroup != null)
            resolutionGroup.SetActive(enableResolution);
    }
    
    /// <summary>
    /// 从全局数据更新UI
    /// </summary>
    private void UpdateUIFromGlobalData()
    {
        if (globalData == null)
        {
            globalData = GlobalDataManager.GetInstance().GetGlobalData();
            if (globalData == null)
            {
                Debug.LogError("[SettingsPanel] 无法获取GlobalData，UI更新失败。请确保GlobalDataManager已初始化。");
                return;
            }
        }
        
        // 调试日志：输出当前globalData的值
        Debug.Log($"[SettingsPanel] 更新UI值 - MasterVolume: {globalData.MasterVolume}, BGMVolume: {globalData.BGMVolume}, " +
                  $"VoiceVolume: {globalData.VoiceVolume}, SFXVolume: {globalData.SFXVolume}, " +
                  $"TextSpeed: {globalData.TextSpeed}, AutoSpeed: {globalData.AutoSpeed}");
        
        // 临时移除所有事件监听器，避免在设置值时触发事件
        UnbindEvents();
        
        // 音频设置
        if (masterVolumeSlider != null)
        {
            masterVolumeSlider.value = globalData.MasterVolume;
            UpdateVolumeText(masterVolumeText, globalData.MasterVolume);
            Debug.Log($"[SettingsPanel] 设置MasterVolume Slider值: {masterVolumeSlider.value}");
        }
        if (bgmVolumeSlider != null)
        {
            bgmVolumeSlider.value = globalData.BGMVolume;
            UpdateVolumeText(bgmVolumeText, globalData.BGMVolume);
        }
        if (voiceVolumeSlider != null)
        {
            voiceVolumeSlider.value = globalData.VoiceVolume;
            UpdateVolumeText(voiceVolumeText, globalData.VoiceVolume);
        }
        if (sfxVolumeSlider != null)
        {
            sfxVolumeSlider.value = globalData.SFXVolume;
            UpdateVolumeText(sfxVolumeText, globalData.SFXVolume);
        }
        
        // 文本设置
        if (textSpeedSlider != null)
        {
            textSpeedSlider.value = globalData.TextSpeed;
            UpdateTextSpeedText(globalData.TextSpeed);
        }
        if (autoSpeedSlider != null)
        {
            // 旧存档可能存在越界值，先钳制到有效范围
            float autoSpeed = Mathf.Clamp(globalData.AutoSpeed, AutoSpeedMin, AutoSpeedMax);
            autoSpeedSlider.value = autoSpeed;
            UpdateAutoSpeedText(autoSpeed);
        }
        
        // 显示设置
        if (displayModeDropdown != null)
        {
            displayModeDropdown.value = globalData.IsFullScreen ? 0 : 1;
        }
        
        // 分辨率设置
        if (resolutionDropdown != null)
        {
            SetupResolutionDropdown();
        }
        
        // 重新绑定事件监听器
        BindEvents();
        
        Debug.Log("[SettingsPanel] UI值更新完成");
    }
    
    /// <summary>
    /// 设置分辨率下拉菜单
    /// </summary>
    private void SetupResolutionDropdown()
    {
        if (resolutionDropdown == null || availableResolutions.Count == 0)
        {
            Debug.LogWarning("[SettingsPanel] 分辨率下拉菜单或可用分辨率列表为空");
            return;
        }
        
        resolutionDropdown.ClearOptions();
        List<string> options = new List<string>();
        int currentIndex = 0;
        
        // 如果当前分辨率不在列表中，添加当前分辨率
        bool foundCurrent = false;
        for (int i = 0; i < availableResolutions.Count; i++)
        {
            Resolution res = availableResolutions[i];
            if (res.width == globalData.ScreenWidth && res.height == globalData.ScreenHeight)
            {
                currentIndex = i;
                foundCurrent = true;
            }
        }
        
        // 如果当前分辨率不在列表中，添加到列表开头
        if (!foundCurrent)
        {
            Resolution currentRes = new Resolution
            {
                width = globalData.ScreenWidth,
                height = globalData.ScreenHeight,
                refreshRateRatio = Screen.currentResolution.refreshRateRatio
            };
            availableResolutions.Insert(0, currentRes);
            currentIndex = 0;
        }
        
        // 填充选项
        for (int i = 0; i < availableResolutions.Count; i++)
        {
            Resolution res = availableResolutions[i];
            string optionText = $"{res.width} x {res.height}";
            options.Add(optionText);
        }
        
        resolutionDropdown.AddOptions(options);
        resolutionDropdown.value = currentIndex;
    }
    
    /// <summary>
    /// 更新音量文本显示
    /// </summary>
    private void UpdateVolumeText(TextMeshProUGUI text, float value)
    {
        if (text != null)
        {
            text.text = Mathf.RoundToInt(value * 100).ToString() + "%";
        }
    }
    
    /// <summary>
    /// 更新文本速度文本显示
    /// </summary>
    private void UpdateTextSpeedText(float value)
    {
        if (textSpeedText != null)
        {
            // 显示为秒/字，值越小速度越快
            textSpeedText.text = $"{value:F2} 秒/字";
        }
    }
    
    /// <summary>
    /// 更新自动播放间隔文本显示
    /// </summary>
    private void UpdateAutoSpeedText(float value)
    {
        if (autoSpeedText != null)
        {
            autoSpeedText.text = $"{value:F1} 秒";
        }
    }
    
    /// <summary>
    /// 主音量变化（即时生效）
    /// </summary>
    private void OnMasterVolumeChanged(float value)
    {
        if (globalData == null)
        {
            globalData = GlobalDataManager.GetInstance().GetGlobalData();
            if (globalData == null) return;
        }
        
        globalData.MasterVolume = value;
        AudioListener.volume = value;
        UpdateVolumeText(masterVolumeText, value);
        GlobalDataManager.GetInstance().UpdateVolumeSettings(
            globalData.MasterVolume,
            globalData.BGMVolume,
            globalData.VoiceVolume,
            globalData.SFXVolume
        );
    }
    
    /// <summary>
    /// BGM音量变化（即时生效）
    /// </summary>
    private void OnBGMVolumeChanged(float value)
    {
        if (globalData == null)
        {
            globalData = GlobalDataManager.GetInstance().GetGlobalData();
            if (globalData == null) return;
        }
        
        globalData.BGMVolume = value;
        MusicManager.GetInstance().ChangeBGMVolume(value);
        UpdateVolumeText(bgmVolumeText, value);
        GlobalDataManager.GetInstance().UpdateVolumeSettings(
            globalData.MasterVolume,
            globalData.BGMVolume,
            globalData.VoiceVolume,
            globalData.SFXVolume
        );
    }
    
    /// <summary>
    /// 语音音量变化（即时生效）
    /// </summary>
    private void OnVoiceVolumeChanged(float value)
    {
        if (globalData == null)
        {
            globalData = GlobalDataManager.GetInstance().GetGlobalData();
            if (globalData == null) return;
        }
        
        globalData.VoiceVolume = value;
        VoiceManager.GetInstance().ChangeVoiceVolume(value);
        UpdateVolumeText(voiceVolumeText, value);
        GlobalDataManager.GetInstance().UpdateVolumeSettings(
            globalData.MasterVolume,
            globalData.BGMVolume,
            globalData.VoiceVolume,
            globalData.SFXVolume
        );
    }
    
    /// <summary>
    /// 音效音量变化（即时生效）
    /// </summary>
    private void OnSFXVolumeChanged(float value)
    {
        if (globalData == null)
        {
            globalData = GlobalDataManager.GetInstance().GetGlobalData();
            if (globalData == null) return;
        }
        
        globalData.SFXVolume = value;
        MusicManager.GetInstance().ChangeSFXVolume(value);
        UpdateVolumeText(sfxVolumeText, value);
        GlobalDataManager.GetInstance().UpdateVolumeSettings(
            globalData.MasterVolume,
            globalData.BGMVolume,
            globalData.VoiceVolume,
            globalData.SFXVolume
        );
    }
    
    /// <summary>
    /// 文本速度变化（即时生效）
    /// </summary>
    private void OnTextSpeedChanged(float value)
    {
        if (globalData == null)
        {
            globalData = GlobalDataManager.GetInstance().GetGlobalData();
            if (globalData == null) return;
        }
        
        globalData.TextSpeed = value;
        UpdateTextSpeedText(value);
        GlobalDataManager.GetInstance().UpdateTextSpeed(value);
    }
    
    /// <summary>
    /// 自动播放间隔变化（即时生效）
    /// </summary>
    private void OnAutoSpeedChanged(float value)
    {
        if (globalData == null)
        {
            globalData = GlobalDataManager.GetInstance().GetGlobalData();
            if (globalData == null) return;
        }

        // 吸附到 0.1s 步进（Slider 无内置步进，值变化时量化；
        // 同步回 slider 仅在量化改变原值时触发，不会形成事件循环）
        float snapped = Mathf.Round(value / AutoSpeedStep) * AutoSpeedStep;
        snapped = Mathf.Clamp(snapped, AutoSpeedMin, AutoSpeedMax);
        if (!Mathf.Approximately(snapped, value))
        {
            autoSpeedSlider.value = snapped;
            return; // slider.value 赋值会重新触发本回调，按量化后的值处理
        }

        globalData.AutoSpeed = snapped;
        UpdateAutoSpeedText(snapped);
        GlobalDataManager.GetInstance().UpdateAutoSpeed(snapped);
    }
    
    /// <summary>
    /// 显示模式变化（即时生效）
    /// </summary>
    private void OnDisplayModeChanged(int value)
    {
        if (globalData == null)
        {
            globalData = GlobalDataManager.GetInstance().GetGlobalData();
            if (globalData == null) return;
        }
        
        bool isFullScreen = value == 0;
        globalData.IsFullScreen = isFullScreen;
        Screen.fullScreen = isFullScreen;
        GlobalDataManager.GetInstance().UpdateDisplayMode(isFullScreen);
    }
    
    /// <summary>
    /// 分辨率变化（即时生效）
    /// </summary>
    private void OnResolutionChanged(int index)
    {
        if (globalData == null)
        {
            globalData = GlobalDataManager.GetInstance().GetGlobalData();
            if (globalData == null) return;
        }
        
        if (index >= 0 && index < availableResolutions.Count)
        {
            Resolution selectedRes = availableResolutions[index];
            globalData.ScreenWidth = selectedRes.width;
            globalData.ScreenHeight = selectedRes.height;
            GlobalDataManager.GetInstance().UpdateResolution(
                selectedRes.width,
                selectedRes.height,
                globalData.IsFullScreen
            );
        }
    }
    
    /// <summary>
    /// 关闭按钮点击
    /// </summary>
    private void OnCloseBtnClick()
    {
        UIManager.GetInstance().HidePanel("SettingsPanel");
        
        // 如果当前状态是Settings，检查是否是从Pause打开的（栈中有状态）
        // 如果是，使用PopState；否则使用RestoreState
        GameStateManager stateManager = GameStateManager.GetInstance();
        if (stateManager.CurrentState == GameState.Settings)
        {
            // 尝试从栈中弹出状态（如果是从Pause打开的）
            stateManager.PopState();
        }
        else
        {
            stateManager.RestoreState();
        }
        
        // 如果恢复后的状态是Pause，重新显示PausePanel
        if (GameStateManager.GetInstance().CurrentState == GameState.Pause)
        {
            UIManager.GetInstance().Show<PausePanel>();
        }
    }

    private void OnDestroy()
    {
        // 面板被Destroy时，如果当前状态是Settings，需要恢复游戏状态
        if (GameStateManager.GetInstance() != null && 
            GameStateManager.GetInstance().CurrentState == GameState.Settings)
        {
            // 尝试从栈中弹出状态（如果是从Pause打开的）
            GameStateManager.GetInstance().PopState();
            Debug.Log("[SettingsPanel] 面板被Destroy，已恢复游戏状态");
        }
    }
}
