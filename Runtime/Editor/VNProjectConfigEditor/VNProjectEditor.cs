using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using System.Linq;

[CustomEditor(typeof(VNProjectConfig))]
public class VNProjectConfigEditor : Editor
{
    private VisualElement root;

    public override VisualElement CreateInspectorGUI()
    {
        root = new VisualElement();

        // 1. 标题栏
        var header = new Label("VNovelizer 全局配置");
        header.style.fontSize = 18;
        header.style.unityFontStyleAndWeight = FontStyle.Bold;
        header.style.marginBottom = 10;
        header.style.marginTop = 5;
        header.style.alignSelf = Align.Center;
        root.Add(header);

        // 2. Excel 工具设置
        var excelGroup = CreateGroup("Excel 导表工具 (Editor Only)");
        excelGroup.Add(new PropertyField(serializedObject.FindProperty("ExcelSourceFolder"), "Excel 源文件夹"));
        excelGroup.Add(new PropertyField(serializedObject.FindProperty("CsvOutputFolder"), "CSV 输出文件夹"));
        root.Add(excelGroup);

        // 3. 运行时资源路径 (Foldout)
        var pathsFoldout = new Foldout { text = "运行时资源路径 (Resources)", value = false };
        pathsFoldout.Add(new PropertyField(serializedObject.FindProperty("VNScriptResPath"), "剧本路径"));
        pathsFoldout.Add(new PropertyField(serializedObject.FindProperty("BackgroundResPath"), "背景图片(Background)"));
        pathsFoldout.Add(new PropertyField(serializedObject.FindProperty("VideoResPath"), "视频文件(Videos)"));
        pathsFoldout.Add(new PropertyField(serializedObject.FindProperty("CharacterResPath"), "角色配置(Characters)"));
        pathsFoldout.Add(new PropertyField(serializedObject.FindProperty("BgmResPath"), "背景音乐(BGM)"));
        pathsFoldout.Add(new PropertyField(serializedObject.FindProperty("SFXResPath"), "音效(SFX)"));
        pathsFoldout.Add(new PropertyField(serializedObject.FindProperty("VoiceResPath"), "语音(Voice)"));
        pathsFoldout.Add(new PropertyField(serializedObject.FindProperty("ParticalEffectPath"), "粒子特效(ParticalEffect)"));
        pathsFoldout.Add(new PropertyField(serializedObject.FindProperty("AnimationPath"), "动画(Animation)"));
        root.Add(pathsFoldout);

        // 3.5. UI 默认资源 (Foldout)
        var uiDefaultFoldout = new Foldout { text = "UI 默认资源", value = false };
        uiDefaultFoldout.Add(new PropertyField(serializedObject.FindProperty("DefaultSpeakerBoxSprite"), "默认姓名框"));
        uiDefaultFoldout.Add(new PropertyField(serializedObject.FindProperty("DefaultHeadFrameSprite"), "默认头像边框"));
        root.Add(uiDefaultFoldout);

        // 4. UI 预制体路径 (Foldout)
        var uiFoldout = new Foldout { text = "UI 预制体路径", value = false };
        uiFoldout.Add(new PropertyField(serializedObject.FindProperty("UI_VNGamePlayPath"), "游戏面板"));
        uiFoldout.Add(new PropertyField(serializedObject.FindProperty("UI_HistoryPath"), "历史记录"));
        uiFoldout.Add(new PropertyField(serializedObject.FindProperty("UI_SettingsPath"), "设置面板"));
        uiFoldout.Add(new PropertyField(serializedObject.FindProperty("UI_SaveLoadPath"), "存读档面板"));
        uiFoldout.Add(new PropertyField(serializedObject.FindProperty("UI_ConfirmPath"), "确认弹窗"));
        uiFoldout.Add(new PropertyField(serializedObject.FindProperty("UI_PromptPath"), "信息提示框"));
        uiFoldout.Add(new PropertyField(serializedObject.FindProperty("UI_ChoicePath"), "选项面板"));
        uiFoldout.Add(new PropertyField(serializedObject.FindProperty("UI_MainMenuPath"), "游戏主界面"));
        uiFoldout.Add(new PropertyField(serializedObject.FindProperty("UI_PausePath"), "暂停面板"));
        uiFoldout.Add(new PropertyField(serializedObject.FindProperty("UI_GalleryPath"), "画廊面板"));
        uiFoldout.Add(new PropertyField(serializedObject.FindProperty("UI_LoadingPath"), "加载面板"));
        root.Add(uiFoldout);

        //音效/视频Obj路径(Foldout)
        var objFoldout = new Foldout { text = "音效/视频Obj路径", value = false };
        objFoldout.Add(new PropertyField(serializedObject.FindProperty("SoundObjPath"), "音效Obj"));
        objFoldout.Add(new PropertyField(serializedObject.FindProperty("VideoObjPath"), "视频Obj"));
        root.Add(objFoldout);

        // 画廊内容路径 (Foldout)
        var galleryFoldout = new Foldout { text = "画廊内容路径", value = false };
        galleryFoldout.Add(new PropertyField(serializedObject.FindProperty("CG_DataPath"), "CG数据容器路径"));
        galleryFoldout.Add(new PropertyField(serializedObject.FindProperty("Music_DataPath"), "音乐数据容器路径"));
        galleryFoldout.Add(new PropertyField(serializedObject.FindProperty("Scene_DataPath"), "场景数据容器路径"));
        root.Add(galleryFoldout);

        // 5. 游戏启动设置 (Foldout)
        var gameStartFoldout = new Foldout { text = "开始新游戏设置", value = true };
        gameStartFoldout.Add(new PropertyField(serializedObject.FindProperty("DefaultScriptName"), "默认剧本名称"));
        gameStartFoldout.Add(new PropertyField(serializedObject.FindProperty("DefaultLineID"), "默认行ID (可选)"));
        
        // 添加提示信息
        var gameStartHelp = new HelpBox("设置主界面点击新游戏时加载的默认剧本和起始位置。\n" +
            "• 剧本名称：不含扩展名，例如 Test101\n" +
            "• 行ID：留空则从剧本开头开始，填写则从指定行ID开始", HelpBoxMessageType.Info);
        gameStartFoldout.Add(gameStartHelp);
        
        root.Add(gameStartFoldout);

        // 6. AES 加密设置 (高级逻辑)
        var aesFoldout = new Foldout { text = "存档加密设置", value = true };

        // 获取 UseAES 属性
        var useAESProp = serializedObject.FindProperty("UseAES");
        var useAESField = new PropertyField(useAESProp, "启用加密");
        aesFoldout.Add(useAESField);

        // 加密详情容器
        var aesDetails = new VisualElement();

        // Key 输入框 + 校验
        var keyProp = serializedObject.FindProperty("Key");
        var keyField = new PropertyField(keyProp);
        var keyError = new HelpBox("Key 必须正好是 32 个字符！", HelpBoxMessageType.Error);
        keyError.style.display = DisplayStyle.None;

        keyField.RegisterCallback<ChangeEvent<string>>(evt => CheckLength(evt.newValue, 32, keyError));
        CheckLength(keyProp.stringValue, 32, keyError);

        // IV 输入框 + 校验
        var ivProp = serializedObject.FindProperty("IV");
        var ivField = new PropertyField(ivProp);
        var ivError = new HelpBox("IV 必须正好是 16 个字符！", HelpBoxMessageType.Error);
        ivError.style.display = DisplayStyle.None;

        ivField.RegisterCallback<ChangeEvent<string>>(evt => CheckLength(evt.newValue, 16, ivError));
        CheckLength(ivProp.stringValue, 16, ivError);

        // 生成按钮
        var genBtn = new Button(() =>
        {
            keyProp.stringValue = GenerateRandomString(32);
            ivProp.stringValue = GenerateRandomString(16);
            serializedObject.ApplyModifiedProperties();
            CheckLength(keyProp.stringValue, 32, keyError);
            CheckLength(ivProp.stringValue, 16, ivError);
        })
        { text = "🎲 生成随机密钥", style = { marginTop = 5 } };

        aesDetails.Add(keyField);
        aesDetails.Add(keyError);
        aesDetails.Add(ivField);
        aesDetails.Add(ivError);
        aesDetails.Add(genBtn);

        // 初始显示状态
        aesDetails.style.display = useAESProp.boolValue ? DisplayStyle.Flex : DisplayStyle.None;

        // 【修复】此处变量名由 useAES 改为 useAESProp
        root.TrackPropertyValue(useAESProp, prop =>
        {
            aesDetails.style.display = prop.boolValue ? DisplayStyle.Flex : DisplayStyle.None;
        });

        aesFoldout.Add(aesDetails);
        root.Add(aesFoldout);

        return root;
    }

