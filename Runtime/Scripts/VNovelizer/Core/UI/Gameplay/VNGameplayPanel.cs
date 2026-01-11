using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.EventSystems;
using VNovelizer.Core.API;
using PrimeTween;
using System.Transactions;

public class VNGameplayPanel : BasePanel
{

    #region 所有定义
    // 模块组件
    [SerializeField] private Image bgImage_F;
    [SerializeField] private Image bgImage_B;
    [SerializeField] private Image charLeftImage;
    [SerializeField] private Image charMidImage;
    [SerializeField] private Image charRightImage;
    [SerializeField] private Image speakerBox;
    [SerializeField] private TMP_Text speakerText;
    [SerializeField] private TMP_Text dialogueText;
    [SerializeField] private Transform effectLayer;
    
    // HeadProfile 组件
    [SerializeField] private Image headImage; // 头像显示的 Image
    [SerializeField] private Image headFrame;  // 头像边框
    [SerializeField] private Transform headProfileTransform; // HeadProfile 根节点（用于隐藏/显示）

    [Header("SpeakerBox 配置")]
    [Tooltip("默认姓名框 Sprite（可选，如果为空则使用 VNProjectConfig 中的全局默认值）")]
    [SerializeField] private Sprite defaultSpeakerBoxSprite;
    
    // 【新增】存储每个位置的默认 Transform（用于 setchartrans 命令的恢复）
    private Dictionary<string, Vector2> defaultCharPositions = new Dictionary<string, Vector2>();
    private Dictionary<string, float> defaultCharScales = new Dictionary<string, float>();
    private HashSet<string> modifiedCharTransforms = new HashSet<string>(); // 记录哪些位置的 Transform 被修改过

    // 【新增】存储对话文本的默认属性（用于 t_color 和 t_size 命令的恢复）
    private Color? defaultDialogueTextColor = null;
    private float? defaultDialogueTextSize = null;
    private bool isDialogueTextModified = false; // 记录文本属性是否被修改过

    [Header("UI Components")]
    [SerializeField] private Image continueIcon;
    private Sequence _iconSequence;
    // 功能按钮
    [SerializeField] private Button autoButton;
    [SerializeField] private Button skipButton;
    [SerializeField] private Button saveButton;
    [SerializeField] private Button loadButton;
    [SerializeField] private Button historyButton;
    [SerializeField] private Button hideButton;
    [SerializeField] private Button pauseButton;
    // 状态变量
    private bool isAutoPlaying = false;
    private bool isSkipping = false;
    private bool isTextTyping = false;
    private bool isUIHidden = false;
    private float textSpeed;
    private float currentBaseSpeed;
    private float autoSpeed;
    private bool useNewInputSystem = true; // 默认使用新系统
    private bool isClickOnUI = false;

    //Notification
    [Header("Prompt System")]
    [SerializeField] private Transform promptContainer; // 挂一个放在左上角的空物体
    private GameObject promptPrefab;
    //private Coroutine currentTypingCoroutine;
    private Coroutine autoPlayCoroutine;

    private Tween _typewriterTween;

    // UI根节点
    [SerializeField] private Transform uiRoot;

    // --- 新版输入系统 (纯C#类方式) ---
    private VNInputActions inputActions;

    #endregion

