using System;
using UnityEngine;

namespace VNovelizer.Core.Compat
{
    /// <summary>
    /// Ease 枚举的单点求值器（Penner 标准 30 曲线）。
    ///
    /// 剧场层 / UI 层自驱动协程动画（MeshActor.FadeAsync/MoveAsync、bgfade、charjump 等）
    /// 用它替代 PrimeTween——PrimeTween 不暴露"给定 t 求值"的公开 API。
    /// 与 PrimeTween 语义对齐：Ease.Default 按 PrimeTween 缺省缓动（OutQuad）求值。
    /// </summary>
    public static class EaseEvaluator
    {
        private const double BackC1 = 1.70158;
        private const double BackC3 = BackC1 + 1;
        private const double BackC2 = BackC1 * 1.525;

        /// <summary>求值 [0,1] 进度经缓动曲线后的值（t 越界自动钳制）</summary>
        public static float Evaluate(Ease ease, float t)
        {
            t = Mathf.Clamp01(t);
            switch (ease)
            {
                case Ease.Linear: return t;
                case Ease.Default: return t * (2f - t); // PrimeTween 缺省 = OutQuad

                case Ease.InSine: return 1f - (float)Math.Cos(t * Math.PI / 2.0);
                case Ease.OutSine: return (float)Math.Sin(t * Math.PI / 2.0);
                case Ease.InOutSine: return (float)(-(Math.Cos(Math.PI * t) - 1) / 2.0);

                case Ease.InQuad: return t * t;
                case Ease.OutQuad: return t * (2f - t);
                case Ease.InOutQuad: return t < 0.5f ? 2f * t * t : -1f + (4f - 2f * t) * t;

                case Ease.InCubic: return t * t * t;
                case Ease.OutCubic: return (t - 1f) * (t - 1f) * (t - 1f) + 1f;
                case Ease.InOutCubic: return t < 0.5f ? 4f * t * t * t : (t - 1f) * (2f * t - 2f) * (2f * t - 2f) + 1f;

                case Ease.InQuart: return t * t * t * t;
                case Ease.OutQuart: return 1f - (t - 1f) * (t - 1f) * (t - 1f) * (t - 1f);
                case Ease.InOutQuart: return t < 0.5f ? 8f * t * t * t * t : 1f - 8f * (t - 1f) * (t - 1f) * (t - 1f) * (t - 1f);

                case Ease.InQuint: return t * t * t * t * t;
                case Ease.OutQuint: return 1f + (t - 1f) * (t - 1f) * (t - 1f) * (t - 1f) * (t - 1f);
                case Ease.InOutQuint: return t < 0.5f ? 16f * t * t * t * t * t : 1f + 16f * (t - 1f) * (t - 1f) * (t - 1f) * (t - 1f) * (t - 1f);

                case Ease.InExpo: return t <= 0f ? 0f : (float)Math.Pow(2, 10 * (t - 1));
                case Ease.OutExpo: return t >= 1f ? 1f : 1f - (float)Math.Pow(2, -10 * t);
                case Ease.InOutExpo:
                    if (t <= 0f) return 0f;
                    if (t >= 1f) return 1f;
                    return t < 0.5f ? (float)Math.Pow(2, 20 * t - 10) / 2f
                                    : (2f - (float)Math.Pow(2, -20 * t + 10)) / 2f;

                case Ease.InCirc: return 1f - (float)Math.Sqrt(1 - t * t);
                case Ease.OutCirc: return (float)Math.Sqrt(1 - (t - 1) * (t - 1));
                case Ease.InOutCirc:
                    return t < 0.5f ? (1f - (float)Math.Sqrt(1 - 4 * t * t)) / 2f
                                    : ((float)Math.Sqrt(1 - (-2 * t + 2) * (-2 * t + 2)) + 1f) / 2f;

                case Ease.InBack: return (float)(BackC3 * t * t * t - BackC1 * t * t);
                case Ease.OutBack: return (float)(1 + BackC3 * Math.Pow(t - 1, 3) + BackC1 * Math.Pow(t - 1, 2));
                case Ease.InOutBack:
                {
                    if (t < 0.5f)
                    {
                        double x = 2 * t;
                        return (float)(x * x * ((BackC2 + 1) * x - BackC2) / 2.0);
                    }
                    double y = 2 * t - 2;
                    return (float)((y * y * ((BackC2 + 1) * y + BackC2) + 2) / 2.0);
                }

                case Ease.InElastic:
                {
                    if (t <= 0f) return 0f;
                    if (t >= 1f) return 1f;
                    return (float)(-Math.Pow(2, 10 * t - 10) * Math.Sin((t * 10 - 10.75) * (2 * Math.PI / 3.0)));
                }
                case Ease.OutElastic:
                {
                    if (t <= 0f) return 0f;
                    if (t >= 1f) return 1f;
                    return (float)(Math.Pow(2, -10 * t) * Math.Sin((t * 10 - 0.75) * (2 * Math.PI / 3.0)) + 1);
                }
                case Ease.InOutElastic:
                {
                    if (t <= 0f) return 0f;
                    if (t >= 1f) return 1f;
                    const double c5 = 2 * Math.PI / 4.5;
                    if (t < 0.5f)
                        return (float)(-(Math.Pow(2, 20 * t - 10) * Math.Sin((20 * t - 11.125) * c5)) / 2.0);
                    return (float)((Math.Pow(2, -20 * t + 10) * Math.Sin((20 * t - 11.125) * c5)) / 2.0 + 1);
                }

                case Ease.InBounce: return 1f - OutBounce(1f - t);
                case Ease.OutBounce: return OutBounce(t);
                case Ease.InOutBounce:
                    return t < 0.5f ? (1f - OutBounce(1f - 2f * t)) / 2f
                                    : (1f + OutBounce(2f * t - 1f)) / 2f;
            }
            return t;
        }

