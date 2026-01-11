using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SingletonAutoMono<T> : MonoBehaviour where T : MonoBehaviour
{

    private static T instance;

    public static T GetInstance()
    {
        if (instance == null)
        {
            GameObject obj = new GameObject();
            //将对象名字设置为脚本名
            obj.name = typeof(T).ToString();
            instance = obj.AddComponent<T>();

        }
        return instance;
    }


}

public class AutoMono : SingletonAutoMono<AutoMono>
{

}