    #region Awake()
    protected override void Awake()
    {
        base.Awake();

        // 获取组件
        bgImage_F = GetControl<Image>("BG_Front");
        bgImage_B = GetControl<Image>("BG_Back");
        charLeftImage = GetControl<Image>("Char_Left");
        charMidImage = GetControl<Image>("Char_Mid");
        charRightImage = GetControl<Image>("Char_Right");
        speakerBox = GetControl<Image>("SpeakerBox");
        speakerText = GetControl<TMP_Text>("SpeakerText");
        dialogueText = GetControl<TMP_Text>("DialougeText");
        effectLayer = transform.Find("EffectLayer");
        
        // 获取UI根节点（必须在获取 HeadProfile 之前）
        uiRoot = transform.Find("UIRoot");
        if (uiRoot == null)
        {
            Debug.LogWarning("[VNGameplayPanel] UIRoot 未找到，HeadProfile 可能无法正常工作");
        }
        
        // 获取 HeadProfile 组件
        if (uiRoot != null)
        {
            Transform headProfile = uiRoot.Find("HeadProfile");
            if (headProfile != null)
            {
                headProfileTransform = headProfile;
                Transform headImageTransform = headProfile.Find("HeadImage");
                Transform headFrameTransform = headProfile.Find("HeadFrame");
                if (headImageTransform != null) headImage = headImageTransform.GetComponent<Image>();
                if (headFrameTransform != null) headFrame = headFrameTransform.GetComponent<Image>();
            }
        }
        
        // 如果通过 Find 找不到，尝试通过 GetControl（备用方案）
        if (headImage == null) headImage = GetControl<Image>("HeadImage");
        if (headFrame == null) headFrame = GetControl<Image>("HeadFrame");
        if (headProfileTransform == null) headProfileTransform = transform.Find("UIRoot/HeadProfile");

        // 检查关键组件是否获取成功
        if (dialogueText == null) Debug.LogError("DialougeText not found in VNGameplayPanel");
        if (speakerText == null) Debug.LogError("SpeakerText not found in VNGameplayPanel");


        if (continueIcon == null)
            continueIcon = transform.Find("UIRoot/DialogueBox/ContinueIcon")?.GetComponent<Image>();

        if (promptContainer == null) promptContainer = transform.Find("PromptLayer");
        //记得要添加路径
        promptPrefab = ResourcesManager.GetInstance().Load<GameObject>(VNProjectConfig.Instance.UI_PromptPath + "/PromptItem");
        // 获取功能按钮
        autoButton = GetControl<Button>("Auto");
        skipButton = GetControl<Button>("Skip");
        saveButton = GetControl<Button>("Save");
        loadButton = GetControl<Button>("Load");
        historyButton = GetControl<Button>("History");
        hideButton = GetControl<Button>("Hide");
        pauseButton = GetControl<Button>("Pause");


        // 绑定按钮事件
        if (autoButton != null) autoButton.onClick.AddListener(OnAutoButtonClick);
        if (skipButton != null) skipButton.onClick.AddListener(OnSkipButtonClick);
        if (saveButton != null) saveButton.onClick.AddListener(OnSaveButtonClick);
        if (loadButton != null) loadButton.onClick.AddListener(OnLoadButtonClick);
        if (historyButton != null) historyButton.onClick.AddListener(OnLogButtonClick);
        if (hideButton != null) hideButton.onClick.AddListener(OnHideButtonClick);
        if (pauseButton != null) pauseButton.onClick.AddListener(OnPauseButtonClick);

        // 初始化状态
        try
        {
            if (GlobalDataManager.GetInstance() != null)
            {
                if (GlobalDataManager.GetInstance().GetGlobalData() == null)
                {
                    GlobalDataManager.GetInstance().Init();
                }

                if (GlobalDataManager.GetInstance().GetGlobalData() != null)
                {
                    textSpeed = GlobalDataManager.GetInstance().GetGlobalData().TextSpeed;
                    autoSpeed = GlobalDataManager.GetInstance().GetGlobalData().AutoSpeed;
                }
                else
                {
                    textSpeed = 0.05f;
                    autoSpeed = 1.0f;
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("Error accessing GlobalDataManager: " + e.Message + ", using default values");
            textSpeed = 0.05f;
            autoSpeed = 1.0f;
        }

        // 注册事件
        EventCenter.GetInstance().AddEventListener<Dictionary<string, string>>("UpdateDialogue", OnUpdateDialogue);
        EventCenter.GetInstance().AddEventListener<string>("ChangeBackground", OnChangeBackground);
        EventCenter.GetInstance().AddEventListener<Dictionary<string, string>>("ShowCharacter", OnShowCharacter);
        EventCenter.GetInstance().AddEventListener<string>("HideCharacter", OnHideCharacter);
        EventCenter.GetInstance().AddEventListener<Dictionary<string, string>>("UpdateHeadProfile", OnUpdateHeadProfile);
        EventCenter.GetInstance().AddEventListener("TextSpeedChanged", OnTextSpeedChanged);
        EventCenter.GetInstance().AddEventListener("AutoSpeedChanged", OnAutoSpeedChanged);

        // --- 初始化输入系统 ---
        InitializeInputSystem();

        // 初始时设置状态
        GameStateManager.GetInstance().SetState(GameState.Gameplay);
    }
    #endregion

    protected override void OnButtonClick(string ButtonName)
    {
        base.OnButtonClick(ButtonName);
    }

    /// <summary>
    /// 显示面板（重写以确保 Input Actions 已启用）
    /// </summary>
    public override void ShowMe()
    {
        gameObject.SetActive(true);
        // 确保 Input Actions 已启用（即使 OnEnable 没有被调用，或者面板已经存在）
        EnsureInputActionsEnabled();
    }

    /// <summary>
    /// 确保 Input Actions 已启用（用于 ShowMe 时确保输入可用）
    /// </summary>
    private void EnsureInputActionsEnabled()
    {
        // 确保 Input Actions 已初始化
        if (inputActions == null)
        {
            InitializeInputSystem();
        }

        if (inputActions != null)
        {
            // 启用 Action Map
            if (!inputActions.VNControls.enabled)
            {
                inputActions.VNControls.Enable();
                Debug.Log("[VNGameplayPanel] ShowMe: Input Actions 已启用");
            }
            
            // 确保事件已绑定（防止重复绑定）
            inputActions.VNControls.Confirm.performed -= OnConfirm;
            inputActions.VNControls.Confirm.performed += OnConfirm;
            
            Debug.Log("[VNGameplayPanel] ShowMe: 确保 Input Actions 已启用并绑定");
        }
        else
        {
            Debug.LogError("[VNGameplayPanel] ShowMe: Input Actions 为 null，无法启用输入！");
        }
    }

    private void Update()
    {
        // 快进模式下，检查是否到达对话末尾
        // 【修复】在 Choice 状态下，不应该继续快进
        if (isSkipping && !isTextTyping)
        {
            if (!isAutoPlaying && !isUIHidden)
            {
                // 检查游戏状态，如果是 Choice 状态，停止快进
                GameStateManager stateManager = GameStateManager.GetInstance();
                if (stateManager != null && stateManager.CurrentState == GameState.Choice)
                {
                    // 在 Choice 状态下，停止快进，等待玩家选择
                    return;
                }
                
                VNManager.GetInstance().NextLine();
            }
        }

        // 只有在明确不使用新系统时才调用旧逻辑
        if (!useNewInputSystem)
        {
            UpdateInputFallback();
        }

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
        {
            isClickOnUI = true;
        }
        else
        {
            isClickOnUI = false;
        }
    }

    #region 新版InputSystem方案 (重写部分)

    // 初始化新版输入系统
    private void InitializeInputSystem()
    {
        // 1. 实例化生成的 C# 类
        // 这会自动读取 .inputactions 里的配置
        if (inputActions == null)
        {
            inputActions = new VNInputActions();
        }

        // 标记使用新系统，防止 Update 里跑旧逻辑
        useNewInputSystem = true;
        Debug.Log("[VNGameplayPanel] Input System Initialized via C# Class");
    }

    // 启用并绑定事件
    protected override void OnEnable()
    {
        base.OnEnable(); // 如果基类有逻辑，需要保留

        Debug.Log($"[VNGameplayPanel] OnEnable 被调用 - inputActions: {inputActions != null}");
        
        // 确保 Input Actions 已初始化
        if (inputActions == null)
        {
            InitializeInputSystem();
        }

        if (inputActions != null)
        {
            // 1. 启用 Action Map (VNControls)
            inputActions.VNControls.Enable();

            // 2. 绑定事件 (Context Binding)
            // 先解绑，防止重复绑定
            inputActions.VNControls.Confirm.performed -= OnConfirm;
            inputActions.VNControls.Skip.performed -= OnSkip;
            inputActions.VNControls.Skip.canceled -= OnSkipCanceled;
            inputActions.VNControls.Auto.performed -= OnAuto;
            inputActions.VNControls.Hide.performed -= OnHide;
            inputActions.VNControls.Log.performed -= OnLog;
            inputActions.VNControls.Save.performed -= OnSave;
            inputActions.VNControls.Settings.performed -= OnPause;
            
            // 重新绑定
            inputActions.VNControls.Confirm.performed += OnConfirm;
            inputActions.VNControls.Skip.performed += OnSkip;
            inputActions.VNControls.Skip.canceled += OnSkipCanceled;
            inputActions.VNControls.Auto.performed += OnAuto;
            inputActions.VNControls.Hide.performed += OnHide;
            inputActions.VNControls.Log.performed += OnLog;
            inputActions.VNControls.Save.performed += OnSave;
            inputActions.VNControls.Settings.performed += OnPause;

            Debug.Log("[VNGameplayPanel] Input Actions Enabled & Bound");
        }
        else
        {
            Debug.LogError("[VNGameplayPanel] Input Actions 为 null，无法启用输入！");
        }
    }

    // 禁用并解绑事件
    private void OnDisable()
    {
        if (inputActions != null)
        {
            // 1. 解绑事件 (养成好习惯，防止内存泄漏或空引用)
            inputActions.VNControls.Confirm.performed -= OnConfirm;

            inputActions.VNControls.Skip.performed -= OnSkip;
            inputActions.VNControls.Skip.canceled -= OnSkipCanceled;

            inputActions.VNControls.Auto.performed -= OnAuto;
            inputActions.VNControls.Hide.performed -= OnHide;
            inputActions.VNControls.Log.performed -= OnLog;

            inputActions.VNControls.Save.performed -= OnSave;

            inputActions.VNControls.Settings.performed -= OnPause;

            // 2. 禁用 Action Map
            inputActions.VNControls.Disable();

            Debug.Log("[VNGameplayPanel] Input Actions Disabled");
        }

        HideContinueIcon();
    }
    #endregion

    #region Input Action Callbacks (具体逻辑)

    // Confirm事件 - 下一句
    public void OnConfirm(InputAction.CallbackContext context)
    {
        Debug.Log($"[VNGameplayPanel] OnConfirm 被调用 - 状态: {GameStateManager.GetInstance().CurrentState}, isClickOnUI: {isClickOnUI}, isAutoPlaying: {isAutoPlaying}, isUIHidden: {isUIHidden}, isTextTyping: {isTextTyping}");
        
        // 先判断当前游戏状态
        if (!GameStateManager.GetInstance().CanInteractGameplay())
        {
            return;
        }
        //看看是不是点在了UI上
        if (isClickOnUI)
        {
            Debug.Log("[VNGameplayPanel] 点击在UI上，忽略");
            return;
        }

        if (!isAutoPlaying && !isUIHidden)
        {
            if (isTextTyping)
            {
                CompleteTextTyping();
            }
            else
            {
                // 继续下一行
                Debug.Log("[VNGameplayPanel] 执行 NextLine");
                VNManager.GetInstance().NextLine();
            }
        }
    }

    // 兼容性重载：允许不带参数直接调用
    public void OnConfirm()
    {
        OnConfirm(new InputAction.CallbackContext());
    }

    // Skip事件 - 激活快进模式
    public void OnSkip(InputAction.CallbackContext context)
    {
        isSkipping = true;
        UpdateSkipButtonState();
        // 设置TimeScale实现快进效果
        Time.timeScale = 10f;
    }

    // Skip取消事件 - 停止快进模式
    public void OnSkipCanceled(InputAction.CallbackContext context)
    {
        isSkipping = false;
        UpdateSkipButtonState();
        // 恢复正常TimeScale
        Time.timeScale = 1f;
    }

    // Auto事件 - 激活自动播放
    public void OnAuto(InputAction.CallbackContext context)
    {
        VNManager.GetInstance().ToggleAutoPlay();
    }

    // Hide事件 - 隐藏/显示UI
    public void OnHide(InputAction.CallbackContext context)
    {
        ToggleUI();
    }

    // Log事件 - 打开历史记录
    public void OnLog(InputAction.CallbackContext context)
    {
        var historyPanel = UIManager.GetInstance().GetPanel<HistoryPanel>("HistoryPanel");
        
        // 【重要】如果HistoryPanel已经打开
        if (historyPanel != null && historyPanel.gameObject.activeSelf)
        {
            // 检查触发源是否是鼠标滚轮
            // 如果是滚轮触发，则不响应，让HistoryPanel的Update方法处理滚轮滚动
            if (context.control != null)
            {
                string controlPath = context.control.path;
                // 检查是否是鼠标滚轮（scroll/up 或 scroll/down）
                if (controlPath.Contains("scroll"))
                {
                    return; // 不执行打开/关闭逻辑，让HistoryPanel自己处理滚轮
                }
            }
            
            // 如果不是滚轮触发（例如按H键），则正常关闭面板
            UIManager.GetInstance().HidePanel("HistoryPanel");
            GameStateManager.GetInstance().RestoreState();
            return;
        }
        
        // 检查是否可以打开历史记录面板
        if (!GameStateManager.GetInstance().CanOpenPanel(GameState.History))
        {
            Debug.LogWarning("[VNGameplayPanel] 当前状态不允许打开历史记录面板");
            return;
        }

        // 打开历史记录面板
        GameStateManager.GetInstance().SetState(GameState.History);
        string path = VNProjectConfig.Instance != null ? VNProjectConfig.Instance.UI_HistoryPath : "VNPrefabs/UI/History";
        if (string.IsNullOrEmpty(path)) path = "VNPrefabs/UI/History";
        UIManager.GetInstance().ShowPanel<HistoryPanel>("HistoryPanel", path, E_UI_Layer.Top, null);
    }

    // Save事件 - 打开保存系统
    public void OnSave(InputAction.CallbackContext context)
    {
        // 检查是否可以打开保存系统面板
        if (!GameStateManager.GetInstance().CanOpenPanel(GameState.SaveLoad))
        {
            Debug.LogWarning("[VNGameplayPanel] 当前状态不允许打开保存系统面板");
            return;
        }

        var saveLoadPanel = UIManager.GetInstance().GetPanel<SaveLoadPanel>("SaveLoadPanel");

        if (saveLoadPanel != null && saveLoadPanel.gameObject.activeSelf)
        {
            // 如果开着，就关掉
            UIManager.GetInstance().HidePanel("SaveLoadPanel");
            GameStateManager.GetInstance().RestoreState();
        }
        else
        {
            // 打开保存面板
            SaveManager.GetInstance().CaptureCurrentScreen();
            UIManager.GetInstance().ShowPanel<SaveLoadPanel>("SaveLoadPanel", VNProjectConfig.Instance.UI_SaveLoadPath, E_UI_Layer.Top, (panel) =>
            {
                panel.SetMode(SaveLoadPanel.Mode.Save);
            });
        }
    }

    // Pause事件 - 打开暂停面板 (ESC键)
    public void OnPause(InputAction.CallbackContext context)
    {
        // 【修改】允许在Gameplay、AutoPlay、Choice和Pause状态下响应ESC键
        GameState currentState = GameStateManager.GetInstance().CurrentState;
        if (currentState != GameState.Gameplay && currentState != GameState.AutoPlay && 
            currentState != GameState.Choice && currentState != GameState.Pause)
        {
            return;
        }
        
        // 检查是否可以打开暂停面板
        if (!GameStateManager.GetInstance().CanOpenPanel(GameState.Pause))
        {
            Debug.LogWarning("[VNGameplayPanel] 当前状态不允许打开暂停面板");
            return;
        }

        var pausePanel = UIManager.GetInstance().GetPanel<PausePanel>("PausePanel");

        if (pausePanel != null && pausePanel.gameObject.activeSelf)
        {
            // 如果开着，就关掉
            UIManager.GetInstance().HidePanel("PausePanel");
            GameStateManager.GetInstance().RestoreState();
        }
        else
        {
            // 【Bug修复】在打开暂停面板之前截屏，避免截图包含PausePanel
            SaveManager.GetInstance().CaptureCurrentScreen();
            
            // 打开暂停面板
            string path = VNProjectConfig.Instance != null ? VNProjectConfig.Instance.UI_PausePath : "VNPrefabs/UI/Pause";
            if (string.IsNullOrEmpty(path)) path = "VNPrefabs/UI/Pause";
            UIManager.GetInstance().ShowPanel<PausePanel>("PausePanel", path, E_UI_Layer.Top, null);
        }
    }
    
    /// <summary>
    /// 暂停按钮点击
    /// </summary>
    private void OnPauseButtonClick()
    {
        OnPause(new InputAction.CallbackContext());
    }
    #endregion

    #region 旧版输入系统备选方案 (保留但默认不使用)
    private void UpdateInputFallback()
    {
        if (useNewInputSystem) return;

        // 检测鼠标点击
        if (Input.GetMouseButtonDown(0)) OnConfirmFallback();

        // 检测键盘输入
        if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return)) OnConfirmFallback();
        if (Input.GetKeyDown(KeyCode.A)) OnAutoFallback();
        if (Input.GetKeyDown(KeyCode.H)) OnHideFallback();
        if (Input.GetKeyDown(KeyCode.L)) OnLogFallback();

        if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
            OnSkipFallback();
        else
            OnSkipCanceledFallback();
    }

