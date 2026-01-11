using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.VisualScripting;

/// <summary>
/// 加载进度面板
/// 用于显示游戏加载进度
/// </summary>
public class LoadingProgressPanel : BasePanel
{
    #region UI控件引用
    
    [Header("进度条组件")]
    [SerializeField] private Image progressBarFill;        // 进度条填充图片
    [SerializeField] private Slider progressSlider;       // 进度条滑块（可选，如果使用Slider）
    
    [Header("文本组件")]
    [SerializeField] private TMP_Text progressText;     // 进度百分比文本（如：50%）
    [SerializeField] private TMP_Text taskNameText;     // 当前任务名称文本
    [SerializeField] private TMP_Text detailText;       // 详细信息文本（可选）
    
    [Header("其他组件")]
    [SerializeField] private GameObject loadingIcon;         // 加载图标（可选，用于旋转动画）
    [SerializeField] private float iconRotationSpeed = 180f; // 图标旋转速度（度/秒）
    
    #endregion
    
    #region 私有变量
    
    private LoadingProgressManager progressManager;
    private bool isListening = false;
    
    #endregion
    
    #region 初始化
    
    protected override void Awake()
    {
        base.Awake();
        
        // 如果没有在Inspector中指定，尝试自动查找
        InitializeComponents();
        
        // 初始化进度管理器
        progressManager = LoadingProgressManager.GetInstance();
    }
    
    /// <summary>
    /// 初始化组件（如果Inspector中未指定，尝试自动查找）
    /// </summary>
    private void InitializeComponents()
    {
        Debug.Log("Initializing ");
        // 尝试查找进度条
        if (progressBarFill == null)
        {
            progressBarFill = transform.Find("ProgressBar/Fill")?.GetComponent<Image>();
        }
        if (progressSlider == null)
        {
            progressSlider = transform.Find("ProgressBar")?.GetComponent<Slider>();
            if (progressSlider != null && progressBarFill == null)
            {
                progressBarFill = progressSlider.fillRect?.GetComponent<Image>();
            }
        }
        
        // 尝试查找文本组件（使用多种方式）
        if (progressText == null)
        {
            //使用BasePanel的GetControl方法
            progressText = GetControl<TMP_Text>("ProgressText");
            //或者直接Find
            if (progressText == null)
            {
                Transform progressTextTransform = transform.Find("ProgressText");
                if (progressTextTransform != null)
                {
                    progressText = progressTextTransform.GetComponent<TMP_Text>();
                }
            }
        }
        
        if (taskNameText == null)
        {
            //使用BasePanel的GetControl方法
            taskNameText = GetControl<TMP_Text>("TaskNameText");
            //直接Find
            if (taskNameText == null)
            {
                Transform taskNameTextTransform = transform.Find("TaskNameText");
                if (taskNameTextTransform != null)
                {
                    taskNameText = taskNameTextTransform.GetComponent<TMP_Text>();
                }
            }
        }
        
        if (detailText == null)
        {
            //使用BasePanel的GetControl方法
            detailText = GetControl<TMP_Text>("DetailText");
            //直接Find
            if (detailText == null)
            {
                Transform detailTextTransform = transform.Find("DetailText");
                if (detailTextTransform != null)
                {
                    detailText = detailTextTransform.GetComponent<TMP_Text>();
                }
            }
        }
        
        // 尝试查找加载图标
        if (loadingIcon == null)
        {
            loadingIcon = transform.Find("LoadingIcon")?.gameObject;
        }
        
        // 调试日志：输出组件查找结果
        Debug.Log($"[LoadingProgressPanel] 组件初始化结果:\n" +
                  $"  progressBarFill: {(progressBarFill != null ? "✓" : "✗")}\n" +
                  $"  progressSlider: {(progressSlider != null ? "✓" : "✗")}\n" +
                  $"  progressText: {(progressText != null ? "✓" : "✗")}\n" +
                  $"  taskNameText: {(taskNameText != null ? "✓" : "✗")}\n" +
                  $"  detailText: {(detailText != null ? "✓" : "✗")}\n" +
                  $"  loadingIcon: {(loadingIcon != null ? "✓" : "✗")}");
    }
    
    public override void ShowMe()
    {
        base.ShowMe();
        
        // 在显示时再次尝试初始化组件（因为面板可能是在Awake之后才被激活的）
        InitializeComponents();
        
        // 初始化显示（在监听前先设置初始状态）
        UpdateProgress(0f, "准备加载...", 0f);
        
        // 开始监听进度更新
        if (!isListening)
        {
            StartListening();
        }
        
        // 立即获取一次当前进度（如果有任务已注册）
        LoadingProgressManager progressMgr = LoadingProgressManager.GetInstance();
        float currentProgress = progressMgr.GetTotalProgress();
        if (currentProgress > 0f)
        {
            LoadingTask mainTask = progressMgr.GetCurrentMainTask();
            if (mainTask != null)
            {
                UpdateProgress(currentProgress, mainTask.TaskName, mainTask.Progress);
            }
        }
    }
    
    public override void HideMe()
    {
        base.HideMe();
        
        // 停止监听
        if (isListening)
        {
            StopListening();
        }
    }
    
    #endregion
    
    #region 事件监听
    
