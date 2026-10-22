using VNovelizer.Core.Commands.Meta;

namespace VNovelizer.Core.Commands
{
    /// <summary>
    /// VHS 模拟滤镜命令：filter_vhs([intensity[, duration]])
    ///
    /// <para>只作用于剧场画面（对话框等 UI 豁免）；重复开启幂等（淡入到满权重并刷新参数）；
    /// 持久视觉状态，持续到 unsetfilter(vhs) / unsetfilter(all) / 剧场清场。</para>
    /// </summary>
    [VNCommandMeta(VNCommandCategory.VFX,
        "开启 VHS 模拟滤镜（行带抖动+色差+扫描线+胶片颗粒，只作用于剧场画面）；intensity 调节故障强度")]
    public class FilterVhsCommand : FilterCommandBase
    {
        [VNParam(0, "intensity", VNParamType.Float, Min = 0f, Max = 1f, Default = "0.6", Optional = true,
            Description = "故障强度（0~1；缺省 = 预设默认 0.6）")]
        [VNParam(1, "duration", VNParamType.Float, Min = 0f, Max = 10f, Optional = true,
            Description = "渐变秒数（缺省 = 预设默认时长 0.5；0 = 立即生效）")]
        public override string CommandName { get { return "filter_vhs"; } }

        protected override string FilterId { get { return "vhs"; } }

        protected override FilterParamSpec[] GetParamSpecs()
        {
            return new[] { new FilterParamSpec("_Intensity", 0.6f) };
        }
    }
}
