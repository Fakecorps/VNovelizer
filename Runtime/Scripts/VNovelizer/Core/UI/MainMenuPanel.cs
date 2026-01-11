using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 主界面面板
/// </summary>
public class MainMenuPanel : BasePanel
{
    #region UI控件引用

    [SerializeField] private Button newGameBtn;
    [SerializeField] private Button loadGameBtn;
    [SerializeField] private Button galleryBtn;
    [SerializeField] private Button settingsBtn;
    [SerializeField] private Button quitBtn;
    
    #endregion
    
    #region 初始化
    
    protected override void Awake()
    {
        base.Awake();
        
        // 初始化控件
        InitializeControls();
        
        // 绑定事件
        BindEvents();
    }
    
    /// <summary>
    /// 初始化控件
    /// </summary>
    private void InitializeControls()
    {
        newGameBtn = GetControl<Button>("NewGameBtn");
        loadGameBtn = GetControl<Button>("LoadGameBtn");
        galleryBtn = GetControl<Button>("GalleryBtn");
        settingsBtn = GetControl<Button>("SettingsBtn");
        quitBtn = GetControl<Button>("QuitBtn");
        
        // 检查关键控件是否存在
        if (newGameBtn == null)
            Debug.LogError("[MainMenuPanel] 找不到 NewGameBtn 按钮！");
        if (loadGameBtn == null)
            Debug.LogError("[MainMenuPanel] 找不到 LoadGameBtn 按钮！");
        if (galleryBtn == null)
            Debug.LogWarning("[MainMenuPanel] 找不到 GalleryBtn 按钮（可选）");
        if (settingsBtn == null)
            Debug.LogError("[MainMenuPanel] 找不到 SettingsBtn 按钮！");
        if (quitBtn == null)
            Debug.LogError("[MainMenuPanel] 找不到 QuitBtn 按钮！");
    }
    
    /// <summary>
    /// 绑定事件
    /// </summary>
    private void BindEvents()
    {
        if (newGameBtn != null)
            newGameBtn.onClick.AddListener(OnNewGameBtnClick);
        if (loadGameBtn != null)
            loadGameBtn.onClick.AddListener(OnLoadGameBtnClick);
        if (galleryBtn != null)
            galleryBtn.onClick.AddListener(OnGalleryBtnClick);
        if (settingsBtn != null)
            settingsBtn.onClick.AddListener(OnSettingsBtnClick);
        if (quitBtn != null)
            quitBtn.onClick.AddListener(OnQuitBtnClick);
    }
    
    /// <summary>
    /// 解绑事件（用于清理）
    /// </summary>
    private void UnbindEvents()
    {
        if (newGameBtn != null)
            newGameBtn.onClick.RemoveListener(OnNewGameBtnClick);
        if (loadGameBtn != null)
            loadGameBtn.onClick.RemoveListener(OnLoadGameBtnClick);
        if (galleryBtn != null)
            galleryBtn.onClick.RemoveListener(OnGalleryBtnClick);
        if (settingsBtn != null)
            settingsBtn.onClick.RemoveListener(OnSettingsBtnClick);
        if (quitBtn != null)
            quitBtn.onClick.RemoveListener(OnQuitBtnClick);
    }
    
    #endregion
    
    #region Unity生命周期
    
    protected override void OnEnable()
    {
        base.OnEnable();
        
        // 确保UIManager已初始化（主菜单场景可能没有初始化UIManager）
        if (UIManager.GetInstance() != null && UIManager.GetInstance().canvas == null)
        {
            UIManager.GetInstance().Init();
        }
        
        // 每次打开面板时刷新存档状态
        RefreshSaveButtonState();
    }
    
    public override void ShowMe()
    {
        gameObject.SetActive(true);
        
        // 刷新存档按钮状态
        RefreshSaveButtonState();
    }
    
    public override void HideMe()
    {
        gameObject.SetActive(false);
    }
    
    private void OnDestroy()
    {
        // 清理事件监听
        UnbindEvents();
    }
    
    #endregion
    
    #region 按钮事件处理
    
