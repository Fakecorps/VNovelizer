using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using LitJson;
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
    public void SaveData(object data, string fileName,JsonType type = JsonType.LitJson) 
    {
        //确定存储路径
        string path = Application.persistentDataPath + "/" + fileName + ".json";
        string jsonStr = "";
        switch (type)
        { 
            case JsonType.JsonUtility:
                jsonStr = JsonUtility.ToJson(data);
                // 【Fix-61】JsonUtility 不支持 Dictionary/List 嵌套：含字典字段的数据会被静默序列化为
                // "{}"（数据全丢且无任何告警）。检测并明确提示改用 LitJson。
                if (jsonStr == "{}" && ContainsDictionaryField(data))
                    Debug.LogWarning($"[JsonManager] {fileName}: JsonUtility 不支持字典字段，数据可能丢失，请使用 LitJson");
                break;
            case JsonType.LitJson:
                jsonStr = JsonMapper.ToJson(data);
                break;
        }

        // 【Fix-61】写盘异常兜底：磁盘满/权限问题不再直接抛异常中断调用方
        try
        {
            File.WriteAllText(path, jsonStr);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[JsonManager] 保存 {fileName} 失败: {e.Message}");
        }
    }

    /// <summary>【Fix-61】检查对象（含嵌套）是否含 IDictionary 字段（JsonUtility 不支持）</summary>
    private static bool ContainsDictionaryField(object data)
    {
        if (data == null) return false;
        var type = data.GetType();
        while (type != null && type != typeof(object))
        {
            foreach (var f in type.GetFields(
                         System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance |
                         System.Reflection.BindingFlags.NonPublic))
            {
                if (typeof(IDictionary).IsAssignableFrom(f.FieldType)) return true;
            }
            type = type.BaseType;
        }
        return false;
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

        // 【Fix-61】读取/解析异常兜底：文件损坏时返回默认对象而非抛异常中断流程
        // （Android 上 StreamingAssets 为 jar 内路径，File.ReadAllText 必然失败，
        //  此处统一降级为默认对象，行为可预期）
        try
        {
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

            return data;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[JsonManager] 读取 {fileName} 失败（返回默认对象）: {e.Message}");
            return new T();
        }
    }
}


