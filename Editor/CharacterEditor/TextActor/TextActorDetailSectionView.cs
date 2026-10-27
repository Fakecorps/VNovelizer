using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using TMPro;
using VNovelizer.Core.Theater;

/// <summary>
/// 文字角色详情面板专属区块（核心角色编辑器 Tab 区）：
/// 面板 Prefab 拖拽 + 播放参数（显示方式/速度覆盖/说话者/段落上限）
/// + 「打开面板 Prefab」「生成内置默认面板预制体」按钮。
/// 所有变更即时 SetDirty；预制体生成后经 onRebuild 刷新面板。
/// </summary>
public class TextActorDetailSectionView : VisualElement
{
    private readonly TextActorProfile profile;
    private readonly Action onRebuild;

    public TextActorDetailSectionView(TextActorProfile profile, Action onRebuild)
    {
        this.profile = profile;
        this.onRebuild = onRebuild;
        style.flexGrow = 1;
        Build();
    }

    private void Build()
    {
        var prefabField = new ObjectField("面板 Prefab")
        {
            objectType = typeof(GameObject),
            value = profile.PanelPrefab,
            allowSceneObjects = false
        };
        prefabField.style.marginBottom = 5;
        prefabField.RegisterValueChangedCallback(evt =>
        {
            profile.PanelPrefab = evt.newValue as GameObject;
            EditorUtility.SetDirty(profile);
        });
        Add(prefabField);

        var btnRow = new VisualElement();
        btnRow.style.flexDirection = FlexDirection.Row;
        btnRow.style.marginBottom = 12;

        var openBtn = new Button(() =>
        {
            if (profile.PanelPrefab != null) AssetDatabase.OpenAsset(profile.PanelPrefab);
            else Debug.LogWarning("[TextActor] 尚未指定面板 Prefab");
        }) { text = "打开面板 Prefab" };
        GalleryStyles.ApplyButton(openBtn, GalleryTheme.BgCard, false);
        openBtn.style.fontSize = 11;
        btnRow.Add(openBtn);

        var genBtn = new Button(() =>
        {
            GeneratePrefabCopy();
            onRebuild?.Invoke();
        }) { text = "生成内置默认面板预制体" };
        GalleryStyles.ApplyButton(genBtn, GalleryTheme.AccentDim, true);
        genBtn.style.fontSize = 11;
        genBtn.style.marginLeft = 6;
        btnRow.Add(genBtn);

        Add(btnRow);

        var directToggle = new Toggle("瞬间全显（关闭 = 逐字打字）")
        { value = profile.DirectDisplay };
        directToggle.style.marginBottom = 5;
        directToggle.RegisterValueChangedCallback(evt =>
        {
            profile.DirectDisplay = evt.newValue;
            EditorUtility.SetDirty(profile);
        });
        Add(directToggle);

        var speedField = new FloatField("打字速度（秒/字符，<0 = 跟随全局）")
        { value = profile.TextSpeedOverride };
        speedField.style.marginBottom = 5;
        speedField.RegisterValueChangedCallback(evt =>
        {
            profile.TextSpeedOverride = evt.newValue;
            EditorUtility.SetDirty(profile);
        });
        Add(speedField);

        var speakerToggle = new Toggle("段落内显示说话者姓名")
        { value = profile.ShowSpeaker };
        speakerToggle.style.marginBottom = 5;
        speakerToggle.RegisterValueChangedCallback(evt =>
        {
            profile.ShowSpeaker = evt.newValue;
            EditorUtility.SetDirty(profile);
        });
        Add(speakerToggle);

        var maxField = new IntegerField("最大段落数（0 = 不限，超出移除最旧）")
        { value = profile.MaxParagraphs };
        maxField.RegisterValueChangedCallback(evt =>
        {
            profile.MaxParagraphs = Mathf.Max(0, evt.newValue);
            EditorUtility.SetDirty(profile);
        });
        Add(maxField);
    }

    /// <summary>
    /// 用内置构造器生成面板预制体副本到项目 Assets 并赋给角色（面板样式从此在 Prefab 里编辑）。
    /// 运行/编辑两域共用同一构造器，保证「生成的面板」与「运行时兜底面版」逐字段一致。
    /// </summary>
    private void GeneratePrefabCopy()
    {
        string path = EditorUtility.SaveFilePanelInProject(
            "保存文字面板预制体", $"{profile.CharacterID}_Panel", "prefab",
            "选择预制体保存位置（生成后自动赋给本角色的面板 Prefab）", "Assets");
        if (string.IsNullOrEmpty(path)) return;

        // 编辑器内不依赖运行时资源链：直接经编辑器解析器取包内思源黑体 SDF
        // （未注册/取不到时 Build 内部兜底 TMP 全局默认字体）
        var font = VNEditorResourceResolver.LoadByKey<TMP_FontAsset>(TextActorDefaultPanelBuilder.DefaultFontKey);
        var go = TextActorDefaultPanelBuilder.Build(profile, font);

        // 让模板在预制体里可见可编辑（运行时实例化时 TextActorView 会自动禁用模板并排除出布局）
        var view = go.GetComponent<TextActorView>();
        if (view != null && view.ParagraphTemplate != null)
        {
            view.ParagraphTemplate.gameObject.SetActive(true);
            if (view.TemplateText != null) view.TemplateText.text = "正文段落模板（克隆源，运行时不显示）";
        }

        GameObject saved;
        try
        {
            saved = PrefabUtility.SaveAsPrefabAsset(go, path);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(go);
        }

        if (saved == null)
        {
            Debug.LogError($"[TextActor] 预制体生成失败: {path}");
            return;
        }

        profile.PanelPrefab = saved;
        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();
    }
}
