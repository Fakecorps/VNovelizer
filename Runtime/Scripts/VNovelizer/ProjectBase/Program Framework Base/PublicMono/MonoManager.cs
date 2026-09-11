using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class MonoManager :BaseManager<MonoManager>
{
    public MonoController controller;
    public MonoManager()
    {
        //保证了MonoController对象的唯一性
        GameObject obj = new GameObject("MonoController");
        UnityEngine.Object.DontDestroyOnLoad(obj);
        controller = obj.AddComponent<MonoController>();
    }

    /// <summary>
    /// 【Fix-43】宿主自愈：关闭 Domain Reload 后再次进入 Play Mode 时，静态 instance 保留
    /// 旧 C# 对象但 Unity 侧 MonoController 已随上次 Play 结束销毁（fake null）。
    /// 每次访问前校验并重建，避免 StartCoroutine/Update 监听全部 MissingReferenceException。
    /// </summary>
    private void EnsureController()
    {
        if (controller == null)
        {
            GameObject obj = new GameObject("MonoController");
            UnityEngine.Object.DontDestroyOnLoad(obj);
            controller = obj.AddComponent<MonoController>();
        }
    }

    //给外部提供的添加帧更新事件的函数
    public void AddUpdateListener(UnityAction fun)
    {
        EnsureController();
        controller.AddUpdateListener(fun);

    }
    //给外部提供的移除帧更新事件的函数 
    public void RemoveUpdateListener(UnityAction fun)
    {
        EnsureController();
        controller.RemoveUpdateListener(fun);
    }

    public Coroutine StartCoroutine(IEnumerator routine)
    { 
        EnsureController();
        return controller.StartCoroutine(routine);
    }

    public Coroutine StartCoroutine(string methodName, object value)
    {
        EnsureController();
        return controller.StartCoroutine(methodName, value);
    }

    public Coroutine StartCoroutine(string methodName)
    { 
        EnsureController();
        return controller.StartCoroutine(methodName);
    }

    public Coroutine StartCoroutine_Auto(IEnumerator routine)
    { 
        EnsureController();
        return controller.StartCoroutine(routine);
    }

    public void StopCoroutine(Coroutine routine)
    {
        if (routine != null)
        {
            EnsureController();
            controller.StopCoroutine(routine);
        }
    }

    public void StopAllCoroutines()
    { 
        EnsureController();
        controller.StopAllCoroutines();
    }
}
