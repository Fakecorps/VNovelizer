using Coffee.UIExtensions;
using UnityEngine;

namespace VNovelizer.Core.Weather
{
    /// <summary>
    /// 粒子特效组件契约（2026-10-23 VFX 注册表扩展）。
    ///
    /// <para>
    /// VFX 注册表粒子条目的 Prefab 必须挂本组件；<see cref="WeatherParticleManager"/> 只认
    /// VN_Particle 接口（不直接操作 ParticleSystem）。强度抽象由
    /// <see cref="SetIntensity"/> 提供：发射率按基准缩放 + 亮度 alpha 调制。
    /// </para>
    ///
    /// <para>
    /// <b>【2026-10-23 减法重构】本组件是纯接口，绝不修改用户资产</b>：
    /// ① 不再自动补挂 UIParticle；② 不再归一化 RectTransform；③ 不再对 Prefab 做任何自愈。
    /// Prefab 自己带什么就是什么（RectTransform / UIParticle / ParticleSystem 需在
    /// Prefab 里配好，缺失时仅警告）。Awake 只捕获基准参数并启动播放。
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public class VN_Particle : MonoBehaviour
    {
        private ParticleSystem _ps;
        private float _baseEmissionRate;
        private ParticleSystem.MinMaxGradient _baseStartColor;
        private bool _captured;

        private void Awake()
        {
            _ps = GetComponent<ParticleSystem>();
            if (_ps == null) _ps = GetComponentInChildren<ParticleSystem>();

            if (_ps == null)
            {
                Debug.LogWarning($"[VN_Particle] {gameObject.name} 未找到 ParticleSystem（自身或子级），强度控制将失效。" +
                                 "请确认 Prefab 自带 ParticleSystem / UIParticle / RectTransform。");
                return;
            }

            _baseEmissionRate = _ps.emission.rateOverTime.constant;
            _baseStartColor = _ps.main.startColor;
            _captured = true;

            var em = _ps.emission;
            em.enabled = true;
            _ps.Play();
        }

        /// <summary>
        /// 强度抽象：intensity 0~1 = 发射率按基准缩放 + 亮度 alpha 调制（下限保留可见性）。
        /// 每次从捕获的基准值缩放（幂等，重复调用不累积）。
        /// </summary>
        public void SetIntensity(float intensity)
        {
            intensity = Mathf.Clamp01(intensity);
            if (!_captured || _ps == null) return;

            var em = _ps.emission;
            em.rateOverTime = new ParticleSystem.MinMaxCurve(_baseEmissionRate * intensity);

            float alpha = Mathf.Lerp(0.3f, 1f, intensity);
            var main = _ps.main;
            main.startColor = ScaleGradientAlpha(_baseStartColor, alpha);
        }

        /// <summary>按 alpha 缩放颜色曲线（Color/TwoColors 直改 alpha；Gradient 等复杂模式只调发射率）</summary>
        private static ParticleSystem.MinMaxGradient ScaleGradientAlpha(ParticleSystem.MinMaxGradient src, float alpha)
        {
            switch (src.mode)
            {
                case ParticleSystemGradientMode.Color:
                    {
                        Color c = src.color;
                        c.a = src.color.a * alpha;
                        return new ParticleSystem.MinMaxGradient(c);
                    }
                case ParticleSystemGradientMode.TwoColors:
                    {
                        Color min = src.colorMin;
                        min.a = src.colorMin.a * alpha;
                        Color max = src.colorMax;
                        max.a = src.colorMax.a * alpha;
                        return new ParticleSystem.MinMaxGradient(min, max);
                    }
                default:
                    // Gradient / RandomColor：alpha 曲线缩放复杂，退回只调发射率（强度仍生效）
                    return src;
            }
        }
    }
}
