using UnityEditor;
using UnityEngine;
using System.IO;
using System.Collections.Generic;

/// <summary>
/// 一键初始化向导（零复制流程，见 Docs/VNResourceProviderRefactoring.md）。
///
/// 原则：不复制任何包内内容到 Assets。全部通过 Addressables 注册（GUID 寻址，文件留在原地）：
/// - 新项目：包内默认资源（Runtime/PackageDefault/VNovelizerRes）注册进 VNovelizer 组；
/// - 存量项目：旧目录（Assets/Resources/VNovelizerRes）中的用户副本注册进组（副本优先于包内原件，
///   与 Resources 兜底所见一致）；
/// - 唯一写入用户 Assets 的资产：VNProjectConfig（全引擎引导配置）与画廊数据容器（用户数据）。
/// </summary>
public class VNovelizerSetup : EditorWindow
{
    private static bool isPrimeTweenInstalled = false;

    [MenuItem("VNovelizer/一键初始化 (Setup Wizard)", false, 50)]
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

        bool legacyExists = Directory.Exists("Assets/Resources/VNovelizerRes");
        if (legacyExists)
        {
            GUILayout.Label("检测到旧版资源目录（Assets/Resources/VNovelizerRes）。\n" +
                "初始化会把该目录注册进 Addressables（不复制、不移动、不修改任何文件），\n" +
                "运行时优先按地址加载其中的内容。", EditorStyles.wordWrappedLabel);
        }
        GUILayout.Space(4);
        GUILayout.Label("初始化内容：\n" +
            "• 包内默认资源注册进 Addressables（不复制任何文件到 Assets）\n" +
            "• 创建用户内容工作区 Assets/VNovelizer（空目录骨架）\n" +
            "• 创建项目配置与画廊数据容器（仅用户必需的数据资产）\n" +
            "• 注册场景到 Build Settings（直接引用包内场景，不复制）\n" +
            "• 配置包依赖 / TMP / Input System", EditorStyles.wordWrappedLabel);
        GUILayout.Space(20);

