using System.Collections;
using System.Globalization;
using UnityEngine;
using VNovelizer.Core.Weather;

namespace VNovelizer.Core.Commands
{
    /// <summary>
    /// particle_snow / particle_rain / particlecustom 公共基类（粒子特效命令族，2026-10-22/23）。
    ///
    /// <para>
    /// 设计（与 filter_xxx 命令族同构）：参数 = intensity（0~1，缺省 0.6）
    /// + 可选 duration（渐变秒数，缺省 = VFX 注册表条目默认时长，0 = 立即）。重复开启幂等
    /// （从当前强度渐变到目标强度并刷新）。intensity 经 VN_Particle.SetIntensity 映射到
    /// 粒子发射率与亮度。
    /// </para>
    ///
    /// <para>
    /// 【VFX 注册表扩展】目标 id 由子类解析（内置命令恒为 snow/rain，particlecustom 取首参数）；
    /// id 未注册/已禁用 → 管理器警告"未注册"并返回 false。
    /// </para>
    ///
    /// <para>语义：Execute/Simulate/Interrupt = 立即终态；ExecuteAsync 等待渐变时长后返回
    /// （保持 &amp; 时序）。粒子特效是持久视觉状态，不入存档（快进/读档经 Simulate 重建终态）；
    /// 随剧场清场（WeatherParticleManager.ResetAll）复位。</para>
    /// </summary>
    public abstract class WeatherParticleCommandBase : VNCommand
    {
        /// <summary>从 args 解析目标粒子 id（返回 false = 参数缺失/非法）</summary>
        protected abstract bool TryParseId(string args, out string id);

        /// <summary>缺省强度（particlecustom 覆写 ParseArgs 时共用）</summary>
        protected const float DefaultIntensity = 0.6f;

        // 渐变等待中的并行实例计数 + 中断时回放的目标（与 FilterCommandBase 同构）
        private int _pendingCount;
        private string _pendingId;
        private float _pendingIntensity;

        public override bool Execute(string args)
        {
            if (!TryParseId(args, out string id)) return false;
            if (!ParseArgs(args, out float intensity, out _)) return false;
            WeatherParticleManager.EnsureInstance();
            return WeatherParticleManager.Instance.SetWeather(id, intensity, 0f);
        }

        public override IEnumerator ExecuteAsync(string args)
        {
            if (!TryParseId(args, out string id)) yield break;
            if (!ParseArgs(args, out float intensity, out float duration)) yield break;
            if (duration < 0f) duration = WeatherParticleManager.GetDefaultDuration(id);

            WeatherParticleManager.EnsureInstance();
            _pendingCount++;
            _pendingId = id;
            _pendingIntensity = intensity;
            try
            {
                WeatherParticleManager.Instance.SetWeather(id, intensity, duration);
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
            WeatherParticleManager.Instance.SetWeather(_pendingId, _pendingIntensity, 0f);
        }

        public override void Simulate(string args)
        {
            // 快进/读档重建：粒子特效是持久视觉状态，直接到终态保证画面与真实播放一致
            if (!TryParseId(args, out string id)) return;
            if (!ParseArgs(args, out float intensity, out _)) return;
            WeatherParticleManager.EnsureInstance();
            WeatherParticleManager.Instance.SetWeather(id, intensity, 0f);
        }

        /// <summary>
        /// 解析 intensity/duration（intensity 恒为首参数）。particlecustom 覆写本方法
        /// 跳过 id 首参数（intensity 在第 2 位）。
        /// </summary>
        protected virtual bool ParseArgs(string args, out float intensity, out float duration)
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
