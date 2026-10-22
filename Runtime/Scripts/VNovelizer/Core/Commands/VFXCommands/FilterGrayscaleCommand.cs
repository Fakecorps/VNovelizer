using VNovelizer.Core.Commands.Meta;

namespace VNovelizer.Core.Commands
{
    /// <summary>
    /// 黑白滤镜命令：filter_grayscale([amount[, duration]])
    ///
    /// <para>只作用于剧场画面（对话框等 UI 豁免）；重复开启幂等（淡入到满权重并刷新参数）；
    /// 持久视觉状态，持续到 unsetfilter(grayscale) / unsetfilter(all) / 剧场清场。</para>
    /// </summary>
    [VNCommandMeta(VNCommandCategory.VFX,
        "开启黑白滤镜（只作用于剧场画面）；amount 调节去色量（1 = 完全黑白）")]
    public class FilterGrayscaleCommand : FilterCommandBase
    {
        [VNParam(0, "amount", VNParamType.Float, Min = 0f, Max = 1f, Default = "1", Optional = true,
            Description = "去色量（0~1；缺省 = 预设默认 1，完全黑白）")]
        [VNParam(1, "duration", VNParamType.Float, Min = 0f, Max = 10f, Optional = true,
            Description = "渐变秒数（缺省 = 预设默认时长 0.5；0 = 立即生效）")]
        public override string CommandName { get { return "filter_grayscale"; } }

        protected override string FilterId { get { return "grayscale"; } }

        protected override FilterParamSpec[] GetParamSpecs()
        {
            return new[] { new FilterParamSpec("_Amount", 1f) };
        }
    }
}