        private static float OutBounce(float t)
        {
            const double n1 = 7.5625;
            const double d1 = 2.75;
            if (t < 1 / d1) return (float)(n1 * t * t);
            if (t < 2 / d1) return (float)(n1 * (t -= 1.5f / (float)d1) * t + 0.75);
            if (t < 2.5 / d1) return (float)(n1 * (t -= 2.25f / (float)d1) * t + 0.9375);
            return (float)(n1 * (t -= 2.625f / (float)d1) * t + 0.984375);
        }
    }

    /// <summary>解析后的 [InEase, OutEase] 参数对</summary>
    public struct EaseArgs
    {
        /// <summary>进入曲线（未指定为 null）</summary>
        public Ease? In;

        /// <summary>退出曲线（未指定为 null）</summary>
        public Ease? Out;

        public bool HasIn => In.HasValue;
        public bool HasOut => Out.HasValue;

        /// <summary>In 槽取值，未指定时回退线性</summary>
        public Ease InOrDefault => In ?? Ease.Linear;
    }

    /// <summary>
    /// 命令参数中的 [InEase, OutEase] 解析工具（R13 ease 参数方案）。
    /// 语法约定：追加在命令现有参数末尾的两个可选参数：
    ///   cmd(..., InEase)                  → 只填一个 = InOutEase（进出同曲线）
    ///   cmd(..., InEase, OutEase)         → 各用各的
    ///   cmd(...)                          → 均未指定（调用方按命令现状默认处理）
    /// </summary>
    public static class EaseArgParser
    {
        /// <summary>
        /// Ease 枚举全部候选（VNParam 元数据 Options 用，必须为编译期常量）。
        /// 与 EaseCompat.Ease 枚举成员一致；Default 仍可由 TryParse 兼容解析（手写兼容），
        /// 但编辑器下拉不展示——空选即为现状默认，无需 Default 关键字。
        /// </summary>
        public const string EaseOptions =
            "Linear|InSine|OutSine|InOutSine|InQuad|OutQuad|InOutQuad|InCubic|OutCubic|InOutCubic|" +
            "InQuart|OutQuart|InOutQuart|InQuint|OutQuint|InOutQuint|InExpo|OutExpo|InOutExpo|" +
            "InCirc|OutCirc|InOutCirc|InElastic|OutElastic|InOutElastic|InBack|OutBack|InOutBack|" +
            "InBounce|OutBounce|InOutBounce";

        /// <summary>
        /// 进入曲线候选：Linear + 全部 In*/InOut*（不含 Out* / Default）。
        /// 首项 '|' 切分产生空串首项，ParamCandidateProvider 据此渲染为「（无）」占位
        /// ——「无」= 留空不写 = 命令按现状默认。
        /// </summary>
        public const string InEaseOptions =
            "|" +
            "Linear|InSine|InOutSine|InQuad|InOutQuad|InCubic|InOutCubic|" +
            "InQuart|InOutQuart|InQuint|InOutQuint|InExpo|InOutExpo|" +
            "InCirc|InOutCirc|InElastic|InOutElastic|InBack|InOutBack|" +
            "InBounce|InOutBounce";

        /// <summary>
        /// 退出曲线候选：Linear + 全部 Out*（不含 In* / InOut* / Default）。
        /// 首项 '|' 切分产生空串首项，ParamCandidateProvider 渲染为「（无）」占位。
        /// </summary>
        public const string OutEaseOptions =
            "|" +
            "Linear|OutSine|OutQuad|OutCubic|OutQuart|OutQuint|OutExpo|" +
            "OutCirc|OutElastic|OutBack|OutBounce";

        /// <summary>解析单个 ease 名（大小写不敏感；空/非法返回 false，非法时打警告）</summary>
        public static bool TryParse(string token, out Ease ease)
        {
            ease = Ease.Linear;
            if (string.IsNullOrWhiteSpace(token)) return false;

            if (Enum.TryParse(token.Trim(), true, out ease) && Enum.IsDefined(typeof(Ease), ease))
                return true;

            Debug.LogWarning($"[EaseArgParser] 未知缓动曲线名 \"{token.Trim()}\"，已忽略（可用: {EaseOptions}）");
            return false;
        }

        /// <summary>
        /// 从 args 数组的 <paramref name="startIndex"/> 起解析 [InEase, OutEase]。
        /// 只填一个时两槽同值（InOutEase 语义）；两个都不填时返回全 null。
        /// </summary>
        public static EaseArgs ParseAt(string[] parts, int startIndex)
        {
            var result = new EaseArgs();
            if (parts == null || startIndex < 0 || startIndex >= parts.Length)
                return result;

            Ease first;
            bool hasFirst = TryParse(parts[startIndex], out first);

            Ease second = Ease.Linear;
            bool hasSecond = parts.Length > startIndex + 1 && TryParse(parts[startIndex + 1], out second);

            if (hasFirst)
            {
                result.In = first;
                result.Out = hasSecond ? second : first; // 只填一个 = InOutEase
            }
            else if (hasSecond)
            {
                // 首槽空、次槽有值：仅指定退出曲线（如 charfadeout(L, 0.5, , OutSine)）
                result.Out = second;
            }
            return result;
        }
    }
}
