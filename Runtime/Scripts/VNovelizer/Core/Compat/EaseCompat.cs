/// <summary>
/// 独立 Ease 枚举 — 始终可用，不依赖 PrimeTween。
/// 整数值对齐常见 tween 库（DOTween/PrimeTween），确保跨包序列化兼容。
/// </summary>
namespace VNovelizer.Core.Compat
{
    public enum Ease
    {
        Default = -1,
        Linear = 0,
        InSine = 1, OutSine = 2, InOutSine = 3,
        InQuad = 4, OutQuad = 5, InOutQuad = 6,
        InCubic = 7, OutCubic = 8, InOutCubic = 9,
        InQuart = 10, OutQuart = 11, InOutQuart = 12,
        InQuint = 13, OutQuint = 14, InOutQuint = 15,
        InExpo = 16, OutExpo = 17, InOutExpo = 18,
        InCirc = 19, OutCirc = 20, InOutCirc = 21,
        InElastic = 22, OutElastic = 23, InOutElastic = 24,
        InBack = 25, OutBack = 26, InOutBack = 27,
        InBounce = 28, OutBounce = 29, InOutBounce = 30
    }

    public enum SequenceCycleMode
    {
        Restart = 0,
        Yoyo = 1,
        Incremental = 2,
        Rewind = 3
    }
}
