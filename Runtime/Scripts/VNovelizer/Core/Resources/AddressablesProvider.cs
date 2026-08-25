#if VN_ADDRESSABLES
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using VNovelizer.Core.Diagnostics;

/// <summary>
/// 基于 Unity Addressables 的提供者（安装 com.unity.addressables 后启用，位于链首）。
///
/// 键约定：
/// - 地址 = 资源键（如 "VNovelizerRes/Backgrounds/Beach"），由编辑器端
///   VNAddressablesRegistrar 在注册资产时写入，运行时按同一键查询；
/// - 批量加载（LoadAll）按类别 Label 检索（Label = VNResourceKeys.CategoryToLabel(类别)）。
///
/// 初始化与噪声策略（重要经验教训）：
/// - Addressables 运行时是惰性初始化：ResourceLocators 在首次实际加载
///   （InitializeAsync 完成）之后才有内容。曾用"加载前查 locator 预检键是否存在"
///   来抑制 InvalidKeyException 日志噪声——结果预检在初始化前恒返回"未注册"，
///   且短路了本可触发初始化的加载调用，形成自锁（所有键永远加载失败）。已移除；
/// - 空类别（Label 无条目）的噪声改由编辑器注入的 <see cref="LabelProbe"/> 抑制
///   （直接查编辑器 Addressables 设置，与运行时初始化无关）；
/// - 单个键未注册时 Addressables 内部会打印一次 InvalidKeyException——由链上回退
///   与 VNResourceService 的全链未命中警告兜底，属可接受的诊断信息；
/// - <see cref="WarmupInitialization"/> 在场景加载前预热初始化（编辑器内跟随探针，
///   非 Fast 模式不预热——初始化注定失败，不值得制造错误日志），
///   使启动期的同步加载多数能命中已就绪的 locator。
///
/// 句柄管理：
/// - 内部按 "key|类型" 缓存句柄，重复加载同一资源复用句柄（Addressables 自带引用计数）；
/// - <see cref="Release(string)"/> 释放该 key 缓存的全部句柄（含 Label 批量句柄）。
///
/// 可用性判定：
/// - 编辑器：经 <see cref="EditorAvailabilityProbe"/> 探针（设置资产 + VNovelizer 组 +
///   Play Mode Script 为 Use Asset Database (fastest)——同步安全模式）；
/// - 构建包：首次初始化 Addressables 运行时，失败则熔断（后续直接走 Resources 兜底）。
/// </summary>
public class AddressablesProvider : IVNResourceProvider
{
    /// <summary>
    /// 编辑器可用性探针：返回 true 表示编辑器内应启用本提供者
    /// （设置资产 + VNovelizer 组 + Play Mode Script 为 Fast 模式）。
    /// 由编辑器程序集 VNResourceEditorProbe 经 [InitializeOnLoad] 注入。
    /// </summary>
    public static Func<bool> EditorAvailabilityProbe;

    /// <summary>
    /// 类别 Label 非空探针：返回 true 表示该 Label 下已注册至少一个条目。
    /// 由编辑器程序集注入（直接查编辑器设置，无运行时初始化依赖）——
    /// 用于 LoadAll 的空类别噪声抑制；构建包内为 null（直接加载）。
    /// </summary>
    public static Func<string, bool> LabelProbe;

    /// <summary>
    /// 键存在性探针：返回 true 表示该地址已在 Addressables 设置中注册（任意组）。
    /// 由编辑器程序集注入（直接查编辑器设置的组条目）——注意与曾被移除的
    /// "运行时 locator 预检"有本质区别：那个依赖惰性初始化的 ResourceLocators，
    /// 预检恒 false 并短路本应触发初始化的加载，形成自锁；本探针读编辑器设置，
    /// 无运行时初始化依赖，不存在自锁问题。
    /// 用途：键未注册时静默回退链上下一环，抑制 Addressables 内部打印的
    /// InvalidKeyException 日志噪声；构建包内为 null（直接加载）。
    /// </summary>
    public static Func<string, bool> KeyProbe;

    private const string LabelCachePrefix = "label:";

    /// <summary>"key|TypeName" 或 "label:category|TypeName" → handle</summary>
    private readonly Dictionary<string, AsyncOperationHandle> _handles = new Dictionary<string, AsyncOperationHandle>();

    /// <summary>构建包内运行时初始化结果缓存（失败熔断）</summary>
    private bool? _runtimeAvailable;

#if UNITY_EDITOR
    /// <summary>编辑器可用性缓存（null = 未评估）。注册器注册后由服务重建链时重置。</summary>
    private bool? _editorAvailable;
#endif

