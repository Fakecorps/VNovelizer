using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class SaveLoadPanel : BasePanel
{
    // 面板类型
    public enum Mode { Save, Load }

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

    // 存档数据（延迟初始化，在Awake中根据MAX_SAVE_SLOTS创建）
    private SaveData[] saveDatas;

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

        // 加载存档槽位预制体
        string loadPath = VNProjectConfig.Instance.UI_SaveLoadPath;
        saveSlotPrefab = ResourcesManager.GetInstance().Load<GameObject>(loadPath + "/SaveSlot");
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
    /// 更新页面
    /// </summary>
    private void UpdatePage()
    {
        // 清空现有槽位
        foreach (Transform child in saveSlotsContainer)
        {
            Destroy(child.gameObject);
        }

        // 计算页面信息
        int totalPages = Mathf.CeilToInt((float)MAX_SAVE_SLOTS / SLOTS_PER_PAGE);
        if (totalPages == 0) totalPages = 1; // 至少有一页
        pageText.text = string.Format("{0}/{1}", currentPage + 1, totalPages);

        // 更新按钮状态
        prevPageButton.interactable = currentPage > 0;
        nextPageButton.interactable = currentPage < totalPages - 1;

        // 生成当前页的存档槽位
        int startIndex = currentPage * SLOTS_PER_PAGE;
        int endIndex = Mathf.Min(startIndex + SLOTS_PER_PAGE, MAX_SAVE_SLOTS);

        for (int i = startIndex; i < endIndex; i++)
        {
            GameObject slotObj = Instantiate(saveSlotPrefab, saveSlotsContainer);
            SaveSlot slot = slotObj.GetComponent<SaveSlot>();
            if (slot == null) slot = slotObj.AddComponent<SaveSlot>();

            // 初始化，传入点击回调和删除回调
            slot.Init(i, saveDatas[i], currentMode, OnSaveSlotClick, OnDeleteSlotClick);
        }
    }

    /// <summary>
    /// 存档槽位点击事件
    /// </summary>
    private void OnSaveSlotClick(int slotIndex)
    {
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
            // 加载游戏
            SaveData saveData = saveDatas[slotIndex];
            if (saveData != null)
            {
                // 【Bug修复】加载存档时，需要关闭所有面板并恢复Gameplay状态
                GameStateManager stateManager = GameStateManager.GetInstance();
                
                // 检查是否是从Pause状态打开的（栈中有状态）
                bool wasFromPause = !stateManager.IsStateStackEmpty();
                
                // 关闭SaveLoadPanel
                UIManager.GetInstance().HidePanel("SaveLoadPanel");
                
                // 恢复状态
                if (stateManager.CurrentState == GameState.SaveLoad)
                {
                    // 如果栈中有状态，说明是从Pause打开的
                    if (wasFromPause)
                    {
                        // PopState回到Pause
                        stateManager.PopState();
                        
                        // 关闭PausePanel
                        UIManager.GetInstance().HidePanel("PausePanel");
                        
                        // 直接设置为Gameplay（因为加载存档后应该进入游戏状态）
                        stateManager.SetState(GameState.Gameplay);
                    }
                    else
                    {
                        // 不是从Pause打开的，直接RestoreState
                        stateManager.RestoreState();
                    }
                }
                else
                {
                    stateManager.RestoreState();
                }
                
                // 确保状态是Gameplay（加载存档后应该进入游戏状态）
                if (stateManager.CurrentState != GameState.Gameplay && stateManager.CurrentState != GameState.AutoPlay)
                {
                    stateManager.SetState(GameState.Gameplay);
                }
                
                // 加载存档（这会处理场景切换等）
                VNManager.GetInstance().ContinueGame(saveData);
            }
            else
            {
                Debug.Log($"Slot {slotIndex + 1} 是空的，无法加载。");
            }
        }
    }

    /// <summary>
    /// 存档删除点击事件
    /// </summary>
    private void OnDeleteSlotClick(int slotIndex)
    {
        // 弹出确认框
        string loadPath = VNProjectConfig.Instance.UI_ConfirmPath;
        string confirmPath = loadPath;

        UIManager.GetInstance().ShowPanel<ConfirmPanel>("ConfirmPanel", confirmPath, E_UI_Layer.System, (panel) =>
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
            string path = VNProjectConfig.Instance != null ? VNProjectConfig.Instance.UI_PausePath : "VNPrefabs/UI/Pause";
            if (string.IsNullOrEmpty(path)) path = "VNPrefabs/UI/Pause";
            UIManager.GetInstance().ShowPanel<PausePanel>("PausePanel", path, E_UI_Layer.Top, null);
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
        int totalPages = Mathf.CeilToInt((float)MAX_SAVE_SLOTS / SLOTS_PER_PAGE);
        if (currentPage < totalPages - 1)
        {
            currentPage++;
            UpdatePage();
        }
    }
}