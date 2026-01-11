using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BaseManager<T>where T:new()
{
    private static T instance;
    public static T GetInstance()
    {
        if (instance == null)
        { 
            instance = new T();
        }
        return instance;
    }

}//单例模式基类

public class GameManager : BaseManager<GameManager>
{ 

}
