using UnityEngine;

/// <summary>
/// UI 预制体/资产统一解析入口（模板覆写机制，见 Docs/VNResourceProviderRefactoring.md）。
///
/// 解析顺序：
/// 1. <see cref="VNProjectConfig"/> 覆写字段（用户在 Inspector 拖拽指派的自定义模板）——
///    直接引用，零字符串、零加载、零寻址；
/// 2. 默认模板：按 fallback 资源路径经 <see cref="VNResourceService"/> 提供者链加载
///   （Addressables 注册的包内模板 / 旧项目 Resources 副本）——引擎私有实现，用户无需感知。
///
/// 两个入口：
/// - <see cref="Load"/>/<see cref="LoadAsync"/>：GameObject 预制体（返回本体，调用方 Instantiate）；
/// - <see cref="LoadAsset{T}"/>：非 prefab 资产（画廊数据容器等 ScriptableObject，直接返回资产本体）。
/// </summary>
public static class VNUIPrefabs
{
    /// <summary>同步解析：覆写命中直接返回；否则按 fallback 路径经服务链加载。返回 prefab 本体（不实例化）。</summary>
    public static GameObject Load(string prefabKey, string fallbackResourcePath)
    {
        GameObject overridden = LookupOverride<GameObject>(prefabKey);
        if (overridden != null) return overridden;

        return VNResourceService.Load<GameObject>(fallbackResourcePath);
    }

    /// <summary>异步解析：覆写命中时操作同步完成（零等待）；否则异步走服务链。返回 prefab 本体。</summary>
    public static VNLoadOperation<GameObject> LoadAsync(string prefabKey, string fallbackResourcePath)
    {
        GameObject overridden = LookupOverride<GameObject>(prefabKey);
        if (overridden != null)
        {
            var instant = new VNLoadOperation<GameObject>(prefabKey);
            instant.Complete(overridden);
            return instant;
        }

        return VNResourceService.LoadAsync<GameObject>(fallbackResourcePath);
    }

    /// <summary>
    /// 非 prefab 资产解析（画廊数据容器等 ScriptableObject）：覆写命中返回资产本体；
    /// 否则按 fallback 路径经服务链加载。SO 无需实例化，直接使用。
    /// </summary>
    public static T LoadAsset<T>(string assetKey, string fallbackResourcePath) where T : Object
    {
        T overridden = LookupOverride<T>(assetKey);
        if (overridden != null) return overridden;

        return VNResourceService.Load<T>(fallbackResourcePath);
    }

    /// <summary>覆写查询（类型不匹配视为未覆写——如 GameObject 键下拖了 SO）</summary>
    private static T LookupOverride<T>(string prefabKey) where T : Object
    {
        if (VNProjectConfig.Instance == null) return null;
        Object overridden = VNProjectConfig.Instance.GetUIPrefabOverride(prefabKey);
        return overridden as T;
    }
}
