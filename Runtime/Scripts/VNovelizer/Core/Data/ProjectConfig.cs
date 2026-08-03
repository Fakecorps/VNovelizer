using Alchemy.Inspector;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 全局项目路径配置文件。
/// 使用 Alchemy 特性实现卡片化分组 Inspector。
/// </summary>
[HideScriptField]
[CreateAssetMenu(fileName = "VNProjectConfig", menuName = "VNovelizer/Project Config")]
public class VNProjectConfig : ScriptableObject
{
    private static VNProjectConfig _instance;

    public static VNProjectConfig Instance
    {
        get
        {
            if (_instance == null)
            {
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
            if (_instance == null)
                Debug.LogError("严重错误：未找到 VNProjectConfig 配置文件！请在 Resources 目录下创建。");
            return _instance;
        }
    }

    // ==================== 一、编辑器工具 ====================
    [Order(10), BoxGroup("一、编辑器工具路径"), LabelText("Excel 源文件夹")]
    public Object ExcelSourceFolder;

    [Order(20), BoxGroup("一、编辑器工具路径"), LabelText("CSV 输出文件夹")]
    public Object CsvOutputFolder;

    [Order(30), BoxGroup("一、编辑器工具路径"), LabelText("自动转换 Excel → CSV")]
    [Tooltip("启用后，每次从 Excel 切回 Unity Editor 时自动检测并转换被修改的 Excel 文件")]
    public bool AutoConvertExcel = true;

    // ==================== 二、运行时资源路径 ====================
    [Order(100), BoxGroup("二、运行时资源路径"), LabelText("剧本 CSV")]
    public string VNScriptResPath = "VNovelizerRes/VNScripts";

    [Order(110), BoxGroup("二、运行时资源路径"), LabelText("背景图片")]
    public string BackgroundResPath = "VNovelizerRes/Backgrounds";

    [Order(120), BoxGroup("二、运行时资源路径"), LabelText("视频")]
    public string VideoResPath = "VNovelizerRes/Videos";

    [Order(130), BoxGroup("二、运行时资源路径"), LabelText("角色立绘")]
    public string CharacterResPath = "VNovelizerRes/Characters";

    [Order(140), BoxGroup("二、运行时资源路径"), LabelText("粒子特效")]
    public string ParticalEffectPath = "VNovelizerRes/VFX/Partical";

    [Order(150), BoxGroup("二、运行时资源路径"), LabelText("动画")]
    public string AnimationPath = "VNovelizerRes/VFX/Animation";

    [Order(160), BoxGroup("二、运行时资源路径"), LabelText("BGM（背景音乐）")]
    public string BgmResPath = "VNovelizerRes/Audio/Music/BGM";

    [Order(170), BoxGroup("二、运行时资源路径"), LabelText("SFX（音效）")]
    public string SFXResPath = "VNovelizerRes/Audio/SFX";

    [Order(180), BoxGroup("二、运行时资源路径"), LabelText("Voice（配音）")]
    public string VoiceResPath = "VNovelizerRes/Audio/Voice";

    // ==================== 三、UI 预制件路径 ====================
    [Order(200), BoxGroup("三、UI 预制件路径"), LabelText("GamePlay 面板")]
    public string UI_VNGamePlayPath = "VNovelizerRes/VNPrefabs/UI/VNGamePlay";

    [Order(210), BoxGroup("三、UI 预制件路径"), LabelText("主菜单")]
    public string UI_MainMenuPath = "VNovelizerRes/VNPrefabs/UI/MainMenu";

    [Order(220), BoxGroup("三、UI 预制件路径"), LabelText("暂停面板")]
    public string UI_PausePath = "VNovelizerRes/VNPrefabs/UI/Pause";

    [Order(230), BoxGroup("三、UI 预制件路径"), LabelText("历史记录")]
    public string UI_HistoryPath = "VNovelizerRes/VNPrefabs/UI/History";

    [Order(240), BoxGroup("三、UI 预制件路径"), LabelText("设置")]
    public string UI_SettingsPath = "VNovelizerRes/VNPrefabs/UI/Settings";

    [Order(250), BoxGroup("三、UI 预制件路径"), LabelText("存档/读档")]
    public string UI_SaveLoadPath = "VNovelizerRes/VNPrefabs/UI/SaveLoad";

    [Order(260), BoxGroup("三、UI 预制件路径"), LabelText("确认弹窗")]
    public string UI_ConfirmPath = "VNovelizerRes/VNPrefabs/UI/Confirm";

    [Order(270), BoxGroup("三、UI 预制件路径"), LabelText("Prompt 提示")]
    public string UI_PromptPath = "VNovelizerRes/VNPrefabs/UI/VNGameplay/Prompt";

    [Order(280), BoxGroup("三、UI 预制件路径"), LabelText("分支选择")]
    public string UI_ChoicePath = "VNovelizerRes/VNPrefabs/UI/Choice";

    [Order(290), BoxGroup("三、UI 预制件路径"), LabelText("画廊面板")]
    public string UI_GalleryPath = "VNovelizerRes/VNPrefabs/UI/Gallery";

    [Order(300), BoxGroup("三、UI 预制件路径"), LabelText("加载画面")]
    public string UI_LoadingPath = "VNovelizerRes/VNPrefabs/UI/Loading";

    [Order(310), BoxGroup("三、UI 预制件路径"), LabelText("CG 数据")]
    public string CG_DataPath = "VNovelizerRes/GalleryContent/CG";

    [Order(320), BoxGroup("三、UI 预制件路径"), LabelText("音乐数据")]
    public string Music_DataPath = "VNovelizerRes/GalleryContent/Music";

    [Order(330), BoxGroup("三、UI 预制件路径"), LabelText("场景数据")]
    public string Scene_DataPath = "VNovelizerRes/GalleryContent/Scene";

    // ==================== 四、UI 默认资源 ====================
    [Order(400), BoxGroup("四、UI 默认资源"), LabelText("默认姓名框")]
    public Sprite DefaultSpeakerBoxSprite;

    [Order(410), BoxGroup("四、UI 默认资源"), LabelText("默认头像框")]
    public Sprite DefaultHeadFrameSprite;

    [Order(420), BoxGroup("四、UI 默认资源"), LabelText("音效对象")]
    public string SoundObjPath = "VNovelizerRes/VNPrefabs/Gameplay/SoundObj";

    [Order(430), BoxGroup("四、UI 默认资源"), LabelText("视频对象")]
    public string VideoObjPath = "VNovelizerRes/VNPrefabs/Gameplay/VideoObj";

    // ==================== 五、游戏启动设置 ====================
    [Order(500), BoxGroup("五、游戏启动设置"), LabelText("默认剧本")]
    [Tooltip("主界面点击新游戏时加载的默认剧本名称（不含扩展名）")]
    public string DefaultScriptName = "Test101";

    [Order(510), BoxGroup("五、游戏启动设置"), LabelText("默认行 ID")]
    [Tooltip("留空则从剧本开头开始，填写则从指定行 ID 开始")]
    public string DefaultLineID = "";

    // ==================== 六、本地化 ====================
    [Order(600), BoxGroup("六、剧情本地化"), LabelText("启用剧情本地化")]
    [Tooltip("启用剧情 Text/Speaker 的多语言本地化。关闭时保持旧版 CSV 行为")]
    public bool EnableLocalization = false;

    [Order(610), BoxGroup("六、剧情本地化"), ShowIf("EnableLocalization"), LabelText("Collection 名称")]
    [Tooltip("兼容旧方案：共享 StringTableCollection 名称")]
    public string LocalizationCollectionName = "VN_Scripts";

    [Order(620), BoxGroup("六、剧情本地化"), ShowIf("EnableLocalization"), LabelText("表名前缀")]
    [Tooltip("一剧本一表方案使用的 Collection 前缀")]
    public string ScriptTablePrefix = "VNScript_";

    [Order(630), BoxGroup("六、剧情本地化"), ShowIf("EnableLocalization"), LabelText("缺失时回退 CSV")]
    [Tooltip("当前语言缺失翻译时，是否回退显示本行 CSV 的 Speaker/Text")]
    public bool FallbackToCsvWhenMissing = true;

    // ==================== 七、AES 加密 ====================
    [Order(700), BoxGroup("七、AES 存档加密"), LabelText("启用加密")]
    [Tooltip("开发时建议关闭，发布时开启")]
    public bool UseAES = false;

    [Order(710), BoxGroup("七、AES 存档加密"), ShowIf("UseAES"), LabelText("加密秘钥")]
    [ValidateInput("ValidateKey", "Key 必须正好是 32 个字符！")]
    public string Key = "12345678901234567890123456789012";

    [Order(720), BoxGroup("七、AES 存档加密"), ShowIf("UseAES"), LabelText("偏移向量")]
    [ValidateInput("ValidateIV", "IV 必须正好是 16 个字符！")]
    public string IV = "1234567890123456";

    [Order(730), BoxGroup("七、AES 存档加密"), ShowIf("UseAES"), Button, LabelText("")]
    private void GenerateRandomKey()
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        var random = new System.Random();
        Key = new string(Enumerable.Repeat(chars, 32).Select(s => s[random.Next(s.Length)]).ToArray());
        IV = new string(Enumerable.Repeat(chars, 16).Select(s => s[random.Next(s.Length)]).ToArray());
    }

    private bool ValidateKey(string value) => value != null && value.Length == 32;
    private bool ValidateIV(string value) => value != null && value.Length == 16;

    // ==================== 辅助方法 ====================
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
