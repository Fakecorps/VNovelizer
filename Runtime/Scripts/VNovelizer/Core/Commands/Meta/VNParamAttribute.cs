using System;

namespace VNovelizer.Core.Commands.Meta
{
    /// <summary>
    /// 标注在 <see cref="VNCommand"/> 子类上，声明其**第 <see cref="Index"/> 个参数**的签名。
    /// 同一个类上可标注多个（每个参数一条），图编辑器按 <see cref="Index"/> 排序生成表单。
    ///
    /// <para>
    /// 标注位置约定：统一标在 <c>CommandName</c> 属性上（该属性是每个命令必然存在的成员，
    /// 语义上代表"这个命令"，便于集中阅读）。标在类上亦可，读取器两处都扫。
    /// </para>
    ///
    /// <example>
    /// <code>
    /// [VNParam(0, "target", VNParamType.Enum, Options = "screen|dialogue|L|ML|M|MR|R",
    ///          Description = "震动目标")]
    /// [VNParam(1, "duration", VNParamType.Float, Min = 0f, Max = 10f, Default = "0.5")]
    /// [VNParam(2, "strength", VNParamType.Float, Min = 0f, Max = 100f, Default = "10", Optional = true)]
    /// public override string CommandName => "shake";
    /// </code>
    /// </example>
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Property,
        Inherited = false, AllowMultiple = true)]
    public sealed class VNParamAttribute : Attribute
    {
        /// <summary>参数位置（从 0 开始，与命令内部 Split 后的下标一致）</summary>
        public int Index { get; }

        /// <summary>参数名（表单标签 / 节点参数 chip 的键名，非运行时标识）</summary>
        public string Name { get; }

        /// <summary>值类型（决定表单控件与候选来源，见 <see cref="VNParamType"/>）</summary>
        public VNParamType Type { get; }

        /// <summary>参数说明（表单悬停提示）</summary>
        public string Description { get; set; } = "";

        /// <summary>
        /// <see cref="VNParamType.Enum"/> 的候选值，以 '|' 分隔（如 "screen|dialogue|L|M|R"）。
        /// 动态取值域（角色/背景/BGM 等）不要写在这里——用对应的 <see cref="VNParamType"/> 表达。
        /// </summary>
        public string Options { get; set; }

        /// <summary>数值下限（仅 Int / Float 有效，用于滑块与越界校验）</summary>
        public float Min { get; set; } = float.NaN;

        /// <summary>数值上限（仅 Int / Float 有效）</summary>
        public float Max { get; set; } = float.NaN;

        /// <summary>
        /// 默认值的**文本形式**（与写入 Command 列的字面量一致）。
        /// 图编辑器新建节点时预填；命令实现中的默认值应与此保持一致。
        /// </summary>
        public string Default { get; set; }

        /// <summary>可省略参数（命令实现中有默认值兜底）</summary>
        public bool Optional { get; set; }

        /// <summary>
        /// 该参数支持**隐式绑定**：留空时引用本行数据列（仅系统命令族使用）。
        /// 为 true 时必须同时指定 <see cref="BoundColumn"/>。
        /// </summary>
        public bool ImplicitBinding { get; set; }

        /// <summary>
        /// 隐式绑定引用的数据列名（如 "Text" / "Background" / "BGM"）。
        /// 图编辑器以 📎 徽章显示"引用：XX 列"，点击可跳转表格对应单元格。
        /// </summary>
        public string BoundColumn { get; set; }

        /// <summary>
        /// 禁止改为内联值（只能引用数据列）。
        /// <c>showDialogue</c> / <c>showSpeaker</c> 的文本参数为 true——
        /// 由此 <c>text.{lineID}</c> / <c>speaker.{lineID}</c> 本地化键**结构性不可能失效**。
        /// </summary>
        public bool InlineForbidden { get; set; }

        public VNParamAttribute(int index, string name, VNParamType type)
        {
            Index = index;
            Name = name;
            Type = type;
        }

        /// <summary>是否声明了数值范围（Min/Max 均有效）</summary>
        public bool HasRange => !float.IsNaN(Min) && !float.IsNaN(Max);

        /// <summary>枚举候选值数组（未声明时返回空数组）</summary>
        public string[] GetOptions()
        {
            if (string.IsNullOrEmpty(Options)) return Array.Empty<string>();
            return Options.Split('|');
        }
    }
}