    private void OnConfirmFallback()
    {
        if (!isAutoPlaying && !isUIHidden)
        {
            if (isTextTyping) CompleteTextTyping();
            else VNManager.GetInstance().NextLine();
        }
    }

    private void OnSkipFallback()
    {
        if (!isSkipping)
        {
            isSkipping = true;
            UpdateSkipButtonState();
            Time.timeScale = 10f;
        }
    }

    private void OnSkipCanceledFallback()
    {
        if (isSkipping)
        {
            isSkipping = false;
            UpdateSkipButtonState();
            Time.timeScale = 1f;
        }
    }

    private void OnAutoFallback()
    {
        isAutoPlaying = !isAutoPlaying;
        UpdateAutoButtonState();
    }

    private void OnHideFallback()
    {
        ToggleUI();
    }

    private void OnLogFallback()
    {
        UIManager.GetInstance().ShowPanel<HistoryPanel>("HistoryPanel", VNProjectConfig.Instance.UI_HistoryPath, E_UI_Layer.Middle, null);
    }
    #endregion

    #region 核心逻辑 (UI更新与事件响应)

    /// <summary>
    /// 更新说话人显示（根据 CharacterProfile.SpeakerBox 决定显示方式）
    /// </summary>
    /// <param name="speaker">说话人ID或名称</param>
    public void UpdateSpeakerDisplay(string speaker)
    {
        // 情况0：如果 speaker 为 "hide"，隐藏 SpeakerBox 和 SpeakerText
        if (!string.IsNullOrEmpty(speaker) && speaker.Trim().ToLower() == "hide")
        {
            // 隐藏 SpeakerBox
            if (speakerBox != null)
            {
                speakerBox.transform.gameObject.SetActive(false);
            }
            
            // 隐藏 SpeakerText
            if (speakerText != null)
            {
                speakerText.transform.gameObject.SetActive(false);
            }

            return;
        }

        // 确保 SpeakerBox 和 SpeakerText 显示（如果不是 hide）
        if (speakerBox != null)
        {
            speakerBox.gameObject.SetActive(true);
        }
        
        if (speakerText != null)
        {
            speakerText.gameObject.SetActive(true);
        }

        // 尝试通过 CharacterResManager 静默查找角色配置（不打印错误日志）
        CharacterResManager charResMgr = CharacterResManager.GetInstance();
        if (charResMgr != null)
        {
            CharacterProfile profile = charResMgr.TryGetCharacterProfile(speaker);
            if (profile != null && profile.SpeakerBox != null)
            {
                // 情况1：找到角色配置且 SpeakerBox 有引用
                // 设置姓名框为 profile 中的 SpeakerBox Sprite
                if (speakerBox != null)
                {
                    speakerBox.sprite = profile.SpeakerBox;
                }
                // SpeakerText 不显示任何内容
                if (speakerText != null)
                {
                    speakerText.text = "";
                }
                return;
            }
        }

        // 情况2：CharacterProfile.SpeakerBox 无引用或找不到角色配置
        // 直接显示用户在 Excel 中写的 Speaker 内容（无论是什么）
        if (speakerText != null)
        {
            speakerText.text = speaker;
        }
        // 设置默认 SpeakerBox Sprite（优先使用面板级配置，其次使用全局配置）
        if (speakerBox != null)
        {
            Sprite defaultSprite = defaultSpeakerBoxSprite; // 优先使用面板级配置
            if (defaultSprite == null && VNProjectConfig.Instance != null)
            {
                defaultSprite = VNProjectConfig.Instance.DefaultSpeakerBoxSprite; // 使用全局配置
            }
            speakerBox.sprite = defaultSprite;
        }
    }

