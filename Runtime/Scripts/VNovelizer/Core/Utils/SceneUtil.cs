#if UNITY_EDITOR  
using UnityEngine;
using UnityEditor;
using System.Linq;

public class AddSceneToBuildSettings : Editor
{
    // 添加菜单项到右键菜单  
    [MenuItem("Assets/Add Scene to Build Settings")]
    private static void AddSelectedScenesToBuildSettings()
    {        // 获取所有选中的对象  
        Object[] selectedObjects = Selection.objects;

        // 过滤出选中的场景文件  
        var sceneAssets = selectedObjects
            .Where(obj => obj is SceneAsset) // 只处理类型为 SceneAsset 的对象（即场景文件）  
            .Select(AssetDatabase.GetAssetPath) // 获取场景文件的路径  
            .ToArray(); // 将结果转换为数组  

        // 如果没有选中任何场景文件，提示警告并退出  
        if (sceneAssets.Length == 0)
        {
            Debug.LogWarning("No scene files selected. Please select one or more scene files.");
            return;
        }
        // 获取当前的 Build Settings 场景列表，并转换为 List 以便修改  
        var buildScenes = EditorBuildSettings.scenes.ToList();

        // 遍历选中的场景文件  
        foreach (var scenePath in sceneAssets)
        {            // 检查场景是否已经存在于 Build Settings 中  
            bool sceneAlreadyInBuild = buildScenes.Any(scene => scene.path == scenePath);

            // 如果场景不在 Build Settings 中，则添加  
            if (!sceneAlreadyInBuild)
            {                // 创建一个新的 EditorBuildSettingsScene 对象，并将其添加到列表中  
                buildScenes.Add(new EditorBuildSettingsScene(scenePath, true));
                Debug.Log("Scene added to Build Settings: " + scenePath);
            }
            else
            {
                // 如果场景已经存在于 Build Settings 中，提示警告  
                Debug.LogWarning("Scene already in Build Settings: " + scenePath);
            }
        }
        // 更新 Build Settings 的场景列表  
        EditorBuildSettings.scenes = buildScenes.ToArray();
    }
    // 验证菜单项是否可用  
    [MenuItem("Assets/Add Scene to Build Settings", true)]
    private static bool ValidateAddSelectedScenesToBuildSettings()
    {        // 只有在选中至少一个场景文件时才启用菜单项  
        return Selection.objects.Any(obj => obj is SceneAsset);
    }
}
#endif