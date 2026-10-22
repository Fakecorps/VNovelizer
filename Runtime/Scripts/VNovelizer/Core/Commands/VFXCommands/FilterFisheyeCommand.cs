using VNovelizer.Core.Commands.Meta;

namespace VNovelizer.Core.Commands
{
    /// <summary>
    /// 鱼眼滤镜命令：filter_fisheye([strength[, duration]])
    ///
    /// <para>只作用于剧场画面（对话框等 UI 豁免）；重复开启幂等（淡入到满权重并刷新参数）；
    /// 持久视觉状态，持续到 unsetfilter(fisheye) / unsetfilter(all) / 剧场清场。</para>
    /// </summary>
    [VNCommandMeta(VNCommandCategory.VFX,
        "开启鱼眼滤镜（桶形畸变，只作用于剧场画面）；strength 调节畸变强度")]
    public class FilterFisheyeCommand : FilterCommandBase
    {
        [VNParam(0, "strength", VNParamType.Float, Min = 0f, Max = 2f, Default = "0.6", Optional = true,
            Description = "畸变强度（0~2；缺省 = 预设默认 0.6）")]
        [VNParam(1, "duration", VNParamType.Float, Min = 0f, Max = 10f, Optional = true,
            Description = "渐变秒数（缺省 = 预设默认时长 0.5；0 = 立即生效）")]
        public override string CommandName { get { return "filter_fisheye"; } }

        protected override string FilterId { get { return "fisheye"; } }

        protected override FilterParamSpec[] GetParamSpecs()
        {
            return new[] { new FilterParamSpec("_Strength", 0.6f) };
        }
    }
}
