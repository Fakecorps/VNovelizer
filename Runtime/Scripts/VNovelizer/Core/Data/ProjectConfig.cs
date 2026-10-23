using Alchemy.Inspector;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 【VFX 注册表扩展】滤镜注册表条目：滤镜逻辑名 + Shader 直接引用 + 默认时长 + 启用开关。
/// 注册表是唯一入口——只有列出的条目才能被 filter_xxx / filtercustom / unsetfilter 使用。
/// </summary>
[System.Serializable]
public class VNProjectFilterEntry
{
    [LabelText("滤镜 ID")] public string Id;
    [LabelText("显示名")] public string DisplayName;
    [LabelText("Shader")] public Shader Shader;
    [LabelText("默认时长（秒）")] public float DefaultDuration = 0.5f;
    [LabelText("启用")] public bool Enabled = true;
}

/// <summary>
/// 【VFX 注册表扩展】粒子特效注册表条目：粒子逻辑名 + Prefab 直接引用 + 默认时长 + 启用开关。
/// Prefab 必须挂 <see cref="VNovelizer.Core.Weather.VN_Particle"/> 组件（管理器只认该接口）；
/// 注册表是唯一入口——只有列出的条目才能被 particle_rain/particle_snow/particlecustom/unsetparticle 使用。
/// </summary>
[System.Serializable]
public class VNProjectParticleEntry
{
    [LabelText("粒子 ID")] public string Id;
    [LabelText("显示名")] public string DisplayName;
    [LabelText("预制体")] public GameObject Prefab;
    [LabelText("默认时长（秒）")] public float DefaultDuration = 0.5f;
    [LabelText("启用")] public bool Enabled = true;
}

/// <summary>
/// 全局项目配置文件（含 UI 模板覆写）。
/// 使用 Alchemy 特性实现卡片化分组 Inspector。
///
/// 资源寻址（见 Docs/VNResourceProviderRefactoring.md）：
/// - 媒体资源（背景/音频/立绘/剧本/VFX）：按"二、资源默认地址"的类别前缀经
///   资源服务链解析（Addressables 拖放分配优先 → Resources 兜底）；
/// - UI 预制体/画廊数据容器：按"八、UI 模板覆写"指派（直接引用），
///   未指派经服务链加载包内默认模板（键见 VNUIPrefabKeys）。
/// </summary>
[HideScriptField]
[CreateAssetMenu(fileName = "VNProjectConfig", menuName = "VNovelizer/Project Config")]
public class VNProjectConfig : ScriptableObject
{
    private static VNProjectConfig _instance;
    private static VNProjectConfig _tempInstance;
    private static bool _warnedMissingConfig;

    /// <summary>
    /// 全局配置访问（带兜底）：未创建持久配置资产时，使用内置默认值的临时实例
    /// （零配置开箱即用——引擎的全部默认值都内置于字段初始化器，临时实例即完整可用），
    /// 并提示一次"打开 Edit → Project Settings → VNovelizer 创建持久配置"。
    /// </summary>
    public static VNProjectConfig Instance
    {
        get
        {
            VNProjectConfig config = LoadInstance();
            if (config != null) return config;

            if (_tempInstance == null)
            {
                _tempInstance = CreateInstance<VNProjectConfig>();
                // 【VFX 注册表扩展】零配置兜底：临时实例预填内置滤镜/粒子条目
                // （Shader/Prefab 留空 → 运行时内置兜底解析），与持久资产的编辑器预填语义一致
                _tempInstance.PrefillBuiltinVfxRegistry();
                if (!_warnedMissingConfig)
                {
                    _warnedMissingConfig = true;
                    Debug.LogWarning("[VNProjectConfig] 未找到持久配置资产，正在使用内置默认值（临时实例，重启后丢失修改）。\n" +
                                     "要保存配置请打开：Edit → Project Settings → VNovelizer（首次打开会自动创建配置资产）。");
                }
            }
            return _tempInstance;
        }
    }

    /// <summary>
    /// 静默探测（编辑器轮询/后台任务专用）："配置尚未创建"是合法的未初始化状态
    ///（刚安装插件、尚未打开设置页），不应刷错误日志，也不应触发临时实例兜底。
    /// </summary>
    public static bool TryGetInstance(out VNProjectConfig config)
    {
        config = LoadInstance();
        return config != null;
    }

