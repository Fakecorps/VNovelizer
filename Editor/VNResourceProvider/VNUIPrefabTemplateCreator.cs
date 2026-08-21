using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// UI 模板创建器：把包内默认 UI 预制体复制为用户自定义模板，并自动填入
/// VNProjectConfig"九、UI 模板覆写"对应字段（见 Docs/VNResourceProviderRefactoring.md）。
///
/// 工作流：
/// 选择模板 → 自选保存位置（SaveFilePanelInProject）→ 复制 prefab → 自动赋值覆写字段。
/// 之后用户编辑自己的副本即可；清空覆写字段即恢复包内默认模板。
/// </summary>
public static class VNUIPrefabTemplateCreator
{
    private class TemplateInfo
    {
        public string Key;          // VNUIPrefabKeys 常量
        public string DisplayName;  // 菜单显示名
        public string FieldName;    // VNProjectConfig 覆写字段名（Override_Xxx）
        public string Section;      // 菜单分段
    }

    /// <summary>全部可覆写模板（与 VNUIPrefabKeys / VNProjectConfig 覆写字段一一对应）</summary>
    private static readonly TemplateInfo[] Templates =
    {
        new TemplateInfo { Key = VNUIPrefabKeys.VNGameplayPanel,      DisplayName = "游戏主面板 (VNGameplayPanel)",      FieldName = "Override_VNGameplayPanel",      Section = "主面板" },
        new TemplateInfo { Key = VNUIPrefabKeys.MainMenuPanel,        DisplayName = "主菜单面板 (MainMenuPanel)",        FieldName = "Override_MainMenuPanel",        Section = "主面板" },
        new TemplateInfo { Key = VNUIPrefabKeys.GalleryPanel,         DisplayName = "画廊面板 (GalleryPanel)",           FieldName = "Override_GalleryPanel",         Section = "主面板" },
        new TemplateInfo { Key = VNUIPrefabKeys.PausePanel,           DisplayName = "暂停面板 (PausePanel)",             FieldName = "Override_PausePanel",           Section = "主面板" },
        new TemplateInfo { Key = VNUIPrefabKeys.HistoryPanel,         DisplayName = "历史记录面板 (HistoryPanel)",       FieldName = "Override_HistoryPanel",         Section = "主面板" },
        new TemplateInfo { Key = VNUIPrefabKeys.SaveLoadPanel,        DisplayName = "存读档面板 (SaveLoadPanel)",        FieldName = "Override_SaveLoadPanel",        Section = "主面板" },
        new TemplateInfo { Key = VNUIPrefabKeys.SettingsPanel,        DisplayName = "设置面板 (SettingsPanel)",          FieldName = "Override_SettingsPanel",        Section = "主面板" },
        new TemplateInfo { Key = VNUIPrefabKeys.ChoicePanel,          DisplayName = "分支选择面板 (ChoicePanel)",        FieldName = "Override_ChoicePanel",          Section = "主面板" },
        new TemplateInfo { Key = VNUIPrefabKeys.ConfirmPanel,         DisplayName = "确认弹窗面板 (ConfirmPanel)",       FieldName = "Override_ConfirmPanel",         Section = "主面板" },
        new TemplateInfo { Key = VNUIPrefabKeys.LoadingProgressPanel, DisplayName = "加载进度面板 (LoadingProgressPanel)", FieldName = "Override_LoadingProgressPanel", Section = "主面板" },
        new TemplateInfo { Key = VNUIPrefabKeys.PromptItem,           DisplayName = "对话提示项 (PromptItem)",          FieldName = "Override_PromptItem",           Section = "子项预制体" },
        new TemplateInfo { Key = VNUIPrefabKeys.ChoiceItem,           DisplayName = "分支选项项 (ChoiceItem)",          FieldName = "Override_ChoiceItem",           Section = "子项预制体" },
        new TemplateInfo { Key = VNUIPrefabKeys.SaveSlot,             DisplayName = "存档槽 (SaveSlot)",                FieldName = "Override_SaveSlot",             Section = "子项预制体" },
        new TemplateInfo { Key = VNUIPrefabKeys.HistoryItem,          DisplayName = "历史记录条目 (HistoryItem)",       FieldName = "Override_HistoryItem",          Section = "子项预制体" },
        new TemplateInfo { Key = VNUIPrefabKeys.CGSlot,               DisplayName = "画廊 CG 槽位 (CGSlot)",            FieldName = "Override_CGSlot",               Section = "子项预制体" },
        new TemplateInfo { Key = VNUIPrefabKeys.MusicSlot,            DisplayName = "画廊音乐条目 (MusicSlot)",         FieldName = "Override_MusicSlot",            Section = "子项预制体" },
        new TemplateInfo { Key = VNUIPrefabKeys.SceneSlot,            DisplayName = "画廊场景槽位 (SceneSlot)",         FieldName = "Override_SceneSlot",            Section = "子项预制体" },
        new TemplateInfo { Key = VNUIPrefabKeys.EventSystem,          DisplayName = "EventSystem",                      FieldName = "Override_EventSystem",          Section = "基础设施" },
        new TemplateInfo { Key = VNUIPrefabKeys.SoundObj,             DisplayName = "音效对象 (SoundObj)",              FieldName = "Override_SoundObj",             Section = "基础设施" },
        new TemplateInfo { Key = VNUIPrefabKeys.VideoObj,             DisplayName = "视频对象 (VideoObj)",              FieldName = "Override_VideoObj",             Section = "基础设施" },
    };

