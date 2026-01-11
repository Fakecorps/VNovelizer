using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using LitJson;
using UnityEditor;
using System.IO;

/// <summary>
/// Json数据管理类 主要用于进行Json的序列化储存到硬盘和反序列化读取
/// </summary>
/// 
public enum JsonType
{ 
    JsonUtility,
    LitJson
}

public class JsonManager :BaseManager<JsonManager>
{
    //储存Json数据 序列化
    public void   SaveData(object data, string fileName,JsonType type = JsonType.LitJson) 
    {
        //确定存储路径
        string path = Application.persistentDataPath + "/" + fileName + ".json";
        string jsonStr = "";
        switch (type)
        { 
            case JsonType.JsonUtility:
                jsonStr = JsonUtility.ToJson(data);
                break;
            case JsonType.LitJson:
                jsonStr = JsonMapper.ToJson(data);
                break;
        }
        File.WriteAllText(path, jsonStr);
    }

    //读取指定文件中的数据
    public T LoadData<T>(string fileName, JsonType type = JsonType.LitJson) where T : new()
    {
        //确定从哪个路径读取
        string path = Application.streamingAssetsPath + "/" + fileName + ".json";
        //先判断是否存在这个文件
        //如果不存在默认文件，就从读写文件夹中去寻找
        if (!File.Exists(path))
        { 
            path = Application.persistentDataPath + "/" + fileName + ".json";
        }
        //如果读写文件夹中都还没有，那就返回一个默认对象
        if (!File.Exists(path))
        {
            return new T();
        }

        string jsonStr = File.ReadAllText(path);

        //进行反序列化
        T data = default(T);
        switch (type)
        {
            case JsonType.JsonUtility:
                data = JsonUtility.FromJson<T>(jsonStr);
                break;
            case JsonType.LitJson:
                data = JsonMapper.ToObject<T>(jsonStr);
                break;
        }

        return default(T);
    }
}


