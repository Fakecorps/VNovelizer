using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 场景回放页面
/// </summary>
public class ScenePage : MonoBehaviour
{
    // UI组件
    private GridLayoutGroup sceneGridLayout;
    private Transform sceneContentTransform;
    [SerializeField] private Button scenePrevPageButton;
    [SerializeField] private Button sceneNextPageButton;
    [SerializeField] private TextMeshProUGUI scenePageText;
    
    // 状态
    private int currentScenePage = 0;
    private const int SCENE_PER_PAGE = 12; // 每页显示的场景数量
    
    // 数据
    private SceneDataContainer sceneDataContainer;
    private GlobalData globalData;
    private List<SceneSlot> sceneSlots = new List<SceneSlot>();
    private List<VNScene> allSceneData = new List<VNScene>();
    
    // 场景槽位预制体
    private GameObject sceneSlotPrefab;
    
    private void Awake()
    {
        // 获取场景回放相关控件
        Transform sceneContainer = transform.Find("SceneSlotContainer");
        if (sceneContainer != null)
        {
            sceneGridLayout = sceneContainer.GetComponent<GridLayoutGroup>();
            sceneContentTransform = sceneContainer;
        }
        
        // 翻页控件在Pagination下
        Transform pagination = transform.Find("Sc_Pagination");
        if (pagination != null)
        {
            scenePrevPageButton = pagination.Find("ScPrevBtn")?.GetComponent<Button>();
            sceneNextPageButton = pagination.Find("ScNextBtn")?.GetComponent<Button>();
            scenePageText = pagination.Find("ScPageText")?.GetComponent<TextMeshProUGUI>();
        }
        
        // 绑定翻页按钮事件
        if (scenePrevPageButton != null) scenePrevPageButton.onClick.AddListener(OnScenePrevPage);
        if (sceneNextPageButton != null) sceneNextPageButton.onClick.AddListener(OnSceneNextPage);
        
        // 监听场景解锁事件
        EventCenter.GetInstance().AddEventListener<string>("SceneUnlocked", OnSceneUnlocked);
    }
    
    /// <summary>
    /// 初始化场景页面
    /// </summary>
    public void Initialize()
    {
        // 加载全局数据（GlobalDataManager会自动初始化）
        globalData = GlobalDataManager.GetInstance().GetGlobalData();
        
        // 加载场景数据容器
        LoadSceneDataContainer();
        
        // 加载场景回放列表
        LoadSceneGallery();
    }
    
    /// <summary>
    /// 显示场景页面
    /// </summary>
    public void Show()
    {
        gameObject.SetActive(true);
        Initialize();
    }
    
    /// <summary>
    /// 隐藏场景页面
    /// </summary>
    public void Hide()
    {
        gameObject.SetActive(false);
        ClearSceneSlots();
    }
    
    private void OnDestroy()
    {
        // 移除事件监听
        EventCenter.GetInstance().RemoveEventListener<string>("SceneUnlocked", OnSceneUnlocked);
    }
    
    /// <summary>
    /// 加载场景数据容器
    /// </summary>
    private void LoadSceneDataContainer()
    {
        string path = VNProjectConfig.Instance.Scene_DataPath + "/SceneDataContainer";
        
        sceneDataContainer = ResourcesManager.GetInstance().Load<SceneDataContainer>(path);
        
        if (sceneDataContainer == null)
        {
            Debug.LogWarning($"[ScenePage] 未找到场景数据容器: {path}");
            allSceneData = new List<VNScene>();
        }
        else
        {
            allSceneData = new List<VNScene>(sceneDataContainer.sceneList);
        }
    }
    
    /// <summary>
    /// 加载场景回放列表
    /// </summary>
    private void LoadSceneGallery()
    {
        ClearSceneSlots();
        
        if (sceneContentTransform == null)
        {
            Debug.LogError("[ScenePage] 场景内容容器未找到");
            return;
        }
        
        // 加载场景槽位预制体
        if (sceneSlotPrefab == null)
        {
            string loadPath = VNProjectConfig.Instance.UI_GalleryPath + "/Scene";
            sceneSlotPrefab = ResourcesManager.GetInstance().Load<GameObject>(loadPath + "/SceneSlot");
        }
        
        if (sceneSlotPrefab == null)
        {
            Debug.LogError("[ScenePage] 场景槽位预制体未找到");
            return;
        }
        
        // 更新场景页面
        UpdateScenePage();
    }
    