    private void OnUpdateDialogue(Dictionary<string, string> dialogueInfo)
    {
        string speaker = dialogueInfo["speaker"];
        string text = dialogueInfo["text"];

        if (_typewriterTween.isAlive)
        {
            _typewriterTween.Stop();
        }

        if (autoPlayCoroutine != null)
        {
            StopCoroutine(autoPlayCoroutine);
            autoPlayCoroutine = null;
        }

        isTextTyping = true;

        // 处理 SpeakerBox 显示逻辑（UpdateSpeakerDisplay 内部已处理显示/隐藏）
        UpdateSpeakerDisplay(speaker);

        // --- 核心修改 ---
        // 1. 记录基准速度
        currentBaseSpeed = textSpeed;

        dialogueText.text = text;
        dialogueText.maxVisibleCharacters = 0;

        float duration = text.Length * textSpeed;
        //隐藏继续图标
        HideContinueIcon();

        // 2. 启动打字机 (使用 Linear 匀速)
        _typewriterTween = Tween.Custom(0, text.Length, duration, onValueChange: (val) =>
        {
            dialogueText.maxVisibleCharacters = Mathf.FloorToInt(val);
        }, ease: Ease.Linear)
        .OnComplete(OnTypewriterComplete);
    }

    private void OnTypewriterComplete()
    {
        if (dialogueText != null) dialogueText.maxVisibleCharacters = 99999;
        isTextTyping = false;

        ShowContinueIcon();

        EventCenter.GetInstance().EventTrigger("TypingFinished");
    }


