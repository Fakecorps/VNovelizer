using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Metadata;
using UnityEngine.Localization.Settings;

/// <summary>
/// 【UI 本地化开箱即送】LocalizationSettings / Locale 资产自动兜底。
///
/// <para>背景（Localization 1.5.2 源码验证）：
/// 包内 UI 预制体预装了官方 <see cref="UnityEngine.Localization.Components.LocalizeStringEvent"/>，
/// 其 OnEnable → RegisterChangeHandler → <see cref="LocalizationSettings.ValidateSettingsExist"/>
/// 在工程没有 LocalizationSettings 资产（EditorBuildSettings config 缺失）时<b>抛异常</b>。
/// 该资产无法随包分发（工程级 BuildSettings 引用），因此必须由编辑器自动创建兜底。</para>
///
/// <list type="bullet">
/// <item><see cref="VNLocaleBootstrapper"/> 静态构造：编辑器加载即静默检测，
///     缺失则创建资产并注册（零弹窗零打扰，只建 Settings 资产不建 Locale——
///     Locale 缺失时官方组件静默保持原文，已源码验证安全）。</item>
/// <item><see cref="EnsureLocalesAndFallbacks"/>：导入本地化资源时调用——
///     缺失的源/目标 Locale 资产自动创建，目标 Locale 的 fallback 链自动指向源 Locale
///     （译文缺失时回退中文原文，永不显示裸 key）。</item>
/// </list>
/// </summary>
[InitializeOnLoad]
public static class VNLocaleBootstrapper
{
    /// <summary>LocalizationSettings 在 EditorBuildSettings 中的 config key（Localization 1.5.2 内部常量）。</summary>
    private const string SettingsConfigName = "com.unity.localization.settings";

    /// <summary>LocalizationSettings 资产路径（与 Unity 官方 Project Settings 创建的位置一致）。</summary>
    private const string SettingsAssetPath = "Assets/LocalizationSettings.asset";

    /// <summary>自动创建的 Locale 资产目录。</summary>
    private const string LocaleFolder = "Assets/Localization/Locales";

    static VNLocaleBootstrapper()
    {
        EnsureLocalizationSettings();
    }

    /// <summary>
    /// 确保 LocalizationSettings 资产存在并注册到 EditorBuildSettings。
    /// 返回 true = 本次创建了资产。
    /// </summary>
    public static bool EnsureLocalizationSettings()
    {
        try
        {
            if (EditorBuildSettings.TryGetConfigObject(SettingsConfigName, out LocalizationSettings _))
                return false;

            var settings = ScriptableObject.CreateInstance<LocalizationSettings>();
            string dir = Path.GetDirectoryName(SettingsAssetPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            AssetDatabase.CreateAsset(settings, SettingsAssetPath);
            EditorBuildSettings.AddConfigObject(SettingsConfigName, settings, true);
            AssetDatabase.SaveAssets();
            Debug.Log($"[VNLocaleBootstrapper] 已创建 LocalizationSettings 资产：{SettingsAssetPath}");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[VNLocaleBootstrapper] 创建 LocalizationSettings 失败：{e.Message}");
            return false;
        }
    }

    /// <summary>
    /// 确保源 + 目标 Locale 资产存在（缺失则创建），并自动搭 fallback 链（目标 → 源）。
    /// 用户已手动创建的同名 Locale 复用；已手动配置 fallback 的目标 Locale 不覆盖。
    /// </summary>
    public static void EnsureLocalesAndFallbacks(string sourceLocaleCode, IEnumerable<string> targetLocaleCodes)
    {
        if (string.IsNullOrEmpty(sourceLocaleCode))
            return;

        Locale source = EnsureLocale(sourceLocaleCode);
        if (source == null)
            return;

        if (targetLocaleCodes != null)
        {
            foreach (var code in targetLocaleCodes)
            {
                if (string.IsNullOrEmpty(code))
                    continue;
                Locale target = EnsureLocale(code);
                if (target != null)
                    EnsureFallbackChain(target, source);
            }
        }

        AssetDatabase.SaveAssets();
    }

    /// <summary>按 code 找项目 Locale（下划线/连字符归一化 + 忽略大小写，与聚合器规则一致）；缺失则创建资产。</summary>
    private static Locale EnsureLocale(string code)
    {
        string normalized = (code ?? "").Replace('_', '-');

        var locales = LocalizationEditorSettings.GetLocales();
        if (locales != null)
        {
            foreach (var l in locales)
            {
                if (l == null)
                    continue;
                if (string.Equals((l.Identifier.Code ?? "").Replace('_', '-'), normalized, StringComparison.OrdinalIgnoreCase))
                    return l;
            }
        }

        try
        {
            var locale = Locale.CreateLocale(code);
            Directory.CreateDirectory(LocaleFolder);
            string assetPath = $"{LocaleFolder}/{code}.asset";
            AssetDatabase.CreateAsset(locale, assetPath);
            LocalizationEditorSettings.AddLocale(locale);
            EditorUtility.SetDirty(locale);
            Debug.Log($"[VNLocaleBootstrapper] 已创建 Locale 资产：{assetPath}");
            return locale;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[VNLocaleBootstrapper] 创建 Locale「{code}」失败：{e.Message}");
            return null;
        }
    }

    /// <summary>目标 Locale 无 fallback 时自动指向源 Locale（链根不动）。</summary>
    private static void EnsureFallbackChain(Locale target, Locale source)
    {
        if (target == null || source == null || target == source)
            return;

        if (target.Metadata.GetMetadata<FallbackLocale>() != null)
            return; // 用户已手动配置，不覆盖

        target.Metadata.AddMetadata(new FallbackLocale(source));
        EditorUtility.SetDirty(target);
        Debug.Log($"[VNLocaleBootstrapper] 已搭 fallback 链：{target.Identifier.Code} → {source.Identifier.Code}");
    }
}
