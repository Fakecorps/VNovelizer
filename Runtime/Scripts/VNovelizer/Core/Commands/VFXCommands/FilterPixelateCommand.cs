using VNovelizer.Core.Commands.Meta;

namespace VNovelizer.Core.Commands
{
    /// <summary>
    /// 像素化滤镜命令：filter_pixelate([pixelCount[, duration]])
    ///
    /// <para>只作用于剧场画面（对话框等 UI 豁免）；重复开启幂等（淡入到满权重并刷新参数）；
    /// 持久视觉状态，持续到 unsetfilter(pixelate) / unsetfilter(all) / 剧场清场。
    /// pixelCount = 横向分块数（分辨率无关，越小越马赛克）。</para>
    /// </summary>
    [VNCommandMeta(VNCommandCategory.VFX,
        "开启像素化滤镜（只作用于剧场画面）；pixelCount 调节横向分块数（越小越马赛克）")]
    public class FilterPixelateCommand : FilterCommandBase
    {
        [VNParam(0, "pixelCount", VNParamType.Int, Min = 2f, Max = 512f, Default = "96", Optional = true,
            Description = "横向分块数（2~512；缺省 = 预设默认 96，分辨率无关）")]
        [VNParam(1, "duration", VNParamType.Float, Min = 0f, Max = 10f, Optional = true,
            Description = "渐变秒数（缺省 = 预设默认时长 0.5；0 = 立即生效）")]
        public override string CommandName { get { return "filter_pixelate"; } }

        protected override string FilterId { get { return "pixelate"; } }

        protected override FilterParamSpec[] GetParamSpecs()
        {
            return new[] { new FilterParamSpec("_PixelCount", 96f) };
        }
    }
}
