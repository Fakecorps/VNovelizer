using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// CG数据容器（文件名必须是 CGDataContainer.cs）
/// </summary>
[CreateAssetMenu(fileName = "CGDataContainer", menuName = "VNovelizer/CG Data Container")]
public class CGDataContainer : ScriptableObject
{
    [Tooltip("CG数据列表")]
    public List<CGData> cgList = new List<CGData>();

    public CGData GetCGData(string cgName)
    {
        return cgList.Find(cg => cg.cgName == cgName);
    }

    public void AddCGData(CGData cgData)
    {
        if (!cgList.Contains(cgData))
        {
            cgList.Add(cgData);
        }
    }

    public void RemoveCGData(CGData cgData)
    {
        cgList.Remove(cgData);
    }
}