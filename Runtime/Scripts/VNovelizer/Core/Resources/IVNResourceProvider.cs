using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 资源提供者接口：VNovelizer 资源加载抽象层的最小契约。
///
/// 内置实现：
/// - <see cref="ResourcesProvider"/>：UnityEngine.Resources（默认兜底，行为与旧版一致）；
/// - <see cref="AddressablesProvider"/>：Unity Addressables（安装 com.unity.addressables 后启用，位于链首）。
///
/// 契约约定：
/// - 所有方法对"资源不存在"统一返回 null / 完成值为 null 的操作对象，
///   不抛异常、不打印错误（是否回退、如何报告由 <see cref="VNResourceService"/> 与调用方决定）；
/// - 键 = 资源键（见 <see cref="VNResourceKeys"/>）；
/// - 同步 <c>Load</c> 在 Addressables 实现内部以 WaitForCompletion 桥接
///   （编辑器资源库模式即时返回；WebGL 平台请使用异步 API）。
/// </summary>
public interface IVNResourceProvider
{
    /// <summary>提供者名称（诊断日志用）</summary>
    string Name { get; }

    /// <summary>初始化（幂等）。服务链构建时调用。</summary>
    void Initialize();

    /// <summary>当前是否可用（不可用时服务层直接跳过，走链上下一环）</summary>
    bool IsAvailable { get; }

    /// <summary>同步加载单个资源。未命中返回 null。</summary>
    T Load<T>(string key) where T : UnityEngine.Object;

    /// <summary>异步加载单个资源。</summary>
    VNLoadOperation<T> LoadAsync<T>(string key) where T : UnityEngine.Object;

    /// <summary>
    /// 按类别批量加载（如 "VNovelizerRes/Characters"）。
    /// Resources 实现按文件夹扫描；Addressables 实现按类别 Label 检索。
    /// 未命中/为空返回 null。
    /// </summary>
    IList<T> LoadAll<T>(string category) where T : UnityEngine.Object;

    /// <summary>
    /// 按 key 尽力卸载（Phase 3 生命周期管理的入口；Resources 实现为空操作）。
    /// Addressables 实现释放该 key 缓存的全部句柄。
    /// </summary>
    void Release(string key);
}
