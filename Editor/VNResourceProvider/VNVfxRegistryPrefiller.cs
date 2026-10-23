using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VNovelizer.Core.Filters;
using VNovelizer.Core.Weather;

/// <summary>
/// VFX 注册表内置条目恢复器（VFX 注册表扩展 2026-10-23，编辑器侧）。
///
/// <para>
/// 【2026-10-23 减法重构】所有自动预填触发已删除（原"首次创建配置资产时 / 打开 VFX
/// 设置页时自动自愈补填"不再执行）。本类现在只在用户**显式点击**设置页
/// 「恢复内置条目」按钮时调用 <see cref="RestoreBuiltins"/>。幂等：只在整个注册表
/// 为空时补填，用户删除/禁用部分条目后不会被打扰（尊重手动真源语义）。
/// 补填内容：内置 7 款滤镜（Shader = 包内 shader 资产直接引用）+ rain/snow 粒子
/// （Prefab = 内置预制体资产引用，纯查询，不存在则不填）。
/// </para>
/// </summary>
public static class VNVfxRegistryPrefiller
{
    /// <summary>包内 Resources 根目录（UPM 包内文件系统路径，ShaderResourcePath 相对此根）</summary>
    private static string PackageResourcesFolder
    {
        get
        {
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(VNVfxRegistryPrefiller).Assembly);
            if (info != null && !string.IsNullOrEmpty(info.resolvedPath))
                return System.IO.Path.GetFullPath(System.IO.Path.Combine(info.resolvedPath, "Runtime/Resources"));
            return System.IO.Path.GetFullPath("Packages/com.fakecorps.vnovelizer/Runtime/Resources");
        }
    }

    /// <summary>显式恢复内置条目：注册表为空时补填内置滤镜/粒子条目；有任意条目则跳过（尊重用户删除意图）</summary>
    public static void RestoreBuiltins(VNProjectConfig config)
    {
        if (config == null) return;
        bool changed = false;

        if (config.VFXFilterRegistry == null)
        {
            config.VFXFilterRegistry = new List<VNProjectFilterEntry>();
            changed = true;
        }
        if (config.VFXFilterRegistry.Count == 0)
        {
            foreach (var info in VNFilterLibrary.GetBuiltinFilterInfos())
                config.VFXFilterRegistry.Add(new VNProjectFilterEntry
                {
                    Id = info.Id,
                    DisplayName = info.DisplayName,
                    Shader = ResolveBuiltinShaderAsset(info),
                    DefaultDuration = info.DefaultDuration,
                    Enabled = true,
                });
            changed = true;
        }

        if (config.VFXParticleRegistry == null)
        {
            config.VFXParticleRegistry = new List<VNProjectParticleEntry>();
            changed = true;
        }
        if (config.VFXParticleRegistry.Count == 0)
        {
            config.VFXParticleRegistry.Add(new VNProjectParticleEntry
            {
                Id = WeatherParticleFactory.SnowId, DisplayName = "下雪",
                Prefab = LoadBuiltinParticlePrefab(WeatherParticleFactory.SnowId),
                DefaultDuration = 0.5f, Enabled = true,
            });
            config.VFXParticleRegistry.Add(new VNProjectParticleEntry
            {
                Id = WeatherParticleFactory.RainId, DisplayName = "下雨",
                Prefab = LoadBuiltinParticlePrefab(WeatherParticleFactory.RainId),
                DefaultDuration = 0.5f, Enabled = true,
            });
            changed = true;
        }

        if (changed)
        {
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
            Debug.Log("[VNVfxRegistryPrefiller] VFX 注册表已预填内置条目（滤镜 + rain/snow 粒子，幂等自愈）");
        }
    }

    /// <summary>内置滤镜 shader → 包内资产直接引用（跨包 GUID 引用，构建依赖链自动携带）</summary>
    private static Shader ResolveBuiltinShaderAsset(VNFilterLibrary.VNBuiltinFilterInfo info)
    {
        if (info == null || string.IsNullOrEmpty(info.ShaderResourcePath)) return null;
        // ShaderResourcePath 形如 "FilterShaders/VNFilterFisheye"（相对 Resources 根）
        string assetPath = System.IO.Path.Combine(PackageResourcesFolder, info.ShaderResourcePath + ".shader").Replace('\\', '/');
        var shader = AssetDatabase.LoadAssetAtPath<Shader>(assetPath);
        if (shader == null) shader = Shader.Find(info.ShaderName); // 兜底（Shader 数据库已编译实例）
        return shader;
    }

    /// <summary>内置粒子预制体 → 资产引用（纯查询：包内模板/工作区已有文件，缺失返回 null 不生成）</summary>
    private static GameObject LoadBuiltinParticlePrefab(string id)
    {
        string assetPath = VNWeatherParticleGenerator.TryGetBuiltinParticlePrefab(id);
        return string.IsNullOrEmpty(assetPath) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
    }
}
