using UnityEditor;
using UnityEngine;
using System.IO;
using VNovelizer.Editor.Bootstrap;

/// <summary>
/// 一键初始化向导（零 Assets 写入，见 Docs/VNResourceProviderRefactoring.md）。
///
/// 原则：不向 Assets 写入任何内容，除非用户自己创建。全部职责：
/// - Addressables 注册（GUID 寻址，零复制）：新项目注册包内默认资源；
///   存量项目注册旧目录用户副本（不复制/不移动/不修改任何文件）；
/// - 引擎配置：TMP Essential Resources、Input System Both 模式；PrimeTween 由独立引导自动安装；
/// - 引导说明（配置/画廊容器/游戏入口均为"首次使用时自动出现"）。
///
/// 用户侧资产均为按需自动生成（不预创建）：
/// - 配置：首次打开 Edit → Project Settings → VNovelizer 时创建；
/// - 画廊数据容器：首次打开画廊编辑器时提示创建（自选路径）；
/// - 工作区/各类文件夹：首次拖放分配/创建 SO 时自动建立。
/// </summary>
public class VNovelizerSetup : EditorWindow
{
    // priority=1 置于 VNovelizer 菜单最顶部（其余编辑器工具从 16 起）
    [MenuItem("VNovelizer/一键初始化 (Setup Wizard)", false, 1)]
    public static void ShowWindow()
    {
        if (!VNDependencyBootstrap.IsReady)
        {
            VNDependencyBootstrap.ShowStatusWindow();
            return;
        }
        GetWindow<VNovelizerSetup>("项目初始化");
    }

    private void OnGUI()
    {
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
            "• 配置 TMP Essential Resources / Input System（PrimeTween 已自动安装）\n\n" +
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
        if (!VNDependencyBootstrap.IsReady)
        {
            VNDependencyBootstrap.ShowStatusWindow();
            return;
        }
        string assetsRoot = Application.dataPath;

        // 1. Addressables 注册（核心步骤，零复制）：
        //    存量项目 → 注册旧目录用户副本；新项目 → 注册包内默认资源（文件留在包里）
        bool legacyExists = Directory.Exists(Path.Combine(assetsRoot, "Resources/VNovelizerRes"));
        int registeredCount = VNAddressablesRegistrar.SyncAll();

        // 3. 导入 TMP Essential Resources
        ImportTMPEssentialResources();

        // 3.5 【UI 本地化开箱即送】LocalizationSettings 资产兜底：
        // 包内 UI 预制体预装官方 LocalizeStringEvent，其 OnEnable 要求工程存在
        // LocalizationSettings 资产（否则抛异常）。此处显式兜底创建（另有
        // InitializeOnLoad 静默兜底双保险）。
        VNLocaleBootstrapper.EnsureLocalizationSettings();

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

        // 【Fix-3】优先走官方 SerializedObject API 修改（不会误伤文件内其他同名文本，
        // 也避免 Unity 运行中整体重写该文件造成冲突丢失）；API 不可用时才回退到文本替换。
        try
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (assets != null && assets.Length > 0)
            {
                var so = new SerializedObject(assets[0]);
                var prop = so.FindProperty("activeInputHandler");
                if (prop != null)
                {
                    if (prop.intValue == 2)
                    {
                        Debug.Log("[Setup] Input System 已为 Both 模式，无需修改");
                        return false;
                    }
                    prop.intValue = 2;
                    so.ApplyModifiedProperties();
                    AssetDatabase.SaveAssets();
                    Debug.Log("[Setup] Input System 已切换为 Both 模式（需重启 Editor 生效）");
                    return true;
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[Setup] SerializedObject 修改 activeInputHandler 失败，回退文本替换: " + e.Message);
        }

        // 回退：文本替换（保留原逻辑，但限制为整字段精确匹配，降低误伤）
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
        string projectKey = "VNovelizer_Setup_Shown:" + Application.dataPath;
        if (!Application.isBatchMode && !EditorPrefs.GetBool(projectKey, false))
        {
            EditorApplication.delayCall += () =>
            {
                if (!VNDependencyBootstrap.IsReady) return;
                VNovelizerSetup.ShowWindow();
                EditorPrefs.SetBool(projectKey, true);
            };
        }
    }
}
