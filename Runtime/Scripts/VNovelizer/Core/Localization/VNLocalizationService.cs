using System;

#if VN_LOCALIZATION
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;
#endif

namespace VNovelizer.Core.Localization
{
    /// <summary>
    /// 【本地化重构 P1】剧情/命令/UI 本地化读取门面（单一 Collection 方案）。
    ///
    /// <para>设计（Docs/VNLocalizationRefactoring.md）：</para>
    /// <list type="bullet">
    /// <item>所有翻译 CSV 由编辑器聚合器合并进单一 StringTableCollection <see cref="CollectionName"/>。</item>
    /// <item>key 全部全局唯一，拼装规则见 <see cref="VNLocalizationKeyPlanner"/>（唯一实现源）。</item>
    /// <item>按当前 locale 缓存 StringTable，语言切换（SelectedLocaleChanged）时失效并广播
    ///     <see cref="LocaleChanged"/> 供 UI 面板全量重刷。</item>
    /// <item>缺失翻译不回退 CSV、不输出日志——由 Unity 原生 locale fallback 链承担
    ///     （源 locale 表由聚合器自动填原文）。</item>
    /// </list>
    ///
    /// <para>宏约束：<c>#if VN_LOCALIZATION</c> 只允许出现在本文件与编辑器聚合器，
    /// 其余代码一律经由本门面（零宏）。</para>
    /// </summary>
    public static class VNLocalizationService
    {
        /// <summary>聚合后的单一 StringTableCollection 名称（运行时固定，不再按剧本动态定位）。</summary>
        public const string CollectionName = "VNLocalization";

        /// <summary>语言切换广播（SettingsPanel 切换 locale 后由 SelectedLocaleChanged 触发）。</summary>
        public static event Action LocaleChanged;

        /// <summary>是否在项目配置中开启了剧情本地化。</summary>
        public static bool IsEnabled()
        {
            return VNProjectConfig.Instance != null && VNProjectConfig.Instance.EnableLocalization;
        }

#if VN_LOCALIZATION
        private static StringTable _cachedTable;
        private static Locale _cachedLocale;

        static VNLocalizationService()
        {
            // 语言切换：清表缓存 + 广播刷新（Unity 官方事件在 SetSelectedLocale 成功后触发）
            LocalizationSettings.SelectedLocaleChanged += OnSelectedLocaleChanged;
        }

        private static void OnSelectedLocaleChanged(Locale locale)
        {
            _cachedTable = null;
            _cachedLocale = null;
            LocaleChanged?.Invoke();
        }

        private static StringTable GetTableCached()
        {
            var locale = LocalizationSettings.SelectedLocale;
            if (locale == null)
                return null;

            if (_cachedTable == null || _cachedLocale != locale)
            {
                _cachedTable = LocalizationSettings.StringDatabase.GetTable(CollectionName, locale);
                _cachedLocale = locale;
            }
            return _cachedTable;
        }

        private static bool TryGetEntryValue(string fullKey, out string localizedValue)
        {
            localizedValue = null;

            if (!IsEnabled())
                return false;

            if (string.IsNullOrEmpty(fullKey))
                return false;

            var table = GetTableCached();
            if (table == null)
                return false;

            var entry = table.GetEntry(fullKey);
            if (entry == null)
                return false;

            var raw = entry.Value;
            if (string.IsNullOrEmpty(raw))
                return false;

            localizedValue = raw;
            return true;
        }
#else
        private static bool TryGetEntryValue(string fullKey, out string localizedValue)
        {
            localizedValue = null;
            return false;
        }
#endif

        // ---------------- 剧情：文本 / 说话人 ----------------

        /// <summary>剧情正文（key = {script}.{lineID}.text）。</summary>
        public static bool TryGetText(string scriptName, string lineID, out string localized)
        {
            localized = null;
            if (string.IsNullOrEmpty(scriptName) || string.IsNullOrEmpty(lineID))
                return false;
            return TryGetEntryValue(VNLocalizationKeyPlanner.TextKey(scriptName, lineID), out localized);
        }

        /// <summary>说话人（key = {script}.{lineID}.speakername）。</summary>
        public static bool TryGetSpeaker(string scriptName, string lineID, out string localized)
        {
            localized = null;
            if (string.IsNullOrEmpty(scriptName) || string.IsNullOrEmpty(lineID))
                return false;
            return TryGetEntryValue(VNLocalizationKeyPlanner.SpeakerKey(scriptName, lineID), out localized);
        }

