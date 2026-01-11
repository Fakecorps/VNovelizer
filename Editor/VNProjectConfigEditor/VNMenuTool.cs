using UnityEditor;
using UnityEngine;

public class VNMenuTools : Editor
{
    [MenuItem("VNovelizer/⚙️ 打开全局配置 (Open Config)",false,10)]
    public static void SelectConfig()
    {
        // 1. 尝试从 Resources 加载
        // 注意：这里加载的是运行时实例，但我们需要选中的是 Assets 里的文件
        // 所以最好用 AssetDatabase 来找文件路径

        // 假设配置文件名固定叫 VNProjectConfig
        string[] guids = AssetDatabase.FindAssets("t:VNProjectConfig");

        if (guids.Length == 0)
        {
            Debug.LogError("找不到 VNProjectConfig 配置文件！请先在 Resources 文件夹下创建它。");
            return;
        }

        // 如果有多个，默认选第一个
        string path = AssetDatabase.GUIDToAssetPath(guids[0]);
        Object configAsset = AssetDatabase.LoadAssetAtPath<Object>(path);

        if (configAsset != null)
        {
            // 2. 选中它
            Selection.activeObject = configAsset;

            // 3. 高亮闪烁一下 (Ping)
            EditorGUIUtility.PingObject(configAsset);
        }
    }
}