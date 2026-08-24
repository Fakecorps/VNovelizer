using Alchemy.Inspector;
using System.Linq;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

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

    // ==================== 四、游戏启动设置 ====================
    [Order(400), BoxGroup("四、游戏启动设置"), LabelText("默认剧本")]
    [Tooltip("主界面点击新游戏时加载的默认剧本名称（不含扩展名）")]
    public string DefaultScriptName = "Test101";

    [Order(410), BoxGroup("四、游戏启动设置"), LabelText("默认行 ID")]
    [Tooltip("留空则从剧本开头开始，填写则从指定行 ID 开始")]
    public string DefaultLineID = "";

    // ==================== 五、本地化 ====================
    [Order(500), BoxGroup("五、剧情本地化"), LabelText("启用剧情本地化")]
    [Tooltip("启用剧情 Text/Speaker 的多语言本地化。关闭时保持旧版 CSV 行为")]
    public bool EnableLocalization = false;

    [Order(510), BoxGroup("五、剧情本地化"), ShowIf("EnableLocalization"), LabelText("Collection 名称")]
    [Tooltip("兼容旧方案：共享 StringTableCollection 名称")]
    public string LocalizationCollectionName = "VN_Scripts";

    [Order(520), BoxGroup("五、剧情本地化"), ShowIf("EnableLocalization"), LabelText("表名前缀")]
    [Tooltip("一剧本一表方案使用的 Collection 前缀")]
    public string ScriptTablePrefix = "VNScript_";

    [Order(530), BoxGroup("五、剧情本地化"), ShowIf("EnableLocalization"), LabelText("缺失时回退 CSV")]
    [Tooltip("当前语言缺失翻译时，是否回退显示本行 CSV 的 Speaker/Text")]
    public bool FallbackToCsvWhenMissing = true;

    // ==================== 六、AES 加密 ====================
    [Order(600), BoxGroup("六、AES 存档加密"), LabelText("启用加密")]
    [Tooltip("开发时建议关闭，发布时开启")]
    public bool UseAES = false;

    [Order(610), BoxGroup("六、AES 存档加密"), ShowIf("UseAES"), LabelText("加密秘钥")]
    [ValidateInput("ValidateKey", "Key 必须正好是 32 个字符！")]
    public string Key = "12345678901234567890123456789012";

    [Order(620), BoxGroup("六、AES 存档加密"), ShowIf("UseAES"), LabelText("偏移向量")]
    [ValidateInput("ValidateIV", "IV 必须正好是 16 个字符！")]
    public string IV = "1234567890123456";

    [Order(630), BoxGroup("六、AES 存档加密"), ShowIf("UseAES"), Button, LabelText("")]
    public void GenerateRandomKey()
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        var random = new System.Random();
        Key = new string(Enumerable.Repeat(chars, 32).Select(s => s[random.Next(s.Length)]).ToArray());
        IV = new string(Enumerable.Repeat(chars, 16).Select(s => s[random.Next(s.Length)]).ToArray());
    }

    private bool ValidateKey(string value) => value != null && value.Length == 32;
    private bool ValidateIV(string value) => value != null && value.Length == 16;

    // ==================== 七、剧场 ====================
    [Order(700), BoxGroup("七、剧场"), LabelText("自定义场景相机")]
    [Tooltip("剧场专用场景相机预制体（可预挂后处理组件，如 Bloom/DoF，默认禁用）。留空使用引擎默认相机")]
    public GameObject CustomSceneCameraPrefab;

    // ==================== 八、UI 模板覆写 ====================
    // 指派自定义模板（Inspector 拖拽引用，位置无关）后优先生效；
    // 全部留空 = 使用包内默认模板（经资源服务链加载，无需任何配置）。
    // 推荐工作流：本分组顶部的"从模板创建…"按钮复制包内模板到自选位置后编辑。
    [Order(800), BoxGroup("八、UI 模板覆写"), LabelText("游戏主面板")]
    [Tooltip("留空使用包内默认模板")]
    public GameObject Override_VNGameplayPanel;

    [Order(810), BoxGroup("八、UI 模板覆写"), LabelText("主菜单面板")]
    public GameObject Override_MainMenuPanel;

    [Order(820), BoxGroup("八、UI 模板覆写"), LabelText("画廊面板")]
    public GameObject Override_GalleryPanel;

    [Order(830), BoxGroup("八、UI 模板覆写"), LabelText("暂停面板")]
    public GameObject Override_PausePanel;

    [Order(840), BoxGroup("八、UI 模板覆写"), LabelText("历史记录面板")]
    public GameObject Override_HistoryPanel;

    [Order(850), BoxGroup("八、UI 模板覆写"), LabelText("存读档面板")]
    public GameObject Override_SaveLoadPanel;

    [Order(860), BoxGroup("八、UI 模板覆写"), LabelText("设置面板")]
    public GameObject Override_SettingsPanel;

    [Order(870), BoxGroup("八、UI 模板覆写"), LabelText("分支选择面板")]
    public GameObject Override_ChoicePanel;

    [Order(880), BoxGroup("八、UI 模板覆写"), LabelText("确认弹窗面板")]
    public GameObject Override_ConfirmPanel;

    [Order(890), BoxGroup("八、UI 模板覆写"), LabelText("加载进度面板")]
    public GameObject Override_LoadingProgressPanel;

    [Order(900), BoxGroup("八、UI 模板覆写"), LabelText("对话提示项 (PromptItem)")]
    public GameObject Override_PromptItem;

    [Order(910), BoxGroup("八、UI 模板覆写"), LabelText("分支选项项 (ChoiceItem)")]
    public GameObject Override_ChoiceItem;

    [Order(920), BoxGroup("八、UI 模板覆写"), LabelText("存档槽 (SaveSlot)")]
    public GameObject Override_SaveSlot;

    [Order(930), BoxGroup("八、UI 模板覆写"), LabelText("历史记录条目 (HistoryItem)")]
    public GameObject Override_HistoryItem;

    [Order(940), BoxGroup("八、UI 模板覆写"), LabelText("画廊 CG 槽位 (CGSlot)")]
    public GameObject Override_CGSlot;

    [Order(950), BoxGroup("八、UI 模板覆写"), LabelText("画廊音乐条目 (MusicSlot)")]
    public GameObject Override_MusicSlot;

    [Order(960), BoxGroup("八、UI 模板覆写"), LabelText("画廊场景槽位 (SceneSlot)")]
    public GameObject Override_SceneSlot;

    [Order(970), BoxGroup("八、UI 模板覆写"), LabelText("EventSystem")]
    public GameObject Override_EventSystem;

    [Order(980), BoxGroup("八、UI 模板覆写"), LabelText("音效对象 (SoundObj)")]
    public GameObject Override_SoundObj;

    [Order(990), BoxGroup("八、UI 模板覆写"), LabelText("视频对象 (VideoObj)")]
    public GameObject Override_VideoObj;

    // —— 画廊数据容器（ScriptableObject，指派后优先生效；留空用包内默认） ——
    [Order(1000), BoxGroup("八、UI 模板覆写"), LabelText("CG 数据容器")]
    [Tooltip("留空使用包内默认数据容器（随画廊内容编辑器自动创建/管理）")]
    public CGDataContainer Override_CGDataContainer;

    [Order(1010), BoxGroup("八、UI 模板覆写"), LabelText("音乐数据容器")]
    public MusicDataContainer Override_MusicDataContainer;

    [Order(1020), BoxGroup("八、UI 模板覆写"), LabelText("场景数据容器")]
    public SceneDataContainer Override_SceneDataContainer;

    // ==================== 辅助方法 ====================

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
