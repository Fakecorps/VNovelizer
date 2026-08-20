using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 基于 UnityEngine.Resources 的提供者（默认兜底后端）。
///
/// 键约定：相对 Assets/Resources 的无扩展名路径——与旧版全项目行为完全一致，
/// 存量项目（Assets/Resources/VNovelizerRes 已有内容）零迁移即可继续工作。
///
/// 卸载语义：Resources 资源无引用计数，<see cref="Release"/> 为空操作
/// （内存回收依赖 Resources.UnloadUnusedAssets / 场景切换，与旧版一致）。
/// </summary>
public class ResourcesProvider : IVNResourceProvider
{
    public string Name => "Resources";

    public bool IsAvailable => true;

    public void Initialize() { }

    public T Load<T>(string key) where T : UnityEngine.Object
    {
        if (string.IsNullOrEmpty(key)) return null;
        return Resources.Load<T>(key);
    }

    public VNLoadOperation<T> LoadAsync<T>(string key) where T : UnityEngine.Object
    {
        var op = new VNLoadOperation<T>(key);
        if (string.IsNullOrEmpty(key)) { op.Complete(null); return op; }

        // 编辑模式（编辑器工具调用）无协程环境且无逐帧进度意义，直接同步完成
        if (!Application.isPlaying)
        {
            op.Complete(Resources.Load<T>(key));
            return op;
        }

        ResourceRequest request = Resources.LoadAsync<T>(key);
        op.SetProgressSource(() => request.progress);
        request.completed += _ => op.Complete(request.asset as T);
        return op;
    }

    public IList<T> LoadAll<T>(string category) where T : UnityEngine.Object
    {
        if (string.IsNullOrEmpty(category)) return null;
        T[] assets = Resources.LoadAll<T>(category);
        return (assets != null && assets.Length > 0) ? assets : null;
    }

    public void Release(string key)
    {
        // Resources 无引用计数：空操作（见类注释）
    }
}
