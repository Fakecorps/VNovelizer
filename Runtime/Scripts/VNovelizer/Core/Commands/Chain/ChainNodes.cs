using System.Collections.Generic;

namespace VNovelizer.Core.Commands.Chain
{
    /// <summary>
    /// 命令链 AST 节点抽象基类。
    /// 命令链语法：& 并行、-> 串行、[] 分组。
    /// 解析结果是一棵 fork-join 树，而非线性序列。
    /// </summary>
    public abstract class ChainNode
    {
        /// <summary>源字符串中的偏移（错误定位）</summary>
        public int Position;
    }

    /// <summary>
    /// 串行链节点：n 元平坦结构，子节点逐个等待执行（上一项完成才执行下一项）。
    /// 由 "->" 连接生成。
    /// </summary>
    public class SeqNode : ChainNode
    {
        public List<ChainNode> Children = new List<ChainNode>();
    }

    /// <summary>
    /// 并行组节点：n 元平坦结构，子节点同时启动，全部完成后才视为本节点完成（fork-join）。
    /// 由 "&" 连接生成。
    /// </summary>
    public class ParNode : ChainNode
    {
        public List<ChainNode> Children = new List<ChainNode>();
    }

    /// <summary>
    /// 命令叶子节点：复用现有 VNCommand 体系（CommandManager.ExecuteSingleCommandAsync）。
    /// </summary>
    public class CommandNode : ChainNode
    {
        /// <summary>命令名（如 showChar）</summary>
        public string Name;

        /// <summary>原始参数串（括号内内容，未拆分）</summary>
        public string Args;
    }

    /// <summary>
    /// choice 命令的一个选项：描述文字 + 点击后执行的命令链（可为 null = 空链，
    /// 点击后直接进入下一行）。
    /// </summary>
    public class ChoiceOption
    {
        /// <summary>选项按钮文字（本地化 @loc:key 在运行时解析）</summary>
        public string Text;

        /// <summary>选项命令链 AST（支持完整链语法，允许嵌套 ChoiceNode；null = 空链）</summary>
        public ChainNode Chain;
    }

    /// <summary>
    /// choice 命令节点（R11 重构）：对应文本 <c>choice{desc1, chain1, desc2, chain2, ...}</c>。
    ///
    /// <para>
    /// 与 CommandNode 的差异：选项链不是主链的一部分——玩家点击某个选项后，
    /// 仅该选项的链被 <see cref="ChainExecutor"/> 独立执行。Simulate 预演时
    /// 选项链不展开（未选择 = 不产生状态）。
    /// </para>
    /// </summary>
    public class ChoiceNode : ChainNode
    {
        public List<ChoiceOption> Options = new List<ChoiceOption>();
    }

    /// <summary>
    /// 命令链解析/词法错误。
    /// </summary>
    public struct ChainError
    {
        public string Message;
        public int Position;

        public ChainError(string message, int position)
        {
            Message = message;
            Position = position;
        }

        public override string ToString() => $"位置 {Position}: {Message}";
    }

    /// <summary>
    /// 命令链解析结果：树根 + 全部错误 + 警告 + 是否使用链式语法。
    /// </summary>
    public class ChainParseResult
    {
        /// <summary>解析树根（可为 null，如空串或致命错误）</summary>
        public ChainNode Root;

        /// <summary>收集到的全部错误（不遇到第一个就停）</summary>
        public List<ChainError> Errors = new List<ChainError>();

        /// <summary>语义警告（不阻断执行，如流程命令不在链尾）</summary>
        public List<ChainError> Warnings = new List<ChainError>();

        /// <summary>命令串中是否检测到 "->" 或 "["（决定是否启用链式语义）</summary>
        public bool UsesChainSyntax;

        public bool Success => Errors.Count == 0;
    }
}
