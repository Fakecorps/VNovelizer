using System.Collections;
using UnityEngine;

/// <summary>
/// 加载进度测试脚本
/// 用于测试和观察加载界面的显示效果
/// 可以挂载到场景中的任意GameObject上，或通过编辑器菜单调用
/// </summary>
public class LoadingProgressTest : MonoBehaviour
{
    [Header("测试设置")]
    [Tooltip("加载总时长（秒）")]
    [SerializeField] private float loadingDuration = 10f;
    
    [Tooltip("进度更新间隔（秒）")]
    [SerializeField] private float updateInterval = 0.1f;
    
    [Tooltip("是否在Start时自动开始测试")]
    [SerializeField] private bool autoStartOnStart = false;
    
    [Header("测试任务配置")]
    [Tooltip("测试任务列表（任务名称和权重）")]
    [SerializeField] private TestTaskConfig[] testTasks = new TestTaskConfig[]
    {
        new TestTaskConfig { taskName = "加载剧本资源", weight = 3f },
        new TestTaskConfig { taskName = "加载角色资源", weight = 2f },
        new TestTaskConfig { taskName = "加载背景资源", weight = 2f },
        new TestTaskConfig { taskName = "加载UI资源", weight = 2f },
        new TestTaskConfig { taskName = "初始化游戏系统", weight = 1f }
    };
    
    [System.Serializable]
    public class TestTaskConfig
    {
        public string taskName;
        public float weight = 1f;
    }
    
    private Coroutine testCoroutine;
    private bool isTesting = false;
    
    private void Start()
    {
        if (autoStartOnStart)
        {
            StartTest();
        }
    }
    
    /// <summary>
    /// 开始测试
    /// </summary>
    [ContextMenu("开始加载测试")]
    public void StartTest()
    {
        if (isTesting)
        {
            Debug.LogWarning("[LoadingProgressTest] 测试正在进行中，请等待完成");
            return;
        }
        
        Debug.Log($"[LoadingProgressTest] 开始加载测试，预计时长: {loadingDuration}秒");
        
        // 启动测试协程
        testCoroutine = StartCoroutine(SimulateLoading());
    }
    
    /// <summary>
    /// 停止测试
    /// </summary>
    [ContextMenu("停止加载测试")]
    public void StopTest()
    {
        if (testCoroutine != null)
        {
            StopCoroutine(testCoroutine);
            testCoroutine = null;
        }
        
        // 清理所有任务
        LoadingProgressManager.GetInstance().ClearAllTasks();
        
        // 隐藏加载面板
        UIManager.GetInstance().HidePanel("LoadingProgressPanel");
        
        isTesting = false;
        Debug.Log("[LoadingProgressTest] 测试已停止");
    }
    
