using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SingletonMono<T> : MonoBehaviour where T : MonoBehaviour
{

    private static T instance;

    public static T GetInstance()
    { //继承了Mono的对象不能直接new，只能通过拖拽脚本或者API取添加脚本
      //U3D内部帮我们去实例化
        return instance;
    }

    protected virtual void Awake()
    {
        instance = this as T;
    }

}