    /// <summary>
    /// 开始监听进度更新
    /// </summary>
    private void StartListening()
    {
        if (isListening) return;
        
        //使用回调
        progressManager.OnProgressUpdated += OnProgressUpdated;
        progressManager.OnAllTasksCompleted += OnAllTasksCompleted;
        
        //或者使用EventCenter（项目现有的事件系统）
        EventCenter.GetInstance().AddEventListener<LoadingProgressInfo>("LoadingProgressUpdated", OnProgressUpdated);
        EventCenter.GetInstance().AddEventListener("LoadingAllTasksCompleted", OnAllTasksCompleted);
        
        isListening = true;
        Debug.Log("[LoadingProgressPanel] 开始监听加载进度");
    }
    
    /// <summary>
    /// 停止监听进度更新
    /// </summary>
    private void StopListening()
    {
        if (!isListening) return;
        
        // 移除回调
        progressManager.OnProgressUpdated -= OnProgressUpdated;
        progressManager.OnAllTasksCompleted -= OnAllTasksCompleted;
        
        // 移除EventCenter监听
        EventCenter.GetInstance().RemoveEventListener<LoadingProgressInfo>("LoadingProgressUpdated", OnProgressUpdated);
        EventCenter.GetInstance().RemoveEventListener("LoadingAllTasksCompleted", OnAllTasksCompleted);
        
        isListening = false;
        Debug.Log("[LoadingProgressPanel] 停止监听加载进度");
    }
    
    #endregion
    
    #region 进度更新处理
    
    /// <summary>
    /// 进度更新回调
    /// </summary>
    private void OnProgressUpdated(LoadingProgressInfo info)
    {
        Debug.Log($"[LoadingProgressPanel] 收到进度更新事件: 总进度={info.TotalProgress:F2}, 任务={info.CurrentTaskName}, 任务进度={info.CurrentTaskProgress:F2}");
        UpdateProgress(
            info.TotalProgress,
            info.CurrentTaskName,
            info.CurrentTaskProgress,
            info.ActiveTaskCount
        );
    }
    
    /// <summary>
    /// 所有任务完成回调
    /// </summary>
    private void OnAllTasksCompleted()
    {
        Debug.Log("[LoadingProgressPanel] 所有加载任务已完成");
        
        // 确保进度条显示100%
        UpdateProgress(1f, "加载完成", 1f);
        
        // 延迟一下在隐藏
        Invoke(nameof(HideMe), 1f);
    }
    
    /// <summary>
    /// 更新进度显示
    /// </summary>
    /// <param name="totalProgress">总进度（0-1）</param>
    /// <param name="taskName">当前任务名称</param>
    /// <param name="taskProgress">当前任务进度（0-1）</param>
    /// <param name="activeTaskCount">活跃任务数量</param>
    private void UpdateProgress(float totalProgress, string taskName, float taskProgress, int activeTaskCount = 0)
    {
        Debug.Log($"[LoadingProgressPanel] UpdateProgress 被调用: 总进度={totalProgress:F2}, 任务={taskName}");
        
        // 如果组件为null，再次尝试初始化（防止异步加载导致的问题）
        if (progressText == null || taskNameText == null || detailText == null || 
            (progressBarFill == null && progressSlider == null))
        {
            Debug.LogWarning("[LoadingProgressPanel] 检测到组件为null，重新初始化...");
            InitializeComponents();
        }
        
        // 更新进度条
        if (progressBarFill != null)
        {
            progressBarFill.fillAmount = totalProgress;
        }
        else if (progressSlider != null)
        {
            progressSlider.value = totalProgress;
           
        }
        else
        {
            Debug.LogWarning("[LoadingProgressPanel] 进度条组件未找到！请检查UI预制体中的ProgressBar或ProgressBar/Fill");
        }
        
        // 更新进度文本
        if (progressText != null)
        {
            string progressTextValue = $"{totalProgress * 100:F1}%";
            progressText.text = progressTextValue;
           
        }
        else
        {
            Debug.LogWarning("[LoadingProgressPanel] ProgressText组件未找到！请检查UI预制体中的ProgressText GameObject");
        }
        
        // 更新任务名称文本
        if (taskNameText != null)
        {
            taskNameText.text = taskName;
            
        }
        else
        {
            Debug.LogWarning("[LoadingProgressPanel] TaskNameText组件未找到！请检查UI预制体中的TaskNameText GameObject");
        }
        
        // 更新详细信息文本（可选）
        if (detailText != null)
        {
            if (activeTaskCount > 0)
            {
                detailText.text = $"正在加载 ({activeTaskCount} 个任务进行中)";
            }
            else
            {
                detailText.text = "";
            }
           
        }
        // DetailText是可选的，不输出警告
    }
    
    #endregion
    
    #region 动画更新
    
    private void Update()
    {
        // 旋转加载图标
        if (loadingIcon != null && loadingIcon.activeSelf)
        {
            loadingIcon.transform.Rotate(0, 0, -iconRotationSpeed * Time.deltaTime);
        }
    }
    
    #endregion
    
    #region 清理
    
    private void OnDestroy()
    {
        // 确保在销毁时移除监听
        if (isListening)
        {
            StopListening();
        }
    }
    
    #endregion
}

