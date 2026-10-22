using VNovelizer.Core.Commands.Meta;

namespace VNovelizer.Core.Commands
{
    /// <summary>
    /// 暗角滤镜命令：filter_vignette([strength[, radius[, softness[, duration]]]])
    ///
    /// <para>只作用于剧场画面（对话框等 UI 豁免）；重复开启幂等（淡入到满权重并刷新参数）；
    /// 持久视觉状态，持续到 unsetfilter(vignette) / unsetfilter(all) / 剧场清场。
    /// 暗角颜色恒用预设默认（黑色），不开放为命令参数。</para>
    /// </summary>
    [VNCommandMeta(VNCommandCategory.VFX,
        "开启暗角滤镜（只作用于剧场画面）；strength/radius/softness 分别调节暗角强度/范围/柔和度")]
    public class FilterVignetteCommand : FilterCommandBase
    {
        [VNParam(0, "strength", VNParamType.Float, Min = 0f, Max = 1f, Default = "0.8", Optional = true,
            Description = "暗角强度（0~1；缺省 = 预设默认 0.8）")]
        [VNParam(1, "radius", VNParamType.Float, Min = 0f, Max = 1.5f, Default = "0.85", Optional = true,
            Description = "暗角起点半径（0~1.5；缺省 = 预设默认 0.85）")]
        [VNParam(2, "softness", VNParamType.Float, Min = 0.01f, Max = 1f, Default = "0.4", Optional = true,
            Description = "过渡柔和度（0.01~1；缺省 = 预设默认 0.4）")]
        [VNParam(3, "duration", VNParamType.Float, Min = 0f, Max = 10f, Optional = true,
            Description = "渐变秒数（缺省 = 预设默认时长 0.5；0 = 立即生效）")]
        public override string CommandName { get { return "filter_vignette"; } }

        protected override string FilterId { get { return "vignette"; } }

        protected override FilterParamSpec[] GetParamSpecs()
        {
            return new[]
            {
                new FilterParamSpec("_Strength", 0.8f),
                new FilterParamSpec("_Radius", 0.85f),
                new FilterParamSpec("_Softness", 0.4f),
            };
        }
    }
}
