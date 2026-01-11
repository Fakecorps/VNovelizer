using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.InputSystem;

/// <summary>
/// 画廊面板
/// </summary>
public class GalleryPanel : BasePanel
{
    // 页面切换按钮
    [SerializeField] private Button cgButton;
    [SerializeField] private Button sceneButton;
    [SerializeField] private Button musicButton;
    
    // 页面容器
    private GameObject pageContainer;
    private CGPage cgPage; // 改为使用CGPage组件
    private ScenePage scenePage; // 改为使用ScenePage组件
    private MusicPage musicPage; // 改为使用MusicPage组件
    
    // 状态
    public enum GalleryPage { CG, Scene, Music }
    private GalleryPage currentPage = GalleryPage.CG;
    
    /// <summary>
    /// 切换页面（供外部调用）
    /// </summary>
    public void SwitchPage(GalleryPage page)
    {
        SwitchPageInternal(page);
    }

    private void SwitchPageInternal(GalleryPage page)
    {
        currentPage = page;
        
        // 隐藏所有页面
        if (cgPage != null) cgPage.Hide();
        if (scenePage != null) scenePage.Hide();
        if (musicPage != null) musicPage.Hide();
        
        // 显示当前页面
        switch (page)
        {
            case GalleryPage.CG:
                if (cgPage != null)
                {
                    cgPage.Show();
                }
                break;
            case GalleryPage.Scene:
                if (scenePage != null)
                {
                    scenePage.Show();
                }
                break;
            case GalleryPage.Music:
                if (musicPage != null)
                {
                    musicPage.Show();
                }
                break;
        }
        
        // 更新按钮状态
        UpdatePageButtons();
    }
    
    protected override void Awake()
    {
        base.Awake();
        
        // 获取页面切换按钮
        cgButton = GetControl<Button>("CGBtn");
        sceneButton = GetControl<Button>("SceneBtn");
        musicButton = GetControl<Button>("MusicBtn");
        
        // 获取页面容器
        pageContainer = transform.Find("PageContainer")?.gameObject;
        if (pageContainer != null)
        {
            GameObject cgPageObj = pageContainer.transform.Find("CGPage")?.gameObject;
            if (cgPageObj != null)
            {
                cgPage = cgPageObj.GetComponent<CGPage>();
                if (cgPage == null)
                {
                    cgPage = cgPageObj.AddComponent<CGPage>();
                }
            }
            GameObject scenePageObj = pageContainer.transform.Find("ScenePage")?.gameObject;
            if (scenePageObj != null)
            {
                scenePage = scenePageObj.GetComponent<ScenePage>();
                if (scenePage == null)
                {
                    scenePage = scenePageObj.AddComponent<ScenePage>();
                }
            }
            
            GameObject musicPageObj = pageContainer.transform.Find("MusicPage")?.gameObject;
            if (musicPageObj != null)
            {
                musicPage = musicPageObj.GetComponent<MusicPage>();
                if (musicPage == null)
                {
                    musicPage = musicPageObj.AddComponent<MusicPage>();
                }
            }
        }
        
        // 绑定事件
        if (cgButton != null) cgButton.onClick.AddListener(() => SwitchPageInternal(GalleryPage.CG));
        if (sceneButton != null) sceneButton.onClick.AddListener(() => SwitchPageInternal(GalleryPage.Scene));
        if (musicButton != null) musicButton.onClick.AddListener(() => SwitchPageInternal(GalleryPage.Music));
    }
    
    protected override void OnEnable()
    {
        base.OnEnable();
        
        // 切换到当前页面
        SwitchPageInternal(currentPage);
    }
    
    public override void ShowMe()
    {
        gameObject.SetActive(true);
    }
    
    public override void HideMe()
    {
        gameObject.SetActive(false);
        // 页面会自己处理清理
        if (cgPage != null)
        {
            cgPage.Hide();
        }
        if (scenePage != null)
        {
            scenePage.Hide();
        }
        if (musicPage != null)
        {
            musicPage.Hide();
        }
    }
    
    private void Update()
    {
        // 检测ESC键关闭GalleryPanel（使用新版Input System）
        // 只有在ImageViewer未显示时才响应ESC（如果ImageViewer显示，ESC应该关闭ImageViewer）
        bool isImageViewerShowing = false;
        if (cgPage != null)
        {
            ImageViewer viewer = cgPage.GetImageViewer();
            isImageViewerShowing = viewer != null && viewer.gameObject.activeSelf;
        }
        
        if (!isImageViewerShowing)
        {
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                UIManager.GetInstance().HidePanel("GalleryPanel");
            }
        }
    }
    
    /// <summary>
    /// 更新页面按钮状态
    /// </summary>
    private void UpdatePageButtons()
    {
        // 可以在这里添加按钮高亮效果
    }
    
    protected override void OnButtonClick(string ButtonName)
    {
        if (ButtonName == "G_CloseBtn")
        {
            UIManager.GetInstance().HidePanel("GalleryPanel");
        }
    }
}


