using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class ResourcesManager : BaseManager<ResourcesManager>
{
    //同步加载资源
    public T Load<T>(string name) where T: UnityEngine.Object
    {
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


