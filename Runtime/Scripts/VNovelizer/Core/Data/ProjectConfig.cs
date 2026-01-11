using UnityEngine;
using UnityEngine.InputSystem;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 全局项目路径配置文件
/// </summary>
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
                // 约定：配置文件必须放在 Resources 根目录下，且名字叫 VNProjectConfig
                _instance = Resources.Load<VNProjectConfig>("VNProjectConfig");

#if UNITY_EDITOR
                if (_instance == null)
                {
                    string[] guids = AssetDatabase.FindAssets("t:VNProjectConfig");
                    foreach (var guid in guids)
                    {
                        string path = AssetDatabase.GUIDToAssetPath(guid);
                        // 过滤掉 Packages 里的 (虽然如果你按步骤A做了，这里不会有)
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
            {
                Debug.LogError("严重错误：未找到 VNProjectConfig 配置文件！请在 Resources 目录下创建。");
            }
            return _instance;
        }
    }

    [Header("=== 编辑器工具路径 (Editor Only) ===")]
    [Tooltip("Excel源文件所在的文件夹 (拖拽文件夹到这里)")]
    public Object ExcelSourceFolder;

    [Tooltip("CSV生成的目标文件夹 (拖拽文件夹到这里)")]
    public Object CsvOutputFolder;



    [Header("=== 运行时加载路径 (Resources relative) ===")]
    [Tooltip("CSV文件在Resources下的相对路径 (例如: VNScripts)")]
    public string VNScriptResPath = "VNovelizerRes/VNScripts";

    [Tooltip("背景图片在Resources下的相对路径 (例如: Backgrounds)")]
    public string BackgroundResPath = "VNovelizerRes/Backgrounds";

    [Tooltip("视频在Resources下的相对路径")]
    public string VideoResPath = "VNovelizerRes/Videos";

    [Tooltip("角色立绘在Resources下的相对路径 (例如: Characters)")]
    public string CharacterResPath = "VNovelizerRes/Characters";

    [Tooltip("音乐在Resources下的相对路径")]
    public string BgmResPath = "VNovelizerRes/Audio/Music/BGM";

    [Tooltip("音效在Resources下的相对路径")]
    public string SFXResPath = "VNovelizerRes/Audio/SFX";

    [Tooltip("配音在Resources下的相对路径")]
    public string VoiceResPath = "VNovelizerRes/Audio/Voice";

    [Tooltip("粒子特效在Resources下的相对路径")]
    public string ParticalEffectPath = "VNovelizerRes/VFX/Partical";

    [Header("=== UI预制件加载路径 (UI Prefabs) ===")]
    [Tooltip("VNGamePanel在Resources下的相对路径 (例如: VNovelizerRes/VNPrefabs/UI/VNGamePlay)")]
    public string UI_VNGamePlayPath = "VNovelizerRes/VNPrefabs/UI/VNGamePlay";

    [Tooltip("HistoryPanel在Resources下的相对路径 (例如: VNovelizerRes/VNPrefabs/UI/VNGamePlay)")]
    public string UI_HistoryPath = "VNovelizerRes/VNPrefabs/UI/History";

    [Tooltip("SettingsPanel在Resources下的相对路径 (例如: VNovelizerRes/VNPrefabs/UI/Settings)")]
    public string UI_SettingsPath = "VNovelizerRes/VNPrefabs/UI/Settings";

    [Tooltip("SaveLoad在Resources下的相对路径 (例如: VNovelizerRes/VNPrefabs/UI/SaveLoad)")]
    public string UI_SaveLoadPath = "VNovelizerRes/VNPrefabs/UI/SaveLoad";

    [Tooltip("Confirm在Resources下的相对路径 (例如: VNPrefabs/UI/Confirm)")]
    public string UI_ConfirmPath = "VNovelizerRes/VNPrefabs/UI/Confirm";

    [Tooltip("Confirm在Resources下的相对路径 (例如: VNovelizerRes/VNPrefabs/UI/VNGameplay/Prompt)")]
    public string UI_PromptPath = "VNovelizerRes/VNPrefabs/UI/VNGameplay/Prompt";

    [Tooltip("Choice在Resources下的相对路径 (例如: VNovelizerRes/VNPrefabs/UI/Choice)")]
    public string UI_ChoicePath = "VNovelizerRes/VNPrefabs/UI/Choice";

    [Tooltip("MainMenuPanel在Resources下的相对路径 (例如: VNovelizerRes/VNPrefabs/UI/MainMenu)")]
    public string UI_MainMenuPath = "VNovelizerRes/VNPrefabs/UI/MainMenu";

    [Tooltip("PausePanel在Resources下的相对路径 (例如: VNovelizerRes/VNPrefabs/UI/Pause)")]
    public string UI_PausePath = "VNovelizerRes/VNPrefabs/UI/Pause";

    [Tooltip("GalleryPanel在Resources下的相对路径 (例如: VNovelizerRes/VNPrefabs/UI/Gallery)")]
    public string UI_GalleryPath = "VNovelizerRes/VNPrefabs/UI/Gallery";

    [Tooltip("LoadingProgressPanel在Resources下的相对路径 (例如: VNovelizerRes/VNPrefabs/UI/Loading)")]
    public string UI_LoadingPath = "VNovelizerRes/VNPrefabs/UI/Loading";

    [Tooltip("CG数据容器在Resources下的相对路径 (例如: VNovelizerRes/GalleryContent/CG)")]
    public string CG_DataPath = "VNovelizerRes/GalleryContent/CG";

    [Tooltip("音乐数据容器在Resources下的相对路径 (例如: VNovelizerRes/GalleryContent/Music)")]
    public string Music_DataPath = "VNovelizerRes/GalleryContent/Music";

    [Tooltip("场景数据容器在Resources下的相对路径 (例如: VNovelizerRes/GalleryContent/Scene)")]
    public string Scene_DataPath = "VNovelizerRes/GalleryContent/Scene";

    [Header("=== UI 默认资源 (UI Default Resources) ===")]
    [Tooltip("默认姓名框 Sprite（当角色没有配置 SpeakerBox 时使用）")]
    public Sprite DefaultSpeakerBoxSprite;

    [Header("=== 音效/视频Obj加载路径 ===")]
    [Tooltip("SoundObj在Resources下的相对路径 (例如: VNovelizerRes/VNPrefabs/Gameplay/SoundObj")]
    public string SoundObjPath = "VNovelizerRes/VNPrefabs/Gameplay/SoundObj";
    [Tooltip("VideoObj在Resources下的相对路径 (例如: VNovelizerRes/VNPrefabs/Gameplay/VideoObj")]
    public string VideoObjPath = "VNovelizerRes/VNPrefabs/Gameplay/VideoObj";

    [Header("=== 游戏启动设置 ===")]
    [Tooltip("主界面点击新游戏时加载的默认剧本名称 (不含扩展名，例如: Test101)")]
    public string DefaultScriptName = "Test101";

    [Tooltip("主界面点击新游戏时加载的默认行ID (留空则从剧本开头开始)")]
    public string DefaultLineID = "";

    [Header("=== AES存档加密设置 ===")]
    [Tooltip("是否启用存档加密 (开发时建议关闭，发布时开启)")]
    public bool UseAES = false;

    [Tooltip("加密秘钥 (必须正好 32 个字符)")]
    public string Key = "12345678901234567890123456789012";

    [Tooltip("偏移量 (必须正好 16 个字符)")]
    public string IV = "1234567890123456";


    // --- 辅助方法：获取编辑器下的真实路径 ---
#if UNITY_EDITOR
    public string GetExcelFolderPath()
    {
        if (ExcelSourceFolder == null) return "";
        
        string path = AssetDatabase.GetAssetPath(ExcelSourceFolder);
        return path;
    }

    public string GetCsvOutputPath()
    {
        if (CsvOutputFolder == null) return "";
        return AssetDatabase.GetAssetPath(CsvOutputFolder);
    }
#endif
}