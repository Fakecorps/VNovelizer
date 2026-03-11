using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class ResourcesManager : BaseManager<ResourcesManager>
{
#if UNITY_EDITOR
    /// <summary>
    /// 编辑器模式下优先从 Assets 加载资源（避免加载 Package 中的资源）
    /// </summary>
    private T LoadFromAssets<T>(string resourcesPath) where T : UnityEngine.Object
    {
        // 获取资源类型名称（用于 AssetDatabase 查找）
        string typeName = typeof(T).Name;
        
        // 特殊处理：GameObject 类型通常对应 Prefab
        if (typeof(T) == typeof(GameObject))
        {
            typeName = "Prefab";
        }
        
        // 如果资源路径包含文件名，提取文件名；否则使用路径的最后一部分
        string fileName = System.IO.Path.GetFileName(resourcesPath);
        if (string.IsNullOrEmpty(fileName))
        {
            string[] pathParts = resourcesPath.Split('/');
            fileName = pathParts[pathParts.Length - 1];
        }
        
        // 移除文件扩展名（如果有）
        string searchName = System.IO.Path.GetFileNameWithoutExtension(fileName);
        
        // 构建完整的 Resources 路径用于匹配（添加 "Resources/" 前缀）
        string fullResourcesPath = $"Resources/{resourcesPath}".Replace("\\", "/");
        
        // 使用 AssetDatabase 查找资源（先按文件名查找）
        string[] guids = AssetDatabase.FindAssets($"{searchName} t:{typeName}");
        
        // 如果按类型查找失败，尝试只按文件名查找
        if (guids.Length == 0)
        {
            guids = AssetDatabase.FindAssets(searchName);
        }
        
        foreach (string guid in guids)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            
            // 过滤掉 Packages 中的资源，只加载 Assets 中的资源
            if (assetPath.StartsWith("Assets/") && !assetPath.StartsWith("Assets/Packages/"))
            {
                string normalizedAssetPath = assetPath.Replace("\\", "/");
                
                // 检查资源路径是否匹配
                // 1. 检查 Assets 路径是否包含完整的 Resources 路径
                // 2. 检查文件名是否匹配
                bool pathMatches = normalizedAssetPath.Contains(fullResourcesPath) || 
                                   normalizedAssetPath.Contains(resourcesPath.Replace("\\", "/"));
                bool nameMatches = normalizedAssetPath.Contains(searchName);
                
                if (pathMatches || nameMatches)
                {
                    T asset = AssetDatabase.LoadAssetAtPath<T>(assetPath);
                    if (asset != null)
                    {
                        Debug.Log($"[ResourcesManager] 编辑器模式：从 Assets 加载资源: {assetPath} (Resources路径: {resourcesPath})");
                        return asset;
                    }
                }
            }
        }
        
        // 如果在 Assets 中找不到，返回 null，让后续代码使用 Resources.Load
        return null;
    }
#endif

    //同步加载资源
    public T Load<T>(string name) where T: UnityEngine.Object
    {
#if UNITY_EDITOR
        // 编辑器模式下优先从 Assets 加载
        T assetRes = LoadFromAssets<T>(name);
        if (assetRes != null)
        {
            // 如果对象是GameObject，则先实例化再返回
            if (assetRes is GameObject)
            {
                return GameObject.Instantiate(assetRes) as T;
            }
            return assetRes;
        }
#endif
        
        // 运行时或 Assets 中找不到时，使用 Resources.Load
        T res = Resources.Load<T>(name);
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
#if UNITY_EDITOR
        // 编辑器模式下优先从 Assets 加载
        T assetRes = LoadFromAssets<T>(name);
        if (assetRes != null)
        {
            // 模拟异步加载（延迟一帧）
            yield return null;
            
            if (assetRes is GameObject)
            {
                callback(GameObject.Instantiate(assetRes) as T);
            }
            else
            {
                callback(assetRes);
            }
            yield break;
        }
#endif
        
        // 运行时或 Assets 中找不到时，使用 Resources.LoadAsync
        ResourceRequest r = Resources.LoadAsync<T>(name);
        yield return r;
        if (r.asset is GameObject)
        {
            callback(GameObject.Instantiate(r.asset) as T);
        }
        else
        {
            callback(r.asset as T);
        }

    }
    
    //异步加载资源（带进度跟踪）
    private IEnumerator ILoadAsync<T>(string name, UnityAction<T> callback, string taskID) where T : UnityEngine.Object
    {
#if UNITY_EDITOR
        // 编辑器模式下优先从 Assets 加载
        T assetRes = LoadFromAssets<T>(name);
        if (assetRes != null)
        {
            // 模拟异步加载进度（延迟几帧以显示进度）
            if (!string.IsNullOrEmpty(taskID))
            {
                LoadingProgressManager progressManager = LoadingProgressManager.GetInstance();
                for (int i = 0; i < 3; i++)
                {
                    progressManager.UpdateTaskProgress(taskID, (i + 1) / 3f);
                    yield return null;
                }
                progressManager.CompleteTask(taskID);
            }
            else
            {
                yield return null;
            }
            
            if (assetRes is GameObject)
            {
                callback(GameObject.Instantiate(assetRes) as T);
            }
            else
            {
                callback(assetRes);
            }
            yield break;
        }
#endif
        
        // 运行时或 Assets 中找不到时，使用 Resources.LoadAsync
        ResourceRequest r = Resources.LoadAsync<T>(name);
        
        // 如果有任务ID，更新进度
        if (!string.IsNullOrEmpty(taskID))
        {
            while (!r.isDone)
            {
                LoadingProgressManager.GetInstance().UpdateTaskProgress(taskID, r.progress);
                yield return null;
            }
        }
        else
        {
            yield return r;
        }
        
        // 加载完成
        if (!string.IsNullOrEmpty(taskID))
        {
            LoadingProgressManager.GetInstance().CompleteTask(taskID);
        }
        
        if (r.asset is GameObject)
        {
            callback(GameObject.Instantiate(r.asset) as T);
        }
        else
        {
            callback(r.asset as T);
        }
    }

}


