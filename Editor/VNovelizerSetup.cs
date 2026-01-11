using UnityEditor;
using UnityEngine;
using System.IO;
using System.Reflection;
using System.Collections.Generic;

public class VNovelizerSetup : EditorWindow
{
    private static bool isPrimeTweenInstalled = false;

    [MenuItem("VNovelizer/🔧 一键初始化 (Setup Wizard)", false, 50)]
    public static void ShowWindow()
    {
        CheckDependencies();
        GetWindow<VNovelizerSetup>("项目初始化");
    }

    private static void CheckDependencies()
    {
        System.Type type = System.Type.GetType("PrimeTween.Tween, PrimeTween");
        if (type == null) type = System.Type.GetType("PrimeTween.Tween, com.kyrylokuzyk.primetween");
        isPrimeTweenInstalled = (type != null);
    }

    private void OnGUI()
    {
        if (!isPrimeTweenInstalled)
        {
            //EditorGUILayout.HelpBox("警告：缺少核心依赖 PrimeTween。请查看文档手动安装。", MessageType.Warning);
        }

        GUILayout.Label("欢迎使用 VNovelizer！", EditorStyles.boldLabel);
        GUILayout.Space(10);
        GUILayout.Label("此工具将帮助您初始化项目结构并导入必要资源。\n(字体文件将保持引用，不进行复制)", EditorStyles.wordWrappedLabel);
        GUILayout.Space(20);

        if (GUILayout.Button("🚀 一键初始化项目", GUILayout.Height(40)))
        {
            SetupAll();
        }
    }

    private static void SetupAll()
    {
        string assetsRoot = Application.dataPath;

        // 1. 获取插件包路径
        var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(VNovelizerSetup).Assembly);
        string packagePath = packageInfo != null ? packageInfo.resolvedPath : null;

        if (string.IsNullOrEmpty(packagePath))
        {
            Debug.LogError("无法定位插件包路径！");
            return;
        }

        // 2. 创建基础目录 (StreamingAssets, Scenes)
        CreateDir(assetsRoot, "StreamingAssets");
        CreateDir(assetsRoot, "StreamingAssets/VNovelizerRes/Videos");
        CreateDir(assetsRoot, "Scenes");

        // 3. 创建 Resources 根目录
        CreateDir(assetsRoot, "Resources/VNovelizerRes");
        string resRootDest = Path.Combine(assetsRoot, "Resources/VNovelizerRes");

        // 4. 精细化复制资源
        if (!string.IsNullOrEmpty(packagePath))
        {
            string resRootSource = Path.Combine(packagePath, "Runtime/Resources/VNovelizerRes");

            if (Directory.Exists(resRootSource))
            {
                // 定义需要复制的文件夹列表
                string[] foldersToCopy = new string[]
                {
                    "Audio",
                    "Backgrounds",
                    "Characters",
                    "ExcelVNScripts",
                    "VNScripts",
                    "Materials",
                    "VFX",
                    "VNPrefabs"
                };

                foreach (var folder in foldersToCopy)
                {
                    string src = Path.Combine(resRootSource, folder);
                    string dest = Path.Combine(resRootDest, folder);

                    if (Directory.Exists(src))
                    {
                        Debug.Log($"[Setup] 正在复制 {folder}...");
                        CopyDirectory(src, dest);
                    }
                    else
                    {
                        Debug.LogWarning($"[Setup] 源文件夹不存在: {folder}");
                    }
                }

                // 复制场景
                string sceneSource = Path.Combine(packagePath, "Runtime/Scenes");
                string sceneDest = Path.Combine(assetsRoot, "Scenes");
                if (Directory.Exists(sceneSource))
                {
                    CopyDirectory(sceneSource, sceneDest);
                    AddSceneToBuildSettings("Assets/Scenes/VNGamePlay.unity");
                    AddSceneToBuildSettings("Assets/Scenes/DebugScene.unity");
                }
            }
        }

