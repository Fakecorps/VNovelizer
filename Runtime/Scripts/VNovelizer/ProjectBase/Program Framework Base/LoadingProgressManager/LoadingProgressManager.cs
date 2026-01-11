using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 加载任务信息
/// </summary>
public class LoadingTask
{
    /// <summary>
    /// 任务唯一ID
    /// </summary>
    public string TaskID { get; private set; }
    
    /// <summary>
    /// 任务名称（用于显示）
    /// </summary>
    public string TaskName { get; set; }
    
    /// <summary>
    /// 当前进度（0-1）
    /// </summary>
    public float Progress { get; set; }
    
    /// <summary>
    /// 任务权重（用于计算总进度，默认为1）
    /// </summary>
    public float Weight { get; set; }
    
    /// <summary>
    /// 是否已完成
    /// </summary>
    public bool IsCompleted { get; set; }
    
    public LoadingTask(string taskID, string taskName, float weight = 1f)
    {
        TaskID = taskID;
        TaskName = taskName;
        Progress = 0f;
        Weight = weight;
        IsCompleted = false;
    }
}

/// <summary>
/// 加载进度信息（用于事件传递）
/// </summary>
public class LoadingProgressInfo
{
    /// <summary>
    /// 总进度（0-1）
    /// </summary>
    public float TotalProgress { get; set; }
    
    /// <summary>
    /// 当前任务名称
    /// </summary>
    public string CurrentTaskName { get; set; }
    
    /// <summary>
    /// 当前任务进度（0-1）
    /// </summary>
    public float CurrentTaskProgress { get; set; }
    
    /// <summary>
    /// 活跃任务数量
    /// </summary>
    public int ActiveTaskCount { get; set; }
    
    /// <summary>
    /// 是否所有任务都已完成
    /// </summary>
    public bool IsAllCompleted { get; set; }
}

/// <summary>
/// 统一的加载进度管理器
/// 用于跟踪和管理所有异步加载任务的进度
/// </summary>
public class LoadingProgressManager : BaseManager<LoadingProgressManager>
{
    /// <summary>
    /// 所有加载任务字典
    /// </summary>
    private Dictionary<string, LoadingTask> tasks = new Dictionary<string, LoadingTask>();
    
    /// <summary>
    /// 进度更新回调
    /// </summary>
    public UnityAction<LoadingProgressInfo> OnProgressUpdated;
    
    /// <summary>
    /// 所有任务完成回调
    /// </summary>
    public UnityAction OnAllTasksCompleted;
    
    /// <summary>
    /// 注册一个新的加载任务
    /// </summary>
    /// <param name="taskID">任务唯一ID</param>
    /// <param name="taskName">任务名称（用于显示）</param>
    /// <param name="weight">任务权重（用于计算总进度，默认为1）</param>
    /// <returns>是否注册成功（如果ID已存在则返回false）</returns>
    public bool RegisterTask(string taskID, string taskName, float weight = 1f)
    {
        if (string.IsNullOrEmpty(taskID))
        {
            Debug.LogError("[LoadingProgressManager] 任务ID不能为空");
            return false;
        }
        
        if (tasks.ContainsKey(taskID))
        {
            Debug.LogWarning($"[LoadingProgressManager] 任务ID '{taskID}' 已存在，注册失败");
            return false;
        }
        
        LoadingTask task = new LoadingTask(taskID, taskName, weight);
        tasks.Add(taskID, task);
        
        Debug.Log($"[LoadingProgressManager] 注册任务: {taskName} (ID: {taskID}, 权重: {weight})");
        
        // 触发进度更新
        UpdateProgress();
        
        return true;
    }
    
    /// <summary>
    /// 更新任务进度
    /// </summary>
    /// <param name="taskID">任务ID</param>
    /// <param name="progress">进度（0-1）</param>
    public void UpdateTaskProgress(string taskID, float progress)
    {
        if (!tasks.ContainsKey(taskID))
        {
            Debug.LogWarning($"[LoadingProgressManager] 任务ID '{taskID}' 不存在，无法更新进度");
            return;
        }
        
        LoadingTask task = tasks[taskID];
        task.Progress = Mathf.Clamp01(progress);
        
        // 如果进度达到1，标记为完成
        if (task.Progress >= 1f && !task.IsCompleted)
        {
            task.IsCompleted = true;
            Debug.Log($"[LoadingProgressManager] 任务完成: {task.TaskName} (ID: {taskID})");
        }
        
        // 触发进度更新
        UpdateProgress();
    }
    
    /// <summary>
    /// 更新任务名称（用于动态更新显示文本）
    /// </summary>
    /// <param name="taskID">任务ID</param>
    /// <param name="newName">新名称</param>
    public void UpdateTaskName(string taskID, string newName)
    {
        if (!tasks.ContainsKey(taskID))
        {
            Debug.LogWarning($"[LoadingProgressManager] 任务ID '{taskID}' 不存在，无法更新名称");
            return;
        }
        
        tasks[taskID].TaskName = newName;
        UpdateProgress();
    }
    
    /// <summary>
    /// 完成任务（直接标记为完成，进度设为1）
    /// </summary>
    /// <param name="taskID">任务ID</param>
    public void CompleteTask(string taskID)
    {
        UpdateTaskProgress(taskID, 1f);
    }
    
    /// <summary>
    /// 注销任务
    /// </summary>
    /// <param name="taskID">任务ID</param>
    public void UnregisterTask(string taskID)
    {
        if (tasks.ContainsKey(taskID))
        {
            Debug.Log($"[LoadingProgressManager] 注销任务: {tasks[taskID].TaskName} (ID: {taskID})");
            tasks.Remove(taskID);
            UpdateProgress();
        }
    }
    