    /// <summary>
    /// 更新场景页面
    /// </summary>
    private void UpdateScenePage()
    {
        if (sceneContentTransform == null || sceneSlotPrefab == null) return;
        
        // 清空现有场景槽位
        foreach (Transform child in sceneContentTransform)
        {
            Destroy(child.gameObject);
        }
        sceneSlots.Clear();
        
        // 计算总页数
        int totalPages = Mathf.CeilToInt((float)allSceneData.Count / SCENE_PER_PAGE);
        if (totalPages == 0) totalPages = 1;
        
        // 更新页面文本
        if (scenePageText != null)
        {
            scenePageText.text = $"{currentScenePage + 1}/{totalPages}";
        }
        
        // 更新翻页按钮状态
        if (scenePrevPageButton != null)
        {
            scenePrevPageButton.interactable = currentScenePage > 0;
        }
        if (sceneNextPageButton != null)
        {
            sceneNextPageButton.interactable = currentScenePage < totalPages - 1;
        }
        
        // 计算当前页的场景范围
        int startIndex = currentScenePage * SCENE_PER_PAGE;
        int endIndex = Mathf.Min(startIndex + SCENE_PER_PAGE, allSceneData.Count);
        
        // 创建当前页的场景槽位
        for (int i = startIndex; i < endIndex; i++)
        {
            VNScene sceneData = allSceneData[i];
            if (sceneData != null)
            {
                CreateSceneSlot(sceneData);
            }
        }
    }
    
    /// <summary>
    /// 创建场景槽位
    /// </summary>
    private void CreateSceneSlot(VNScene sceneData)
    {
        if (sceneSlotPrefab == null || sceneContentTransform == null) return;
        
        if (sceneData == null)
        {
            Debug.LogWarning("[ScenePage] VNScene为null，跳过创建槽位");
            return;
        }
        
        GameObject slotObj = Instantiate(sceneSlotPrefab, sceneContentTransform);
        SceneSlot slot = slotObj.GetComponent<SceneSlot>();
        if (slot == null)
        {
            slot = slotObj.AddComponent<SceneSlot>();
        }
        
        // 检查是否已解锁（需要确保globalData不为null）
        bool isUnlocked = false;
        if (globalData != null && globalData.UnlockedScenes != null && !string.IsNullOrEmpty(sceneData.VNscriptID))
        {
            isUnlocked = globalData.UnlockedScenes.Contains(sceneData.VNscriptID);
        }
        
        // 同步编辑器中的调试设置
        if (sceneData.isUnLocked && !isUnlocked && globalData != null && globalData.UnlockedScenes != null)
        {
            if (!string.IsNullOrEmpty(sceneData.VNscriptID))
            {
                globalData.UnlockedScenes.Add(sceneData.VNscriptID);
                isUnlocked = true;
                GlobalDataManager.GetInstance().UnlockScene(sceneData.VNscriptID); // 这会保存到文件
                Debug.Log($"[ScenePage] 同步场景解锁状态: {sceneData.VNscriptID}");
            }
        }
        
        // 初始化场景槽位
        slot.Init(sceneData, isUnlocked, OnSceneSlotClick);
        
        sceneSlots.Add(slot);
    }
    
    /// <summary>
    /// 场景槽位点击事件
    /// </summary>
    private void OnSceneSlotClick(VNScene sceneData)
    {
        if (sceneData != null && !string.IsNullOrEmpty(sceneData.ScriptName))
        {
            //记录主菜单状态（在隐藏之前）
            MainMenuPanel mainMenuPanel = UIManager.GetInstance().GetPanel<MainMenuPanel>("MainMenuPanel");
            bool wasMainMenuVisible = mainMenuPanel != null && mainMenuPanel.gameObject.activeSelf;
            
            //隐藏画廊面板
            GalleryPanel galleryPanel = UIManager.GetInstance().GetPanel<GalleryPanel>("GalleryPanel");
            if (galleryPanel != null)
            {
                galleryPanel.gameObject.SetActive(false);
            }
            
            //隐藏主菜单面板
            if (mainMenuPanel != null)
            {
                mainMenuPanel.gameObject.SetActive(false);
            }
            
            //启动场景回放（传递主菜单状态）
            VNManager.GetInstance().StartSceneReplay(
                sceneData.ScriptName,
                sceneData.StartLineID,
                sceneData.EndLineID,
                wasMainMenuVisible
            );
        }
    }
    
    /// <summary>
    /// 场景上一页
    /// </summary>
    private void OnScenePrevPage()
    {
        if (currentScenePage > 0)
        {
            currentScenePage--;
            UpdateScenePage();
        }
    }
    
    /// <summary>
    /// 场景下一页
    /// </summary>
    private void OnSceneNextPage()
    {
        int totalPages = Mathf.CeilToInt((float)allSceneData.Count / SCENE_PER_PAGE);
        if (totalPages == 0) totalPages = 1;
        
        if (currentScenePage < totalPages - 1)
        {
            currentScenePage++;
            UpdateScenePage();
        }
    }
    
    /// <summary>
    /// 清理场景槽位
    /// </summary>
    private void ClearSceneSlots()
    {
        if (sceneContentTransform != null)
        {
            foreach (Transform child in sceneContentTransform)
            {
                Destroy(child.gameObject);
            }
        }
        sceneSlots.Clear();
    }
    
    /// <summary>
    /// 场景解锁事件处理
    /// </summary>
    private void OnSceneUnlocked(string sceneID)
    {
        // 更新场景槽位状态
        foreach (SceneSlot slot in sceneSlots)
        {
            if (slot != null && slot.sceneData != null && slot.sceneData.VNscriptID == sceneID)
            {
                slot.Unlock();
                break;
            }
        }
    }
}

