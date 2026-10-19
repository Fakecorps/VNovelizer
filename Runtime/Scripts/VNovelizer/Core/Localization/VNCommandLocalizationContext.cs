using System.Collections.Generic;

namespace VNovelizer.Core.Localization
{
    /// <summary>
    /// 【本地化重构 P1】行内命令同名序号上下文。
    ///
    /// <para>运行时链路：</para>
    /// <list type="number">
    /// <item><see cref="BeginLine"/>：VNManager 开始执行某行的命令串时调用（进入段 / 出口段 / choice 选项链）。</item>
    /// <item><see cref="PushCommand"/> / <see cref="PopCommand"/>：CommandManager 在执行每个命令前后调用
    ///     （仅对"带 LocalizableText 参数"的命令计数，见 CommandManager.HasLocalizableTextParam）。</item>
    /// <item><see cref="EnterChoice"/> / <see cref="ExitChoice"/>：ChainExecutor 执行 choice 节点时调用
    ///     （仅顶层 choice 计数；嵌套 choice 不计数、不生成自动 key，文本只走 @loc:）。</item>
    /// <item>命令实现内（Execute 同步段）用 <see cref="TryGetCurrentOrdinal"/> 取自身同名序号，
    ///     经 VNLocalizationService 查自动 key 译文。</item>
    /// </list>
    ///
    /// <para>
    /// 一致性契约：这里的"同名序号推进规则"必须与模板生成器的静态枚举
    /// （Editor 侧解析 Command 列生成翻译表）逐节点一致，因此两者共用
    /// <see cref="VNLocalizationKeyPlanner"/> 拼 key，序号语义在本类注释与
    /// VNLocalizationRefactoring.md §2 中定义。设计上序号只按"执行顺序推进"，
    /// 而 ChainExecutor 的 DFS 执行序 = 文本序 = 模板生成器的枚举序。
    /// </para>
    ///
    /// <para>
    /// 栈语义说明：Push/Pop 配对维护一个"当前命令"栈，命令执行期间栈顶即自身。
    /// 并行执行（& / []）下的交错仅发生在 yield 之间，而本地化查询发生在命令
    /// Execute 的同步段（Push 与 Pop 之间无 yield），因此同步命令不受并行影响；
    /// 带文本参数的自定义异步命令若写进并行组，序号可能错乱——已知边界，接受。
    /// </para>
    /// </summary>
    public static class VNCommandLocalizationContext
    {
        /// <summary>当前行的剧本名；未 BeginLine（无行上下文）时为 null。</summary>
        public static string ScriptName { get; private set; }

        /// <summary>当前行的行 ID。</summary>
        public static string LineId { get; private set; }

        // 同名计数器：commandName(小写) -> 已推进次数
        private static readonly Dictionary<string, int> _counters = new Dictionary<string, int>();

        // 执行栈：栈顶 = 正在执行（且有本地化文本参数）的命令
        private static readonly List<(string cmdName, int ordinal)> _stack = new List<(string cmdName, int ordinal)>();

        // choice 嵌套深度：只有 depth==0（顶层）的 choice 才计数
        private static int _choiceDepth;

        // 选项链执行抑制：choice 选项链执行时（ExecuteChoiceChainCoroutine）置 true，
        // 链内嵌套 choice 不计数（其选项文本只走 @loc:）——否则嵌套 choice 的
        // 序号与顶层 choice 在重置后的计数器里同号，key 空间碰撞。
        private static bool _suppressChoiceAutoKey;

        // 【Fix-ChoiceChainKey】选项链执行上下文：
        // _inChoiceChain = 正在执行选项链（含嵌套链）；
        // _choiceOrdinal / _choiceOptionIndex = 顶层 choice 的序号与选项索引（决定链内命令 key）；
        // 嵌套链执行时 _choiceOptionIndex = -1 → 链内命令不查自动 key（仅 @loc:）。
        private static bool _inChoiceChain;
        private static int _choiceOrdinal = -1;
        private static int _choiceOptionIndex = -1;

        /// <summary>行上下文是否有效。</summary>
        public static bool HasLine => ScriptName != null;

        /// <summary>
        /// 开始执行某行的命令串（幂等：重复调用重置本行计数）。
        /// </summary>
        public static void BeginLine(string scriptName, string lineId)
        {
            ScriptName = scriptName;
            LineId = lineId;
            _counters.Clear();
            _stack.Clear();
            _choiceDepth = 0;
            // 注意：_suppressChoiceAutoKey 是执行路径标志，不随行上下文重置
        }

        /// <summary>
        /// 【本地化 P1】choice 选项链执行期间置 true：链内嵌套 choice 不参与自动 key
        /// （仅 @loc:）。由 VNManager.ExecuteChoiceChainCoroutine 设置并在 finally 复位。
        /// </summary>
        public static void SetChoiceChainExecution(bool suppress)
        {
            _suppressChoiceAutoKey = suppress;
        }

