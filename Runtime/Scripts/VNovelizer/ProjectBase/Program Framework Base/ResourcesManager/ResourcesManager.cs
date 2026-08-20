using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 通用资源加载门面（历史入口，对外 API 保持不变）。
///
/// 内部已收口到 <see cref="VNResourceService"/> 提供者链（Addressables → Resources），
/// 详见 Docs/VNResourceProviderRefactoring.md。旧契约保留：
/// - GameObject 加载后自动实例化再返回；
/// - LoadAsync 由 MonoManager 协程驱动，可选接入 LoadingProgressManager 进度跟踪。
///
/// 新代码建议直接使用 VNResourceService（无实例化副作用、无 MonoManager 依赖）。
/// </summary>
public class ResourcesManager : BaseManager<ResourcesManager>
{
    //同步加载资源
    public T Load<T>(string name) where T: UnityEngine.Object
    {
        T res = VNResourceService.Load<T>(name);
        //如果对象是GameObject，则先实例化再返回，外部可以直接使用
        if (res is GameObject)
        { 
            return GameObject.Instantiate(res) as T;
        }

        return res;
    }

    //异步加载资源
    public void LoadAsync<T>(string name,UnityAction<T> callback) where T : UnityEngine.Object
    {
        MonoManager.GetInstance().StartCoroutine(ILoadAsync<T>(name,callback));
    }
    
    //异步加载资源（带进度跟踪）
    public void LoadAsync<T>(string name, UnityAction<T> callback, string taskID = null, string taskName = null, float weight = 1f) where T : UnityEngine.Object
    {
        // 如果提供了任务信息，检查任务是否存在，如果不存在则注册
        if (!string.IsNullOrEmpty(taskID))
        {
            LoadingProgressManager progressManager = LoadingProgressManager.GetInstance();
            if (progressManager.GetTaskProgress(taskID) < 0)
            {
                // 任务不存在，注册新任务
                string displayName = string.IsNullOrEmpty(taskName) ? $"加载资源: {name}" : taskName;
                progressManager.RegisterTask(taskID, displayName, weight);
            }
            else
            {
                // 任务已存在，只更新名称（如果提供了新名称）
                if (!string.IsNullOrEmpty(taskName))
                {
                    progressManager.UpdateTaskName(taskID, taskName);
                }
            }
        }
        
        MonoManager.GetInstance().StartCoroutine(ILoadAsync<T>(name, callback, taskID));
    }

    
    private IEnumerator ILoadAsync<T>(string name,UnityAction<T> callback) where T : UnityEngine.Object
    {
        VNLoadOperation<T> op = VNResourceService.LoadAsync<T>(name);
        while (!op.IsDone) yield return null;
        Deliver(op.Asset, callback);
    }
    
    //异步加载资源（带进度跟踪）
    private IEnumerator ILoadAsync<T>(string name, UnityAction<T> callback, string taskID) where T : UnityEngine.Object
    {
        VNLoadOperation<T> op = VNResourceService.LoadAsync<T>(name);
        
        // 如果有任务ID，逐帧更新进度（链上回退时进度按链长加权）
        if (!string.IsNullOrEmpty(taskID))
        {
            while (!op.IsDone)
            {
                LoadingProgressManager.GetInstance().UpdateTaskProgress(taskID, op.Progress);
                yield return null;
            }
            LoadingProgressManager.GetInstance().CompleteTask(taskID);
        }
        else
        {
            while (!op.IsDone) yield return null;
        }
        
        Deliver(op.Asset, callback);
    }

    /// <summary>交付加载结果（保持 GameObject 自动实例化契约）</summary>
    private static void Deliver<T>(T asset, UnityAction<T> callback) where T : UnityEngine.Object
    {
        if (asset is GameObject)
        {
            callback(GameObject.Instantiate(asset) as T);
        }
        else
        {
            callback(asset);
        }
    }
}
