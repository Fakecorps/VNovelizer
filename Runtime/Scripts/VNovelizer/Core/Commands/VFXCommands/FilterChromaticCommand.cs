using VNovelizer.Core.Commands.Meta;

namespace VNovelizer.Core.Commands
{
    /// <summary>
    /// 色差滤镜命令：filter_chromatic([amount[, duration]])
    ///
    /// <para>只作用于剧场画面（对话框等 UI 豁免）；重复开启幂等（淡入到满权重并刷新参数）；
    /// 持久视觉状态，持续到 unsetfilter(chromatic) / unsetfilter(all) / 剧场清场。
    /// amount = R/B 沿「像素到画面中心」方向的径向色散量，中心无色差、边缘最强。</para>
    /// </summary>
    [VNCommandMeta(VNCommandCategory.VFX,
        "开启色差滤镜（只作用于剧场画面）；amount 调节 RGB 径向色散量")]
    public class FilterChromaticCommand : FilterCommandBase
    {
        [VNParam(0, "amount", VNParamType.Float, Min = 0f, Max = 0.05f, Default = "0.012", Optional = true,
            Description = "径向色散量（0~0.05；缺省 = 预设默认 0.012）")]
        [VNParam(1, "duration", VNParamType.Float, Min = 0f, Max = 10f, Optional = true,
            Description = "渐变秒数（缺省 = 预设默认时长 0.5；0 = 立即生效）")]
        public override string CommandName { get { return "filter_chromatic"; } }

        protected override string FilterId { get { return "chromatic"; } }

        protected override FilterParamSpec[] GetParamSpecs()
        {
            return new[] { new FilterParamSpec("_Amount", 0.012f) };
        }
    }
}
