using UnityEditor;
using UnityEngine;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using System.Text.RegularExpressions;

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

        bool legacyMode = Directory.Exists("Assets/Resources/VNovelizerRes");
        if (legacyMode)
        {
            GUILayout.Label("检测到旧版资源目录（Assets/Resources/VNovelizerRes），将沿用兼容模式。\n（新项目不再复制资源到 Assets，改用 Addressables 注册，详见文档）",
                EditorStyles.wordWrappedLabel);
        }
        else
        {
            GUILayout.Label("此工具将帮助您初始化项目结构、安装依赖并注册资源。\n" +
                "新流程不会复制任何资源文件到 Assets：包内默认资源直接注册进 Addressables，\n" +
                "您自己的内容请放在 Assets/VNovelizer 工作区。\n(首次运行将跳过已存在的文件，保留用户定制内容)",
                EditorStyles.wordWrappedLabel);
        }
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
        string packagePath = packageInfo != null ? packageInfo.resolvedPath : null;
        string packageName = packageInfo != null ? packageInfo.name : null;

        if (string.IsNullOrEmpty(packagePath))
        {
            Debug.LogError("无法定位插件包路径！");
            return;
        }

        // 2. 基础目录：StreamingAssets（视频始终走 StreamingAssets，不经资源提供者链）
        CreateDir(assetsRoot, "StreamingAssets");
        CreateDir(assetsRoot, "StreamingAssets/VNovelizerRes/Videos");

        // 3. 项目配置（全项目唯一的 Resources 引导资产，先建好供后续步骤读取路径）
        string configPath = EnsureProjectConfig(assetsRoot);

        // 4. 双模式初始化资源：
        //    存量项目（旧目录存在）→ 兼容模式：沿用旧版复制流程；
        //    新项目 → Addressables 模式：包内默认资源注册进 VNovelizer 组（不复制文件），工作区只建空目录
        bool legacyMode = Directory.Exists(Path.Combine(assetsRoot, "Resources/VNovelizerRes"));
        int registeredCount = 0;
        if (legacyMode)
        {
            SetupLegacyMode(assetsRoot, packagePath);
        }
        else
        {
            registeredCount = SetupAddressablesMode();
        }

        // 5. 场景注册：本地副本优先（存量用户可能已自定义）；无副本直接注册包内场景（不复制）
        RegisterScenes(assetsRoot, packageName, packagePath, legacyMode);

        // 6. 确保包依赖（PrimeTween scoped registry + Package）
        EnsureManifestDependencies();

        // 7. 导入 TMP Essential Resources
        ImportTMPEssentialResources();

        // 8. 配置 Input System 为 Both 模式
        bool needRestart = ConfigureInputSystemBoth();

        AssetDatabase.Refresh();

        var configObj = AssetDatabase.LoadAssetAtPath<Object>(configPath);
        if (configObj != null) Selection.activeObject = configObj;

        string completeMsg = "初始化成功！\n\n" +
            (legacyMode
                ? "兼容模式：检测到旧版资源目录，已沿用 Assets/Resources/VNovelizerRes。\n" +
                  "（如需迁移到 Addressables 模式请查阅 Docs/VNResourceProviderRefactoring.md）"
                : $"Addressables 模式：{registeredCount} 个包内默认资源已注册进 VNovelizer 组（未复制任何文件到 Assets）。\n" +
                  "用户内容工作区：Assets/VNovelizer（角色/背景/剧本/音频放这里即可）") + "\n\n" +
            "1. 数据容器已就绪\n" +
            "2. 场景已配置\n" +
            "3. 包依赖已写入 manifest.json\n" +
            "4. TMP Essential Resources 已导入\n" +
            "5. Input System 已设为 Both 模式";

        if (!legacyMode)
        {
            completeMsg += "\n\n注意：构建游戏（File → Build Settings → Build）之前，请先执行 Addressables 构建：\n" +
                           "Window → Asset Management → Addressables → Groups → Build → New Build → Default Build Script";
        }

        if (needRestart)
        {
            completeMsg += "\n\n请重启 Unity Editor 以使 Input System 配置生效。";
        }

        EditorUtility.DisplayDialog("完成", completeMsg, "好的");
    }

    // ==================== 旧版兼容模式（存量项目） ====================

    /// <summary>旧版流程：复制包内默认内容到 Assets/Resources/VNovelizerRes（已存在的文件跳过）</summary>
    private static void SetupLegacyMode(string assetsRoot, string packagePath)
    {
        CreateDir(assetsRoot, "Resources/VNovelizerRes");
        string resRootDest = Path.Combine(assetsRoot, "Resources/VNovelizerRes");

        string resRootSource = Path.Combine(packagePath, "Runtime/PackageDefault/VNovelizerRes");
        if (Directory.Exists(resRootSource))
        {
            string[] foldersToCopy = new string[]
            {
                "Audio",
                "Backgrounds",
                "Characters",
                "ExcelVNScripts",
                "Fonts",
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
        }

        CreateDir(assetsRoot, "Resources/VNovelizerRes/GalleryContent");
        CreateDir(assetsRoot, "Resources/VNovelizerRes/GalleryContent/CG");
        CreateDir(assetsRoot, "Resources/VNovelizerRes/GalleryContent/Music");
        CreateDir(assetsRoot, "Resources/VNovelizerRes/GalleryContent/Scene");

        CreateDataContainer<CGDataContainer>("Assets/Resources/VNovelizerRes/GalleryContent/CG/CGDataContainer.asset", null);
        CreateDataContainer<MusicDataContainer>("Assets/Resources/VNovelizerRes/GalleryContent/Music/MusicDataContainer.asset", null);
        CreateDataContainer<SceneDataContainer>("Assets/Resources/VNovelizerRes/GalleryContent/Scene/SceneDataContainer.asset", null);
    }

    // ==================== Addressables 模式（新项目） ====================

    /// <summary>
    /// 新版流程：
    /// 1. 创建用户工作区 Assets/VNovelizer（仅空目录骨架，不放任何文件）；
    /// 2. 初始化 Addressables 并注册包内默认资源（地址 = 资源键，文件本体留在包内）；
    /// 3. 在工作区创建画廊数据容器并注册地址。
    /// 返回注册的资产数。
    /// </summary>
    private static int SetupAddressablesMode()
    {
        // 1. 用户工作区（幂等，只建目录）
        VNProjectPaths.EnsureWorkspaceFolders();

        // 2. Addressables 注册（不存在设置时自动创建 Assets/AddressableAssetsData）
        int count = VNAddressablesRegistrar.SyncAll();

        // 3. 画廊数据容器（键与 VNProjectConfig 默认路径一致）
        string cgKey = "VNovelizerRes/GalleryContent/CG/CGDataContainer";
        string musicKey = "VNovelizerRes/GalleryContent/Music/MusicDataContainer";
        string sceneKey = "VNovelizerRes/GalleryContent/Scene/SceneDataContainer";
        if (VNProjectConfig.Instance != null)
        {
            if (!string.IsNullOrEmpty(VNProjectConfig.Instance.CG_DataPath)) cgKey = VNProjectConfig.Instance.CG_DataPath + "/CGDataContainer";
            if (!string.IsNullOrEmpty(VNProjectConfig.Instance.Music_DataPath)) musicKey = VNProjectConfig.Instance.Music_DataPath + "/MusicDataContainer";
            if (!string.IsNullOrEmpty(VNProjectConfig.Instance.Scene_DataPath)) sceneKey = VNProjectConfig.Instance.Scene_DataPath + "/SceneDataContainer";
        }

        CreateDataContainer<CGDataContainer>(VNProjectPaths.ResourceKeyToFolder(VNResourceKeys.KeyToCategory(cgKey)) + "/CGDataContainer.asset", cgKey);
        CreateDataContainer<MusicDataContainer>(VNProjectPaths.ResourceKeyToFolder(VNResourceKeys.KeyToCategory(musicKey)) + "/MusicDataContainer.asset", musicKey);
        CreateDataContainer<SceneDataContainer>(VNProjectPaths.ResourceKeyToFolder(VNResourceKeys.KeyToCategory(sceneKey)) + "/SceneDataContainer.asset", sceneKey);

        return count;
    }

    // ==================== 场景注册 ====================

    /// <summary>
    /// 场景注册：本地副本（Assets/Scenes）优先——存量用户可能已自定义场景；
    /// 无本地副本时直接注册包内场景到 Build Settings（不再复制场景文件）。
    /// </summary>
    private static void RegisterScenes(string assetsRoot, string packageName, string packagePath, bool legacyMode)
    {
        // 本地副本优先（存量用户可能已自定义）
        if (legacyMode || File.Exists(Path.Combine(assetsRoot, "Scenes/VNMainMenu.unity")))
        {
            if (legacyMode)
            {
                // 旧流程：复制包内场景到 Assets/Scenes（已存在的文件跳过，保留用户定制）
                CreateDir(assetsRoot, "Scenes");
                string sceneSource = Path.Combine(packagePath, "Runtime/Scenes");
                string sceneDest = Path.Combine(assetsRoot, "Scenes");
                if (Directory.Exists(sceneSource))
                {
                    CopyDirectory(sceneSource, sceneDest);
                }
            }
            AddSceneToBuildSettings("Assets/Scenes/VNMainMenu.unity");
            AddSceneToBuildSettings("Assets/Scenes/VNGamePlay.unity");
            AddSceneToBuildSettings("Assets/Scenes/VNDebugScene.unity");
            return;
        }

        // 新流程：直接注册包内场景（不复制文件）
        if (string.IsNullOrEmpty(packageName)) return;
        string sceneRoot = $"Packages/{packageName}/Runtime/Scenes";
        AddSceneToBuildSettings(sceneRoot + "/VNMainMenu.unity");
        AddSceneToBuildSettings(sceneRoot + "/VNGamePlay.unity");
        AddSceneToBuildSettings(sceneRoot + "/VNDebugScene.unity");
        Debug.Log("[Setup] 已注册包内场景到 Build Settings（未复制文件）");
    }

    // ==================== 项目配置 ====================

    /// <summary>
    /// 确保 VNProjectConfig 存在（Assets/Resources/VNProjectConfig.asset，全项目唯一的 Resources 引导资产）。
    /// Addressables 模式下顺带为 Excel 工作流填入工作区默认文件夹引用。
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

        // 新模式：为剧本工作流填默认文件夹引用（工作区；仅在用户未配置时）
        if (!VNProjectPaths.IsLegacyMode)
        {
            var config = AssetDatabase.LoadAssetAtPath<VNProjectConfig>(configPath);
            if (config != null)
            {
                bool dirty = false;
                if (config.ExcelSourceFolder == null)
                {
                    config.ExcelSourceFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(VNProjectPaths.ExcelFolder);
                    dirty = true;
                }
                if (config.CsvOutputFolder == null)
                {
                    config.CsvOutputFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(VNProjectPaths.ScriptsFolder);
                    dirty = true;
                }
                if (dirty) EditorUtility.SetDirty(config);
            }
        }

        return configPath;
    }

    private static void CreateDir(string root, string subPath)
    {
        string path = Path.Combine(root, subPath);
        if (!Directory.Exists(path)) Directory.CreateDirectory(path);
    }

    /// <summary>创建数据容器（已存在则跳过）。resourceKey 非空时注册进 Addressables（Addressables 模式）。</summary>
    private static void CreateDataContainer<T>(string path, string resourceKey) where T : ScriptableObject
    {
        if (AssetDatabase.LoadAssetAtPath<T>(path) == null)
        {
            var so = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(so, path);
            Debug.Log($"[VNovelizer Setup] 新建数据容器: {path}");

            if (!string.IsNullOrEmpty(resourceKey))
                VNAddressablesRegistrar.RegisterAssetAtPath(path, resourceKey);
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

            // 排除原始字体文件
            if (file.Extension == ".ttf" || file.Extension == ".otf") continue;

            // 排除 .asset 文件（仅排除 DataContainer 和 Config，TMP SDF 字体正常复制）
            if (file.Extension == ".asset")
            {
                string fileName = Path.GetFileNameWithoutExtension(file.Name);
                if (fileName.Contains("DataContainer") || fileName == "VNProjectConfig")
                    continue;
            }

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

    // ===== 6. 初始化包依赖（manifest.json） =====
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
            // manifest.json 固定结构：
            //   { "dependencies": { ... } }  ← 无 scopedRegistries
            // 或 { "dependencies": { ... }, "scopedRegistries": [...] }
            //
            // 目标：在根对象最末的 } 前插入 scopedRegistries
            // 方法：找到最后一个 \n} 并替换为 ,\n  "scopedRegistries":[...]\n}
            string registryJson =
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

            // 找最后一个 } 的位置（root 对象结尾）
            int lastIdx = content.LastIndexOf('}');
            if (lastIdx >= 0)
            {
                // 找倒数第二个 }（dependencies 块的闭合括号）的位置
                int depCloseIdx = content.LastIndexOf('}', lastIdx - 1);
                if (depCloseIdx >= 0)
                {
                    // 在 dependencies } 后插入逗号，然后插入 scopedRegistries，然后接 root }
                    string before   = content.Substring(0, depCloseIdx + 1); // 包含 dependencies 的 }
                    string regJson  =
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

    // ===== 7. 导入 TMP Essential Resources =====
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

    // ===== 8. 配置 Input System 为 Both 模式 =====
    /// <summary>
    /// 切换 Active Input Handling 为 "Both"，需重启 Editor 生效。
    /// </summary>
    /// <returns>true 表示做了修改（需要重启）</returns>
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