        // 创建数据容器 (不复制旧文件，而是新建)
        // 创建 GalleryContent 文件夹结构
        CreateDir(assetsRoot, "Resources/VNovelizerRes/GalleryContent");
        CreateDir(assetsRoot, "Resources/VNovelizerRes/GalleryContent/CG");
        CreateDir(assetsRoot, "Resources/VNovelizerRes/GalleryContent/Music");
        CreateDir(assetsRoot, "Resources/VNovelizerRes/GalleryContent/Scene");

        // 新建 SO
        CreateDataContainer<CGDataContainer>("Assets/Resources/VNovelizerRes/GalleryContent/CG/CGDataContainer.asset");
        CreateDataContainer<MusicDataContainer>("Assets/Resources/VNovelizerRes/GalleryContent/Music/MusicDataContainer.asset");
        CreateDataContainer<SceneDataContainer>("Assets/Resources/VNovelizerRes/GalleryContent/Scene/SceneDataContainer.asset");

        // 创建 Config 文件 (不复制旧文件，而是新建)
        string configPath = "Assets/Resources/VNProjectConfig.asset";
        if (!Directory.Exists(assetsRoot + "/Resources")) Directory.CreateDirectory(assetsRoot + "/Resources");

        if (!File.Exists(assetsRoot + "/Resources/VNProjectConfig.asset"))
        {
            var config = ScriptableObject.CreateInstance<VNProjectConfig>();
            config.ExcelSourceFolder = null; // 留空让用户自己拖
            AssetDatabase.CreateAsset(config, configPath);
            Debug.Log("✅ 已创建默认配置文件: " + configPath);
        }

        AssetDatabase.Refresh();

        var configObj = AssetDatabase.LoadAssetAtPath<Object>(configPath);
        if (configObj != null) Selection.activeObject = configObj;

        EditorUtility.DisplayDialog("完成", "初始化成功！\n\n1. 核心资源已导入 (不含字体)\n2. 数据容器已新建\n3. 场景已配置", "好的");
    }

    private static void CreateDir(string root, string subPath)
    {
        string path = Path.Combine(root, subPath);
        if (!Directory.Exists(path)) Directory.CreateDirectory(path);
    }

    private static void CreateDataContainer<T>(string path) where T : ScriptableObject
    {
        if (AssetDatabase.LoadAssetAtPath<T>(path) == null)
        {
            var so = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(so, path);
            Debug.Log($"📄 新建数据容器: {path}");
        }
    }

    private static void CopyDirectory(string sourceDir, string destDir)
    {
        DirectoryInfo dir = new DirectoryInfo(sourceDir);
        if (!dir.Exists) return;

        if (!Directory.Exists(destDir)) Directory.CreateDirectory(destDir);

        foreach (FileInfo file in dir.GetFiles())
        {
            if (file.Extension == ".meta") continue;

            // 【过滤】排除字体文件
            if (file.Extension == ".ttf" || file.Extension == ".otf") continue;

            // 【过滤】排除 .asset 文件 (DataContainer 和 Config)
            if (file.Extension == ".asset") continue;

            string tempPath = Path.Combine(destDir, file.Name);
            if (!File.Exists(tempPath))
            {
                file.CopyTo(tempPath, false);
            }
        }

        foreach (DirectoryInfo subdir in dir.GetDirectories())
        {
            string tempPath = Path.Combine(destDir, subdir.Name);
            CopyDirectory(subdir.FullName, tempPath);
        }
    }

    private static void AddSceneToBuildSettings(string scenePath)
    {
        foreach (var scene in EditorBuildSettings.scenes)
        {
            if (scene.path == scenePath) return;
        }

        var original = EditorBuildSettings.scenes;
        var newSettings = new EditorBuildSettingsScene[original.Length + 1];
        System.Array.Copy(original, newSettings, original.Length);

        newSettings[newSettings.Length - 1] = new EditorBuildSettingsScene(scenePath, true);
        EditorBuildSettings.scenes = newSettings;
    }
}

[InitializeOnLoad]
public class AutoOpenWizard
{
    static AutoOpenWizard()
    {
        if (!EditorPrefs.GetBool("VNovelizer_Setup_Shown", false))
        {
            EditorApplication.delayCall += () => {
                VNovelizerSetup.ShowWindow();
                EditorPrefs.SetBool("VNovelizer_Setup_Shown", true);
            };
        }
    }
}