using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

public class ScenesManager :BaseManager<ScenesManager>
{
    public void LoadScene(string name,UnityAction fun)//同步加载场景
    { 
        SceneManager.LoadScene(name);
    }

    public void LoadSceneAsync(string name, UnityAction fun)//异步加载场景
    { 
        MonoManager.GetInstance().StartCoroutine(LoadSceneAysnc(name, fun, null));
    }
    
    //异步加载场景（带进度跟踪）
    public void LoadSceneAsync(string name, UnityAction fun, string taskID = null, float weight = 1f)
    {
        // 如果提供了任务ID，注册进度任务
        if (!string.IsNullOrEmpty(taskID))
        {
            LoadingProgressManager.GetInstance().RegisterTask(taskID, $"加载场景: {name}", weight);
        }
        
        MonoManager.GetInstance().StartCoroutine(LoadSceneAysnc(name, fun, taskID));
    }

    private IEnumerator LoadSceneAysnc(string name, UnityAction fun)
    {
        AsyncOperation asyncOperation = SceneManager.LoadSceneAsync(name);
        yield return asyncOperation;
        while (!asyncOperation.isDone)
        { 
            //更新进度条
            yield return asyncOperation.progress;
        }

        //加载完之后去执行函数
        fun();
    }
    
    //异步加载场景（带进度跟踪）
    private IEnumerator LoadSceneAysnc(string name, UnityAction fun, string taskID)
    {
        AsyncOperation asyncOperation = SceneManager.LoadSceneAsync(name);
        
        // 如果有任务ID，更新进度
        if (!string.IsNullOrEmpty(taskID))
        {
            while (!asyncOperation.isDone)
            {
                // Unity的AsyncOperation.progress最大只到0.9，需要手动处理
                float progress = asyncOperation.progress;
                if (progress >= 0.9f)
                {
                    progress = 0.9f; // 限制在0.9，等isDone为true时再设为1.0
                }
                LoadingProgressManager.GetInstance().UpdateTaskProgress(taskID, progress);
                yield return null;
            }
            
            // 加载完成，设置为100%
            LoadingProgressManager.GetInstance().CompleteTask(taskID);
        }
        else
        {
            yield return asyncOperation;
            while (!asyncOperation.isDone)
            {
                yield return asyncOperation.progress;
            }
        }

        //加载完之后去执行函数
        fun();
    }
}
