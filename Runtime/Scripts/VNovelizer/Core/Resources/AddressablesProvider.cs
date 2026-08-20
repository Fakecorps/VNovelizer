#if VN_ADDRESSABLES
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

/// <summary>
/// 基于 Unity Addressables 的提供者（安装 com.unity.addressables 后启用，位于链首）。
///
/// 键约定：
/// - 地址 = 资源键（如 "VNovelizerRes/Backgrounds/Beach"），由编辑器端
///   VNAddressablesRegistrar 在注册资产时写入，运行时按同一键查询；
/// - 批量加载（LoadAll）按类别 Label 检索（Label = VNResourceKeys.CategoryToLabel(类别)）。
///
/// 句柄管理：
/// - 内部按 "key|类型" 缓存句柄，重复加载同一资源复用句柄（Addressables 自带引用计数）；
/// - <see cref="Release(string)"/> 释放该 key 缓存的全部句柄（含 Label 批量句柄）。
///
/// 可用性判定（避免对未初始化 Addressables 的项目逐次探查产生 InvalidKeyException 日志）：
/// - 编辑器：经 <see cref="EditorAvailabilityProbe"/> 探针查询（由编辑器程序集的
///   VNResourceEditorProbe 经 [InitializeOnLoad] 注入——运行时程序集不能引用
///   Unity.Addressables.Editor，故用委托桥接）。注册器注册后会经
///   VNResourceService.Reset() 重建链，重新评估；
/// - 构建包：首次初始化 Addressables 运行时，失败则熔断（后续直接走 Resources 兜底）。
/// </summary>
public class AddressablesProvider : IVNResourceProvider
{
    /// <summary>
    /// 编辑器可用性探针：返回 true 表示编辑器内应启用本提供者
    /// （Addressables 设置资产与 "VNovelizer" 组均存在）。
    /// 由编辑器程序集 VNResourceEditorProbe 注入；返回结果按提供者实例缓存。
    /// </summary>
    public static Func<bool> EditorAvailabilityProbe;

    private const string LabelCachePrefix = "label:";

    /// <summary>"key|TypeName" 或 "label:category|TypeName" → handle</summary>
    private readonly Dictionary<string, AsyncOperationHandle> _handles = new Dictionary<string, AsyncOperationHandle>();

    /// <summary>构建包内运行时初始化结果缓存（失败熔断）</summary>
    private bool? _runtimeAvailable;

#if UNITY_EDITOR
    /// <summary>编辑器可用性缓存（null = 未评估）。注册器注册后由服务重建链时重置。</summary>
    private bool? _editorAvailable;
#endif

    public string Name => "Addressables";

    public void Initialize() { }

    public bool IsAvailable
    {
        get
        {
#if UNITY_EDITOR
            return GetEditorAvailability();
#else
            return GetRuntimeAvailability();
#endif
        }
    }

#if UNITY_EDITOR
    private bool GetEditorAvailability()
    {
        if (_editorAvailable.HasValue) return _editorAvailable.Value;

        var probe = EditorAvailabilityProbe;
        if (probe != null)
        {
            try { _editorAvailable = probe(); }
            catch { _editorAvailable = false; }
        }
        else
        {
            // 探针未注入（不应发生：编辑器程序集随包一起安装）——退化为运行时初始化探测
            _editorAvailable = GetRuntimeAvailability();
        }
        return _editorAvailable.Value;
    }
#endif

    private bool GetRuntimeAvailability()
    {
        if (_runtimeAvailable.HasValue) return _runtimeAvailable.Value;
        try
        {
            var init = Addressables.InitializeAsync();
            init.WaitForCompletion();
            _runtimeAvailable = init.Status == AsyncOperationStatus.Succeeded && init.Result != null;
            if (!_runtimeAvailable.Value)
            {
                Debug.LogWarning("[AddressablesProvider] Addressables 运行时初始化失败（可能未执行 Addressables 构建），" +
                                 "本进程内将回退到 Resources 提供者。构建游戏前请执行：Window → Asset Management → Addressables → Groups → Build → New Build。");
            }
        }
        catch (Exception e)
        {
            _runtimeAvailable = false;
            Debug.LogWarning($"[AddressablesProvider] Addressables 初始化异常，回退 Resources：{e.Message}");
        }
        return _runtimeAvailable.Value;
    }