    // --- 辅助方法 ---

    private VisualElement CreateGroup(string title)
    {
        var box = new Box();
        box.style.marginTop = 10;
        box.style.marginBottom = 10;
        box.style.paddingTop = 5;
        box.style.paddingBottom = 5;
        box.style.paddingLeft = 5;
        box.style.paddingRight = 5;

        // 【修复】新版 API 必须分别设置四边
        box.style.borderTopWidth = 1;
        box.style.borderBottomWidth = 1;
        box.style.borderLeftWidth = 1;
        box.style.borderRightWidth = 1;

        Color borderColor = new Color(0.3f, 0.3f, 0.3f);
        box.style.borderTopColor = borderColor;
        box.style.borderBottomColor = borderColor;
        box.style.borderLeftColor = borderColor;
        box.style.borderRightColor = borderColor;

        var label = new Label(title);
        label.style.unityFontStyleAndWeight = FontStyle.Bold;
        label.style.marginBottom = 5;
        box.Add(label);

        return box;
    }

    private void CheckLength(string val, int targetLen, HelpBox errorBox)
    {
        if (val == null) return;
        errorBox.style.display = val.Length != targetLen ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private string GenerateRandomString(int length)
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        var random = new System.Random();
        return new string(Enumerable.Repeat(chars, length)
          .Select(s => s[random.Next(s.Length)]).ToArray());
    }
}