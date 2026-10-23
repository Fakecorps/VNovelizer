using System.Globalization;
using UnityEngine;
using VNovelizer.Core.Commands.Meta;

namespace VNovelizer.Core.Commands
{
    /// <summary>
    /// 自定义粒子特效命令：particlecustom(id[, intensity[, duration]])（VFX 注册表扩展 2026-10-23）
    ///
    /// <para>
    /// id = VFX 注册表中的粒子条目（含用户自建 Prefab 条目；下拉候选 = 注册表启用条目，
    /// 不含 all）。intensity = 整体强度 0~1（经 VN_Particle.SetIntensity 映射到发射率与
    /// 亮度；缺省 0.6）。duration = 渐变秒数（缺省 = 注册表条目默认时长，0 = 立即）。
    /// 重复开启幂等（渐变到目标强度并刷新）。关闭用 unsetparticle(id) / unsetparticle(all)。
    /// </para>
    ///
    /// <para>语义（继承自 WeatherParticleCommandBase）：Execute/Simulate/Interrupt
    /// 立即终态，ExecuteAsync 等待渐变；不入存档（读档经 Simulate 重建终态）。</para>
    /// </summary>
    [VNCommandMeta(VNCommandCategory.VFX,
        "开启注册表中的任意粒子特效（含自定义 Prefab 条目）；intensity 调整体强度（发射率+亮度）")]
    public class ParticleCustomCommand : WeatherParticleCommandBase
    {
        [VNParam(0, "id", VNParamType.RegisteredParticleName,
            Description = "注册表粒子条目 id（内置 rain/snow 或自定义预制体条目）")]
        [VNParam(1, "intensity", VNParamType.Float, Min = 0f, Max = 1f, Default = "0.6", Optional = true,
            Description = "整体强度（0~1；缺省 = 预设默认 0.6）")]
        [VNParam(2, "duration", VNParamType.Float, Min = 0f, Max = 10f, Optional = true,
            Description = "渐变秒数（缺省 = 注册表条目默认时长；0 = 立即生效）")]
        public override string CommandName { get { return "particlecustom"; } }

        protected override bool TryParseId(string args, out string id)
        {
            id = null;
            if (string.IsNullOrEmpty(args)) return false;
            string[] parts = args.Split(',');
            if (parts.Length == 0 || string.IsNullOrWhiteSpace(parts[0]))
            {
                Debug.LogWarning("[particlecustom] 缺少粒子 id 参数（格式 particlecustom(id[,intensity[,duration]])）");
                return false;
            }
            id = parts[0].Trim();
            return true;
        }

        /// <summary>覆写：intensity/duration 位于 id 之后的第 2/3 参数位</summary>
        protected override bool ParseArgs(string args, out float intensity, out float duration)
        {
            intensity = DefaultIntensity;
            duration = -1f;

            string[] parts = string.IsNullOrEmpty(args) ? new string[0] : args.Split(',');

            if (parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]))
            {
                if (!float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
                {
                    Debug.LogWarning($"[particlecustom] 第 2 个参数 '{parts[1].Trim()}' 不是有效数值，已用默认值 {DefaultIntensity.ToString(CultureInfo.InvariantCulture)}");
                }
                else
                {
                    intensity = Mathf.Clamp(parsed, 0f, 1f);
                }
            }

            if (parts.Length > 2 && !string.IsNullOrWhiteSpace(parts[2]))
            {
                if (float.TryParse(parts[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
                    duration = Mathf.Max(parsed, 0f);
            }

            return true;
        }
    }
}
