using System.IO;
using UnityEditor;
using UnityEngine;
using VNovelizer.Core.Weather;

/// <summary>
/// 内置天气粒子预制体生成器（VFX 注册表扩展 2026-10-23，编辑器侧）。
///
/// <para>
/// 原 WeatherParticleFactory 的运行时构建逻辑复用为预制体构建器（PrefabUtility 落盘），
/// 生成的预制体挂 VN_Particle 组件（运行时管理器只认该接口）。
/// 分发策略：包内模板 <c>Runtime/PackageDefault/VNovelizerRes/VFX/Particles/{id}.prefab</c>
/// （只读，随包分发）或工作区 <c>Assets/VNovelizer/VFX/Particles/</c> 已有文件。
/// 【2026-10-23 减法重构】本类只做两件事：纯查询 <see cref="TryGetBuiltinParticlePrefab"/>
/// （绝不写文件、绝不覆盖用户资产）与显式烘焙菜单 <see cref="BakeBuiltinPrefabsToPackage"/>。
/// 旧版的"自动生成到工作区 / 坏版检测自动重建"已全部删除——prefab 缺失时运行时走
/// WeatherParticleFactory 代码构建兜底，脚本不再自动触碰任何用户资产。
/// </para>
///
/// <para>
/// 软圆点贴图资产：运行时 Build 挂的是 HideAndDontSave 临时材质（无法序列化进 prefab，
/// 不落盘会退回方块粒子），因此显式烘焙菜单保存 prefab 前经 PersistSoftDotAssets
/// 把贴图落盘为 VN_SoftDot.png + VN_SoftDot.mat 并替换引用。
/// </para>
/// </summary>
public static class VNWeatherParticleGenerator
{
    private static string PackageName
    {
        get
        {
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(VNWeatherParticleGenerator).Assembly);
            return info != null && !string.IsNullOrEmpty(info.name) ? info.name : "com.fakecorps.vnovelizer";
        }
    }

    /// <summary>包内粒子模板目录（资产虚拟路径，AssetDatabase 用）</summary>
    private static string PackageParticleFolderVirtual =>
        "Packages/" + PackageName + "/Runtime/PackageDefault/VNovelizerRes/VFX/Particles";

    /// <summary>包内粒子模板目录（文件系统绝对路径，File IO 用）</summary>
    private static string PackageParticleFolderFs
    {
        get
        {
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(VNWeatherParticleGenerator).Assembly);
            if (info != null && !string.IsNullOrEmpty(info.resolvedPath))
                return Path.GetFullPath(Path.Combine(info.resolvedPath, "Runtime/PackageDefault/VNovelizerRes/VFX/Particles"));
            return Path.GetFullPath(PackageParticleFolderVirtual);
        }
    }

    /// <summary>工作区粒子模板目录（Assets 虚拟路径，同相对路径即文件系统路径）</summary>
    private static string WorkspaceParticleFolder => VNProjectPaths.WorkspaceRoot + "/VFX/Particles";

    /// <summary>
    /// 纯查询：内置粒子预制体资产存在则返回其**资产虚拟路径**（包内模板优先 → 工作区已有文件），
    /// 都不存在返回 null。**绝不生成/重建/覆盖任何文件**（2026-10-23 减法重构：
    /// 原"缺失自动生成 + 坏版自动重建"已删除——缺失时运行时走 WeatherParticleFactory 代码兜底）。
    /// id 仅支持 snow / rain；其他返回 null。
    /// </summary>
    public static string TryGetBuiltinParticlePrefab(string id)
    {
        id = (id ?? "").Trim().ToLowerInvariant();
        if (id != WeatherParticleFactory.SnowId && id != WeatherParticleFactory.RainId) return null;

        // ① 包内模板（随包分发）
        if (File.Exists(Path.Combine(PackageParticleFolderFs, id + ".prefab")))
            return PackageParticleFolderVirtual + "/" + id + ".prefab";

        // ② 工作区已有文件（用户自建/早前生成——只读查询）
        string wsVirtual = WorkspaceParticleFolder + "/" + id + ".prefab";
        if (File.Exists(wsVirtual)) return wsVirtual;

        return null;
    }

    /// <summary>
    /// 包开发用：把内置 rain/snow 粒子预制体烘焙进包内模板目录
    /// （端用户工程 Packages 只读不会执行；生成的包内资产随包分发、GUID 注册只读）。
    /// </summary>
    [MenuItem("VNovelizer/VFX/烘焙内置粒子预制体到包内", false, 46)]
    public static void BakeBuiltinPrefabsToPackage()
    {
        string dirFs = PackageParticleFolderFs;
        if (Directory.Exists(dirFs))
        {
            if (!EditorUtility.DisplayDialog("烘焙内置粒子预制体",
                    "包内模板目录已存在文件，将覆盖 rain/snow 预制体。继续？", "覆盖并烘焙", "取消"))
                return;
        }
        else
        {
            Directory.CreateDirectory(dirFs);
            AssetDatabase.Refresh();
        }

        foreach (string id in new[] { WeatherParticleFactory.SnowId, WeatherParticleFactory.RainId })
            BuildAndSavePrefab(id, PackageParticleFolderVirtual + "/" + id + ".prefab", PackageParticleFolderVirtual, dirFs);

        AssetDatabase.SaveAssets();
        EditorUtility.DisplayDialog("烘焙内置粒子预制体", "已生成 rain/snow 预制体到包内：\n" + dirFs, "确定");
    }

    /// <summary>
    /// Build 产物落盘为 prefab（先落盘软圆点 PNG/材质资产并替换引用，再保存）。
    /// VN_Particle 契约组件与配置版本戳已由 WeatherParticleFactory.Build 统一写入。
    /// </summary>
    private static void BuildAndSavePrefab(string id, string prefabVirtualPath, string assetVirtualFolder, string assetFsFolder)
    {
        bool isSnow = id == WeatherParticleFactory.SnowId;
        var root = isSnow
            ? WeatherParticleFactory.CreateSnow(null)
            : WeatherParticleFactory.CreateRain(null);

        PersistSoftDotAssets(root, assetVirtualFolder, assetFsFolder);
        PrefabUtility.SaveAsPrefabAsset(root, prefabVirtualPath);
        Object.DestroyImmediate(root);
    }

    /// <summary>
    /// 把 Build 产物上的临时软圆点材质落盘为资产（VN_SoftDot.png + VN_SoftDot.mat）并替换引用。
    /// 临时对象（HideAndDontSave）无法序列化进 prefab，不落盘烘焙出的雪花会是方块。
    /// </summary>
    private static void PersistSoftDotAssets(GameObject root, string virtualFolder, string fsFolder)
    {
        foreach (var psr in root.GetComponentsInChildren<ParticleSystemRenderer>(true))
        {
            var mat = psr.sharedMaterial;
            if (mat == null) continue;
            var tex = mat.mainTexture as Texture2D;
            if (tex == null) continue;

            string texVirtual = virtualFolder + "/VN_SoftDot.png";
            string texFs = Path.Combine(fsFolder, "VN_SoftDot.png").Replace('\\', '/');
            if (!File.Exists(texFs))
            {
                File.WriteAllBytes(texFs, tex.EncodeToPNG());
                AssetDatabase.ImportAsset(texVirtual);
            }
            var assetTex = AssetDatabase.LoadAssetAtPath<Texture2D>(texVirtual);

            string matVirtual = virtualFolder + "/VN_SoftDot.mat";
            var assetMat = AssetDatabase.LoadAssetAtPath<Material>(matVirtual);
            if (assetMat == null)
            {
                assetMat = new Material(mat.shader) { name = "VN_SoftDot" };
                assetMat.mainTexture = assetTex;
                AssetDatabase.CreateAsset(assetMat, matVirtual);
            }
            psr.sharedMaterial = assetMat;
        }
    }
}