    /// <summary>
    /// 模拟加载过程
    /// </summary>
    private IEnumerator SimulateLoading()
    {
        isTesting = true;
        
        // 显示加载面板
        Debug.Log("[LoadingProgressTest] 显示加载面板");
        LoadingProgressPanel panel = null;
        UIManager.GetInstance().ShowPanel<LoadingProgressPanel>(
            "LoadingProgressPanel",
            VNProjectConfig.Instance.UI_LoadingPath,
            E_UI_Layer.System,
            (p) => { panel = p; Debug.Log("[LoadingProgressTest] 面板加载完成回调"); }
        );
        
        // 等待面板完全加载和初始化（通过回调确认，或等待足够的时间）
        int waitFrames = 0;
        while (panel == null && waitFrames < 30) // 最多等待30帧
        {
            yield return null;
            waitFrames++;
            // 如果回调还没执行，尝试从字典中获取
            if (panel == null)
            {
                panel = UIManager.GetInstance().GetPanel<LoadingProgressPanel>("LoadingProgressPanel");
            }
        }
        
        if (panel == null)
        {
            Debug.LogError("[LoadingProgressTest] 面板加载超时！");
            isTesting = false;
            yield break;
        }
        
        Debug.Log("[LoadingProgressTest] 面板已完全初始化，开始注册测试任务");
        
        // 注册所有测试任务
        LoadingProgressManager progressManager = LoadingProgressManager.GetInstance();
        progressManager.ClearAllTasks(); // 先清空之前的任务
        
        string[] taskIDs = new string[testTasks.Length];
        for (int i = 0; i < testTasks.Length; i++)
        {
            taskIDs[i] = $"test_task_{i}";
            progressManager.RegisterTask(taskIDs[i], testTasks[i].taskName, testTasks[i].weight);
        }
        
        Debug.Log($"[LoadingProgressTest] 已注册 {testTasks.Length} 个测试任务");
        
        // 模拟加载过程
        float elapsedTime = 0f;
        int currentTaskIndex = 0;
        float[] taskProgress = new float[testTasks.Length];
        
        while (elapsedTime < loadingDuration && currentTaskIndex < testTasks.Length)
        {
            // 计算当前应该处理的任务
            float progressPerTask = loadingDuration / testTasks.Length;
            float taskStartTime = currentTaskIndex * progressPerTask;
            float taskEndTime = (currentTaskIndex + 1) * progressPerTask;
            
            if (elapsedTime >= taskStartTime)
            {
                // 更新当前任务的进度
                float taskElapsed = elapsedTime - taskStartTime;
                float taskDuration = taskEndTime - taskStartTime;
                taskProgress[currentTaskIndex] = Mathf.Clamp01(taskElapsed / taskDuration);
                
                // 更新进度管理器
                progressManager.UpdateTaskProgress(taskIDs[currentTaskIndex], taskProgress[currentTaskIndex]);
                
                // 如果当前任务完成，移动到下一个任务
                if (taskProgress[currentTaskIndex] >= 1f && currentTaskIndex < testTasks.Length - 1)
                {
                    // 完成任务
                    progressManager.CompleteTask(taskIDs[currentTaskIndex]);
                    Debug.Log($"[LoadingProgressTest] 任务完成: {testTasks[currentTaskIndex].taskName}");
                    currentTaskIndex++;
                }
            }
            
            // 更新已过时间
            elapsedTime += updateInterval;
            
            // 等待更新间隔
            yield return new WaitForSeconds(updateInterval);
        }
        
        // 完成所有剩余任务
        for (int i = currentTaskIndex; i < testTasks.Length; i++)
        {
            progressManager.CompleteTask(taskIDs[i]);
            Debug.Log($"[LoadingProgressTest] 任务完成: {testTasks[i].taskName}");
        }
        
        // 等待一帧，确保所有任务完成事件已触发
        yield return null;
        
        // 等待一小段时间，让用户看到100%的进度
        yield return new WaitForSeconds(0.5f);
        
        // 隐藏加载面板
        Debug.Log("[LoadingProgressTest] 加载测试完成，隐藏加载面板");
        UIManager.GetInstance().HidePanel("LoadingProgressPanel");
        
        // 清理任务
        progressManager.ClearAllTasks();
        
        isTesting = false;
        Debug.Log("[LoadingProgressTest] 测试完成");
    }
    
    /// <summary>
    /// 在编辑器中快速测试（通过菜单调用）
    /// </summary>
    #if UNITY_EDITOR
    [UnityEditor.MenuItem("VNovelizer/🎬测试(请在play模式下)/测试加载进度界面")]
    public static void QuickTestInEditor()
    {
        // 查找或创建测试对象
        GameObject testObj = GameObject.Find("LoadingProgressTest");
        if (testObj == null)
        {
            testObj = new GameObject("LoadingProgressTest");
        }
        
        LoadingProgressTest test = testObj.GetComponent<LoadingProgressTest>();
        if (test == null)
        {
            test = testObj.AddComponent<LoadingProgressTest>();
        }
        
        // 开始测试
        test.StartTest();
        
        Debug.Log("[LoadingProgressTest] 已在编辑器中启动测试（需要在Play模式下运行）");
    }
    #endif
}