    private void OnChangeBackground(string backgroundPath)
    {
        if (bgImage_F == null)
        {
            bgImage_F = GetControl<Image>("BackgroundLayer");
            if (bgImage_F == null) return;
        }

        if (backgroundPath == "black")
        {
            bgImage_F.color = Color.black;
            bgImage_F.sprite = null;
        }
        else
        {
            string fullBackgroundPath = backgroundPath;

            if (!fullBackgroundPath.Contains("/"))
            {
                string rootPath = VNProjectConfig.Instance.BackgroundResPath;
                string path = rootPath + "/" + fullBackgroundPath;
                ResourcesManager.GetInstance().LoadAsync<Sprite>(path, (sprite) =>
                {
                    if (sprite != null)
                    {
                        bgImage_F.sprite = sprite;
                        bgImage_F.color = Color.white;
                    }
                    else
                    {
                        string pathWithBackgrounds = "Backgrounds/" + fullBackgroundPath;
                        ResourcesManager.GetInstance().LoadAsync<Sprite>(pathWithBackgrounds, (sprite2) =>
                        {
                            if (sprite2 != null)
                            {
                                bgImage_F.sprite = sprite2;
                                bgImage_F.color = Color.white;
                            }
                        });
                    }
                });
            }
            else
            {
                ResourcesManager.GetInstance().LoadAsync<Sprite>(fullBackgroundPath, (sprite) =>
                {
                    if (sprite != null)
                    {
                        bgImage_F.sprite = sprite;
                        bgImage_F.color = Color.white;
                    }
                });
            }
        }
    }

    private void OnShowCharacter(Dictionary<string, string> characterInfo)
    {
        string position = characterInfo["position"];
        string characterID = characterInfo["characterID"];
        string emotion = characterInfo["emotion"];

        Image charImage = GetCharImage(position);
        if (charImage == null) return;

        CharacterProfile profile = CharacterResManager.GetInstance().GetCharacterProfile(characterID);
        if (profile != null)
        {
            Sprite sprite = profile.GetEmotionSprite(emotion);
            if (sprite != null)
            {
                charImage.sprite = sprite;
                charImage.color = Color.white;
                charImage.gameObject.SetActive(true);
                
                // 应用保存的翻转状态（如果存在）
                // 位置代码转换：VNManager 内部使用 "Left"/"Mid"/"Right"，但 API 使用 "L"/"M"/"R"
                string posCode = position;
                if (position == "Left") posCode = "L";
                else if (position == "Mid") posCode = "M";
                else if (position == "Right") posCode = "R";
                
                float savedScaleX = VNManager.GetInstance().GetCharacterScaleX(posCode);
                if (savedScaleX != 1f) // 如果不是默认值，应用翻转
                {
                    RectTransform charRect = charImage.rectTransform;
                    Vector3 scale = charRect.localScale;
                    scale.x = savedScaleX;
                    charRect.localScale = scale;
                    Debug.Log($"[VNGameplayPanel] 应用位置 {position}({posCode}) 的翻转状态: {savedScaleX}");
                }
            }
        }
    }

    private void OnHideCharacter(string position)
    {
        Image charImage = GetCharImage(position);
        if (charImage != null)
        {
            charImage.gameObject.SetActive(false);
        }
    }
    
