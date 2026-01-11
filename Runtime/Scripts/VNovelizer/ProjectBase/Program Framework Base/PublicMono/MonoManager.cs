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
        controller = obj.AddComponent<MonoController>();
    }

    //给外部提供的添加帧更新事件的函数
    public void AddUpdateListener(UnityAction fun)
    {
        controller.AddUpdateListener(fun);

    }
    //给外部提供的移除帧更新事件的函数 
    public void RemoveUpdateListener(UnityAction fun)
    {
        controller.RemoveUpdateListener(fun);
    }

    public Coroutine StartCoroutine(IEnumerator routine)
    { 
        return controller.StartCoroutine(routine);
    }

    public Coroutine StartCoroutine(string methodName, object value)
    {
        return controller.StartCoroutine(methodName, value);
    }

    public Coroutine StartCoroutine(string methodName)
    { 
        return controller.StartCoroutine(methodName);
    }

    public Coroutine StartCoroutine_Auto(IEnumerator routine)
    { 
        return controller.StartCoroutine(routine);
    }

    public void StopCoroutine(Coroutine routine)
    {
        if (routine != null)
        {
            controller.StopCoroutine(routine);
        }
    }

    public void StopAllCoroutines()
    { 
        controller.StopAllCoroutines();
    }
}
