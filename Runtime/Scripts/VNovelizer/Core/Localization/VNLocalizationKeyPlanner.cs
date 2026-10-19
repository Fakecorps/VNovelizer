namespace VNovelizer.Core.Localization
{
    /// <summary>
    /// 【本地化重构 P1】key 规划的**单一实现源**（Editor/Runtime 禁止各写一套）。
    ///
    /// <para>Key 体系（设计文档 Docs/VNLocalizationRefactoring.md §2）：</para>
    /// <list type="bullet">
    /// <item>剧情文本：<c>{script}.{lineID}.text</c></item>
    /// <item>说话人：<c>{script}.{lineID}.speakername</c></item>
    /// <item>命令文本：<c>{script}.{lineID}.command.{name}.{同名序号}</c></item>
    /// <item>choice 选项：<c>{script}.{lineID}.command.choice.{i}.option.{j}</c></item>
    /// </list>
    ///
    /// <para>
    /// 模板生成器（Editor 侧翻译表生成）与运行时查询（VNLocalizationService）都调用这里，
    /// 保证"生成的 key"与"查询的 key"永远一致。
    /// </para>
    /// </summary>
    public static class VNLocalizationKeyPlanner
    {
        /// <summary>剧情正文 key。</summary>
        public static string TextKey(string scriptName, string lineId) =>
            $"{scriptName}.{lineId}.text";

        /// <summary>说话人 key。</summary>
        public static string SpeakerKey(string scriptName, string lineId) =>
            $"{scriptName}.{lineId}.speakername";

        /// <summary>命令文本 key（<paramref name="ordinal"/> = 行内同名命令序号，0-based）。</summary>
        public static string CommandTextKey(string scriptName, string lineId, string commandName, int ordinal) =>
            $"{scriptName}.{lineId}.command.{commandName}.{ordinal}";

        /// <summary>choice 选项 key（<paramref name="choiceOrdinal"/> = 行内顶层 choice 同名序号，
        /// <paramref name="optionIndex"/> = 块语法展开后的选项序号，0-based）。</summary>
        public static string ChoiceOptionKey(string scriptName, string lineId, int choiceOrdinal, int optionIndex) =>
            $"{scriptName}.{lineId}.command.choice.{choiceOrdinal}.option.{optionIndex}";

        /// <summary>
        /// choice 选项链内命令文本 key（Fix-ChoiceChainKey 2026-10-19）。
        /// 前缀与 <see cref="ChoiceOptionKey"/> 对齐，再挂 <c>.command.{name}.{链内序号}</c>——
        /// 不同选项链的同名命令 key 天然互斥，杜绝"三条选项链各自独立计数 → 同名命令同 key"的碰撞
        /// （旧 key 规则导致聚合器 key 冲突、译文互相覆盖）。
        /// <paramref name="ordinal"/> = 链内同名命令序号（0-based，每条选项链独立计数）。
        /// </summary>
        public static string ChoiceChainCommandKey(string scriptName, string lineId,
            int choiceOrdinal, int optionIndex, string commandName, int ordinal) =>
            $"{scriptName}.{lineId}.command.choice.{choiceOrdinal}.option.{optionIndex}.command.{commandName}.{ordinal}";
    }
}
