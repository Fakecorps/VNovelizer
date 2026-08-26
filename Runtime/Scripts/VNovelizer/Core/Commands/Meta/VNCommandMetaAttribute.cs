using System;

namespace VNovelizer.Core.Commands.Meta
{
    /// <summary>
    /// 标注在 <see cref="VNCommand"/> 子类上，声明该命令的节点化元数据
    /// （分类、描述、参数分隔符等图编辑器需要而反射问不出来的信息）。
    ///
    /// <para>
    /// <b>设计契约</b>：本特性位于 Runtime（与命令实现同位），而非 Editor 侧手写注册表。
    /// 理由：① 单一信息源，签名不会与实现漂移；② <b>第三方自定义命令加个特性即自动
    /// 进入图编辑器</b>——`CommandManager` 反射注册用户命令是本插件的核心扩展点，
    /// 若元数据放在插件的 Editor 注册表里，用户命令永远无法节点化。
    /// </para>
    ///
    /// <para>
    /// <b>可反射推导的信息绝不在此声明</b>：是否异步（是否 override <c>ExecuteAsync</c>）、
    /// 是否实现 <c>Simulate</c>、是否流程命令（查 <c>ChainParser.IsFlowCommand</c>）
    /// 全部由反射/查询获得，避免重复声明造成不一致。
    /// </para>
    ///
    /// <para>
    /// <b>未标注本特性的命令不会被排斥</b>：图编辑器将其渲染为「通用节点」
    /// （单行原始参数文本框，可连线、可拖拽、可序列化）。这是永久兼容层而非过渡方案，
    /// 保证任何人写的任何命令都能上图。
    /// </para>
    ///
    /// <example>
    /// <code>
    /// [VNCommandMeta(VNCommandCategory.Performance, "屏幕 / 角色 / 对话框震动")]
    /// public class ShakeCommand : VNCommand { ... }
    /// </code>
    /// </example>
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class VNCommandMetaAttribute : Attribute
    {
        /// <summary>命令分类（决定面板分组与节点色带）</summary>
        public VNCommandCategory Category { get; }

        /// <summary>一句话描述（命令面板悬停提示 / Inspector 标题下方说明）</summary>
        public string Description { get; }

        /// <summary>
        /// 参数分隔符，默认逗号。
        /// 少数命令使用特殊分隔符（如 <c>choice</c> 用 '|' 分隔显示文本与命令），
        /// 图编辑器按此拆分/重拼参数。
        /// </summary>
        public char ArgSeparator { get; set; } = ',';

        /// <summary>
        /// 声明本命令的参数为**可变长**（如 <c>choice</c> 的多选项、<c>config</c> 的键值对）。
        /// 为 true 时图编辑器在固定参数表单之外额外提供"追加参数"入口。
        /// </summary>
        public bool VariadicArgs { get; set; }

        /// <summary>
        /// 尚未实现 / 计划中的命令：面板灰显、校验器提示"该命令暂未实现"。
        /// 用于占位声明（避免作者写出运行时不存在的命令）。
        /// </summary>
        public bool Planned { get; set; }

        public VNCommandMetaAttribute(VNCommandCategory category, string description = null)
        {
            Category = category;
            Description = description ?? "";
        }
    }
}