    /// <summary>
    /// 更新 HeadProfile 显示
    /// </summary>
    private void OnUpdateHeadProfile(Dictionary<string, string> headProfileInfo)
    {
        if (headProfileInfo == null) return;
        
        string headProfileValue = headProfileInfo.ContainsKey("headProfile") ? headProfileInfo["headProfile"] : "";
        string speaker = headProfileInfo.ContainsKey("speaker") ? headProfileInfo["speaker"] : "";
        
        // 如果 HeadProfile 值为 "hide" 或为空，隐藏整个 HeadProfile（包括边框）
        string trimmedValue = headProfileValue.Trim().ToLower();
        if (string.IsNullOrEmpty(headProfileValue) || trimmedValue == "hide")
        {
            if (headProfileTransform != null)
            {
                headProfileTransform.gameObject.SetActive(false);
                Debug.Log($"[VNGameplayPanel] HeadProfile 已隐藏 (值: {headProfileValue})");
            }
            return;
        }
        
        // 显示 HeadProfile
        if (headProfileTransform != null)
        {
            headProfileTransform.gameObject.SetActive(true);
        }
        
        // 解析 HeadProfile 格式：CharacterID_Emotion（与立绘格式相同）
        string[] parts = headProfileValue.Trim().Split('_');
        if (parts.Length < 2)
        {
            Debug.LogWarning($"[VNGameplayPanel] HeadProfile 格式错误: {headProfileValue}，应为 CharacterID_Emotion");
            if (headProfileTransform != null) headProfileTransform.gameObject.SetActive(false);
            return;
        }
        
        string characterID = parts[0].Trim();
        string emotion = parts[1].Trim();
        
        // 从 CharacterResManager 获取头像
        CharacterProfile profile = CharacterResManager.GetInstance().GetCharacterProfile(characterID);
        if (profile != null)
        {
            Sprite headSprite = profile.GetHeadSprite(emotion);
            if (headSprite != null && headImage != null)
            {
                headImage.sprite = headSprite;
                headImage.color = Color.white;
            }
            else
            {
                Debug.LogWarning($"[VNGameplayPanel] 角色 {characterID} 没有找到情绪 {emotion} 的头像");
                if (headProfileTransform != null) headProfileTransform.gameObject.SetActive(false);
            }
        }
        else
        {
            Debug.LogWarning($"[VNGameplayPanel] 找不到角色配置: {characterID}");
            if (headProfileTransform != null) headProfileTransform.gameObject.SetActive(false);
        }
    }

    // 当前正在显示的完整文本
    private string currentFullText = "";

    // 打字机效果
    private IEnumerator TypeText(string text)
    {
        isTextTyping = true;
        currentFullText = text;

        if (dialogueText == null)
        {
            isTextTyping = false;
            yield break;
        }

        dialogueText.text = "";
        Color textColor = dialogueText.color;
        textColor.a = 1f;
        dialogueText.color = textColor;

        for (int i = 0; i <= text.Length; i++)
        {
            dialogueText.text = text.Substring(0, i);

            if (i < text.Length)
            {
                yield return new WaitForSeconds(textSpeed);
            }
        }

        isTextTyping = false;
        //currentTypingCoroutine = null;
        EventCenter.GetInstance().EventTrigger("TypingFinished");
    }

    // 立即完成打字
    private void CompleteTextTyping()
    {
        if (_typewriterTween.isAlive)
        {
            _typewriterTween.Complete(); // 这会触发上面的 OnTypewriterComplete
        }
        else
        {
            // 双重保险
            OnTypewriterComplete();
        }
    }

    // 按钮点击事件
    private void OnAutoButtonClick()
    {
        if (GameStateManager.GetInstance().CanInteractGameplay())
            VNManager.GetInstance().ToggleAutoPlay();

        isAutoPlaying = !isAutoPlaying;
        UpdateAutoButtonState();
    }

    private void OnSkipButtonClick()
    {
        isSkipping = !isSkipping;
        UpdateSkipButtonState();
    }

    private void OnSaveButtonClick()
    {
        // 检查是否可以打开保存系统面板
        if (!GameStateManager.GetInstance().CanOpenPanel(GameState.SaveLoad))
        {
            Debug.LogWarning("[VNGameplayPanel] 当前状态不允许打开保存系统面板");
            return;
        }

        SaveManager.GetInstance().CaptureCurrentScreen();
        UIManager.GetInstance().ShowPanel<SaveLoadPanel>("SaveLoadPanel", VNProjectConfig.Instance.UI_SaveLoadPath, E_UI_Layer.Top, (panel)=>
        {
            panel.SetMode(SaveLoadPanel.Mode.Save);
        });
    }

    private void OnLoadButtonClick()
    {
        // 检查是否可以打开保存系统面板
        if (!GameStateManager.GetInstance().CanOpenPanel(GameState.SaveLoad))
        {
            Debug.LogWarning("[VNGameplayPanel] 当前状态不允许打开保存系统面板");
            return;
        }

        UIManager.GetInstance().ShowPanel<SaveLoadPanel>("SaveLoadPanel", VNProjectConfig.Instance.UI_SaveLoadPath, E_UI_Layer.Top, (panel) =>
        {
            panel.SetMode(SaveLoadPanel.Mode.Load);
        });
    }

    private void OnLogButtonClick()
    {
        // 检查是否可以打开历史记录面板
        if (!GameStateManager.GetInstance().CanOpenPanel(GameState.History))
        {
            Debug.LogWarning("[VNGameplayPanel] 当前状态不允许打开历史记录面板");
            return;
        }

        var historyPanel = UIManager.GetInstance().GetPanel<HistoryPanel>("HistoryPanel");

        if (historyPanel != null && historyPanel.gameObject.activeSelf)
        {
            // 如果开着，就关掉
            UIManager.GetInstance().HidePanel("HistoryPanel");
            GameStateManager.GetInstance().RestoreState();
        }
        else
        {
            // 打开前先设置状态
            GameStateManager.GetInstance().SetState(GameState.History);
            UIManager.GetInstance().ShowPanel<HistoryPanel>("HistoryPanel", VNProjectConfig.Instance.UI_HistoryPath, E_UI_Layer.Top, null);
        }
    }

    private void OnHideButtonClick()
    {
        ToggleUI();
    }

