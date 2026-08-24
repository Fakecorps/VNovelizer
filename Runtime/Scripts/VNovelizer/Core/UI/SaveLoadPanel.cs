using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class SaveLoadPanel : BasePanel
{
    // 面板类型
    public enum Mode { Save, Load }

    // 自动存档槽的虚拟索引（不占用 0~59 手动槽；回调据此路由）
    private const int AUTO_SLOT_INDEX = -1;

    [Header("自动存档设置")]
    [Tooltip("启用自动存档系统（每 N 行 / 选项选择后 / 跨剧本切换前）")]
    [SerializeField] private bool enableAutoSave = true;
    [Tooltip("每播放 N 行剧情自动保存一次")]
    [SerializeField] private int autoSaveEveryLines = 10;
    [Tooltip("玩家做出选项选择后自动保存")]
    [SerializeField] private bool autoSaveOnChoice = true;
    [Tooltip("跨剧本切换(loadscript)前自动保存")]
    [SerializeField] private bool autoSaveOnScriptSwitch = true;

    [Header("截图缩略图")]
    [Tooltip("存档截图缩略图的最长边像素（保存时下采样；文件更小、面板加载更快）")]
    [SerializeField] private int screenshotThumbnailSize = 480;

    // UI组件
    private Button closeButton;
    private TextMeshProUGUI modeTitle;
    private Button prevPageButton;
    private Button nextPageButton;
    private TextMeshProUGUI pageText;
    private Transform saveSlotsContainer;

    // 状态
    private Mode currentMode = Mode.Save;
    private int currentPage = 0;
    private const int SLOTS_PER_PAGE = 12; // 确保这里是你想要的每页数量
    // 【修改】从SaveManager获取最大存档槽位数，确保一致性
    private int MAX_SAVE_SLOTS => SaveManager.GetInstance().GetMaxSaveSlots();

    // 存档槽位预制体
    private GameObject saveSlotPrefab;

    // 自动存档槽（固定在容器第一位，不随翻页销毁）
    private SaveSlot autoSaveSlot;
    private const string AUTO_SLOT_NAME = "AutoSaveSlot";

    // 存档数据（延迟初始化，在Awake中根据MAX_SAVE_SLOTS创建）
    private SaveData[] saveDatas;

    //读取存档协程变量
    private bool _isLoadingGame = false;
    
    protected override void Awake()
    {
        base.Awake();

        // 获取组件
        closeButton = GetControl<Button>("CloseButton");
        modeTitle = GetControl<TextMeshProUGUI>("ModeTitle");
        prevPageButton = GetControl<Button>("PrevPage");
        nextPageButton = GetControl<Button>("NextPage");
        pageText = GetControl<TextMeshProUGUI>("PageText");
        saveSlotsContainer = transform.Find("SaveSlotsContainer");

        // 绑定事件
        closeButton.onClick.AddListener(OnCloseButtonClick);
        prevPageButton.onClick.AddListener(OnPrevPageButtonClick);
        nextPageButton.onClick.AddListener(OnNextPageButtonClick);

        // 初始化存档数据数组（根据实际的最大槽位数）
        int maxSlots = SaveManager.GetInstance().GetMaxSaveSlots();
        saveDatas = new SaveData[maxSlots];
        Debug.Log($"[SaveLoadPanel] 初始化存档数据数组，最大槽位数: {maxSlots}");

        // 加载存档槽位预制体（模板覆写优先，fallback 经资源服务链；键即默认地址）
        saveSlotPrefab = VNUIPrefabs.Load(VNUIPrefabKeys.SaveSlot, VNUIPrefabKeys.SaveSlot);

        // 自动存档：把 Inspector 配置推送到运行时（VNManager 触发逻辑读取）
        PushAutoSaveConfigToManager();

        // 截图缩略图尺寸（保存时下采样）
        SaveManager.ThumbnailMaxSize = Mathf.Max(64, screenshotThumbnailSize);

        // 自动存档槽：预制体中不存在则自动创建，固定在容器第一位
        EnsureAutoSaveSlot();
    }

    /// <summary>
    /// 把本预制体上配置的自动存档参数推送到 SaveManager（供 VNManager 触发逻辑读取）
    /// </summary>
    public void PushAutoSaveConfigToManager()
    {
        SaveManager.ApplyAutoSaveConfig(enableAutoSave, autoSaveEveryLines, autoSaveOnChoice, autoSaveOnScriptSwitch);
    }

    /// <summary>
    /// 确保自动存档槽节点存在：预制体中手工放置或此处自动创建（基于 SaveSlot 模板），
    /// 固定位于 SaveSlotsContainer 第一位。
    /// </summary>
    private void EnsureAutoSaveSlot()
    {
        if (saveSlotsContainer == null || saveSlotPrefab == null) return;

        Transform autoSlotTrans = saveSlotsContainer.Find(AUTO_SLOT_NAME);
        if (autoSlotTrans == null)
        {
            GameObject autoObj = Instantiate(saveSlotPrefab, saveSlotsContainer);
            autoObj.name = AUTO_SLOT_NAME;
            autoSlotTrans = autoObj.transform;
        }
        autoSlotTrans.SetSiblingIndex(0);

        autoSaveSlot = autoSlotTrans.GetComponent<SaveSlot>();
        if (autoSaveSlot == null) autoSaveSlot = autoSlotTrans.gameObject.AddComponent<SaveSlot>();
    }

    /// <summary>
    /// 刷新自动存档槽显示（打开面板 / 模式切换 / 保存删除后调用）
    /// </summary>
    private void RefreshAutoSaveSlot()
    {
        if (autoSaveSlot == null) return;
        SaveData autoData = SaveManager.GetInstance().LoadAutoGame();
        autoSaveSlot.Init(AUTO_SLOT_INDEX, autoData, currentMode, OnSaveSlotClick, OnDeleteSlotClick, true);
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        
        // 每次打开面板时设置状态（而不是在Awake中，因为ShowPanel可能复用已存在的面板）
        // 检查是否可以打开
        if (!GameStateManager.GetInstance().CanOpenPanel(GameState.SaveLoad))
        {
            Debug.LogWarning("[SaveLoadPanel] 当前状态不允许打开保存系统面板，已关闭");
            gameObject.SetActive(false);
            return;
        }
        
        // 如果当前状态是Pause，使用PushState（嵌套状态）
        // 否则使用SetState（普通状态切换）
        GameState currentState = GameStateManager.GetInstance().CurrentState;
        if (currentState == GameState.Pause)
        {
            GameStateManager.GetInstance().PushState(GameState.SaveLoad);
        }
        else
        {
            GameStateManager.GetInstance().SetState(GameState.SaveLoad);
        }
    }

    /// <summary>
    /// 设置面板模式
    /// </summary>
    /// <param name="mode">模式</param>
    public void SetMode(Mode mode)
    {
        currentMode = mode;
        modeTitle.text = currentMode == Mode.Save ? "Save" : "Load";

        // 加载存档数据
        LoadAllSaveDatas();

        // 刷新自动存档槽（独立于手动槽数据）
        RefreshAutoSaveSlot();

        // 更新页面
        UpdatePage();
    }

    /// <summary>
    /// 加载所有存档数据
    /// </summary>
    private void LoadAllSaveDatas()
    {
        // 确保数组已初始化且长度正确
        int maxSlots = MAX_SAVE_SLOTS;
        if (saveDatas == null || saveDatas.Length != maxSlots)
        {
            saveDatas = new SaveData[maxSlots];
            Debug.LogWarning($"[SaveLoadPanel] 存档数据数组未初始化或长度不匹配，已重新初始化: {maxSlots}");
        }
        
        for (int i = 0; i < maxSlots; i++)
        {
            saveDatas[i] = SaveManager.GetInstance().LoadGame(i);
        }
    }

    /// <summary>
    /// 总页数：格数 = 自动档(1) + 手动槽(MAX_SAVE_SLOTS)，每页 12 格。
    /// 虚拟格 0 = 自动存档（仅第一页第一位），虚拟格 v (v≥1) = 手动槽 (v-1)。
    /// </summary>
    private int TotalPages => Mathf.Max(1, Mathf.CeilToInt((float)(MAX_SAVE_SLOTS + 1) / SLOTS_PER_PAGE));

    /// <summary>
    /// 更新页面
    /// </summary>
    private void UpdatePage()
    {
        // 清空现有槽位（跳过固定的自动存档槽）
        foreach (Transform child in saveSlotsContainer)
        {
            if (autoSaveSlot != null && child == autoSaveSlot.transform) continue;
            Destroy(child.gameObject);
        }

        // 计算页面信息
        int totalPages = TotalPages;
        pageText.text = string.Format("{0}/{1}", currentPage + 1, totalPages);

        // 更新按钮状态
        prevPageButton.interactable = currentPage > 0;
        nextPageButton.interactable = currentPage < totalPages - 1;

        // 当前页对应的虚拟格区间 [startCell, endCell)
        int totalCells = MAX_SAVE_SLOTS + 1;
        int startCell = currentPage * SLOTS_PER_PAGE;
        int endCell = Mathf.Min(startCell + SLOTS_PER_PAGE, totalCells);

        // 自动存档槽：仅第一页的第一格显示，其他页隐藏（保证每页恰好 12 格）
        bool showAutoOnThisPage = startCell == 0 && autoSaveSlot != null;
        if (autoSaveSlot != null)
        {
            autoSaveSlot.gameObject.SetActive(showAutoOnThisPage);
        }

        // 生成当前页的手动槽位（虚拟格 v ≥ 1 映射物物理槽 v-1）
        for (int cell = startCell; cell < endCell; cell++)
        {
            if (cell == 0) continue; // 虚拟格 0 = 自动档，已处理

            int manualIndex = cell - 1;
            GameObject slotObj = Instantiate(saveSlotPrefab, saveSlotsContainer);
            SaveSlot slot = slotObj.GetComponent<SaveSlot>();
            if (slot == null) slot = slotObj.AddComponent<SaveSlot>();

            // 初始化，传入点击回调和删除回调
            slot.Init(manualIndex, saveDatas[manualIndex], currentMode, OnSaveSlotClick, OnDeleteSlotClick);
        }

        // 自动存档槽保持在第一位；重新 Init 以恢复被禁用时中断的截图加载协程
        if (showAutoOnThisPage)
        {
            RefreshAutoSaveSlot();
            autoSaveSlot.transform.SetSiblingIndex(0);
        }
    }

    /// <summary>
    /// 存档槽位点击事件
    /// </summary>
    // private void OnSaveSlotClick(int slotIndex)
    // {
    //     if (currentMode == Mode.Save)
    //     {
    //         // 保存游戏
    //         VNManager.GetInstance().SaveGame(slotIndex);
    //
    //         // 更新存档数据
    //         saveDatas[slotIndex] = SaveManager.GetInstance().LoadGame(slotIndex);
    //         UpdatePage();
    //     }
    //     else
    //     {
    //         // 加载游戏
    //         SaveData saveData = saveDatas[slotIndex];
    //         if (saveData != null)
    //         {
    //             // 【Bug修复】加载存档时，需要关闭所有面板并恢复Gameplay状态
    //             GameStateManager stateManager = GameStateManager.GetInstance();
    //             
    //             // 检查是否是从Pause状态打开的（栈中有状态）
    //             bool wasFromPause = !stateManager.IsStateStackEmpty();
    //             
    //             // 关闭SaveLoadPanel
    //             UIManager.GetInstance().HidePanel("SaveLoadPanel");
    //             
    //             // 恢复状态
    //             if (stateManager.CurrentState == GameState.SaveLoad)
    //             {
    //                 // 如果栈中有状态，说明是从Pause打开的
    //                 if (wasFromPause)
    //                 {
    //                     // PopState回到Pause
    //                     stateManager.PopState();
    //                     
    //                     // 关闭PausePanel
    //                     UIManager.GetInstance().HidePanel("PausePanel");
    //                     
    //                     // 直接设置为Gameplay（因为加载存档后应该进入游戏状态）
    //                     stateManager.SetState(GameState.Gameplay);
    //                 }
    //                 else
    //                 {
    //                     // 不是从Pause打开的，直接RestoreState
    //                     stateManager.RestoreState();
    //                 }
    //             }
    //             else
    //             {
    //                 stateManager.RestoreState();
    //             }
    //             
    //             // 确保状态是Gameplay（加载存档后应该进入游戏状态）
    //             if (stateManager.CurrentState != GameState.Gameplay && stateManager.CurrentState != GameState.AutoPlay)
    //             {
    //                 stateManager.SetState(GameState.Gameplay);
    //             }
    //             
    //             // 加载存档（这会处理场景切换等）
    //             VNManager.GetInstance().ContinueGame(saveData);
    //         }
    //         else
    //         {
    //             Debug.Log($"Slot {slotIndex + 1} 是空的，无法加载。");
    //         }
    //     }
    // }
    private void OnSaveSlotClick(int slotIndex)
    {
        // 自动存档槽路由
        if (slotIndex == AUTO_SLOT_INDEX)
        {
            if (currentMode == Mode.Save)
            {
                // 手动覆盖自动档（截图使用打开面板前缓存的画面）
                VNManager.GetInstance().SaveAutoGameNow();
                RefreshAutoSaveSlot();
            }
            else
            {
                if (_isLoadingGame) return;

                SaveData autoData = SaveManager.GetInstance().LoadAutoGame();
                if (autoData != null)
                {
                    StartCoroutine(LoadGameFlow(autoData));
                }
                else
                {
                    Debug.Log("自动存档为空，无法加载。");
                }
            }
            return;
        }

        if (currentMode == Mode.Save)
        {
            // 保存游戏
            VNManager.GetInstance().SaveGame(slotIndex);

            // 更新存档数据
            saveDatas[slotIndex] = SaveManager.GetInstance().LoadGame(slotIndex);
            UpdatePage();
        }
        else
        {
            if (_isLoadingGame)
                return;

            SaveData saveData = saveDatas[slotIndex];
            if (saveData != null)
            {
                StartCoroutine(LoadGameFlow(saveData));
            }
            else
            {
                Debug.Log($"Slot {slotIndex + 1} 是空的，无法加载。");
            }
        }
    }
    /// <summary>
    /// 加载存档协程
    /// </summary>
    /// <param name="saveData"></param>
    /// <returns></returns>
    private IEnumerator LoadGameFlow(SaveData saveData)
    {
        _isLoadingGame = true;

        // 记录当前是否是从 Pause 打开的
        GameStateManager stateManager = GameStateManager.GetInstance();
        bool wasFromPause = !stateManager.IsStateStackEmpty();

        // 先显示常驻 loading
        UIManager.GetInstance().Show<LoadingProgressPanel>();

        // 强制刷新并等待一帧，让 loading 先真正显示出来
        Canvas.ForceUpdateCanvases();
        yield return null;
        yield return new WaitForEndOfFrame();

        // 再关闭当前面板
        UIManager.GetInstance().HidePanel("SaveLoadPanel");

        // 如果是从 Pause 打开的，再关闭 PausePanel
        if (wasFromPause)
        {
            UIManager.GetInstance().HidePanel("PausePanel");
        }

        // 恢复状态
        if (stateManager.CurrentState == GameState.SaveLoad)
        {
            if (wasFromPause)
            {
                // 先退回 Pause
                stateManager.PopState();
                // 然后明确切到 Gameplay
                stateManager.SetState(GameState.Gameplay);
            }
            else
            {
                stateManager.RestoreState();
            }
        }
        else
        {
            stateManager.RestoreState();
        }

        // 保证状态正确
        if (stateManager.CurrentState != GameState.Gameplay &&
            stateManager.CurrentState != GameState.AutoPlay)
        {
            stateManager.SetState(GameState.Gameplay);
        }

        //正式继续游戏（这里会走加载存档、场景恢复等逻辑）
        VNManager.GetInstance().ContinueGame(saveData);
    }
    
    
    /// <summary>
    /// 存档删除点击事件
    /// </summary>
    private void OnDeleteSlotClick(int slotIndex)
    {
        // 自动存档槽路由
        if (slotIndex == AUTO_SLOT_INDEX)
        {
            UIManager.GetInstance().Show<ConfirmPanel>((panel) =>
            {
                panel.Show(
                    "Delete",
                    $"Are you sure you want to delete the Auto Save?",
                    () => {
                        SaveManager.GetInstance().DeleteAutoSave();
                        RefreshAutoSaveSlot();
                    },
                    null
                );
            });
            return;
        }

        // 弹出确认框
        UIManager.GetInstance().Show<ConfirmPanel>((panel) =>
        {
            panel.Show(
                "Delete",
                $"Are you sure you want to delete Save {slotIndex + 1}?",
                () => {
                    // 确定删除
                    PerformDelete(slotIndex);
                },
                null // 取消无需操作
            );
        });
    }

    /// <summary>
    /// 执行删除操作
    /// </summary>
    private void PerformDelete(int slotIndex)
    {
        // 删除文件
        SaveManager.GetInstance().DeleteSave(slotIndex);
        // 清空内存数据
        saveDatas[slotIndex] = null;
        // 刷新界面
        UpdatePage();
    }

    // 按钮点击事件
    private void OnCloseButtonClick()
    {
        UIManager.GetInstance().HidePanel("SaveLoadPanel");
        
        // 如果当前状态是SaveLoad，检查是否是从Pause打开的（栈中有状态）
        // 如果是，使用PopState；否则使用RestoreState
        GameStateManager stateManager = GameStateManager.GetInstance();
        if (stateManager.CurrentState == GameState.SaveLoad)
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
        // 面板被Destroy时，如果当前状态是SaveLoad，需要恢复游戏状态
        if (GameStateManager.GetInstance() != null && 
            GameStateManager.GetInstance().CurrentState == GameState.SaveLoad)
        {
            // 尝试从栈中弹出状态（如果是从Pause打开的）
            GameStateManager.GetInstance().PopState();
            Debug.Log("[SaveLoadPanel] 面板被Destroy，已恢复游戏状态");
        }
    }

    private void OnPrevPageButtonClick()
    {
        if (currentPage > 0)
        {
            currentPage--;
            UpdatePage();
        }
    }

    private void OnNextPageButtonClick()
    {
        int totalPages = TotalPages;
        if (currentPage < totalPages - 1)
        {
            currentPage++;
            UpdatePage();
        }
    }
}