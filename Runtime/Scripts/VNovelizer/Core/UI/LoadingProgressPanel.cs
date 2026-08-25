using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 加载进度面板
/// 用于显示游戏加载进度
///
/// 事件订阅契约：仅订阅 <see cref="LoadingProgressManager"/> 的 C# 回调
/// （OnProgressUpdated / OnAllTasksCompleted）。Manager 侧虽仍向 EventCenter
/// 广播同名事件（供外部扩展），但本面板不得重复订阅，否则每次进度更新会被处理两次。
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
    /// 初始化组件（如果Inspector中未指定，尝试自动查找）。
    /// 预制体契约：脚本挂在根节点，控件在任意子层级——兜底查找用 GetControl（全子树按名），
    /// 序列化引用正常时（默认预制体已配置）不会走到兜底分支。
    /// </summary>
    private void InitializeComponents()
    {
        // 尝试查找进度条
        if (progressBarFill == null)
        {
            progressBarFill = GetControl<Image>("Fill");
            if (progressBarFill == null)
                progressBarFill = transform.Find("ProgressBar/Fill")?.GetComponent<Image>();
        }
        if (progressSlider == null)
        {
            progressSlider = GetControl<Slider>("ProgressBar");
            if (progressSlider == null)
                progressSlider = transform.Find("ProgressBar")?.GetComponent<Slider>();
            if (progressSlider != null && progressBarFill == null)
            {
                progressBarFill = progressSlider.fillRect?.GetComponent<Image>();
            }
        }
        
        // 尝试查找文本组件
        if (progressText == null) progressText = GetControl<TMP_Text>("ProgressText");
        if (taskNameText == null) taskNameText = GetControl<TMP_Text>("TaskNameText");
        if (detailText == null) detailText = GetControl<TMP_Text>("DetailText");
        
        // 尝试查找加载图标
        if (loadingIcon == null)
        {
            loadingIcon = GetControl<Image>("LoadingIcon")?.gameObject;
            if (loadingIcon == null)
                loadingIcon = transform.Find("LoadingIcon")?.gameObject;
        }
        
        if (progressBarFill == null && progressSlider == null)
            Debug.LogWarning("[LoadingProgressPanel] 进度条组件未找到，请检查预制体上的序列化引用");
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
        
        // 取消挂起的自动隐藏计时：加载流程（VNManager）通常会在本面板自主隐藏前
        // 主动 HidePanel——本方法是常驻面板，若不取消，残留的 Invoke 计时会在
        // 下次 ShowMe 后触发 HideMe，把正在显示的加载面板意外关闭。
        CancelInvoke(nameof(HideMe));
        
        // 停止监听
        if (isListening)
        {
            StopListening();
        }
    }
    
    private void OnDisable()
    {
        // 双保险：面板被外部 SetActive(false) 隐藏时同样取消挂起的自动隐藏计时
        // （GameObject 非激活期间 Invoke 会被挂起，重新激活后继续执行）
        CancelInvoke(nameof(HideMe));
    }
    
    #endregion
    
    #region 事件监听
    
    /// <summary>
    /// 开始监听进度更新
    /// </summary>
    private void StartListening()
    {
        if (isListening) return;
        
        progressManager.OnProgressUpdated += OnProgressUpdated;
        progressManager.OnAllTasksCompleted += OnAllTasksCompleted;
        
        isListening = true;
    }
    
    /// <summary>
    /// 停止监听进度更新
    /// </summary>
    private void StopListening()
    {
        if (!isListening) return;
        
        progressManager.OnProgressUpdated -= OnProgressUpdated;
        progressManager.OnAllTasksCompleted -= OnAllTasksCompleted;
        
        isListening = false;
    }
    
    #endregion
    
    #region 进度更新处理
    
    /// <summary>
    /// 进度更新回调
    /// </summary>
    private void OnProgressUpdated(LoadingProgressInfo info)
    {
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
        // 确保进度条显示100%
        UpdateProgress(1f, "加载完成", 1f);
        
        // 延迟隐藏（让玩家看到 100%）。常规加载流程（VNManager）会在此之前主动
        // HidePanel 并触发 HideMe 中的 CancelInvoke，此计时会被安全取消；
        // 本自主隐藏仅作为无驱动者流程的兜底。
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
        // 如果组件为null，再次尝试初始化（防止异步加载导致的问题）
        if (progressText == null || taskNameText == null ||
            (progressBarFill == null && progressSlider == null))
        {
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
        
        // 更新进度文本
        if (progressText != null)
        {
            progressText.text = $"{totalProgress * 100:F1}%";
        }
        
        // 更新任务名称文本
        if (taskNameText != null)
        {
            taskNameText.text = taskName;
        }
        
        // 更新详细信息文本（可选）
        if (detailText != null)
        {
            detailText.text = activeTaskCount > 0 ? $"正在加载 ({activeTaskCount} 个任务进行中)" : "";
        }
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
