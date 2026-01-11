using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// CG画廊页面
/// </summary>
public class CGPage : MonoBehaviour
{
    // UI组件
                     private GridLayoutGroup cgGridLayout;
                     private Transform cgContentTransform;
    [SerializeField] private Button cgPrevPageButton;
    [SerializeField] private Button cgNextPageButton;
    [SerializeField] private TextMeshProUGUI cgPageText;
                     private ImageViewer imageViewer;
    
    // 状态
    private int currentCGPage = 0;
    private const int CG_PER_PAGE = 12; // 每页显示的CG数量
    
    // 数据
    private CGDataContainer cgDataContainer;
    private GlobalData globalData;
    private List<CGSlot> cgSlots = new List<CGSlot>();
    private List<CGData> allCGData = new List<CGData>();
    
    // CG槽位预制体
    private GameObject cgSlotPrefab;
    
    private void Awake()
    {
        // 获取CG画廊相关控件
        // CGContainer是GridLayoutGroup的直接父对象
        Transform cgContainer = transform.Find("CGSlotContainer");
        if (cgContainer != null)
        {
            cgGridLayout = cgContainer.GetComponent<GridLayoutGroup>();
            cgContentTransform = cgContainer;
        }
        
        // 翻页控件在Pagination下
        Transform pagination = transform.Find("CG_Pagination");
        if (pagination != null)
        {
            cgPrevPageButton = pagination.Find("CGPrevBtn")?.GetComponent<Button>();
            cgNextPageButton = pagination.Find("CGNextBtn")?.GetComponent<Button>();
            cgPageText = pagination.Find("CGPageText")?.GetComponent<TextMeshProUGUI>();
        }
        
        // ImageViewer在CGPage下
        imageViewer = GetComponentInChildren<ImageViewer>();
        
        // 绑定翻页按钮事件
        if (cgPrevPageButton != null) cgPrevPageButton.onClick.AddListener(OnCGPrevPage);
        if (cgNextPageButton != null) cgNextPageButton.onClick.AddListener(OnCGNextPage);
        
        // 监听CG解锁事件
        EventCenter.GetInstance().AddEventListener<string>("CGUnlocked", OnCGUnlocked);
    }
    
    /// <summary>
    /// 初始化CG页面
    /// </summary>
    public void Initialize()
    {
        // 加载全局数据（GlobalDataManager会自动初始化）
        globalData = GlobalDataManager.GetInstance().GetGlobalData();
        
        // 加载CG数据容器
        LoadCGDataContainer();
        
        // 加载CG画廊
        LoadCGGallery();
    }
    
    /// <summary>
    /// 显示CG页面
    /// </summary>
    public void Show()
    {
        gameObject.SetActive(true);
        Initialize();
    }
    
    /// <summary>
    /// 隐藏CG页面
    /// </summary>
    public void Hide()
    {
        gameObject.SetActive(false);
        ClearCGThumbnails();
    }
    
    private void OnDestroy()
    {
        // 移除事件监听
        EventCenter.GetInstance().RemoveEventListener<string>("CGUnlocked", OnCGUnlocked);
    }
    
    /// <summary>
    /// 加载CG数据容器
    /// </summary>
    private void LoadCGDataContainer()
    {
        string path = VNProjectConfig.Instance.CG_DataPath + "/CGDataContainer";
        Debug.Log(path);
        
        cgDataContainer = ResourcesManager.GetInstance().Load<CGDataContainer>(path);
        
        if (cgDataContainer == null)
        {
            Debug.LogWarning($"[CGPage] 未找到CG数据容器: {path}");
            allCGData = new List<CGData>();
        }
        else
        {
            allCGData = new List<CGData>(cgDataContainer.cgList);
        }
    }
    
    /// <summary>
    /// 加载CG画廊
    /// </summary>
    private void LoadCGGallery()
    {
        ClearCGThumbnails();
        
        if (cgContentTransform == null)
        {
            Debug.LogError("[CGPage] CG内容容器未找到");
            return;
        }
        
        // 加载CG槽位预制体
        if (cgSlotPrefab == null)
        {
            string loadPath = VNProjectConfig.Instance.UI_GalleryPath + "/CG";
            cgSlotPrefab = ResourcesManager.GetInstance().Load<GameObject>(loadPath + "/CGSlot");
        }
        
        if (cgSlotPrefab == null)
        {
            Debug.LogError("[CGPage] CG槽位预制体未找到");
            return;
        }
        
        // 更新CG页面
        UpdateCGPage();
    }
    
