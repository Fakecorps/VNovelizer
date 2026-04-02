using System;

#if VN_LOCALIZATION
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;
#endif

namespace VNovelizer.Core.Localization
{
    /// <summary>
    /// 剧情本地化读取服务（共享 Collection）。
    /// 设计目标：缺失/空翻译不触发 Unity Localization 的 "No translation found" 日志。
    /// </summary>
    public static class VNLocalizationService
    {
        public static string GetCollectionNameForScript(string scriptName)
        {
            string prefix = "VNScript_";
            if (VNProjectConfig.Instance != null && !string.IsNullOrEmpty(VNProjectConfig.Instance.ScriptTablePrefix))
            {
                prefix = VNProjectConfig.Instance.ScriptTablePrefix;
            }
            return prefix + scriptName;
        }

        public static bool IsEnabled()
        {
            return VNProjectConfig.Instance != null && VNProjectConfig.Instance.EnableLocalization;
        }

        private static bool TryGetEntryValue(string scriptName, string entryKey, out string localizedValue)
        {
            localizedValue = null;

            if (!IsEnabled())
                return false;

#if VN_LOCALIZATION
            var config = VNProjectConfig.Instance;
            if (config == null)
                return false;
            if (string.IsNullOrEmpty(scriptName))
                return false;

            var locale = LocalizationSettings.SelectedLocale;
            if (locale == null)
                return false;

            var collectionName = GetCollectionNameForScript(scriptName);

            // TableReference 按剧本动态定位 collection
            var table = LocalizationSettings.StringDatabase.GetTable(collectionName, locale);
            if (table == null)
                return false;

            var entry = table.GetEntry(entryKey);
            if (entry == null)
                return false;

            var raw = entry.Value;
            if (string.IsNullOrEmpty(raw))
                return false;

            localizedValue = raw;
            return true;
#else
            return false;
#endif
        }

        public static bool TryGetText(string scriptName, string lineID, out string localized)
        {
            localized = null;
            if (string.IsNullOrEmpty(scriptName) || string.IsNullOrEmpty(lineID))
                return false;

            string key = $"text.{lineID}";
            return TryGetEntryValue(scriptName, key, out localized);
        }

        public static bool TryGetSpeaker(string scriptName, string lineID, out string localized)
        {
            localized = null;
            if (string.IsNullOrEmpty(scriptName) || string.IsNullOrEmpty(lineID))
                return false;

            string key = $"speaker.{lineID}";
            return TryGetEntryValue(scriptName, key, out localized);
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

        /// <summary>
        /// 读取 Choice 参数等 "完整 key"：entryKey 直接等于 fullKey。
        /// </summary>
        public static bool TryGetByFullKey(string scriptName, string fullKey, out string localized)
        {
            localized = null;
            if (string.IsNullOrEmpty(fullKey))
                return false;

            return TryGetEntryValue(scriptName, fullKey, out localized);
        }

        public static string GetByFullKey(string scriptName, string fullKey, string fallback)
        {
            if (TryGetByFullKey(scriptName, fullKey, out var localized))
                return localized;
            return fallback;
        }
    }
}