    /// <summary>
    /// 获取任务进度
    /// </summary>
    /// <param name="taskID">任务ID</param>
    /// <returns>进度（0-1），如果任务不存在返回-1</returns>
    public float GetTaskProgress(string taskID)
    {
        if (tasks.ContainsKey(taskID))
        {
            return tasks[taskID].Progress;
        }
        return -1f;
    }
    
    /// <summary>
    /// 获取任务对象
    /// </summary>
    /// <param name="taskID">任务ID</param>
    /// <returns>任务对象，如果不存在返回null</returns>
    public LoadingTask GetTask(string taskID)
    {
        if (tasks.ContainsKey(taskID))
        {
            return tasks[taskID];
        }
        return null;
    }
    
    /// <summary>
    /// 获取总进度
    /// </summary>
    /// <returns>总进度（0-1）</returns>
    public float GetTotalProgress()
    {
        if (tasks.Count == 0)
        {
            return 1f; // 没有任务时返回100%
        }
        
        float totalWeight = 0f;
        float weightedProgress = 0f;
        
        foreach (var task in tasks.Values)
        {
            totalWeight += task.Weight;
            weightedProgress += task.Progress * task.Weight;
        }
        
        if (totalWeight <= 0f)
        {
            return 0f;
        }
        
        return weightedProgress / totalWeight;
    }
    
    /// <summary>
    /// 获取当前活跃的任务（未完成的任务）
    /// </summary>
    /// <returns>当前活跃的任务列表</returns>
    public List<LoadingTask> GetActiveTasks()
    {
        List<LoadingTask> activeTasks = new List<LoadingTask>();
        foreach (var task in tasks.Values)
        {
            if (!task.IsCompleted)
            {
                activeTasks.Add(task);
            }
        }
        return activeTasks;
    }
    
    /// <summary>
    /// 获取当前主要任务（进度最大的未完成任务）
    /// </summary>
    /// <returns>当前主要任务，如果没有则返回null</returns>
    public LoadingTask GetCurrentMainTask()
    {
        LoadingTask mainTask = null;
        float maxProgress = -1f;
        
        foreach (var task in tasks.Values)
        {
            if (!task.IsCompleted && task.Progress > maxProgress)
            {
                maxProgress = task.Progress;
                mainTask = task;
            }
        }
        
        return mainTask;
    }
    
    /// <summary>
    /// 检查是否所有任务都已完成
    /// </summary>
    /// <returns>是否所有任务都已完成</returns>
    public bool IsAllTasksCompleted()
    {
        if (tasks.Count == 0)
        {
            return true; // 没有任务时认为已完成
        }
        
        foreach (var task in tasks.Values)
        {
            if (!task.IsCompleted)
            {
                return false;
            }
        }
        
        return true;
    }
    
    /// <summary>
    /// 更新进度并触发回调
    /// </summary>
    private void UpdateProgress()
    {
        float totalProgress = GetTotalProgress();
        LoadingTask mainTask = GetCurrentMainTask();
        List<LoadingTask> activeTasks = GetActiveTasks();
        bool isAllCompleted = IsAllTasksCompleted();
        
        // 创建进度信息
        LoadingProgressInfo info = new LoadingProgressInfo
        {
            TotalProgress = totalProgress,
            CurrentTaskName = mainTask != null ? mainTask.TaskName : "完成",
            CurrentTaskProgress = mainTask != null ? mainTask.Progress : 1f,
            ActiveTaskCount = activeTasks.Count,
            IsAllCompleted = isAllCompleted
        };
        
        Debug.Log($"[LoadingProgressManager] UpdateProgress: 总进度={totalProgress:F2}, 任务={info.CurrentTaskName}, 回调订阅数={OnProgressUpdated?.GetInvocationList()?.Length ?? 0}");
        
        // 触发回调
        if (OnProgressUpdated != null)
        {
            Debug.Log("[LoadingProgressManager] 触发 OnProgressUpdated 回调");
            OnProgressUpdated.Invoke(info);
        }
        else
        {
            Debug.LogWarning("[LoadingProgressManager] OnProgressUpdated 回调为 null，没有监听者！");
        }
        
        // 通过 EventCenter 发送事件（兼容项目现有的事件系统）
        Debug.Log("[LoadingProgressManager] 通过 EventCenter 触发 LoadingProgressUpdated 事件");
        EventCenter.GetInstance().EventTrigger("LoadingProgressUpdated", info);
        
        // 如果所有任务都完成，触发完成回调
        if (isAllCompleted && OnAllTasksCompleted != null)
        {
            OnAllTasksCompleted.Invoke();
            EventCenter.GetInstance().EventTrigger("LoadingAllTasksCompleted");
        }
    }
    
    /// <summary>
    /// 清空所有任务
    /// </summary>
    public void ClearAllTasks()
    {
        Debug.Log("[LoadingProgressManager] 清空所有任务");
        tasks.Clear();
        UpdateProgress();
    }
    
    /// <summary>
    /// 获取任务数量
    /// </summary>
    /// <returns>任务总数</returns>
    public int GetTaskCount()
    {
        return tasks.Count;
    }
    
    /// <summary>
    /// 获取活跃任务数量
    /// </summary>
    /// <returns>活跃任务数量</returns>
    public int GetActiveTaskCount()
    {
        return GetActiveTasks().Count;
    }
}