    /// <summary>
    /// 更新CG页面
    /// </summary>
    private void UpdateCGPage()
    {
        if (cgContentTransform == null || cgSlotPrefab == null) return;
        
        // 清空现有CG槽位
        foreach (Transform child in cgContentTransform)
        {
            Destroy(child.gameObject);
        }
        cgSlots.Clear();
        
        // 计算总页数
        int totalPages = Mathf.CeilToInt((float)allCGData.Count / CG_PER_PAGE);
        if (totalPages == 0) totalPages = 1;
        
        // 更新页面文本
        if (cgPageText != null)
        {
            cgPageText.text = $"{currentCGPage + 1}/{totalPages}";
        }
        
        // 更新翻页按钮状态
        if (cgPrevPageButton != null)
        {
            cgPrevPageButton.interactable = currentCGPage > 0;
        }
        if (cgNextPageButton != null)
        {
            cgNextPageButton.interactable = currentCGPage < totalPages - 1;
        }
        
        // 计算当前页的CG范围
        int startIndex = currentCGPage * CG_PER_PAGE;
        int endIndex = Mathf.Min(startIndex + CG_PER_PAGE, allCGData.Count);
        
        // 创建当前页的CG槽位
        for (int i = startIndex; i < endIndex; i++)
        {
            CGData cgData = allCGData[i];
            if (cgData != null)
            {
                CreateCGSlot(cgData);
            }
        }
    }
    
    /// <summary>
    /// 创建CG槽位
    /// </summary>
    private void CreateCGSlot(CGData cgData)
    {
        if (cgSlotPrefab == null || cgContentTransform == null) return;
        
        if (cgData == null)
        {
            Debug.LogWarning("[CGPage] CGData为null，跳过创建槽位");
            return;
        }
        
        GameObject slotObj = Instantiate(cgSlotPrefab, cgContentTransform);
        CGSlot slot = slotObj.GetComponent<CGSlot>();
        if (slot == null)
        {
            slot = slotObj.AddComponent<CGSlot>();
        }
        
        // 检查是否已解锁（需要确保globalData不为null）
        bool isUnlocked = false;
        if (globalData != null && globalData.UnlockedCGs != null && !string.IsNullOrEmpty(cgData.cgName))
        {
            isUnlocked = globalData.UnlockedCGs.Contains(cgData.cgName);
        }
        
        //同步编辑器中的调试设置
        if (cgData.isUnlocked && !isUnlocked && globalData != null && globalData.UnlockedCGs != null)
        {
            if (!string.IsNullOrEmpty(cgData.cgName))
            {
                globalData.UnlockedCGs.Add(cgData.cgName);
                isUnlocked = true;
                GlobalDataManager.GetInstance().UnlockCG(cgData.cgName); // 这会保存到文件
                Debug.Log($"[CGPage] 同步CG解锁状态: {cgData.cgName}");
            }
        }
        
        // 初始化CG槽位
        slot.Init(cgData, isUnlocked, OnCGSlotClick);
        
        cgSlots.Add(slot);
    }
    
    /// <summary>
    /// CG槽位点击事件
    /// </summary>
    private void OnCGSlotClick(CGData cgData)
    {
        if (imageViewer != null && cgData != null && cgData.sprites != null && cgData.sprites.Count > 0)
        {
            imageViewer.Show(cgData.sprites);
        }
    }
    
    /// <summary>
    /// CG上一页
    /// </summary>
    private void OnCGPrevPage()
    {
        if (currentCGPage > 0)
        {
            currentCGPage--;
            UpdateCGPage();
        }
    }
    
    /// <summary>
    /// CG下一页
    /// </summary>
    private void OnCGNextPage()
    {
        int totalPages = Mathf.CeilToInt((float)allCGData.Count / CG_PER_PAGE);
        if (totalPages == 0) totalPages = 1;
        
        if (currentCGPage < totalPages - 1)
        {
            currentCGPage++;
            UpdateCGPage();
        }
    }
    
    /// <summary>
    /// 清理CG槽位
    /// </summary>
    private void ClearCGThumbnails()
    {
        if (cgContentTransform != null)
        {
            foreach (Transform child in cgContentTransform)
            {
                Destroy(child.gameObject);
            }
        }
        cgSlots.Clear();
    }
    
    /// <summary>
    /// CG解锁事件处理
    /// </summary>
    private void OnCGUnlocked(string cgName)
    {
        // 更新CG槽位状态
        foreach (CGSlot slot in cgSlots)
        {
            if (slot != null && slot.cgData != null && slot.cgData.cgName == cgName)
            {
                slot.Unlock();
                break;
            }
        }
    }
    
    /// <summary>
    /// 获取ImageViewer（供外部检查是否正在显示）
    /// </summary>
    public ImageViewer GetImageViewer()
    {
        return imageViewer;
    }
}

