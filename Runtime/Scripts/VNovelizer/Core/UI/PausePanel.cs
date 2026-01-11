using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using PrimeTween;
using VNovelizer.Core.API;

/// <summary>
/// 暂停面板
/// </summary>
public class PausePanel : BasePanel
{
    #region UI控件引用

    [SerializeField] private Button saveBtn;
    [SerializeField] private Button loadBtn;
    [SerializeField] private Button settingsBtn;
    [SerializeField] private Button exitBtn;
    
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
        saveBtn = GetControl<Button>("SaveBtn");
        loadBtn = GetControl<Button>("LoadBtn");
        settingsBtn = GetControl<Button>("SettingsBtn");
        exitBtn = GetControl<Button>("ExitBtn");
        
        // 检查关键控件是否存在
        if (saveBtn == null)
            Debug.LogError("[PausePanel] 找不到 SaveBtn 按钮！");
        if (loadBtn == null)
            Debug.LogError("[PausePanel] 找不到 LoadBtn 按钮！");
        if (settingsBtn == null)
            Debug.LogError("[PausePanel] 找不到 SettingsBtn 按钮！");
        if (exitBtn == null)
            Debug.LogError("[PausePanel] 找不到 ExitBtn 按钮！");
    }
    
    /// <summary>
    /// 绑定事件
    /// </summary>
    private void BindEvents()
    {
        if (saveBtn != null)
            saveBtn.onClick.AddListener(OnSaveBtnClick);
        if (loadBtn != null)
            loadBtn.onClick.AddListener(OnLoadBtnClick);
        if (settingsBtn != null)
            settingsBtn.onClick.AddListener(OnSettingsBtnClick);
        if (exitBtn != null)
            exitBtn.onClick.AddListener(OnExitBtnClick);
    }
    
    /// <summary>
    /// 解绑事件（用于清理）
    /// </summary>
    private void UnbindEvents()
    {
        if (saveBtn != null)
            saveBtn.onClick.RemoveListener(OnSaveBtnClick);
        if (loadBtn != null)
            loadBtn.onClick.RemoveListener(OnLoadBtnClick);
        if (settingsBtn != null)
            settingsBtn.onClick.RemoveListener(OnSettingsBtnClick);
        if (exitBtn != null)
            exitBtn.onClick.RemoveListener(OnExitBtnClick);
    }
    
    #endregion
    
    #region Unity生命周期
    
    protected override void OnEnable()
    {
        base.OnEnable();
        
        // 每次打开面板时设置状态
        // 检查是否可以打开（允许在Gameplay、AutoPlay、Choice和Pause状态下打开）
        GameState currentState = GameStateManager.GetInstance().CurrentState;
        if (currentState != GameState.Gameplay && currentState != GameState.AutoPlay && 
            currentState != GameState.Choice && currentState != GameState.Pause)
        {
            Debug.LogWarning($"[PausePanel] 当前状态 {currentState} 不允许打开暂停面板，已关闭");
            gameObject.SetActive(false);
            return;
        }
        
        // 只有在非Pause状态时才设置Pause状态
        // 如果已经在Pause状态，不需要重复设置
        if (currentState != GameState.Pause)
        {
            GameStateManager.GetInstance().SetState(GameState.Pause);
        }
    }
    
    public override void ShowMe()
    {
        gameObject.SetActive(true);
    }
    
    public override void HideMe()
    {
        gameObject.SetActive(false);
        
        // 恢复游戏状态
        if (GameStateManager.GetInstance() != null && 
            GameStateManager.GetInstance().CurrentState == GameState.Pause)
        {
            GameStateManager.GetInstance().RestoreState();
        }
    }
    
    private void OnDestroy()
    {
        // 清理事件监听
        UnbindEvents();
        
        // 面板被Destroy时，如果当前状态是Pause，需要恢复游戏状态
        if (GameStateManager.GetInstance() != null && 
            GameStateManager.GetInstance().CurrentState == GameState.Pause)
        {
            GameStateManager.GetInstance().RestoreState();
            Debug.Log("[PausePanel] 面板被Destroy，已恢复游戏状态");
        }
    }
    
    #endregion
    
    #region 按钮事件处理
    
    /// <summary>
    /// 保存按钮点击
    /// </summary>
    private void OnSaveBtnClick()
    {
        // 检查是否可以打开保存系统面板
        if (!GameStateManager.GetInstance().CanOpenPanel(GameState.SaveLoad))
        {
            Debug.LogWarning("[PausePanel] 当前状态不允许打开保存系统面板");
            return;
        }
        
        // 隐藏暂停面板（不关闭，保持Pause状态）
        gameObject.SetActive(false);
        
        // 【Bug修复】截图已经在打开PausePanel之前完成，这里不需要再次截屏
        // 打开保存面板
        UIManager.GetInstance().ShowPanel<SaveLoadPanel>("SaveLoadPanel", VNProjectConfig.Instance.UI_SaveLoadPath, E_UI_Layer.Top, (panel) =>
        {
            panel.SetMode(SaveLoadPanel.Mode.Save);
        });
    }
    
    /// <summary>
    /// 加载按钮点击
    /// </summary>
    private void OnLoadBtnClick()
    {
        // 检查是否可以打开保存系统面板
        if (!GameStateManager.GetInstance().CanOpenPanel(GameState.SaveLoad))
        {
            Debug.LogWarning("[PausePanel] 当前状态不允许打开保存系统面板");
            return;
        }
        
        // 隐藏暂停面板（不关闭，保持Pause状态）
        gameObject.SetActive(false);
        
        // 打开加载面板
        UIManager.GetInstance().ShowPanel<SaveLoadPanel>("SaveLoadPanel", VNProjectConfig.Instance.UI_SaveLoadPath, E_UI_Layer.Top, (panel) =>
        {
            panel.SetMode(SaveLoadPanel.Mode.Load);
        });
    }
    
    /// <summary>
    /// 设置按钮点击
    /// </summary>
    private void OnSettingsBtnClick()
    {
        // 检查是否可以打开设置面板
        if (!GameStateManager.GetInstance().CanOpenPanel(GameState.Settings))
        {
            Debug.LogWarning("[PausePanel] 当前状态不允许打开设置面板");
            return;
        }
        
        // 隐藏暂停面板（不关闭，保持Pause状态）
        gameObject.SetActive(false);
        
        // 打开设置面板
        string path = VNProjectConfig.Instance != null ? VNProjectConfig.Instance.UI_SettingsPath : "VNPrefabs/UI/Settings";
        if (string.IsNullOrEmpty(path)) path = "VNPrefabs/UI/Settings";
        UIManager.GetInstance().ShowPanel<SettingsPanel>("SettingsPanel", path, E_UI_Layer.Top, null);
    }
    
    /// <summary>
    /// 退出按钮点击 - 返回主菜单场景
    /// </summary>
    private void OnExitBtnClick()
    {
        // 关闭暂停面板
        UIManager.GetInstance().HidePanel("PausePanel");
        
        // 恢复游戏状态
        if (GameStateManager.GetInstance() != null && 
            GameStateManager.GetInstance().CurrentState == GameState.Pause)
        {
            GameStateManager.GetInstance().RestoreState();
            PrimeTween.Tween.StopAll();
            VNAPI.ClearAllEffects();
            PoolManager.GetInstance().Clear();
        }
        

        // 加载主菜单场景
        SceneManager.LoadScene("VNMainMenu");
        
        Debug.Log("[PausePanel] 返回主菜单场景");
    }
    
    #endregion
}