    /// <summary>包内模板资产路径（Packages 虚拟路径）；定位失败返回 null</summary>
    private static string GetPackageTemplatePath(string key)
    {
        var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(VNUIPrefabTemplateCreator).Assembly);
        if (packageInfo == null || string.IsNullOrEmpty(packageInfo.name)) return null;
        return $"Packages/{packageInfo.name}/Runtime/PackageDefault/{key}.prefab";
    }

    /// <summary>
    /// 弹出模板选择菜单（由 VNProjectConfigEditor 的"八、UI 模板覆写"分组按钮调用）。
    /// 选择后：自选保存位置 → 复制包内模板 → 自动填入对应覆写字段。
    /// </summary>
    public static void ShowTemplateMenu(VNProjectConfig config)
    {
        var menu = new GenericMenu();
        string currentSection = null;

        foreach (var t in Templates)
        {
            if (t.Section != currentSection)
            {
                if (currentSection != null) menu.AddSeparator("");
                menu.AddDisabledItem(new GUIContent(t.Section + "/"));
                currentSection = t.Section;
            }
            menu.AddItem(new GUIContent($"{t.Section}/{t.DisplayName}"), false, () => CreateFromTemplate(config, t));
        }
        menu.ShowAsContext();
    }

    /// <summary>按模板键直接创建自定义副本（UI 预制体管理器等外部入口用）</summary>
    public static void CreateFromKey(VNProjectConfig config, string key)
    {
        foreach (var t in Templates)
        {
            if (t.Key == key) { CreateFromTemplate(config, t); return; }
        }
        Debug.LogWarning($"[VNUIPrefabTemplateCreator] 未知模板键: {key}");
    }

    private static void CreateFromTemplate(VNProjectConfig config, TemplateInfo t)
    {
        string templatePath = GetPackageTemplatePath(t.Key);
        if (templatePath == null || !AssetDatabase.LoadAssetAtPath<GameObject>(templatePath))
        {
            EditorUtility.DisplayDialog("模板缺失",
                $"找不到包内默认模板：\n{templatePath ?? "(包定位失败)"}", "确定");
            return;
        }

        // 已有覆写：提示将覆盖引用（文件不动）
        var current = config.GetUIPrefabOverride(t.Key);
        if (current != null)
        {
            if (!EditorUtility.DisplayDialog("替换覆写",
                $"「{t.DisplayName}」当前已指派自定义模板：\n{AssetDatabase.GetAssetPath(current)}\n\n" +
                "创建新模板副本将替换该指派（原文件不受影响）。", "继续", "取消"))
                return;
        }

        // 自选保存位置（默认落在用户工作区 UI 目录）
        string defaultDir = VNProjectPaths.WorkspaceRoot + "/UI";
        string suggestedName = GetSuggestedName(t.Key);
        string savePath = EditorUtility.SaveFilePanelInProject(
            $"创建模板副本 - {t.DisplayName}", suggestedName, "prefab",
            "选择模板副本的保存位置（保存在项目内任意位置均可，运行时索引不受影响）。",
            defaultDir);
        if (string.IsNullOrEmpty(savePath)) return; // 用户取消

        if (AssetDatabase.LoadAssetAtPath<Object>(savePath) != null)
        {
            EditorUtility.DisplayDialog("已存在", $"目标位置已有同名资产：\n{savePath}", "确定");
            return;
        }

        // 确保目标文件夹存在
        string dir = System.IO.Path.GetDirectoryName(savePath)?.Replace('\\', '/');
        if (!string.IsNullOrEmpty(dir)) VNProjectPaths.EnsureFolder(dir);

        // 复制模板并自动填入覆写字段
        if (!AssetDatabase.CopyAsset(templatePath, savePath))
        {
            EditorUtility.DisplayDialog("复制失败", $"无法复制模板到：\n{savePath}", "确定");
            return;
        }
        AssetDatabase.SaveAssets();

        var copy = AssetDatabase.LoadAssetAtPath<GameObject>(savePath);
        var so = new SerializedObject(config);
        var prop = so.FindProperty(t.FieldName);
        if (prop == null)
        {
            Debug.LogError($"[VNUIPrefabTemplateCreator] 覆写字段不存在: {t.FieldName}");
            return;
        }
        prop.objectReferenceValue = copy;
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(config);
        AssetDatabase.SaveAssets();

        Debug.Log($"[VNUIPrefabTemplateCreator] 已创建模板副本并指派覆写: {t.DisplayName} → {savePath}" +
                  "（清空对应覆写字段即可恢复包内默认模板）");
        EditorGUIUtility.PingObject(copy);
    }

    /// <summary>key → 建议副本名（如 "VNovelizerRes/VNPrefabs/UI/Pause/PausePanel" → "PausePanel_Custom"）</summary>
    private static string GetSuggestedName(string key)
    {
        int idx = key.LastIndexOf('/');
        string leaf = idx >= 0 ? key.Substring(idx + 1) : key;
        return leaf + "_Custom";
    }
}