    /// <summary>
    /// 预热 Addressables 初始化（场景加载前由 <see cref="RuntimeInitializeOnLoadMethod"/> 自动触发一次）。
    /// 编辑器内跟随提供者可用性探针——非 Fast 模式下不预热（初始化注定失败，
    /// 只会制造错误日志）；构建包内总是预热（内容已随构建就绪）。
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void WarmupInitialization()
    {
        if (!Application.isPlaying) return;
#if UNITY_EDITOR
        var probe = EditorAvailabilityProbe;
        if (probe != null)
        {
            try { if (!probe()) return; }
            catch { return; }
        }
        // 编辑器 Fast 模式：同步等待初始化完成，消除"InitializeAsync 未完成 →
        // LoadAssetAsync IsDone==false → 直接 Release → 加载失败"的启动期竞争窗口。
        // Fast 模式（Use Asset Database）保证操作不会永久阻塞（不存在死锁风险）。
        var initHandle = Addressables.InitializeAsync();
        if (!initHandle.IsDone)
            initHandle.WaitForCompletion();
#else
        // 构建包：异步预热即可，运行时加载有 WaitForCompletion 兜底
        Addressables.InitializeAsync();
#endif
    }

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
            // 探针未注入（不应发生：编辑器程序集随包一起安装）——直接禁用。
            // 注意：绝不在此退化为运行时初始化探测——那会在编辑器主线程上
            // 调用 WaitForCompletion，属于死锁风险点（见探针类注释）。
            _editorAvailable = false;
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
#if UNITY_EDITOR
        if (IsKeyUnregistered(key)) return null; // 未注册键静默回退（抑制 InvalidKeyException 噪声）
#endif
        string cacheKey = BuildCacheKey(key, typeof(T));

        // 已缓存：直接复用（Addressables 内部引用计数）
        if (_handles.TryGetValue(cacheKey, out var cached) && cached.IsValid() && cached.Status == AsyncOperationStatus.Succeeded)
            return cached.Result as T;

        try
        {
            var handle = Addressables.LoadAssetAsync<T>(key);
#if UNITY_EDITOR
            // 编辑器 Fast 模式：初始化完成后操作通常同步完成，但完成回调的调度并不保证
            // 在当帧返回前就绪（首个加载/依赖链较长时 IsDone==false 属常态）。
            // WaitForCompletion 是无死锁风险的正常补救路径（Fast 模式不阻塞），
            // 不作为警告刷屏；补救失败则释放并回退链上下一环。
            if (!handle.IsDone)
            {
                VNDebug.LogVerbose($"[AddressablesProvider] Fast 模式下 LoadAssetAsync 未同步完成（key={key}），WaitForCompletion 补救。");
                try { handle.WaitForCompletion(); }
                catch
                {
                    if (handle.IsValid()) Addressables.Release(handle);
                    return null;
                }
            }
            if (!handle.IsDone)
            {
                if (handle.IsValid()) Addressables.Release(handle);
                return null;
            }
            T asset = handle.Result;
#else
            T asset = handle.WaitForCompletion();
#endif
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
#if UNITY_EDITOR
        if (IsKeyUnregistered(key)) { op.Complete(null); return op; } // 未注册键静默回退
#endif
        string cacheKey = BuildCacheKey(key, typeof(T));
        if (_handles.TryGetValue(cacheKey, out var cached) && cached.IsValid() && cached.Status == AsyncOperationStatus.Succeeded)
        {
            op.Complete(cached.Result as T);
            return op;
        }

        try
        {
            // 直接加载（不做键预检——见类注释"初始化与噪声策略"：
            // 预检依赖惰性初始化的 locator，会短路本应触发初始化的加载）
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

            // 编辑器端 Label 探针：空类别（Label 无任何条目）直接回退，避免
            // LoadAssetsAsync 的 InvalidKeyException 噪声。探针直接查编辑器设置，
            // 无运行时初始化依赖；构建包内探针为 null，直接加载。
            if (LabelProbe != null)
            {
                bool hasEntries;
                try { hasEntries = LabelProbe(label); }
                catch { hasEntries = false; }
                if (!hasEntries) return null;
            }

            var handle = Addressables.LoadAssetsAsync<T>(new List<object> { label }, null, Addressables.MergeMode.Union);
#if UNITY_EDITOR
            // 编辑器 Fast 模式：初始化完成后操作应同步完成。
            // 意外未完成时尝试 WaitForCompletion 补救一次，失败则回退链上下一环。
            if (!handle.IsDone)
            {
                try { handle.WaitForCompletion(); }
                catch
                {
                    if (handle.IsValid()) Addressables.Release(handle);
                    return null;
                }
            }
            if (!handle.IsDone)
            {
                if (handle.IsValid()) Addressables.Release(handle);
                return null;
            }
            IList<T> results = handle.Result;
#else
            IList<T> results = handle.WaitForCompletion();
#endif
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

#if UNITY_EDITOR
    /// <summary>
    /// 编辑器键探针判定：true = 该键确认未注册（应静默回退，不发起加载）。
    /// 探针未注入或探针自身异常时返回 false（放行，交给加载结果裁决——
    /// 宁可多一次无效加载，不可误杀有效键）。
    /// </summary>
    private static bool IsKeyUnregistered(string key)
    {
        var probe = KeyProbe;
        if (probe == null) return false;
        try { return !probe(key); }
        catch { return false; }
    }
#endif
}
#endif
