using System.IO;
using UnityEditor;
using UnityEngine;
using VNovelizer.Core.Weather;

/// <summary>
/// 粒子「从模板创建」（VFX 注册表扩展 2026-10-23，编辑器侧）。
///
/// <para>
/// 把内置 rain/snow 粒子预制体（包内只读模板）复制到用户自选位置供魔改，
/// 并把副本以新条目追加进 VNProjectConfig.VFXParticleRegistry（id = 文件名主干）。
/// 用户随后可在 VFX 设置页重命名 id / 调整默认时长 / 禁用，经 particlecustom 使用。
/// </para>
/// </summary>
public static class VNParticleTemplateCreator
{
    /// <summary>
    /// 弹出模板选择菜单（由 VFX 设置页 / VNProjectConfigEditor 的"八、VFX"分组按钮调用）。
    /// 选择后：自选保存位置 → 复制模板 → 追加注册表条目。
    /// </summary>
    public static void ShowCreateMenu(VNProjectConfig config)
    {
        if (config == null) return;

        var menu = new GenericMenu();
        menu.AddItem(new GUIContent("下雪 (Snow)"), false, () => CreateFromTemplate(config, WeatherParticleFactory.SnowId, "下雪"));
        menu.AddItem(new GUIContent("下雨 (Rain)"), false, () => CreateFromTemplate(config, WeatherParticleFactory.RainId, "下雨"));
        menu.ShowAsContext();
    }

    /// <summary>复制内置模板到用户自选位置并追加注册表条目</summary>
    private static void CreateFromTemplate(VNProjectConfig config, string templateId, string displayName)
    {
        string templatePath = VNWeatherParticleGenerator.TryGetBuiltinParticlePrefab(templateId);
        if (string.IsNullOrEmpty(templatePath))
        {
            EditorUtility.DisplayDialog("从模板创建粒子",
                "内置模板预制体不可用（包内未烘焙且工作区无 snow/rain.prefab）。\n" +
                "请手动创建粒子 Prefab，或由包开发者重新烘焙包内模板。", "确定");
            return;
        }

        string defaultDir = VNProjectPaths.WorkspaceRoot + "/VFX/Particles";
        if (!AssetDatabase.IsValidFolder(defaultDir)) defaultDir = "Assets";

        string suggested = char.ToUpperInvariant(templateId[0]) + templateId.Substring(1) + "_Custom";
        string savePath = EditorUtility.SaveFilePanelInProject(
            "从模板创建粒子", suggested, "prefab",
            $"保存自定义粒子预制体（将自动追加到 VFX 粒子注册表）", defaultDir);
        if (string.IsNullOrEmpty(savePath)) return;

        if (!AssetDatabase.CopyAsset(templatePath, savePath))
        {
            EditorUtility.DisplayDialog("从模板创建粒子", "复制模板失败：" + templatePath, "确定");
            return;
        }

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(savePath);
        if (prefab == null)
        {
            EditorUtility.DisplayDialog("从模板创建粒子", "复制后加载预制体失败：" + savePath, "确定");
            return;
        }

        // 追加注册表条目（id = 文件名主干；重名时加后缀保证唯一）
        string baseId = Path.GetFileNameWithoutExtension(savePath);
        string entryId = baseId;
        if (config.VFXParticleRegistry == null)
            config.VFXParticleRegistry = new System.Collections.Generic.List<VNProjectParticleEntry>();
        int suffix = 1;
        while (config.VFXParticleRegistry.Exists(e => e != null &&
               string.Equals(e.Id, entryId, System.StringComparison.OrdinalIgnoreCase)))
        {
            entryId = baseId + "_" + (++suffix);
        }
        config.VFXParticleRegistry.Add(new VNProjectParticleEntry
        {
            Id = entryId,
            DisplayName = string.IsNullOrEmpty(displayName) ? entryId : displayName + "（自定义）",
            Prefab = prefab,
            DefaultDuration = 0.5f,
            Enabled = true,
        });

        EditorUtility.SetDirty(config);
        AssetDatabase.SaveAssets();
        Selection.activeObject = prefab;
        EditorGUIUtility.PingObject(prefab);
        Debug.Log($"[VNParticleTemplateCreator] 已复制粒子模板到 {savePath} 并追加注册表条目 '{entryId}'");
    }
}