        if (GUILayout.Button("一键初始化项目", GUILayout.Height(40)))
        {
            SetupAll();
        }
    }

    private static void SetupAll()
    {
        string assetsRoot = Application.dataPath;

        // 1. 获取插件包路径
        var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(VNovelizerSetup).Assembly);
        string packageName = packageInfo != null ? packageInfo.name : null;

        if (string.IsNullOrEmpty(packageName))
        {
            Debug.LogError("无法定位插件包路径！");
            return;
        }

        // 2. 基础目录：StreamingAssets（视频始终走 StreamingAssets 原始文件，不经资源提供者链）
        CreateDir(assetsRoot, "StreamingAssets");
        CreateDir(assetsRoot, "StreamingAssets/VNovelizerRes/Videos");

        // 3. 项目配置（全项目唯一的 Resources 引导资产）
        string configPath = EnsureProjectConfig(assetsRoot);

        // 4. 用户工作区（空目录骨架，不写任何文件）
        VNProjectPaths.EnsureWorkspaceFolders();

        // 5. Addressables 注册（核心步骤，零复制）：
        //    存量项目 → 注册旧目录用户副本；新项目 → 注册包内默认资源（文件留在包里）
        bool legacyExists = Directory.Exists(Path.Combine(assetsRoot, "Resources/VNovelizerRes"));
        int registeredCount = VNAddressablesRegistrar.SyncAll();

        // 6. 画廊数据容器（用户数据；目录按存量/新项目双模式解析，已存在则跳过）
        string cgKey = VNUIPrefabKeys.CGDataContainer;
        string musicKey = VNUIPrefabKeys.MusicDataContainer;
        string sceneKey = VNUIPrefabKeys.SceneDataContainer;

        CreateDataContainer<CGDataContainer>(VNProjectPaths.ResourceKeyToFolder(VNResourceKeys.KeyToCategory(cgKey)) + "/CGDataContainer.asset", cgKey);
        CreateDataContainer<MusicDataContainer>(VNProjectPaths.ResourceKeyToFolder(VNResourceKeys.KeyToCategory(musicKey)) + "/MusicDataContainer.asset", musicKey);
        CreateDataContainer<SceneDataContainer>(VNProjectPaths.ResourceKeyToFolder(VNResourceKeys.KeyToCategory(sceneKey)) + "/SceneDataContainer.asset", sceneKey);

        // 7. 场景注册：本地副本（Assets/Scenes，存量用户可能已自定义）存在则用副本，
        //    否则直接注册包内场景（不复制文件）
        RegisterScenes(assetsRoot, packageName);

        // 8. 确保包依赖（PrimeTween scoped registry + Package）
        EnsureManifestDependencies();

        // 9. 导入 TMP Essential Resources
        ImportTMPEssentialResources();

        // 10. 配置 Input System 为 Both 模式
        bool needRestart = ConfigureInputSystemBoth();

        AssetDatabase.Refresh();

        var configObj = AssetDatabase.LoadAssetAtPath<Object>(configPath);
        if (configObj != null) Selection.activeObject = configObj;

        string completeMsg = "初始化成功！\n\n" +
            (legacyExists
                ? $"检测到旧版资源目录，已将其内容注册进 Addressables（{registeredCount} 个资产，未复制/未修改任何文件）。\n" +
                  "运行时优先按地址加载其中内容；如需彻底迁移到工作区模式，可逐步把内容移入 Assets/VNovelizer 并重新同步。"
                : $"Addressables 模式：{registeredCount} 个包内默认资源已注册进 VNovelizer 组（未复制任何文件到 Assets）。\n" +
                  "用户内容工作区：Assets/VNovelizer（角色/背景/剧本/音频放这里即可，或经资源管理器拖放分配——资产可放在项目内任意位置）") + "\n\n" +
            "1. 画廊数据容器已就绪\n" +
            "2. 场景已注册到 Build Settings\n" +
            "3. 包依赖已写入 manifest.json\n" +
            "4. TMP Essential Resources 已导入\n" +
            "5. Input System 已设为 Both 模式" +
            "\n\n注意：构建游戏（File → Build Settings → Build）之前，请先执行 Addressables 构建：\n" +
            "Window → Asset Management → Addressables → Groups → Build → New Build → Default Build Script";

        if (needRestart)
        {
            completeMsg += "\n\n请重启 Unity Editor 以使 Input System 配置生效。";
        }

        EditorUtility.DisplayDialog("完成", completeMsg, "好的");
    }

    // ==================== 场景注册 ====================

    /// <summary>
    /// 场景注册（零复制）：本地副本（Assets/Scenes，存量用户可能已自定义）存在则注册副本，
    /// 否则直接注册包内场景到 Build Settings（按名加载行为不变）。
    /// </summary>
    private static void RegisterScenes(string assetsRoot, string packageName)
    {
        if (File.Exists(Path.Combine(assetsRoot, "Scenes/VNMainMenu.unity")))
        {
            AddSceneToBuildSettings("Assets/Scenes/VNMainMenu.unity");
            AddSceneToBuildSettings("Assets/Scenes/VNGamePlay.unity");
            AddSceneToBuildSettings("Assets/Scenes/VNDebugScene.unity");
            return;
        }

        string sceneRoot = $"Packages/{packageName}/Runtime/Scenes";
        AddSceneToBuildSettings(sceneRoot + "/VNMainMenu.unity");
        AddSceneToBuildSettings(sceneRoot + "/VNGamePlay.unity");
        AddSceneToBuildSettings(sceneRoot + "/VNDebugScene.unity");
        Debug.Log("[Setup] 已注册包内场景到 Build Settings（未复制文件）");
    }

    // ==================== 项目配置 ====================

    /// <summary>
    /// 确保 VNProjectConfig 存在（Assets/Resources/VNProjectConfig.asset，全项目唯一的 Resources 引导资产）。
    /// 顺带为 Excel 工作流填入工作区默认文件夹引用（仅在用户未配置时）。
    /// </summary>
    private static string EnsureProjectConfig(string assetsRoot)
    {
        string configPath = "Assets/Resources/VNProjectConfig.asset";
        if (!Directory.Exists(assetsRoot + "/Resources")) Directory.CreateDirectory(assetsRoot + "/Resources");

        if (!File.Exists(assetsRoot + "/Resources/VNProjectConfig.asset"))
        {
            var config = ScriptableObject.CreateInstance<VNProjectConfig>();
            config.ExcelSourceFolder = null;
            AssetDatabase.CreateAsset(config, configPath);
            Debug.Log("[VNovelizer Setup] 已创建默认配置文件: " + configPath);
        }

        var loadedConfig = AssetDatabase.LoadAssetAtPath<VNProjectConfig>(configPath);
        if (loadedConfig != null)
        {
            bool dirty = false;
            if (loadedConfig.ExcelSourceFolder == null)
            {
                loadedConfig.ExcelSourceFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(VNProjectPaths.ExcelFolder);
                dirty = true;
            }
            if (loadedConfig.CsvOutputFolder == null)
            {
                loadedConfig.CsvOutputFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(VNProjectPaths.ScriptsFolder);
                dirty = true;
            }
            if (dirty) EditorUtility.SetDirty(loadedConfig);
        }

        return configPath;
    }

    private static void CreateDir(string root, string subPath)
    {
        string path = Path.Combine(root, subPath);
        if (!Directory.Exists(path)) Directory.CreateDirectory(path);
    }

    /// <summary>创建数据容器（已存在则跳过）并注册进 Addressables。</summary>
    private static void CreateDataContainer<T>(string path, string resourceKey) where T : ScriptableObject
    {
        if (AssetDatabase.LoadAssetAtPath<T>(path) == null)
        {
            var so = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(so, path);
            Debug.Log($"[VNovelizer Setup] 新建数据容器: {path}");
        }

        // 无论新建还是已存在都确保注册（存量项目的容器首次纳入地址管理）
        VNAddressablesRegistrar.RegisterAssetAtPath(path, resourceKey);
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

    // ===== 包依赖（manifest.json） =====
    private static void EnsureManifestDependencies()
    {
        string manifestPath = Path.Combine(Application.dataPath, "..", "Packages", "manifest.json");
        if (!File.Exists(manifestPath))
        {
            Debug.LogError("[Setup] 找不到 Packages/manifest.json，跳过包依赖配置");
            return;
        }

        // 先读取，统一换行符，避免 \r\n 干扰
        string content = File.ReadAllText(manifestPath).Replace("\r\n", "\n").Replace("\r", "\n");

        bool needRegistry   = !content.Contains("\"com.kyrylokuzyk\"");
        bool needPrimeTween = !content.Contains("\"com.kyrylokuzyk.primetween\"");

        if (!needRegistry && !needPrimeTween)
        {
            Debug.Log("[Setup] 包依赖已就绪，无需修改");
            return;
        }

        if (needRegistry)
        {
            // 在 dependencies 块闭合后追加 scopedRegistries（保持根对象为合法 JSON）
            string regJson =
                ",\n" +
                "  \"scopedRegistries\": [\n" +
                "    {\n" +
                "      \"name\": \"npm\",\n" +
                "      \"url\": \"https://registry.npmjs.org\",\n" +
                "      \"scopes\": [\n" +
                "        \"com.kyrylokuzyk\"\n" +
                "      ]\n" +
                "    }\n" +
                "  ]\n" +
                "}";

            int lastIdx = content.LastIndexOf('}');
            if (lastIdx >= 0)
            {
                int depCloseIdx = content.LastIndexOf('}', lastIdx - 1);
                if (depCloseIdx >= 0)
                {
                    string before = content.Substring(0, depCloseIdx + 1); // 包含 dependencies 的 }
                    content = before + regJson;
                    File.WriteAllText(manifestPath, content);
                    Debug.Log("[Setup] 已添加 scoped registry: npm (com.kyrylokuzyk)");
                }
                else
                {
                    Debug.LogError("[Setup] manifest.json 格式异常，找不到 dependencies 闭合括号");
                }
            }
            else
            {
                Debug.LogError("[Setup] manifest.json 格式异常，无法写入 scopedRegistries");
                return;
            }
        }

        if (needPrimeTween)
        {
            try
            {
                UnityEditor.PackageManager.Client.Add("com.kyrylokuzyk.primetween");
                Debug.Log("[Setup] 已发起 PrimeTween 安装请求，Unity 将在后台解析版本并安装");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[Setup] PrimeTween 安装请求失败: " + e.Message);
            }
        }
    }

    // ===== TMP Essential Resources =====
    private static void ImportTMPEssentialResources()
    {
        var tmpSettings = AssetDatabase.LoadAssetAtPath<Object>("Assets/Resources/TMP Settings.asset");
        if (tmpSettings == null)
        {
            tmpSettings = Resources.Load<Object>("TMP Settings");
        }
        if (tmpSettings != null)
        {
            Debug.Log("[Setup] TMP Essential Resources 已存在，跳过导入");
            return;
        }

        try
        {
            EditorApplication.ExecuteMenuItem("Window/TextMeshPro/Import TMP Essential Resources");
            Debug.Log("[Setup] 已触发 TMP Essential Resources 导入");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[Setup] TMP Essential Resources 导入失败: " + e.Message);
        }
    }

    // ===== Input System 为 Both 模式 =====
    /// <summary>切换 Active Input Handling 为 "Both"，需重启 Editor 生效。返回 true 表示做了修改。</summary>
    private static bool ConfigureInputSystemBoth()
    {
        string projectSettingsPath = Path.Combine(Application.dataPath, "..", "ProjectSettings", "ProjectSettings.asset");
        if (!File.Exists(projectSettingsPath))
        {
            Debug.LogWarning("[Setup] 找不到 ProjectSettings.asset");
            return false;
        }

        string content = File.ReadAllText(projectSettingsPath);

        if (content.Contains("activeInputHandler: 2"))
        {
            Debug.Log("[Setup] Input System 已为 Both 模式，无需修改");
            return false;
        }

        if (content.Contains("activeInputHandler: 0"))
        {
            content = content.Replace("activeInputHandler: 0", "activeInputHandler: 2");
        }
        else if (content.Contains("activeInputHandler: 1"))
        {
            content = content.Replace("activeInputHandler: 1", "activeInputHandler: 2");
        }
        else
        {
            Debug.LogWarning("[Setup] ProjectSettings.asset 中未找到 activeInputHandler");
            return false;
        }

        File.WriteAllText(projectSettingsPath, content);
        Debug.Log("[Setup] Input System 已切换为 Both 模式（需重启 Editor 生效）");
        return true;
    }
}

[InitializeOnLoad]
public class AutoOpenWizard
{
    static AutoOpenWizard()
    {
        if (!EditorPrefs.GetBool("VNovelizer_Setup_Shown", false))
        {
            EditorApplication.delayCall += () =>
            {
                VNovelizerSetup.ShowWindow();
                EditorPrefs.SetBool("VNovelizer_Setup_Shown", true);
            };
        }
    }
}