    // 更新按钮状态
    private void UpdateAutoButtonState()
    {
        TMP_Text buttonText = autoButton.GetComponentInChildren<TMP_Text>();
        if (buttonText != null)
        {
            buttonText.text = isAutoPlaying ? "Auto (On)" : "Auto (Off)";
        }
    }

    private void UpdateSkipButtonState()
    {
        TMP_Text buttonText = skipButton.GetComponentInChildren<TMP_Text>();
        if (buttonText != null)
        {
            buttonText.text = isSkipping ? "Skip (On)" : "Skip (Off)";
        }
    }

    // 切换UI显示
    private void ToggleUI()
    {
        isUIHidden = !isUIHidden;
        uiRoot.gameObject.SetActive(!isUIHidden);
    }

    private void OnTextSpeedChanged()
    {
        float newSpeed = GlobalDataManager.GetInstance().GetGlobalData().TextSpeed;

        // 更新本地变量
        textSpeed = newSpeed;


        if (isTextTyping && _typewriterTween.isAlive && newSpeed > 0.00001f)
        {
            float newTimeScale = currentBaseSpeed / newSpeed;

            _typewriterTween.timeScale = newTimeScale;
        }
    }

    private void OnAutoSpeedChanged()
    {
        autoSpeed = GlobalDataManager.GetInstance().GetGlobalData().AutoSpeed;
    }

    private void OnDestroy()
    {
        // 移除事件监听
        EventCenter.GetInstance().RemoveEventListener<Dictionary<string, string>>("UpdateDialogue", OnUpdateDialogue);
        EventCenter.GetInstance().RemoveEventListener<string>("ChangeBackground", OnChangeBackground);
        EventCenter.GetInstance().RemoveEventListener<Dictionary<string, string>>("ShowCharacter", OnShowCharacter);
        EventCenter.GetInstance().RemoveEventListener<string>("HideCharacter", OnHideCharacter);
        EventCenter.GetInstance().RemoveEventListener<Dictionary<string, string>>("UpdateHeadProfile", OnUpdateHeadProfile);
        EventCenter.GetInstance().RemoveEventListener("TextSpeedChanged", OnTextSpeedChanged);
        EventCenter.GetInstance().RemoveEventListener("AutoSpeedChanged", OnAutoSpeedChanged);


        // 恢复正常TimeScale
        Time.timeScale = 1f;
    }

    private void ShowContinueIcon()
    {
        if (continueIcon == null) return;

        continueIcon.gameObject.SetActive(true);

        // 重置状态 (防止上次动画停在半透明或偏移的位置)
        continueIcon.color = Color.white; // 假设原色是白
        continueIcon.rectTransform.anchoredPosition = new Vector2(continueIcon.rectTransform.anchoredPosition.x, 0); // 假设Y轴归零

        // 使用 PrimeTween 创建循环动画
        // cycles: -1 (无限循环)
        // cycleMode: Yoyo (像悠悠球一样往复运动)
        _iconSequence = Sequence.Create(cycles: -1, cycleMode: Sequence.SequenceCycleMode.Yoyo)
            // 1. 上下浮动 (修改 AnchoredPosition Y)
            // endValue: -10 (向下移动10像素), duration: 0.8秒
            .Group(Tween.UIAnchoredPositionY(continueIcon.rectTransform, endValue: -10f, duration: 0.8f, ease: Ease.InOutSine))
            // 2. 透明度闪烁
            // endValue: 0.2 (变淡), duration: 0.8秒
            .Group(Tween.Alpha(continueIcon, endValue: 0.2f, duration: 0.8f, ease: Ease.InOutSine));
    }

    private void HideContinueIcon()
    {
        if (_iconSequence.isAlive)
        {
            _iconSequence.Stop(); // 停止动画
        }

        if (continueIcon != null)
        {
            continueIcon.gameObject.SetActive(false);
        }
    }


    public void ShowPrompt(string text, float duration)
    {
        if (promptPrefab == null || promptContainer == null) return;

        // 1. 生成
        GameObject go = Instantiate(promptPrefab, promptContainer);
        // 设为第一个子物体或最后一个，看你想要最新的在上面还是下面
        go.transform.SetAsLastSibling();

        TMP_Text txt = go.GetComponentInChildren<TMP_Text>();
        if (txt != null) txt.text = text;

        CanvasGroup cg = go.GetComponent<CanvasGroup>();
        if (cg == null) cg = go.AddComponent<CanvasGroup>();

        RectTransform rect = go.GetComponent<RectTransform>();

        // 2. 初始状态 (在屏幕左边外面，透明)
        cg.alpha = 0;
        // 假设 Prompt 宽 300，初始 X = -300
        float width = rect.sizeDelta.x;
        rect.anchoredPosition = new Vector2(-width, rect.anchoredPosition.y);

        // 3. 进场动画 (移入 + 淡入)
        Sequence.Create()
            .Group(Tween.Alpha(cg, 1, 0.5f, Ease.OutQuad))
            .Group(Tween.UIAnchoredPositionX(rect, 0, 0.5f, Ease.OutBack)) // 带点回弹
            .ChainDelay(duration) // 停留时间
            .Chain(Tween.Alpha(cg, 0, 0.5f, Ease.InQuad)) // 淡出
            .Group(Tween.UIAnchoredPositionX(rect, -width, 0.5f, Ease.InQuad)) // 移出
            .OnComplete(() => Destroy(go)); // 销毁
    }
    #endregion

    #region 可供外部调用的API
    public RectTransform GetCharRect(string posCode)
    {
        if (string.IsNullOrEmpty(posCode)) return null;

        switch (posCode.ToUpper())
        {
            case "L":
            case "LEFT":
                return charLeftImage != null ? charLeftImage.rectTransform : null;

            case "M":
            case "MID":
            case "MIDDLE":
                return charMidImage != null ? charMidImage.rectTransform : null;

            case "R":
            case "RIGHT":
                return charRightImage != null ? charRightImage.rectTransform : null;

            default:
                Debug.LogWarning($"[VNGameplayPanel] 未知的位置代码: {posCode}");
                return null;
        }
    }

