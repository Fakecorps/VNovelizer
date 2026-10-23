using VNovelizer.Core.Commands.Meta;

namespace VNovelizer.Core.Commands
{
    /// <summary>
    /// 下雨天气粒子命令：particle_rain([intensity[, duration]])
    ///
    /// <para>
    /// 开启代码生成的下雨粒子系统（EffectLayer，滤镜层之上、对话框之下；
    /// 雨丝带 ≈10° 风斜角 + 每粒 ±3° 抖动）。intensity = 雨量 0~1（缺省 0.6，
    /// 映射到发射率与亮度）；duration = 渐变秒数（缺省 0.5，0 = 立即）。
    /// 重复开启幂等（渐变到目标强度并刷新）。
    /// 关闭：unsetparticle(rain) 或 unsetparticle(all)。
    /// </para>
    ///
    /// <para>语义（继承自 WeatherParticleCommandBase）：Execute/Simulate/Interrupt
    /// 立即终态，ExecuteAsync 等待渐变；不入存档（读档经 Simulate 重建终态）。</para>
    /// </summary>
    [VNCommandMeta(VNCommandCategory.VFX,
        "开启下雨天气粒子（代码生成粒子系统，位于特效层）；关闭用 unsetparticle(rain) 或 unsetparticle(all)")]
    public class ParticleRainCommand : WeatherParticleCommandBase
    {
        [VNParam(0, "intensity", VNParamType.Float, Min = 0f, Max = 1f, Default = "0.6", Optional = true,
            Description = "雨量（0~1；缺省 = 预设默认 0.6）")]
        [VNParam(1, "duration", VNParamType.Float, Min = 0f, Max = 10f, Optional = true,
            Description = "渐变秒数（缺省 = VFX 注册表 rain 条目默认时长；0 = 立即生效）")]
        public override string CommandName { get { return "particle_rain"; } }

        protected override bool TryParseId(string args, out string id)
        {
            id = "rain";
            return true;
        }
    }
}