        public static string GetText(string scriptName, string lineID, string csvFallbackText)
        {
            if (TryGetText(scriptName, lineID, out var localized))
                return localized;
            return csvFallbackText;
        }

        public static string GetSpeaker(string scriptName, string lineID, string csvFallbackSpeaker)
        {
            if (TryGetSpeaker(scriptName, lineID, out var localized))
                return localized;
            return csvFallbackSpeaker;
        }

        // ---------------- 命令：自动 key ----------------

        /// <summary>
        /// 命令文本（key = {script}.{lineID}.command.{commandName}.{ordinal}）。
        /// <paramref name="ordinal"/> 为行内同名命令序号（0-based），由
        /// <see cref="VNCommandLocalizationContext"/> 在执行链路中推进。
        /// </summary>
        public static bool TryGetCommandText(string scriptName, string lineID, string commandName, int ordinal, out string localized)
        {
            localized = null;
            if (string.IsNullOrEmpty(scriptName) || string.IsNullOrEmpty(lineID) ||
                string.IsNullOrEmpty(commandName) || ordinal < 0)
                return false;
            return TryGetEntryValue(
                VNLocalizationKeyPlanner.CommandTextKey(scriptName, lineID, commandName, ordinal),
                out localized);
        }

        /// <summary>
        /// choice 选项文本（key = {script}.{lineID}.command.choice.{choiceOrdinal}.option.{optionIndex}）。
        /// </summary>
        public static bool TryGetChoiceOptionText(string scriptName, string lineID, int choiceOrdinal, int optionIndex, out string localized)
        {
            localized = null;
            if (string.IsNullOrEmpty(scriptName) || string.IsNullOrEmpty(lineID) ||
                choiceOrdinal < 0 || optionIndex < 0)
                return false;
            return TryGetEntryValue(
                VNLocalizationKeyPlanner.ChoiceOptionKey(scriptName, lineID, choiceOrdinal, optionIndex),
                out localized);
        }

        /// <summary>
        /// 【Fix-ChoiceChainKey】choice 选项链内命令文本
        /// （key = {script}.{lineID}.command.choice.{choiceOrdinal}.option.{optionIndex}.command.{name}.{ordinal}）。
        /// 链内同名命令序号 0-based、每条选项链独立计数（运行时链执行 BeginLine 重置，与扫描器一致）。
        /// </summary>
        public static bool TryGetChoiceChainCommandText(string scriptName, string lineID,
            int choiceOrdinal, int optionIndex, string commandName, int ordinal, out string localized)
        {
            localized = null;
            if (string.IsNullOrEmpty(scriptName) || string.IsNullOrEmpty(lineID) ||
                string.IsNullOrEmpty(commandName) ||
                choiceOrdinal < 0 || optionIndex < 0 || ordinal < 0)
                return false;
            return TryGetEntryValue(
                VNLocalizationKeyPlanner.ChoiceChainCommandKey(scriptName, lineID, choiceOrdinal, optionIndex, commandName, ordinal),
                out localized);
        }

        // ---------------- UI / 自由 key ----------------

        /// <summary>UI 文本（key = ui.*，手工清单维护）。缺失时返回 fallback。</summary>
        public static string GetUi(string key, string fallback)
        {
            if (TryGetByFullKey(key, out var localized))
                return localized;
            return fallback;
        }

        /// <summary>
        /// 读取"完整 key"（自由 key / UI key）。<paramref name="scriptName"/> 为兼容旧签名保留，
        /// 新 key 体系下 fullKey 全局唯一，不再需要剧本维度。
        /// </summary>
        public static bool TryGetByFullKey(string scriptName, string fullKey, out string localized)
        {
            return TryGetEntryValue(fullKey, out localized);
        }

        /// <summary>按完整 key 读取（无剧本维度）。</summary>
        public static bool TryGetByFullKey(string fullKey, out string localized)
        {
            return TryGetEntryValue(fullKey, out localized);
        }

        public static string GetByFullKey(string scriptName, string fullKey, string fallback)
        {
            if (TryGetByFullKey(scriptName, fullKey, out var localized))
                return localized;
            return fallback;
        }
    }
}