    /// <summary>
    /// 新游戏按钮点击
    /// </summary>
    private void OnNewGameBtnClick()
    {
        if (VNManager.GetInstance() == null)
        {
            Debug.LogError("[MainMenuPanel] VNManager 未初始化！");
            return;
        }
        
        // 从配置中读取默认剧本名称和行ID
        string defaultScriptName = "Test101"; // 默认值
        string defaultLineID = ""; // 默认从开头开始
        
        if (VNProjectConfig.Instance != null)
        {
            defaultScriptName = string.IsNullOrEmpty(VNProjectConfig.Instance.DefaultScriptName) 
                ? "Test101" 
                : VNProjectConfig.Instance.DefaultScriptName;
            defaultLineID = VNProjectConfig.Instance.DefaultLineID ?? "";
        }
        else
        {
            Debug.LogWarning("[MainMenuPanel] VNProjectConfig 未找到，使用默认值");
        }
        
        // 隐藏主菜单（VNManager.StartGame() 会自动显示游戏面板）
        UIManager.GetInstance().HidePanel("MainMenuPanel");
        
        // 启动游戏（VNManager.StartGame() 内部会调用 ShowPanel<VNGameplayPanel>）
        VNManager.GetInstance().StartGame(defaultScriptName, defaultLineID);
        
        Debug.Log($"[MainMenuPanel] 开始新游戏: 剧本={defaultScriptName}, 行ID={defaultLineID}");
    }
    
    /// <summary>
    /// 加载游戏按钮点击
    /// </summary>
    private void OnLoadGameBtnClick()
    {
        if (!loadGameBtn.interactable)
        {
            Debug.LogWarning("[MainMenuPanel] 没有可用的存档");
            return;
        }
        
        // 显示存档加载面板
        UIManager.GetInstance().ShowPanel<SaveLoadPanel>(
            "SaveLoadPanel", 
            VNProjectConfig.Instance.UI_SaveLoadPath, 
            E_UI_Layer.Middle, 
            (panel) =>
            {
                if (panel != null)
                {
                    panel.SetMode(SaveLoadPanel.Mode.Load);
                }
            }
        );
    }
    
    /// <summary>
    /// 画廊按钮点击
    /// </summary>
    private void OnGalleryBtnClick()
    {
        // 显示画廊面板
        // 注意：GalleryPanel 的路径可能需要从配置中读取
        string galleryPath = VNProjectConfig.Instance.UI_GalleryPath; // 临时使用Settings路径
        UIManager.GetInstance().ShowPanel<GalleryPanel>(
            "GalleryPanel", 
            galleryPath, 
            E_UI_Layer.Middle, 
            null
        );
    }
    
    /// <summary>
    /// 设置按钮点击
    /// </summary>
    private void OnSettingsBtnClick()
    {
        // 显示设置面板
        UIManager.GetInstance().ShowPanel<SettingsPanel>(
            "SettingsPanel", 
            VNProjectConfig.Instance.UI_SettingsPath, 
            E_UI_Layer.Middle, 
            null
        );
    }
    
    /// <summary>
    /// 退出按钮点击
    /// </summary>
    private void OnQuitBtnClick()
    {
        // 显示确认对话框
        string confirmPath = VNProjectConfig.Instance.UI_ConfirmPath;
        UIManager.GetInstance().ShowPanel<ConfirmPanel>(
            "ConfirmPanel", 
            confirmPath, 
            E_UI_Layer.System, 
            (panel) =>
            {
                if (panel != null)
                {
                    panel.Show(
                        "退出游戏",
                        "确定要退出游戏吗？",
                        () =>
                        {
                            // 确定退出
                            Debug.Log("[MainMenuPanel] 退出游戏");
                            Application.Quit();
                            
                            // 在编辑器中，Application.Quit() 不会生效，使用这个替代
                            #if UNITY_EDITOR
                            UnityEditor.EditorApplication.isPlaying = false;
                            #endif
                        },
                        null // 取消无需操作
                    );
                }
            }
        );
    }
    
    #endregion
    
    #region 辅助方法
    
    /// <summary>
    /// 刷新存档按钮状态
    /// </summary>
    private void RefreshSaveButtonState()
    {
        if (loadGameBtn == null) return;
        
        // 检查是否存在存档
        bool hasSave = false;
        if (SaveManager.GetInstance() != null)
        {
            var saveDataList = SaveManager.GetInstance().GetAllSaveData();
            hasSave = saveDataList != null && saveDataList.Count > 0;
        }
        
        loadGameBtn.interactable = hasSave;
        
        if (hasSave)
        {
            Debug.Log("[MainMenuPanel] 检测到存档，加载按钮已启用");
        }
        else
        {
            Debug.Log("[MainMenuPanel] 未检测到存档，加载按钮已禁用");
        }
    }
    
    #endregion
}