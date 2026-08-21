/// <summary>
/// UI 预制体模板键（固定常量，运行时覆写查询与编辑器模板创建共用）。
///
/// 语义（见 Docs/VNResourceProviderRefactoring.md "UI 模板覆写"节）：
/// - 包内预制体 = 模板（向导注册进 Addressables，不复制文件）；
/// - 用户可复制模板自建变体，在 VNProjectConfig"九、UI 模板覆写"中按引用指派——
///   指派后物理位置无关（直接引用，零寻址）；
/// - 未指派 → 按键经资源服务链加载包内默认模板（引擎私有 fallback，用户无需感知）。
///
/// 键值 = 包内默认资源路径（无扩展名），同时是包内模板的资产路径后缀：
/// Packages/{包名}/Runtime/PackageDefault/{键}.prefab
/// </summary>
public static class VNUIPrefabKeys
{
    // ==================== 主面板 ====================
    public const string VNGameplayPanel    = "VNovelizerRes/VNPrefabs/UI/VNGamePlay/VNGamePlayPanel";
    public const string MainMenuPanel      = "VNovelizerRes/VNPrefabs/UI/MainMenu/MainMenuPanel";
    public const string GalleryPanel       = "VNovelizerRes/VNPrefabs/UI/Gallery/GalleryPanel";
    public const string PausePanel         = "VNovelizerRes/VNPrefabs/UI/Pause/PausePanel";
    public const string HistoryPanel       = "VNovelizerRes/VNPrefabs/UI/History/HistoryPanel";
    public const string SaveLoadPanel      = "VNovelizerRes/VNPrefabs/UI/SaveLoad/SaveLoadPanel";
    public const string SettingsPanel      = "VNovelizerRes/VNPrefabs/UI/Settings/SettingsPanel";
    public const string ChoicePanel        = "VNovelizerRes/VNPrefabs/UI/Choice/ChoicePanel";
    public const string ConfirmPanel       = "VNovelizerRes/VNPrefabs/UI/Confirm/ConfirmPanel";
    public const string LoadingProgressPanel = "VNovelizerRes/VNPrefabs/UI/Loading/LoadingProgressPanel";

    // ==================== 子项预制体 ====================
    public const string PromptItem         = "VNovelizerRes/VNPrefabs/UI/VNGamePlay/Prompt/PromptItem";
    public const string ChoiceItem         = "VNovelizerRes/VNPrefabs/UI/Choice/ChoiceItem";
    public const string SaveSlot           = "VNovelizerRes/VNPrefabs/UI/SaveLoad/SaveSlot";
    public const string HistoryItem        = "VNovelizerRes/VNPrefabs/UI/History/HistoryItem";
    public const string CGSlot             = "VNovelizerRes/VNPrefabs/UI/Gallery/CG/CGSlot";
    public const string MusicSlot          = "VNovelizerRes/VNPrefabs/UI/Gallery/Music/MusicSlot";
    public const string SceneSlot          = "VNovelizerRes/VNPrefabs/UI/Gallery/Scene/SceneSlot";

    // ==================== 基础设施 ====================
    public const string EventSystem        = "VNovelizerRes/VNPrefabs/UI/EventSystem";
    public const string SoundObj           = "VNovelizerRes/VNPrefabs/Gameplay/SoundObj";
    public const string VideoObj           = "VNovelizerRes/VNPrefabs/Gameplay/VideoObj";
    public const string TransitionManagerRoot = "VNovelizerRes/VNPrefabs/UI/TransitionManagerRoot";

    // ==================== 画廊数据容器（SO，非 prefab） ====================
    public const string CGDataContainer    = "VNovelizerRes/GalleryContent/CG/CGDataContainer";
    public const string MusicDataContainer = "VNovelizerRes/GalleryContent/Music/MusicDataContainer";
    public const string SceneDataContainer = "VNovelizerRes/GalleryContent/Scene/SceneDataContainer";
}
