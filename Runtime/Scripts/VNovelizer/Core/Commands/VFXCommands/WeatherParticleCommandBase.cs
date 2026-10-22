using System.Collections;
using System.Globalization;
using UnityEngine;
using VNovelizer.Core.Weather;

namespace VNovelizer.Core.Commands
{
    /// <summary>
    /// particle_snow / particle_rain 公共基类（天气粒子命令族，2026-10-22）。
    ///
    /// <para>
    /// 设计（与 filter_xxx 命令族同构）：命令参数 = intensity（0~1，缺省 0.6）
    /// + 可选 duration（渐变秒数，缺省 0.5，0 = 立即）。重复开启幂等（从当前
    /// 强度渐变到目标强度并刷新）。intensity 映射到粒子发射率与亮度。
    /// </para>
    ///
    /// <para>语义：Execute/Simulate/Interrupt = 立即终态；ExecuteAsync 等待渐变
    /// 时长后返回（保持 &amp; 时序）。天气粒子是持久视觉状态，不入存档（快进/
    /// 读档经 Simulate 重建终态）；随剧场清场（WeatherParticleManager.ResetAll）复位。</para>
    /// </summary>
    public abstract class WeatherParticleCommandBase : VNCommand
    {
        protected abstract string WeatherId { get; }

        private const float DefaultIntensity = 0.6f;
        private const float DefaultDuration = 0.5f;

        // 渐变等待中的并行实例计数 + 中断时回放的目标强度（与 FilterCommandBase 同构）
        private int _pendingCount;
        private float _pendingIntensity;

        public override bool Execute(string args)
        {
            if (!ParseArgs(args, out float intensity, out _)) return false;
            WeatherParticleManager.EnsureInstance();
            return WeatherParticleManager.Instance.SetWeather(WeatherId, intensity, 0f);
        }

        public override IEnumerator ExecuteAsync(string args)
        {
            if (!ParseArgs(args, out float intensity, out float duration)) yield break;
            if (duration < 0f) duration = DefaultDuration;

            WeatherParticleManager.EnsureInstance();
            _pendingCount++;
            _pendingIntensity = intensity;
            try
            {
                WeatherParticleManager.Instance.SetWeather(WeatherId, intensity, duration);
                if (duration > 0f) yield return new WaitForSeconds(duration);
            }
            finally
            {
                _pendingCount--;
            }
        }

        public override void Interrupt()
        {
            // 点击跳过：渐变瞬间完成到目标强度（密度不停在中间值）
            if (_pendingCount <= 0) return;
            _pendingCount = 0;
            WeatherParticleManager.EnsureInstance();
            WeatherParticleManager.Instance.SetWeather(WeatherId, _pendingIntensity, 0f);
        }

        public override void Simulate(string args)
        {
            // 快进/读档重建：天气粒子是持久视觉状态，直接到终态保证画面与真实播放一致
            if (!ParseArgs(args, out float intensity, out _)) return;
            WeatherParticleManager.EnsureInstance();
            WeatherParticleManager.Instance.SetWeather(WeatherId, intensity, 0f);
        }

        private static bool ParseArgs(string args, out float intensity, out float duration)
        {
            intensity = DefaultIntensity;
            duration = -1f;

            string[] parts = string.IsNullOrEmpty(args) ? new string[0] : args.Split(',');

            if (parts.Length > 0 && !string.IsNullOrWhiteSpace(parts[0]))
            {
                if (!float.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
                {
                    Debug.LogWarning($"[WeatherParticle] 第 1 个参数 '{parts[0].Trim()}' 不是有效数值，已用默认值 {DefaultIntensity.ToString(CultureInfo.InvariantCulture)}");
                }
                else
                {
                    intensity = Mathf.Clamp(parsed, 0f, 1f);
                }
            }

            if (parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]))
            {
                if (float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
                    duration = Mathf.Max(parsed, 0f);
            }

            return true;
        }
    }
}
