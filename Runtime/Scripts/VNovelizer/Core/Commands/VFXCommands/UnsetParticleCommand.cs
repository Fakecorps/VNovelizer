using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using VNovelizer.Core.Commands.Meta;
using VNovelizer.Core.Weather;

namespace VNovelizer.Core.Commands
{
    /// <summary>
    /// 关闭粒子特效命令：unsetparticle(name[, duration])（唯一生命周期命令，VFX 注册表扩展 2026-10-23）
    ///
    /// <para>
    /// name = VFX 注册表粒子条目名（内置 rain/snow 或自定义 Prefab 条目；
    /// 下拉候选 = 注册表启用条目 + all）；特殊值 <c>all</c> = 关闭全部粒子特效。
    /// duration = 强度衰减秒数（可选；缺省 = 注册表条目默认时长，0 = 立即移除）。
    /// 强度渐变衰减到 0 后销毁粒子系统；未激活时幂等静默。
    /// </para>
    ///
    /// <para>语义：Execute/Simulate/Interrupt = 立即移除（快进/中断画面一致）；
    /// ExecuteAsync 等待衰减时长后返回（保持 &amp; 时序）。</para>
    /// </summary>
    [VNCommandMeta(VNCommandCategory.VFX,
        "关闭粒子特效（强度渐变衰减后移除）；name=all 关闭全部粒子特效")]
    public class UnsetParticleCommand : VNCommand
    {
        [VNParam(0, "name", VNParamType.WeatherParticleName,
            Description = "注册表粒子条目名（内置 rain/snow 或自定义条目；all = 关闭全部）")]
        [VNParam(1, "duration", VNParamType.Float, Min = 0f, Max = 10f, Optional = true,
            Description = "衰减秒数（缺省 = 注册表条目默认时长；0 = 立即移除）")]
        public override string CommandName { get { return "unsetparticle"; } }

        // 多实例并行支持：衰减等待中的天气名清单（Interrupt 时逐个立即移除）
        private readonly List<string> _pending = new List<string>();

        public override bool Execute(string args)
        {
            string name = ParseName(args);
            if (string.IsNullOrEmpty(name)) return false;
            WeatherParticleManager.EnsureInstance();
            WeatherParticleManager.Instance.RemoveWeather(name, 0f);
            return true;
        }

        public override IEnumerator ExecuteAsync(string args)
        {
            string name = ParseName(args);
            if (string.IsNullOrEmpty(name)) yield break;

            float duration = ParseDuration(args);
            if (duration < 0f) duration = WeatherParticleManager.GetDefaultDuration(name);

            WeatherParticleManager.EnsureInstance();
            _pending.Add(name);
            try
            {
                WeatherParticleManager.Instance.RemoveWeather(name, duration);
                if (duration > 0f) yield return new WaitForSeconds(duration);
            }
            finally
            {
                _pending.Remove(name);
            }
        }

        public override void Interrupt()
        {
            // 点击跳过：衰减瞬间完成（天气粒子立即移除）
            if (_pending.Count == 0) return;
            var snapshot = new List<string>(_pending);
            _pending.Clear();
            WeatherParticleManager.EnsureInstance();
            foreach (var n in snapshot)
                WeatherParticleManager.Instance.RemoveWeather(n, 0f);
        }

        public override void Simulate(string args)
        {
            // 快进/读档重建：直接移除到终态（保证重建画面与真实播放一致）
            string name = ParseName(args);
            if (string.IsNullOrEmpty(name)) return;
            WeatherParticleManager.EnsureInstance();
            WeatherParticleManager.Instance.RemoveWeather(name, 0f);
        }

        private static string ParseName(string args)
        {
            if (string.IsNullOrEmpty(args)) return null;
            string[] parts = args.Split(',');
            string name = parts[0].Trim();
            if (name.Length == 0)
            {
                Debug.LogWarning("[UnsetParticle] 天气名为空，已忽略");
                return null;
            }
            return name;
        }

        /// <summary>解析可选衰减时长；未提供返回 -1（由调用方回退默认）</summary>
        private static float ParseDuration(string args)
        {
            string[] parts = args.Split(',');
            if (parts.Length < 2) return -1f;
            float duration;
            if (!float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out duration))
                return -1f;
            return Mathf.Max(duration, 0f);
        }
    }
}
