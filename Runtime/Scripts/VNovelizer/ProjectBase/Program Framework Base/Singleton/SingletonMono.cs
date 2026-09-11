using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SingletonMono<T> : MonoBehaviour where T : MonoBehaviour
{

    private static T instance;

    public static T GetInstance()
    { //继承了Mono的对象不能直接new，只能通过拖拽脚本或者API取添加脚本
      //U3D内部帮我们去实例化
        // 【Fix-44】惰性查找：Awake 前调用返回 null 会让调用方 NRE；
        // 场景切换后原对象销毁，instance 变成假 null 引用——此处统一兜底查找
        if (instance == null)
        {
            instance = Object.FindFirstObjectByType<T>();
        }
        return instance;
    }

    protected virtual void Awake()
    {
        instance = this as T;
        // 【Fix-44】单例跨场景常驻：销毁后 GetInstance 的兜底查找才能维持单例语义
        DontDestroyOnLoad(gameObject);
    }

}
