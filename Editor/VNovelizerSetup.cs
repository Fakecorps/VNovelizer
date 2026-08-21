using UnityEditor;
using UnityEngine;
using System.IO;

/// <summary>
/// 一键初始化向导（零 Assets 写入，见 Docs/VNResourceProviderRefactoring.md）。
///
/// 原则：不向 Assets 写入任何内容，除非用户自己创建。全部职责：
/// - Addressables 注册（GUID 寻址，零复制）：新项目注册包内默认资源；
///   存量项目注册旧目录用户副本（不复制/不移动/不修改任何文件）；
/// - 引擎依赖配置：PrimeTween scoped registry、TMP Essential Resources、Input System Both 模式；
/// - 引导说明（配置/画廊容器/游戏入口均为"首次使用时自动出现"）。
///
/// 用户侧资产均为按需自动生成（不预创建）：
/// - 配置：首次打开 Edit → Project Settings → VNovelizer 时创建；
/// - 画廊数据容器：首次打开画廊编辑器时提示创建（自选路径）；
/// - 工作区/各类文件夹：首次拖放分配/创建 SO 时自动建立。
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
        GUILayout.Label("初始化内容（零 Assets 写入）：\n" +
            "• 包内默认资源注册进 Addressables（不复制任何文件）\n" +
            "• 配置包依赖（PrimeTween / TMP / Input System）\n\n" +
            "其余一切按需自动出现（无需初始化）：\n" +
            "• 项目配置：首次打开 Edit → Project Settings → VNovelizer 时创建\n" +
            "• 画廊数据容器：首次打开画廊编辑器时提示创建（自选路径）\n" +
            "• 资源内容：资源管理器拖放分配 / 编辑器创建（位置自选）\n" +
            "• 游戏入口：任意场景挂 VNRuntimeInitializer 组件即可开始游戏", EditorStyles.wordWrappedLabel);
        GUILayout.Space(20);

        if (GUILayout.Button("一键初始化项目", GUILayout.Height(40)))
        {
            SetupAll();
        }
    }

    private static void SetupAll()
    {
        string assetsRoot = Application.dataPath;

        // 1. Addressables 注册（核心步骤，零复制）：
        //    存量项目 → 注册旧目录用户副本；新项目 → 注册包内默认资源（文件留在包里）
        bool legacyExists = Directory.Exists(Path.Combine(assetsRoot, "Resources/VNovelizerRes"));
        int registeredCount = VNAddressablesRegistrar.SyncAll();

        // 2. 确保包依赖（PrimeTween scoped registry + Package）
        EnsureManifestDependencies();

        // 3. 导入 TMP Essential Resources
        ImportTMPEssentialResources();

        // 4. 配置 Input System 为 Both 模式
        bool needRestart = ConfigureInputSystemBoth();

        AssetDatabase.Refresh();

        string completeMsg = "初始化成功！\n\n" +
            (legacyExists
                ? $"检测到旧版资源目录，已将其内容注册进 Addressables（{registeredCount} 个资产，未复制/未修改任何文件）。"
                : $"Addressables 模式：{registeredCount} 个包内默认资源已注册进 VNovelizer 组（未向 Assets 写入任何文件）。") + "\n\n" +
            "• 项目配置：Edit → Project Settings → VNovelizer（首次打开自动创建）\n" +
            "• 资源管理：VNovelizer → 资源管理器（拖放分配，资产可放任意位置）\n" +
            "• 游戏入口：任意场景挂 VNRuntimeInitializer 组件，或代码调用 VNManager.StartGame\n" +
            "• 剧本试玩：剧本管理器「试玩」按钮（任意场景直接 Play）" +
            "\n\n注意：构建游戏之前，请先执行 Addressables 构建：\n" +
            "Window → Asset Management → Addressables → Groups → Build → New Build → Default Build Script";

        if (needRestart)
        {
            completeMsg += "\n\n请重启 Unity Editor 以使 Input System 配置生效。";
        }

        EditorUtility.DisplayDialog("完成", completeMsg, "好的");
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
