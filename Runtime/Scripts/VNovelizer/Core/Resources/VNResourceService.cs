using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 资源服务门面：全项目运行时资源加载的统一入口（Phase 0 收口目标，
/// 见 Docs/VNResourceProviderRefactoring.md）。
///
/// 提供者链：Addressables（若可用）→ Resources（始终兜底）。
/// - 新项目（Addressables 模式）：包内默认资源与用户工作区资源由
///   VNAddressablesRegistrar 注册，地址 = 资源键，链首命中；
/// - 存量项目（Assets/Resources/VNovelizerRes）：链首未命中自动回退
///   Resources，行为与旧版完全一致，零迁移成本；
/// - 两条腿同时可用（部分注册 + 部分 Resources 的混合状态）。
///
/// 键约定：沿用 VNProjectConfig 的路径前缀（如 "VNovelizerRes/Backgrounds/Beach"）。
/// 旧代码请勿再直接调用 Resources.Load——统一走本服务或 ResourcesManager（内部已委托本服务）。
/// </summary>
public static class VNResourceService
{
    private static readonly List<IVNResourceProvider> _providers = new List<IVNResourceProvider>();
    private static bool _initialized;

    /// <summary>当前提供者链（只读视图，诊断用）</summary>
    public static IReadOnlyList<IVNResourceProvider> Providers
    {
        get { EnsureInitialized(); return _providers; }
    }

    /// <summary>确保提供者链已构建（幂等，首次访问任意 API 时自动触发）</summary>
    public static void EnsureInitialized()
    {
        if (_initialized) return;
        _initialized = true;
        _providers.Clear();

#if VN_ADDRESSABLES
        _providers.Add(new AddressablesProvider());
#endif
        _providers.Add(new ResourcesProvider());

        for (int i = 0; i < _providers.Count; i++)
            _providers[i].Initialize();

        Debug.Log($"[VNResourceService] 资源提供者链就绪: {DescribeChain()}");
    }

    /// <summary>
    /// 重建提供者链。编辑器端 VNAddressablesRegistrar 在 Addressables 注册状态变化后调用，
    /// 使编辑器可用性检查重新评估（构建包运行时不应调用）。
    /// </summary>
    public static void Reset()
    {
        _initialized = false;
        _providers.Clear();
    }

    /// <summary>链描述（日志/诊断用）</summary>
    public static string DescribeChain()
    {
        EnsureInitialized();
        var names = new List<string>();
        for (int i = 0; i < _providers.Count; i++)
            names.Add(_providers[i].IsAvailable ? _providers[i].Name : _providers[i].Name + "(不可用)");
        return string.Join(" → ", names);
    }

    /// <summary>同步加载。依次询问链上可用提供者，首个命中即返回；全部未命中返回 null。</summary>
    public static T Load<T>(string key) where T : UnityEngine.Object
    {
        EnsureInitialized();
        if (string.IsNullOrEmpty(key)) return null;

        for (int i = 0; i < _providers.Count; i++)
        {
            if (!_providers[i].IsAvailable) continue;
            T asset = _providers[i].Load<T>(key);
            if (asset != null) return asset;
        }
        return null;
    }

    /// <summary>
    /// 异步加载（带回退链）：当前提供者未命中时自动尝试下一环；
    /// 进度按链长加权。全部未命中时完成值为 null。
    /// </summary>
    public static VNLoadOperation<T> LoadAsync<T>(string key) where T : UnityEngine.Object
    {
        EnsureInitialized();
        var op = new VNLoadOperation<T>(key);
        if (string.IsNullOrEmpty(key)) { op.Complete(null); return op; }
        ChainLoadAsync(0, key, op);
        return op;
    }

    private static void ChainLoadAsync<T>(int index, string key, VNLoadOperation<T> finalOp) where T : UnityEngine.Object
    {
        // 跳过不可用提供者
        while (index < _providers.Count && !_providers[index].IsAvailable) index++;
        if (index >= _providers.Count)
        {
            // 全链未命中：让失败可见（异步调用方大多对 null 静默，此处是唯一的统一报告点）
            Debug.LogWarning($"[VNResourceService] 资源加载失败（提供者链全部未命中）: \"{key}\" ({typeof(T).Name})，" +
                             $"链: {DescribeChain()}。请检查资源名/逻辑名是否一致、资源是否已分配（资源管理器）或存在于旧 Resources 目录。");
            finalOp.Complete(null);
            return;
        }

        int providerIndex = index;
        float chainLength = Mathf.Max(1, _providers.Count);

        var sub = _providers[index].LoadAsync<T>(key);
        finalOp.SetProgressSource(() =>
        {
            float inner = sub.IsDone ? 1f : sub.Progress;
            return (providerIndex + inner) / chainLength;
        });

        sub.Completed += r =>
        {
            if (r.Asset != null) { finalOp.Complete(r.Asset); return; }
            ChainLoadAsync(index + 1, key, finalOp); // 未命中 → 下一环
        };
    }

    /// <summary>
    /// 按类别批量加载（如 CharacterResManager 加载全部 CharacterProfile）。
    /// 依次询问链上可用提供者，首个返回非空集合即采用；全部为空返回 null。
    /// </summary>
    public static IList<T> LoadAll<T>(string category) where T : UnityEngine.Object
    {
        EnsureInitialized();
        if (string.IsNullOrEmpty(category)) return null;

        for (int i = 0; i < _providers.Count; i++)
        {
            if (!_providers[i].IsAvailable) continue;
            IList<T> assets = _providers[i].LoadAll<T>(category);
            if (assets != null && assets.Count > 0) return assets;
        }
        return null;
    }

    /// <summary>按 key 尽力卸载（转发给所有可用提供者；Resources 为空操作）。</summary>
    public static void Release(string key)
    {
        EnsureInitialized();
        for (int i = 0; i < _providers.Count; i++)
        {
            if (_providers[i].IsAvailable) _providers[i].Release(key);
        }
    }
}