        /// <summary>清除行上下文（不再处于任何行的命令执行中）。</summary>
        public static void Clear()
        {
            ScriptName = null;
            LineId = null;
            _counters.Clear();
            _stack.Clear();
            _choiceDepth = 0;
            _suppressChoiceAutoKey = false;
            _inChoiceChain = false;
            _choiceOrdinal = -1;
            _choiceOptionIndex = -1;
        }

        /// <summary>
        /// 【Fix-ChoiceChainKey】进入选项链执行上下文（由 VNManager.ExecuteChoiceChainCoroutine
        /// 在链执行前调用，finally 配对 <see cref="EndChoiceChain"/>）。
        /// 顶层链（<paramref name="topLevelChain"/> = true）：链内命令按
        /// {script}.{line}.command.choice.{ordinal}.option.{index}.command.* 查自动 key；
        /// 嵌套链（false）：链内命令不查自动 key（仅 @loc:，与嵌套 choice 选项文本一致）。
        /// </summary>
        public static void BeginChoiceChain(bool topLevelChain, int choiceOrdinal, int optionIndex)
        {
            _inChoiceChain = true;
            _choiceOrdinal = choiceOrdinal;
            _choiceOptionIndex = topLevelChain ? optionIndex : -1;
        }

        /// <summary>退出选项链执行上下文（与 <see cref="BeginChoiceChain"/> 配对）。</summary>
        public static void EndChoiceChain()
        {
            _inChoiceChain = false;
            _choiceOrdinal = -1;
            _choiceOptionIndex = -1;
        }

        /// <summary>
        /// 命令执行前推进同名序号并压栈（仅对带本地化文本参数的命令调用）。
        /// 返回是否真正压栈（调用方据此在 finally 中配对 Pop）。
        /// </summary>
        public static bool PushCommand(string commandName)
        {
            if (ScriptName == null || string.IsNullOrEmpty(commandName))
                return false;

            string key = commandName.ToLower();
            _counters.TryGetValue(key, out int ordinal);
            _counters[key] = ordinal + 1;
            _stack.Add((key, ordinal));
            return true;
        }

        /// <summary>命令执行结束出栈（与 <see cref="PushCommand"/> 配对）。</summary>
        public static void PopCommand()
        {
            if (_stack.Count > 0)
                _stack.RemoveAt(_stack.Count - 1);
        }

        /// <summary>
        /// choice 节点执行前调用。仅"顶层（<see cref="_choiceDepth"/>==0）+ 有行上下文 +
        /// 非选项链抑制"时压栈计数；嵌套 choice / 选项链内的 choice 返回 -1
        /// （其选项文本不生成自动 key，只走 @loc:）。
        /// 返回压栈的命令序号（= 行内顶层 choice 同名序号，供选项链 key 使用）；-1 = 未压栈
        /// （调用方据此在 finally 中配对 PopCommand）。
        /// </summary>
        public static int EnterChoice()
        {
            bool topLevel = _choiceDepth == 0;
            _choiceDepth++;

            if (topLevel && ScriptName != null && !_suppressChoiceAutoKey)
            {
                if (PushCommand("choice"))
                    return _stack[_stack.Count - 1].ordinal;
            }
            return -1;
        }

        /// <summary>choice 节点执行结束（与 <see cref="EnterChoice"/> 配对，只负责深度递减）。</summary>
        public static void ExitChoice()
        {
            if (_choiceDepth > 0)
                _choiceDepth--;
        }

        /// <summary>
        /// 命令实现内查询"我正在执行的第几个同名命令"。
        /// 栈顶命令名匹配才返回 true（防嵌套执行串线）。
        /// </summary>
        public static bool TryGetCurrentOrdinal(string commandName, out int ordinal)
        {
            ordinal = -1;
            if (ScriptName == null || _stack.Count == 0 || string.IsNullOrEmpty(commandName))
                return false;

            var top = _stack[_stack.Count - 1];
            if (top.cmdName != commandName.ToLower())
                return false;

            ordinal = top.ordinal;
            return true;
        }

        /// <summary>
        /// 【Fix-ChoiceChainKey】命令文本本地化的统一查询入口（命令实现内调用）：
        /// <list type="bullet">
        /// <item>非选项链执行：查行内自动 key（{script}.{line}.command.{name}.{序号}）；</item>
        /// <item>顶层选项链执行：查链 key（...command.choice.{i}.option.{j}.command.{name}.{序号}），
        ///     跨链唯一、无碰撞；</item>
        /// <item>嵌套选项链执行：不查自动 key（返回 false，文本只走 @loc:），
        ///     避免嵌套链命令误命中行内/顶层链 key。</item>
        /// </list>
        /// </summary>
        public static bool TryGetCommandLocalized(string commandName, out string localized)
        {
            localized = null;
            if (!TryGetCurrentOrdinal(commandName, out int ordinal))
                return false;

            if (!_inChoiceChain)
                return VNLocalizationService.TryGetCommandText(
                    ScriptName, LineId, commandName, ordinal, out localized);

            if (_choiceOptionIndex < 0)
                return false; // 嵌套链：仅 @loc:

            return VNLocalizationService.TryGetChoiceChainCommandText(
                ScriptName, LineId, _choiceOrdinal, _choiceOptionIndex, commandName, ordinal, out localized);
        }
    }
}
