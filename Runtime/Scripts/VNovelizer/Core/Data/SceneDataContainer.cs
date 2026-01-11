using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 场景数据容器（ScriptableObject）
/// </summary>
[CreateAssetMenu(fileName = "SceneDataContainer", menuName = "VNovelizer/Scene Data Container")]
public class SceneDataContainer : ScriptableObject
{
    [Tooltip("场景列表")]
    public List<VNScene> sceneList = new List<VNScene>();

    /// <summary>
    /// 根据ID获取场景数据
    /// </summary>
    public VNScene GetSceneByID(string sceneID)
    {
        if (sceneList == null) return null;

        foreach (VNScene scene in sceneList)
        {
            if (scene != null && scene.VNscriptID == sceneID)
            {
                return scene;
            }
        }
        return null;
    }

    /// <summary>
    /// 添加场景
    /// </summary>
    public void AddScene(VNScene scene)
    {
        if (sceneList == null)
        {
            sceneList = new List<VNScene>();
        }
        sceneList.Add(scene);
    }

    /// <summary>
    /// 移除场景
    /// </summary>
    public void RemoveScene(VNScene scene)
    {
        if (sceneList != null)
        {
            sceneList.Remove(scene);
        }
    }
}