    private static VNProjectConfig LoadInstance()
    {
        if (_instance == null)
        {
            // 引导配置：始终经 Resources 加载（资源服务链初始化前即被各管理器访问，
            // 且作为全项目唯一的 Resources 引导资产，属 Phase 2 既定决策，见
            // Docs/VNResourceProviderRefactoring.md）
            _instance = Resources.Load<VNProjectConfig>("VNProjectConfig");
#if UNITY_EDITOR
            if (_instance == null)
            {
                string[] guids = AssetDatabase.FindAssets("t:VNProjectConfig");
                foreach (var guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!path.StartsWith("Packages"))
                    {
                        _instance = AssetDatabase.LoadAssetAtPath<VNProjectConfig>(path);
                        break;
                    }
                }
            }
#endif
        }
        return _instance;
    }

    // ==================== 一、编辑器工具 ====================
    [Order(10), BoxGroup("一、编辑器工具"), LabelText("Excel 源文件夹")]
    public Object ExcelSourceFolder;

    [Order(20), BoxGroup("一、编辑器工具"), LabelText("CSV 输出文件夹")]
    public Object CsvOutputFolder;

    [Order(30), BoxGroup("一、编辑器工具"), LabelText("自动转换 Excel → CSV")]
    [Tooltip("启用后，每次从 Excel 切回 Unity Editor 时自动检测并转换被修改的 Excel 文件")]
    public bool AutoConvertExcel = true;

    // ==================== 二、资源默认地址（引擎内部，勿改） ====================
    // 语义（见 Docs/VNResourceProviderRefactoring.md）：
    // - Addressables 托管模式：逻辑类别前缀——拖放分配时资产获得地址 {前缀}/{逻辑名}，
    //   物理位置无关，无需任何文件夹约定；
    // - Resources 兼容模式（旧项目）：同时是 Assets/Resources 下的相对文件夹路径（旧行为）。
    // 这是引擎私有寻址常量（VN 引擎内部地址前缀），正常使用无需修改。
    [Order(100), BoxGroup("二、资源默认地址（引擎内部，勿改）"), ReadOnly, LabelText("剧本 CSV")]
    public string VNScriptResPath = "VNovelizerRes/VNScripts";

    [Order(110), BoxGroup("二、资源默认地址（引擎内部，勿改）"), ReadOnly, LabelText("背景图片")]
    public string BackgroundResPath = "VNovelizerRes/Backgrounds";

    [Order(120), BoxGroup("二、资源默认地址（引擎内部，勿改）"), ReadOnly, LabelText("视频")]
    [Tooltip("视频走 StreamingAssets 原始文件（不经 Addressables），此为其中相对路径")]
    public string VideoResPath = "VNovelizerRes/Videos";

    [Order(130), BoxGroup("二、资源默认地址（引擎内部，勿改）"), ReadOnly, LabelText("角色立绘")]
    public string CharacterResPath = "VNovelizerRes/Characters";

    [Order(140), BoxGroup("二、资源默认地址（引擎内部，勿改）"), ReadOnly, LabelText("粒子特效")]
    public string ParticalEffectPath = "VNovelizerRes/VFX/Partical";

    [Order(150), BoxGroup("二、资源默认地址（引擎内部，勿改）"), ReadOnly, LabelText("动画")]
    public string AnimationPath = "VNovelizerRes/VFX/Animation";

    [Order(155), BoxGroup("二、资源默认地址（引擎内部，勿改）"), ReadOnly, LabelText("画面滤镜预设")]
    public string FilterResPath = "VNovelizerRes/VFX/Filters";

    [Order(158), BoxGroup("二、资源默认地址（引擎内部，勿改）"), ReadOnly, LabelText("粒子预制体")]
    [Tooltip("VFX 粒子预制体类别前缀（内置 rain/snow 模板与用户粒子副本所在类别）")]
    public string ParticleResPath = "VNovelizerRes/VFX/Particles";

    [Order(160), BoxGroup("二、资源默认地址（引擎内部，勿改）"), ReadOnly, LabelText("BGM（背景音乐）")]
    public string BgmResPath = "VNovelizerRes/Audio/Music/BGM";

    [Order(170), BoxGroup("二、资源默认地址（引擎内部，勿改）"), ReadOnly, LabelText("SFX（音效）")]
    public string SFXResPath = "VNovelizerRes/Audio/SFX";

    [Order(180), BoxGroup("二、资源默认地址（引擎内部，勿改）"), ReadOnly, LabelText("Voice（配音）")]
    public string VoiceResPath = "VNovelizerRes/Audio/Voice";

    // ==================== 三、UI 默认资源 ====================
    [Order(300), BoxGroup("三、UI 默认资源"), LabelText("默认姓名框")]
    public Sprite DefaultSpeakerBoxSprite;

    [Order(310), BoxGroup("三、UI 默认资源"), LabelText("默认头像框")]
    public Sprite DefaultHeadFrameSprite;

    // ==================== 四、本地化 ====================
    // 详见 Docs/VNLocalizationRefactoring.md：翻译 CSV（一种语言一张表）为真源，
    // 编辑器聚合器全自动合并进单一 StringTableCollection "VNLocalization"。
    [Order(500), BoxGroup("四、剧情本地化"), LabelText("启用剧情本地化")]
    [Tooltip("启用后剧情 Text/Speaker/命令文本/UI 走本地化表。关闭时保持 CSV 原文行为")]
    public bool EnableLocalization = false;

    [Order(510), BoxGroup("四、剧情本地化"), ShowIf("EnableLocalization"), LabelText("翻译表文件夹")]
    [Tooltip("翻译 CSV 根目录（一种语言一张表，可任意组织/任意多张）。默认 Assets/Localization/Translations/")]
    public string TranslationFolder = "Assets/Localization/Translations/";

    [Order(520), BoxGroup("四、剧情本地化"), ShowIf("EnableLocalization"), LabelText("源语言 (Text_Origin)")]
    [Tooltip("源语言 locale 代码（如 zh-Hans）。聚合时源 locale 表自动填原文，作为 fallback 链根")]
    public string SourceLocale = "zh-Hans";

    [Order(530), BoxGroup("四、剧情本地化"), ShowIf("EnableLocalization"), LabelText("目标语言列表")]
    [Tooltip("模板生成器为这些语言生成翻译表（locale 代码，逗号分隔，如 en,ja）")]
    public string TargetLocales = "en";

    // ==================== 五、AES 加密 ====================
    [Order(600), BoxGroup("五、AES 存档加密"), LabelText("启用加密")]
    [Tooltip("开发时建议关闭，发布时开启")]
    public bool UseAES = false;

    [Order(610), BoxGroup("五、AES 存档加密"), ShowIf("UseAES"), LabelText("加密秘钥")]
    [ValidateInput("ValidateKey", "Key 必须正好是 32 个字符！")]
    public string Key = "12345678901234567890123456789012";

    [Order(620), BoxGroup("五、AES 存档加密"), ShowIf("UseAES"), LabelText("偏移向量")]
    [ValidateInput("ValidateIV", "IV 必须正好是 16 个字符！")]
    public string IV = "1234567890123456";

    [Order(630), BoxGroup("五、AES 存档加密"), ShowIf("UseAES"), Button, LabelText("")]
    public void GenerateRandomKey()
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        var random = new System.Random();
        Key = new string(Enumerable.Repeat(chars, 32).Select(s => s[random.Next(s.Length)]).ToArray());
        IV = new string(Enumerable.Repeat(chars, 16).Select(s => s[random.Next(s.Length)]).ToArray());
    }

    private bool ValidateKey(string value) => value != null && value.Length == 32;
    private bool ValidateIV(string value) => value != null && value.Length == 16;

    // ==================== 六、剧场 ====================
    [Order(700), BoxGroup("六、剧场"), LabelText("自定义场景相机")]
    [Tooltip("剧场专用场景相机预制体（可预挂后处理组件，如 Bloom/DoF，默认禁用）。留空使用引擎默认相机")]
    public GameObject CustomSceneCameraPrefab;

    // ==================== 七、UI 模板覆写 ====================
    // 指派自定义模板（Inspector 拖拽引用，位置无关）后优先生效；
    // 全部留空 = 使用包内默认模板（经资源服务链加载，无需任何配置）。
    // 推荐工作流：本分组顶部的"从模板创建…"按钮复制包内模板到自选位置后编辑。
    [Order(800), BoxGroup("七、UI 模板覆写"), LabelText("游戏主面板")]
    [Tooltip("留空使用包内默认模板")]
    public GameObject Override_VNGameplayPanel;

    [Order(810), BoxGroup("七、UI 模板覆写"), LabelText("主菜单面板")]
    public GameObject Override_MainMenuPanel;

    [Order(820), BoxGroup("七、UI 模板覆写"), LabelText("画廊面板")]
    public GameObject Override_GalleryPanel;

    [Order(830), BoxGroup("七、UI 模板覆写"), LabelText("暂停面板")]
    public GameObject Override_PausePanel;

    [Order(840), BoxGroup("七、UI 模板覆写"), LabelText("历史记录面板")]
    public GameObject Override_HistoryPanel;

    [Order(850), BoxGroup("七、UI 模板覆写"), LabelText("存读档面板")]
    public GameObject Override_SaveLoadPanel;

    [Order(860), BoxGroup("七、UI 模板覆写"), LabelText("设置面板")]
    public GameObject Override_SettingsPanel;

    [Order(870), BoxGroup("七、UI 模板覆写"), LabelText("分支选择面板")]
    public GameObject Override_ChoicePanel;

    [Order(880), BoxGroup("七、UI 模板覆写"), LabelText("确认弹窗面板")]
    public GameObject Override_ConfirmPanel;

    [Order(890), BoxGroup("七、UI 模板覆写"), LabelText("加载进度面板")]
    public GameObject Override_LoadingProgressPanel;

    [Order(900), BoxGroup("七、UI 模板覆写"), LabelText("对话提示项 (PromptItem)")]
    public GameObject Override_PromptItem;

    [Order(910), BoxGroup("七、UI 模板覆写"), LabelText("分支选项项 (ChoiceItem)")]
    public GameObject Override_ChoiceItem;

    [Order(920), BoxGroup("七、UI 模板覆写"), LabelText("存档槽 (SaveSlot)")]
    public GameObject Override_SaveSlot;

    [Order(930), BoxGroup("七、UI 模板覆写"), LabelText("历史记录条目 (HistoryItem)")]
    public GameObject Override_HistoryItem;

    [Order(940), BoxGroup("七、UI 模板覆写"), LabelText("画廊 CG 槽位 (CGSlot)")]
    public GameObject Override_CGSlot;

    [Order(950), BoxGroup("七、UI 模板覆写"), LabelText("画廊音乐条目 (MusicSlot)")]
    public GameObject Override_MusicSlot;

    [Order(960), BoxGroup("七、UI 模板覆写"), LabelText("画廊场景槽位 (SceneSlot)")]
    public GameObject Override_SceneSlot;

    [Order(970), BoxGroup("七、UI 模板覆写"), LabelText("EventSystem")]
    public GameObject Override_EventSystem;

    [Order(980), BoxGroup("七、UI 模板覆写"), LabelText("音效对象 (SoundObj)")]
    public GameObject Override_SoundObj;

    [Order(990), BoxGroup("七、UI 模板覆写"), LabelText("视频对象 (VideoObj)")]
    public GameObject Override_VideoObj;

    // —— 画廊数据容器（ScriptableObject，指派后优先生效；留空用包内默认） ——
    [Order(1000), BoxGroup("七、UI 模板覆写"), LabelText("CG 数据容器")]
    [Tooltip("留空使用包内默认数据容器（随画廊内容编辑器自动创建/管理）")]
    public CGDataContainer Override_CGDataContainer;

    [Order(1010), BoxGroup("七、UI 模板覆写"), LabelText("音乐数据容器")]
    public MusicDataContainer Override_MusicDataContainer;

    [Order(1020), BoxGroup("七、UI 模板覆写"), LabelText("场景数据容器")]
    public SceneDataContainer Override_SceneDataContainer;

    // ==================== 八、VFX（特效注册表） ====================
    // 【VFX 注册表扩展 2026-10-23】手动真源：只有列在这里的条目才能被命令使用
    // （拖入才可用，唯一入口）。内置 7 款滤镜 + rain/snow 粒子由编辑器预填
    // （打开 VFX 设置页 / 首次创建配置资产时自动补填，幂等；可禁用/可删）。
    // 零配置（未创建持久资产）场景由临时实例 PrefillBuiltinVfxRegistry 兜底。
    [Order(1100), BoxGroup("八、VFX（特效注册表）"), LabelText("滤镜注册表")]
    [Tooltip("滤镜特效唯一入口：id + Shader（拖入即可用）。内置 7 款已预填，可禁用/删除；删除后对应 filter_xxx 命令会警告未注册")]
    public List<VNProjectFilterEntry> VFXFilterRegistry = new List<VNProjectFilterEntry>();

    [Order(1110), BoxGroup("八、VFX（特效注册表）"), LabelText("粒子注册表")]
    [Tooltip("粒子特效唯一入口：id + Prefab（需挂 VN_Particle 组件）。内置 rain/snow 已预填，可禁用/删除")]
    public List<VNProjectParticleEntry> VFXParticleRegistry = new List<VNProjectParticleEntry>();

    // ==================== 辅助方法 ====================

    /// <summary>
    /// 【本地化重构 P1】解析目标语言列表（逗号分隔，去除空白/空项，统一小写）。
    /// 模板生成器据此为每个语言生成翻译表。
    /// </summary>
    public string[] GetTargetLocales()
    {
        if (string.IsNullOrEmpty(TargetLocales))
            return System.Array.Empty<string>();
        return TargetLocales.Split(',')
            .Select(s => s.Trim().ToLowerInvariant())
            .Where(s => s.Length > 0)
            .Distinct()
            .ToArray();
    }

    /// <summary>
    /// 【本地化重构 P1】翻译表根目录（规范化，去尾斜杠）。
    /// </summary>
    public string GetTranslationFolderPath()
    {
        return string.IsNullOrEmpty(TranslationFolder)
            ? "Assets/Localization/Translations/"
            : TranslationFolder.TrimEnd('/', '\\');
    }

    /// <summary>
    /// UI 预制体/资产覆写查询（由 VNUIPrefabs 统一调用）。
    /// prefabKey = VNUIPrefabKeys 常量（固定键，不随配置改动而漂移）。
    /// 返回用户指派的自定义模板；未指派返回 null（走包内默认模板 fallback）。
    /// </summary>
    public Object GetUIPrefabOverride(string prefabKey)
    {
        if (string.IsNullOrEmpty(prefabKey)) return null;
        switch (prefabKey)
        {
            case VNUIPrefabKeys.VNGameplayPanel:      return Override_VNGameplayPanel;
            case VNUIPrefabKeys.MainMenuPanel:        return Override_MainMenuPanel;
            case VNUIPrefabKeys.GalleryPanel:         return Override_GalleryPanel;
            case VNUIPrefabKeys.PausePanel:           return Override_PausePanel;
            case VNUIPrefabKeys.HistoryPanel:         return Override_HistoryPanel;
            case VNUIPrefabKeys.SaveLoadPanel:        return Override_SaveLoadPanel;
            case VNUIPrefabKeys.SettingsPanel:        return Override_SettingsPanel;
            case VNUIPrefabKeys.ChoicePanel:          return Override_ChoicePanel;
            case VNUIPrefabKeys.ConfirmPanel:         return Override_ConfirmPanel;
            case VNUIPrefabKeys.LoadingProgressPanel: return Override_LoadingProgressPanel;
            case VNUIPrefabKeys.PromptItem:           return Override_PromptItem;
            case VNUIPrefabKeys.ChoiceItem:           return Override_ChoiceItem;
            case VNUIPrefabKeys.SaveSlot:             return Override_SaveSlot;
            case VNUIPrefabKeys.HistoryItem:          return Override_HistoryItem;
            case VNUIPrefabKeys.CGSlot:               return Override_CGSlot;
            case VNUIPrefabKeys.MusicSlot:            return Override_MusicSlot;
            case VNUIPrefabKeys.SceneSlot:            return Override_SceneSlot;
            case VNUIPrefabKeys.EventSystem:          return Override_EventSystem;
            case VNUIPrefabKeys.SoundObj:             return Override_SoundObj;
            case VNUIPrefabKeys.VideoObj:             return Override_VideoObj;
            case VNUIPrefabKeys.CGDataContainer:      return Override_CGDataContainer;
            case VNUIPrefabKeys.MusicDataContainer:   return Override_MusicDataContainer;
            case VNUIPrefabKeys.SceneDataContainer:   return Override_SceneDataContainer;
            default: return null;
        }
    }

    /// <summary>
    /// 【VFX 注册表扩展】按 id 查询滤镜注册表条目（大小写不敏感；仅返回启用条目）。
    /// 返回 null = 未注册 / 已禁用 / 注册表为空（零配置临时实例由预填保证内置条目存在）。
    /// </summary>
    public VNProjectFilterEntry GetFilterEntry(string filterId)
    {
        if (string.IsNullOrEmpty(filterId) || VFXFilterRegistry == null) return null;
        foreach (var e in VFXFilterRegistry)
            if (e != null && e.Enabled &&
                string.Equals(e.Id, filterId.Trim(), System.StringComparison.OrdinalIgnoreCase))
                return e;
        return null;
    }

    /// <summary>
    /// 【VFX 注册表扩展】按 id 查询粒子注册表条目（大小写不敏感；仅返回启用条目）。
    /// 返回 null = 未注册 / 已禁用 / 注册表为空。
    /// </summary>
    public VNProjectParticleEntry GetParticleEntry(string particleId)
    {
        if (string.IsNullOrEmpty(particleId) || VFXParticleRegistry == null) return null;
        foreach (var e in VFXParticleRegistry)
            if (e != null && e.Enabled &&
                string.Equals(e.Id, particleId.Trim(), System.StringComparison.OrdinalIgnoreCase))
                return e;
        return null;
    }

    /// <summary>
    /// 【VFX 注册表扩展】零配置兜底预填（仅临时实例调用，无 UnityEditor 依赖）：
    /// 内置 7 款滤镜（Shader 留空 → VNFilterLibrary 内置 shader 名称表兜底解析）+
    /// rain/snow 粒子（Prefab 留空 → WeatherParticleManager 运行时代码构建兜底）。
    /// 持久配置资产由编辑器 VNVfxRegistryPrefiller 预填真实资产引用；两者语义一致（幂等）。
    /// </summary>
    public void PrefillBuiltinVfxRegistry()
    {
        if (VFXFilterRegistry == null) VFXFilterRegistry = new List<VNProjectFilterEntry>();
        if (VFXParticleRegistry == null) VFXParticleRegistry = new List<VNProjectParticleEntry>();

        if (VFXFilterRegistry.Count == 0)
        {
            foreach (var info in VNovelizer.Core.Filters.VNFilterLibrary.GetBuiltinFilterInfos())
                VFXFilterRegistry.Add(new VNProjectFilterEntry
                {
                    Id = info.Id,
                    DisplayName = info.DisplayName,
                    Shader = null, // 运行时经内置名称表兜底解析
                    DefaultDuration = info.DefaultDuration,
                    Enabled = true,
                });
        }

        if (VFXParticleRegistry.Count == 0)
        {
            VFXParticleRegistry.Add(new VNProjectParticleEntry
            {
                Id = "snow", DisplayName = "下雪",
                Prefab = null, // 运行时经代码构建兜底（WeatherParticleFactory）
                DefaultDuration = 0.5f, Enabled = true,
            });
            VFXParticleRegistry.Add(new VNProjectParticleEntry
            {
                Id = "rain", DisplayName = "下雨",
                Prefab = null,
                DefaultDuration = 0.5f, Enabled = true,
            });
        }
    }
#if UNITY_EDITOR
    public string GetExcelFolderPath()
    {
        if (ExcelSourceFolder == null) return "";
        return AssetDatabase.GetAssetPath(ExcelSourceFolder);
    }

    public string GetCsvOutputPath()
    {
        if (CsvOutputFolder == null) return "";
        return AssetDatabase.GetAssetPath(CsvOutputFolder);
    }
#endif
}
