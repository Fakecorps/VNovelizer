using System;
using UnityEngine;

/// <summary>
/// 一次资源加载的异步操作句柄（由 IVNResourceProvider 创建，VNResourceService 透传给调用方）。
///
/// 设计要点：
/// - <see cref="Progress"/> 惰性求值：由底层 request/handle 的实时进度驱动，
///   跨提供者回退时由服务层按链长加权，无需每帧轮询推送；
/// - <see cref="Completed"/> 订阅时若已完成则立即回调（一次性语义）；
/// - 完成（含失败，Asset=null）后不可再变更。
/// </summary>
public class VNLoadOperation<T> where T : UnityEngine.Object
{
    private Func<float> _progressSource;
    private T _asset;
    private bool _isDone;
    private Action<VNLoadOperation<T>> _completed;

    /// <summary>本次加载的资源键（诊断用）</summary>
    public string Key { get; }

    /// <summary>是否已完成（无论成败）</summary>
    public bool IsDone => _isDone;

    /// <summary>加载结果（失败/未命中为 null）</summary>
    public T Asset => _asset;

    /// <summary>当前进度 [0,1]。已完成恒为 1；无进度源时未完成为 0。</summary>
    public float Progress => _isDone ? 1f : (_progressSource != null ? _progressSource() : 0f);

    /// <summary>完成事件（一次性：完成后再订阅会立即回调）</summary>
    public event Action<VNLoadOperation<T>> Completed
    {
        add
        {
            if (_isDone) { if (value != null) value(this); }
            else _completed += value;
        }
        remove { _completed -= value; }
    }

    public VNLoadOperation(string key)
    {
        Key = key;
    }

    /// <summary>设置底层实时进度源（ResourceRequest.progress / AsyncOperationHandle.PercentComplete 等）</summary>
    internal void SetProgressSource(Func<float> source)
    {
        _progressSource = source;
    }

    /// <summary>完成操作（成功传资源，失败传 null）。幂等：只有首次调用生效。</summary>
    internal void Complete(T asset)
    {
        if (_isDone) return;
        _isDone = true;
        _asset = asset;
        _progressSource = null;
        var handlers = _completed;
        _completed = null;
        if (handlers != null) handlers(this);
    }
}
