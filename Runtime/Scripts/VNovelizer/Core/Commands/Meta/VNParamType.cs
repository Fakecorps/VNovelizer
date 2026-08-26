using System;

namespace VNovelizer.Core.Commands.Meta
{
    /// <summary>
    /// 命令参数的值类型。
    ///
    /// 设计要点：**动态取值域用类型表达，而非在特性里写死候选列表**。
    /// 例如 <see cref="CharacterId"/> 不需要声明"有哪些角色"——图编辑器按类型去
    /// CharacterResManager 拉实时列表。这样纯静态特性也能提供动态下拉，
    /// 无需第二套接口，且新增角色/背景后无需改任何特性标注。
    /// </summary>
    public enum VNParamType
    {
        /// <summary>自由文本</summary>
        String = 0,

        /// <summary>整数（可用 Min/Max 约束）</summary>
        Int,

        /// <summary>浮点数（可用 Min/Max 约束，编辑器渲染为滑块）</summary>
        Float,

        /// <summary>布尔（true/false）</summary>
        Bool,

        /// <summary>固定枚举，候选值由 <see cref="VNParamAttribute.Options"/> 以 '|' 分隔声明</summary>
        Enum,

        // ---- 以下为动态取值域：候选由编辑器按类型实时查询 ----

        /// <summary>角色 ID（候选来自全部 CharacterProfile）</summary>
        CharacterId,

        /// <summary>立绘分组（候选依赖已选定的角色）</summary>
        CharacterGroup,

        /// <summary>表情（候选依赖已选定的角色 + 分组）</summary>
        Emotion,

        /// <summary>立绘引用全串 `角色#分组#表情`</summary>
        CharacterRef,

        /// <summary>槽位代码 L / ML / M / MR / R</summary>
        SlotCode,

        /// <summary>背景名（候选来自资源注册表）</summary>
        BackgroundName,

        /// <summary>BGM 名（候选来自资源注册表）</summary>
        BgmName,

        /// <summary>音效名</summary>
        SfxName,

        /// <summary>语音名</summary>
        VoiceName,

        /// <summary>视频名</summary>
        VideoName,

        /// <summary>动画名</summary>
        AnimName,

        /// <summary>粒子特效名</summary>
        ParticleName,

        /// <summary>行 ID（候选来自当前剧本全部行）</summary>
        LineId,

        /// <summary>剧本名（候选来自 CSV 目录）</summary>
        ScriptName,

        /// <summary>场景名（候选来自 Build Settings）</summary>
        SceneName,

        /// <summary>标志名（候选来自 Flag 编辑器登记项）</summary>
        FlagName,

        /// <summary>条件表达式（如 `Favor&gt;=60`、`!Met_Amy`，由 ConditionParser 解析）</summary>
        Condition,

        /// <summary>颜色（编辑器渲染取色器）</summary>
        Color,

        /// <summary>嵌套命令串（如 choice 的选项命令、playvideo 的结束后命令）</summary>
        CommandString,
    }

    /// <summary>
    /// 命令分类：决定图编辑器命令面板的分组与节点色带。
    /// </summary>
    public enum VNCommandCategory
    {
        /// <summary>系统命令：由数据列隐式驱动，构成默认演出模板（蓝色带）</summary>
        System = 0,

        /// <summary>演出命令：动画 / 特效 / 文本样式（紫色带）</summary>
        Performance,

        /// <summary>流程命令：改变行索引 / 剧本 / 场景，仅可置于链尾（橙色带）</summary>
        Flow,

        /// <summary>逻辑与变量：标志读写、解锁</summary>
        Logic,

        /// <summary>音频</summary>
        Audio,

        /// <summary>其他 / 未分类</summary>
        Misc,
    }
}
