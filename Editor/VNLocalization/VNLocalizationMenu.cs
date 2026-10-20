using UnityEditor;

/// <summary>
/// 【菜单统一 2026-10】本地化菜单路径常量与启用状态（菜单 validate / 设置页联动共用）。
///
/// <para>菜单命名统一为「中文 (English)」；未启用剧情本地化时三项灰显
///（Unity MenuItem 无动态隐藏 API，validate 灰显为官方语义——开启本地化后立即恢复可用）。</para>
/// </summary>
public static class VNLocalizationMenu
{
    public const string Root = "VNovelizer/本地化 (Localization)";
    public const string Manager = Root + "/剧情本地化管理器 (Story Localization Manager)";
    public const string Import = Root + "/导入翻译表 (Import Translation Tables)";
    public const string Assemble = Root + "/装配 UI 本地化组件 (Assemble UI Localization)";

    /// <summary>剧情本地化是否已启用（菜单灰显判定 / 设置页联动共用）。</summary>
    public static bool Enabled()
    {
        return VNProjectConfig.TryGetInstance(out var config) && config.EnableLocalization;
    }

    /// <summary>打开剧情本地化管理器（设置页「打开管理器」按钮与菜单共用入口）。</summary>
    [MenuItem(VNLocalizationMenu.Manager)]
    public static void ShowManager()
    {
        VNLocalizationWindow.ShowWindow();
    }

    [MenuItem(VNLocalizationMenu.Manager, true)]
    public static bool ShowManagerValidate() => Enabled();

    [MenuItem(VNLocalizationMenu.Import)]
    public static void ImportNow()
    {
        VNTranslationImportPostprocessor.ImportNow();
    }

    [MenuItem(VNLocalizationMenu.Import, true)]
    public static bool ImportNowValidate() => Enabled();

    [MenuItem(VNLocalizationMenu.Assemble)]
    public static void AssembleUiLocalization()
    {
        VNUiLocalizationAssembler.Assemble();
    }

    [MenuItem(VNLocalizationMenu.Assemble, true)]
    public static bool AssembleUiLocalizationValidate() => Enabled();
}