    public Image GetCharImage(string position)
    {
        switch (position)
        {
            case "Left":
            case "L":
                return charLeftImage;
            case "Mid":
            case "M":
                return charMidImage;
            case "Right":
            case "R":
                return charRightImage;
            default:
                Debug.LogError($"Invalid character position: {position}");
                return null;
        }
    }

    public Image GetBG_F()
    {
        if (bgImage_F != null)
        {
            return bgImage_F;
        }
        return null;
    }

    public Image GetBG_B()
    {
        if (bgImage_B != null)
        { 
            return bgImage_B;
        }
        return null;
        
    }

    public TMP_Text GetDialogueText()
    {
        if (dialogueText != null)
        { 
            return dialogueText;
        }
        return null;
    
    }

    public Image GetSpeakerBox()
    {
        if (speakerBox != null)
        { 
            return speakerBox;
        }
        return null;
    }
    public TMP_Text GetSpeakerText()
    {
        if (speakerText != null)
        {
            return speakerText;
        }
        return null;
    }

    public Transform GetEffectLayer() => effectLayer;
    
    /// <summary>
    /// 保存指定位置的默认 Transform（在第一次修改时调用）
    /// </summary>
    public void SaveDefaultCharTransform(string posCode)
    {
        RectTransform rect = GetCharRect(posCode);
        if (rect != null)
        {
            string normalizedPos = NormalizePositionCode(posCode);
            if (!defaultCharPositions.ContainsKey(normalizedPos))
            {
                defaultCharPositions[normalizedPos] = rect.anchoredPosition;
                defaultCharScales[normalizedPos] = rect.localScale.y; // 使用 y 值作为缩放（通常 x 和 y 相同）
                Debug.Log($"[VNGameplayPanel] 保存位置 {posCode}({normalizedPos}) 的默认 Transform: 位置={rect.anchoredPosition}, 缩放={rect.localScale.y}");
            }
            // 标记该位置已被修改
            modifiedCharTransforms.Add(normalizedPos);
        }
    }
    
    /// <summary>
    /// 恢复所有被修改的角色 Transform 到默认值
    /// </summary>
    public void RestoreDefaultCharTransforms()
    {
        if (modifiedCharTransforms.Count == 0) return;
        
        foreach (string posCode in modifiedCharTransforms)
        {
            RectTransform rect = GetCharRect(posCode);
            if (rect != null && defaultCharPositions.ContainsKey(posCode) && defaultCharScales.ContainsKey(posCode))
            {
                rect.anchoredPosition = defaultCharPositions[posCode];
                
                // 恢复缩放时，保持原有的翻转状态（scale.x 的符号）
                Vector3 currentScale = rect.localScale;
                float defaultScale = defaultCharScales[posCode];
                float scaleX = Mathf.Sign(currentScale.x) * Mathf.Abs(defaultScale);
                rect.localScale = new Vector3(scaleX, defaultScale, 1f);
                
                Debug.Log($"[VNGameplayPanel] 恢复位置 {posCode} 的默认 Transform: 位置={defaultCharPositions[posCode]}, 缩放={defaultScale}");
            }
        }
        
        // 清空修改记录，准备下一行的处理
        modifiedCharTransforms.Clear();
    }
    
    /// <summary>
    /// 标准化位置代码（L/M/R）
    /// </summary>
    private string NormalizePositionCode(string posCode)
    {
        if (string.IsNullOrEmpty(posCode)) return posCode;
        string upper = posCode.ToUpper();
        if (upper == "LEFT" || upper == "L") return "L";
        if (upper == "MID" || upper == "MIDDLE" || upper == "M") return "M";
        if (upper == "RIGHT" || upper == "R") return "R";
        return posCode; // 未知格式，原样返回
    }

    /// <summary>
    /// 获取对话框的 RectTransform（用于震动）
    /// </summary>
    public RectTransform GetDialogueBoxRect()
    {
        if (uiRoot != null)
        {
            Transform dialogueBox = uiRoot.Find("DialogueBox");
            if (dialogueBox != null)
            {
                return dialogueBox.GetComponent<RectTransform>();
            }
        }
        // 备用方案：尝试通过 dialogueText 的父对象查找
        if (dialogueText != null && dialogueText.transform.parent != null)
        {
            return dialogueText.transform.parent.GetComponent<RectTransform>();
        }
        return null;
    }

    /// <summary>
    /// 保存对话文本的默认属性（在第一次修改时调用）
    /// </summary>
    private void SaveDefaultTextProperties()
    {
        if (dialogueText == null) return;

        if (!defaultDialogueTextColor.HasValue)
        {
            defaultDialogueTextColor = dialogueText.color;
        }
        if (!defaultDialogueTextSize.HasValue)
        {
            defaultDialogueTextSize = dialogueText.fontSize;
        }
        isDialogueTextModified = true;
    }

    /// <summary>
    /// 设置对话文本颜色
    /// </summary>
    public void SetDialogueTextColor(Color color)
    {
        if (dialogueText == null) return;

        SaveDefaultTextProperties(); // 保存默认值（如果还没保存）
        dialogueText.color = color;
    }

    /// <summary>
    /// 设置对话文本大小
    /// </summary>
    public void SetDialogueTextSize(float size)
    {
        if (dialogueText == null) return;

        SaveDefaultTextProperties(); // 保存默认值（如果还没保存）
        dialogueText.fontSize = size;
    }

    /// <summary>
    /// 恢复对话文本的默认属性（在下一行开始时调用）
    /// </summary>
    public void RestoreDefaultTextProperties()
    {
        if (!isDialogueTextModified || dialogueText == null) return;

        if (defaultDialogueTextColor.HasValue)
        {
            dialogueText.color = defaultDialogueTextColor.Value;
        }
        if (defaultDialogueTextSize.HasValue)
        {
            dialogueText.fontSize = defaultDialogueTextSize.Value;
        }

        // 清空修改标记，准备下一行的处理
        isDialogueTextModified = false;
    }
    #endregion
}