    public T Load<T>(string key) where T : UnityEngine.Object
    {
        if (string.IsNullOrEmpty(key) || !IsAvailable) return null;

        string cacheKey = BuildCacheKey(key, typeof(T));

        // 已缓存：直接复用（Addressables 内部引用计数）
        if (_handles.TryGetValue(cacheKey, out var cached) && cached.IsValid() && cached.Status == AsyncOperationStatus.Succeeded)
            return cached.Result as T;

        try
        {
            var handle = Addressables.LoadAssetAsync<T>(key);
            T asset = handle.WaitForCompletion();
            if (handle.Status == AsyncOperationStatus.Succeeded && asset != null)
            {
                _handles[cacheKey] = handle;
                return asset;
            }
            // 未命中：释放句柄并静默返回 null，交给链上下一环
            if (handle.IsValid()) Addressables.Release(handle);
            return null;
        }
        catch
        {
            // 键不存在 / 运行时未就绪等：静默失败，走回退链
            return null;
        }
    }

    public VNLoadOperation<T> LoadAsync<T>(string key) where T : UnityEngine.Object
    {
        var op = new VNLoadOperation<T>(key);
        if (string.IsNullOrEmpty(key) || !IsAvailable) { op.Complete(null); return op; }

        string cacheKey = BuildCacheKey(key, typeof(T));
        if (_handles.TryGetValue(cacheKey, out var cached) && cached.IsValid() && cached.Status == AsyncOperationStatus.Succeeded)
        {
            op.Complete(cached.Result as T);
            return op;
        }

        try
        {
            var handle = Addressables.LoadAssetAsync<T>(key);
            op.SetProgressSource(() => handle.IsDone ? 1f : handle.PercentComplete);
            handle.Completed += h =>
            {
                if (h.Status == AsyncOperationStatus.Succeeded && h.Result != null)
                {
                    _handles[cacheKey] = h;
                    op.Complete(h.Result);
                }
                else
                {
                    if (h.IsValid()) Addressables.Release(h);
                    op.Complete(null);
                }
            };
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[AddressablesProvider] 异步加载异常（key={key}）：{e.Message}");
            op.Complete(null);
        }
        return op;
    }

    public IList<T> LoadAll<T>(string category) where T : UnityEngine.Object
    {
        if (string.IsNullOrEmpty(category) || !IsAvailable) return null;

        string cacheKey = LabelCachePrefix + BuildCacheKey(category, typeof(T));
        if (_handles.TryGetValue(cacheKey, out var cached) && cached.IsValid() && cached.Status == AsyncOperationStatus.Succeeded)
            return cached.Result as IList<T>;

        try
        {
            string label = VNResourceKeys.CategoryToLabel(category);
            var handle = Addressables.LoadAssetsAsync<T>(new List<object> { label }, null, Addressables.MergeMode.Union);
            IList<T> results = handle.WaitForCompletion();
            if (handle.Status == AsyncOperationStatus.Succeeded && results != null && results.Count > 0)
            {
                _handles[cacheKey] = handle;
                return results;
            }
            if (handle.IsValid()) Addressables.Release(handle);
            return null;
        }
        catch
        {
            // Label 不存在（未注册任何该类别资产）等：静默失败，走回退链
            return null;
        }
    }

    public void Release(string key)
    {
        if (string.IsNullOrEmpty(key)) return;

        // 匹配该 key 的单资产句柄（"key|..."）与批量句柄（"label:key|..."）
        List<string> toRelease = null;
        foreach (var k in _handles.Keys)
        {
            bool match = k.StartsWith(key + "|", StringComparison.Ordinal)
                      || k.StartsWith(LabelCachePrefix + key + "|", StringComparison.Ordinal);
            if (match) (toRelease ?? (toRelease = new List<string>())).Add(k);
        }
        if (toRelease == null) return;

        foreach (var k in toRelease)
        {
            if (_handles.TryGetValue(k, out var h) && h.IsValid())
                Addressables.Release(h);
            _handles.Remove(k);
        }
    }

    private static string BuildCacheKey(string key, Type assetType)
    {
        return key + "|" + (assetType != null ? assetType.FullName : "Object");
    }
}
#endif
