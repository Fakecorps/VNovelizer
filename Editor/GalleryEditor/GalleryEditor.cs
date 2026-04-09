using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public class GalleryEditor : EditorWindow
{
    private enum EditorMode { CG, Music, Scene }
    private EditorMode currentMode = EditorMode.CG;

    // --- 数据容器 ---
    private CGDataContainer cgContainer;
    private MusicDataContainer musicContainer;
    private SceneDataContainer sceneContainer;

    // --- UI 引用 ---
    private VisualElement root;
    private ListView leftList;
    private VisualElement rightPane;

    // --- 选中项 ---
    private object selectedItem;

    [MenuItem("VNovelizer/画廊编辑器 (Gallery Editor)", false, 26)]
    public static void ShowWindow()
    {
        var wnd = GetWindow<GalleryEditor>();
        wnd.titleContent = new GUIContent("画廊编辑器");
        wnd.minSize = new Vector2(900, 600);
    }

    private void OnEnable()
    {
        LoadContainers();
    }

    private void LoadContainers()
    {
        // 1. 加载 CG 数据
        string cgPath = "VNovelizerRes/Data/CGDataContainer";
        if (VNProjectConfig.Instance != null && !string.IsNullOrEmpty(VNProjectConfig.Instance.CG_DataPath))
            cgPath = VNProjectConfig.Instance.CG_DataPath + "/CGDataContainer";
        cgContainer = Resources.Load<CGDataContainer>(cgPath);

        // 2. 加载 Music 数据
        string musicPath = "VNovelizerRes/Data/MusicDataContainer";
        if (VNProjectConfig.Instance != null && !string.IsNullOrEmpty(VNProjectConfig.Instance.Music_DataPath))
            musicPath = VNProjectConfig.Instance.Music_DataPath + "/MusicDataContainer";
        musicContainer = Resources.Load<MusicDataContainer>(musicPath);

        // 3. 加载 Scene 数据
        string scenePath = "VNovelizerRes/Data/SceneDataContainer";
        if (VNProjectConfig.Instance != null && !string.IsNullOrEmpty(VNProjectConfig.Instance.Scene_DataPath))
            scenePath = VNProjectConfig.Instance.Scene_DataPath + "/SceneDataContainer";
        sceneContainer = Resources.Load<SceneDataContainer>(scenePath);
    }

    public void CreateGUI()
    {
        root = rootVisualElement;
        root.style.backgroundColor = new Color(0.2f, 0.2f, 0.2f);

        //顶部：模式切换
        var toolbar = new VisualElement();
        toolbar.style.flexDirection = FlexDirection.Row;
        toolbar.style.height = 40;
        toolbar.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f);
        toolbar.style.alignItems = Align.Center;
        toolbar.style.paddingLeft = 10;
        toolbar.style.borderBottomWidth = 1;
        toolbar.style.borderBottomColor = Color.black;

        var cgBtn = new Button(() => SwitchMode(EditorMode.CG)) { text = "CG 管理", style = { height = 30, width = 120 } };
        var musicBtn = new Button(() => SwitchMode(EditorMode.Music)) { text = "音乐管理", style = { height = 30, width = 120 } };
        var sceneBtn = new Button(() => SwitchMode(EditorMode.Scene)) { text = "场景管理", style = { height = 30, width = 120 } };

        toolbar.Add(cgBtn);
        toolbar.Add(musicBtn);
        toolbar.Add(sceneBtn);
        root.Add(toolbar);

        //主内容区 (分栏)
        var splitView = new TwoPaneSplitView(0, 250, TwoPaneSplitViewOrientation.Horizontal);
        root.Add(splitView);

        //左侧列表
        var leftPane = new VisualElement();
        leftPane.style.backgroundColor = new Color(0.18f, 0.18f, 0.18f);

        //列表工具栏
        var listToolbar = new VisualElement();
        listToolbar.style.flexDirection = FlexDirection.Row;
        listToolbar.style.paddingTop = 5; listToolbar.style.paddingBottom = 5;
        listToolbar.style.paddingLeft = 5; listToolbar.style.paddingRight = 5;

        listToolbar.Add(new Button(CreateNewItem) { text = "新建项目", style = { flexGrow = 1 } });
        leftPane.Add(listToolbar);

        leftList = new ListView();
        leftList.makeItem = () => new Label() { style = { paddingLeft = 10, paddingTop = 5, paddingBottom = 5 } };
        leftList.selectionType = SelectionType.Single;
        leftList.selectionChanged += OnSelectionChanged;
        leftList.style.flexGrow = 1;
        leftPane.Add(leftList);

        splitView.Add(leftPane);

        //右侧详情
        rightPane = new VisualElement();
        rightPane.style.paddingTop = 10;
        rightPane.style.paddingLeft = 20;
        rightPane.style.paddingRight = 20;
        splitView.Add(rightPane);

        //初始刷新
        SwitchMode(EditorMode.CG);
    }

    private void SwitchMode(EditorMode mode)
    {
        currentMode = mode;
        rightPane.Clear();
        selectedItem = null;
        leftList.ClearSelection();
        leftList.itemsSource = null;

        if (mode == EditorMode.CG)
        {
            if (cgContainer == null)
            {
                rightPane.Add(CreateMissingContainerUI("CGDataContainer", EditorMode.CG));
                return;
            }
            leftList.bindItem = (e, i) => { (e as Label).text = string.IsNullOrEmpty(cgContainer.cgList[i].cgName) ? "[未命名]" : cgContainer.cgList[i].cgName; };
            leftList.itemsSource = cgContainer.cgList;
        }
        else if (mode == EditorMode.Music)
        {
            if (musicContainer == null)
            {
                rightPane.Add(CreateMissingContainerUI("MusicDataContainer", EditorMode.Music));
                return;
            }
            leftList.bindItem = (e, i) => { (e as Label).text = string.IsNullOrEmpty(musicContainer.musicList[i].name) ? "[未命名]" : musicContainer.musicList[i].name; };
            leftList.itemsSource = musicContainer.musicList;
        }
        else
        {
            if (sceneContainer == null)
            {
                rightPane.Add(CreateMissingContainerUI("SceneDataContainer", EditorMode.Scene));
                return;
            }
            leftList.bindItem = (e, i) => { (e as Label).text = string.IsNullOrEmpty(sceneContainer.sceneList[i].VNscriptID) ? "[未命名]" : sceneContainer.sceneList[i].VNscriptID; };
            leftList.itemsSource = sceneContainer.sceneList;
        }

        leftList.Rebuild();
    }

    private VisualElement CreateMissingContainerUI(string name, EditorMode mode)
    {
        var box = new VisualElement();
        box.style.alignItems = Align.Center;
        box.style.paddingTop = 50;

        box.Add(new Label($"未找到 {name}！\n请点击下方按钮创建。") { style = { color = Color.red, fontSize = 16, unityTextAlign = TextAnchor.MiddleCenter } });
        box.Add(new Button(() => CreateContainer(name, mode)) { text = "立即创建", style = { width = 150, marginTop = 20 } });

        return box;
    }

    private void CreateContainer(string name, EditorMode mode)
    {
        string folder = "Assets/Resources/VNovelizerRes/Data";
        if (VNProjectConfig.Instance != null)
        {
            if (mode == EditorMode.CG && !string.IsNullOrEmpty(VNProjectConfig.Instance.CG_DataPath))
                folder = "Assets/Resources/" + VNProjectConfig.Instance.CG_DataPath;
            else if (mode == EditorMode.Music && !string.IsNullOrEmpty(VNProjectConfig.Instance.Music_DataPath))
                folder = "Assets/Resources/" + VNProjectConfig.Instance.Music_DataPath;
            else if (mode == EditorMode.Scene && !string.IsNullOrEmpty(VNProjectConfig.Instance.Scene_DataPath))
                folder = "Assets/Resources/" + VNProjectConfig.Instance.Scene_DataPath;
        }

        if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

        ScriptableObject so;
        if (name == "CGDataContainer") so = ScriptableObject.CreateInstance<CGDataContainer>();
        else if (name == "MusicDataContainer") so = ScriptableObject.CreateInstance<MusicDataContainer>();
        else so = ScriptableObject.CreateInstance<SceneDataContainer>();

        AssetDatabase.CreateAsset(so, $"{folder}/{name}.asset");
        AssetDatabase.SaveAssets();

        LoadContainers();
        SwitchMode(currentMode);
    }

    private void CreateNewItem()
    {
        if (currentMode == EditorMode.CG)
        {
            if (cgContainer == null) return;
            cgContainer.AddCGData(new CGData($"New_CG_{cgContainer.cgList.Count + 1}"));
            EditorUtility.SetDirty(cgContainer);
        }
        else if (currentMode == EditorMode.Music)
        {
            if (musicContainer == null) return;
            musicContainer.AddMusic(new VNMusic($"New_Music_{musicContainer.musicList.Count + 1}"));
            EditorUtility.SetDirty(musicContainer);
        }
        else // Scene
        {
            if (sceneContainer == null) return;
            sceneContainer.AddScene(new VNScene($"New_Scene_{sceneContainer.sceneList.Count + 1}"));
            EditorUtility.SetDirty(sceneContainer);
        }

        leftList.Rebuild();
        leftList.SetSelection(leftList.itemsSource.Count - 1);
    }

    private void OnSelectionChanged(IEnumerable<object> selection)
    {
        rightPane.Clear();
        foreach (var item in selection)
        {
            selectedItem = item;
            if (currentMode == EditorMode.CG) DrawCGDetail(item as CGData);
            else if (currentMode == EditorMode.Music) DrawMusicDetail(item as VNMusic);
            else DrawSceneDetail(item as VNScene);
            break;
        }
    }

    // =========================================================
    //                    详情页绘制逻辑
    // =========================================================

    // --- CG 详情页 ---
    private void DrawCGDetail(CGData cg)
    {
        if (cg == null) return;
        
        // 【Bug修复】先清空 rightPane，防止重复添加内容
        rightPane.Clear();

        // 1. ID + 删除
        DrawHeader("CG ID", cg.cgName, (val) => {
            cg.cgName = val;
            EditorUtility.SetDirty(cgContainer);
            leftList.RefreshItem(cgContainer.cgList.IndexOf(cg));
        }, () => {
            if (EditorUtility.DisplayDialog("删除", $"确定删除 {cg.cgName} 吗?", "是", "否"))
            {
                cgContainer.RemoveCGData(cg);
                EditorUtility.SetDirty(cgContainer);
                rightPane.Clear();
                leftList.Rebuild();
            }
        });

        // 2. Unlock
        var unlock = new Toggle("已解锁 (Debug)") { value = cg.isUnlocked };
        unlock.RegisterValueChangedCallback(evt => { cg.isUnlocked = evt.newValue; EditorUtility.SetDirty(cgContainer); });
        rightPane.Add(unlock);

        // 3. Locked Sprite
        DrawSpriteField("未解锁占位图", cg.lockedSprite, (val) => { cg.lockedSprite = val; EditorUtility.SetDirty(cgContainer); });

        // 4. 图片列表标题
        var listHeader = new VisualElement();
        listHeader.style.flexDirection = FlexDirection.Row;
        listHeader.style.marginTop = 20;
        listHeader.style.backgroundColor = new Color(0.25f, 0.25f, 0.25f);
        listHeader.style.paddingLeft = 5; listHeader.style.paddingRight = 5;
        listHeader.style.height = 24;
        listHeader.style.alignItems = Align.Center;

        listHeader.Add(new Label("差分图片 (Sprites)") { style = { flexGrow = 1, unityFontStyleAndWeight = FontStyle.Bold } });

        var addBtn = new Button(() => {
            cg.sprites.Add(null);
            EditorUtility.SetDirty(cgContainer);
            // 这里我们需要刷新 ListView，而不是整个面板
            // 为了简单，我们还是刷新整个面板，但在使用 ListView 后这不会有问题
            DrawCGDetail(cg);
        })
        { text = "+" };
        listHeader.Add(addBtn);

        rightPane.Add(listHeader);

        // 5. 图片列表 (使用 ListView)
        var spriteList = new ListView();
        spriteList.style.flexGrow = 1;
        spriteList.style.minHeight = 200; // 给个最小高度
        spriteList.itemsSource = cg.sprites;
        spriteList.fixedItemHeight = 70; // 固定高度，确保显示正常

        // 创建列表项 UI
        spriteList.makeItem = () =>
        {
            var container = new VisualElement();
            // 复用 DrawSpriteListRow 的逻辑，但这里我们需要返回一个 VisualElement
            // 为了复用代码，我们这里手动构建结构
            return container;
        };

        // 绑定数据
        spriteList.bindItem = (element, index) =>
        {
            element.Clear(); // 清理旧内容

            // 使用辅助方法绘制行，注意传入的是 element
            DrawSpriteListRow(element, cg.sprites[index], (val) => {
                cg.sprites[index] = val;
                EditorUtility.SetDirty(cgContainer);
            }, () => {
                cg.sprites.RemoveAt(index);
                EditorUtility.SetDirty(cgContainer);
                spriteList.Rebuild(); // 刷新列表
            });
        };

        rightPane.Add(spriteList);
    }

    // --- Music 详情页 ---
    private void DrawMusicDetail(VNMusic music)
    {
        if (music == null) return;

        // 1. ID + 删除
        DrawHeader("音乐名称", music.name, (val) => {
            music.name = val;
            EditorUtility.SetDirty(musicContainer);
            leftList.RefreshItem(musicContainer.musicList.IndexOf(music));
        }, () => {
            if (EditorUtility.DisplayDialog("删除", $"确定删除 {music.name} 吗?", "是", "否"))
            {
                musicContainer.RemoveMusic(music);
                EditorUtility.SetDirty(musicContainer);
                rightPane.Clear();
                leftList.Rebuild();
            }
        });

        // 2. Unlock
        var unlock = new Toggle("已解锁 (Debug)") { value = music.isUnlocked };
        unlock.RegisterValueChangedCallback(evt => { music.isUnlocked = evt.newValue; EditorUtility.SetDirty(musicContainer); });
        rightPane.Add(unlock);

        // 3. Cover
        DrawSpriteField("封面图 (Cover)", music.picture, (val) => { music.picture = val; EditorUtility.SetDirty(musicContainer); });

        // 4. Audio Clip
        var clipField = new ObjectField("音频文件 (Clip)") { objectType = typeof(AudioClip), value = music.music };
        clipField.style.marginTop = 10;
        clipField.RegisterValueChangedCallback(evt => {
            music.music = evt.newValue as AudioClip;
            EditorUtility.SetDirty(musicContainer);
        });
        rightPane.Add(clipField);
    }

    // --- 【修改】Scene 详情页 ---
    private void DrawSceneDetail(VNScene scene)
    {
        if (scene == null) return;

        // 1. ID + 删除
        DrawHeader("场景 ID", scene.VNscriptID, (val) => {
            scene.VNscriptID = val;
            EditorUtility.SetDirty(sceneContainer);
            leftList.RefreshItem(sceneContainer.sceneList.IndexOf(scene));
        }, () => {
            if (EditorUtility.DisplayDialog("删除", $"确定删除场景 '{scene.VNscriptID}' 吗?", "是", "否"))
            {
                sceneContainer.RemoveScene(scene);
                EditorUtility.SetDirty(sceneContainer);
                rightPane.Clear();
                leftList.Rebuild();
            }
        });

        // 2. Unlock
        var unlock = new Toggle("已解锁 (Debug)") { value = scene.isUnLocked };
        unlock.RegisterValueChangedCallback(evt => { scene.isUnLocked = evt.newValue; EditorUtility.SetDirty(sceneContainer); });
        rightPane.Add(unlock);

        // 3. Locked Sprite (未解锁图)
        DrawSpriteField("未解锁图 (Locked)", scene.LockedSprite, (val) => { scene.LockedSprite = val; EditorUtility.SetDirty(sceneContainer); });

        // 4. Unlocked Sprite (缩略图)
        DrawSpriteField("缩略图 (Unlocked)", scene.UnLockedSprite, (val) => { scene.UnLockedSprite = val; EditorUtility.SetDirty(sceneContainer); });

        // 5. Script Name
        var scriptNameField = new TextField("剧本文件名") { value = scene.ScriptName };
        scriptNameField.RegisterValueChangedCallback(evt => { scene.ScriptName = evt.newValue; EditorUtility.SetDirty(sceneContainer); });
        rightPane.Add(scriptNameField);

        // 6. Start Line ID
        var startLineField = new TextField("起始行 ID") { value = scene.StartLineID };
        startLineField.RegisterValueChangedCallback(evt => { scene.StartLineID = evt.newValue; EditorUtility.SetDirty(sceneContainer); });
        rightPane.Add(startLineField);

        // 7. End Line ID
        var endLineField = new TextField("结束行 ID") { value = scene.EndLineID };
        endLineField.RegisterValueChangedCallback(evt => { scene.EndLineID = evt.newValue; EditorUtility.SetDirty(sceneContainer); });
        rightPane.Add(endLineField);
    }

    // --- 辅助绘制方法 ---

    private void DrawHeader(string label, string value, System.Action<string> onNameChange, System.Action onDelete)
    {
        var box = new VisualElement();
        box.style.flexDirection = FlexDirection.Row;
        box.style.marginBottom = 10;
        box.style.borderBottomWidth = 1;
        box.style.borderBottomColor = Color.gray;
        box.style.paddingBottom = 10;

        var nameField = new TextField(label) { value = value, style = { flexGrow = 1 } };
        nameField.RegisterValueChangedCallback(evt => onNameChange(evt.newValue));

        var delBtn = new Button(onDelete) { text = "删除" };
        delBtn.style.backgroundColor = new Color(0.6f, 0.2f, 0.2f);

        box.Add(nameField);
        box.Add(delBtn);
        rightPane.Add(box);
    }

    private void DrawSpriteField(string label, Sprite value, System.Action<Sprite> onChange)
    {
        var box = new Box();
        box.style.marginTop = 10;
        box.style.flexDirection = FlexDirection.Row;
        box.style.alignItems = Align.Center;
        box.style.backgroundColor = new Color(0.25f, 0.25f, 0.25f);

        var preview = new Image();
        preview.style.width = 60; preview.style.height = 60;
        preview.scaleMode = ScaleMode.ScaleToFit;
        if (value != null) preview.image = AssetPreview.GetAssetPreview(value);

        var field = new ObjectField(label) { objectType = typeof(Sprite), value = value, style = { flexGrow = 1, marginLeft = 10 } };
        field.RegisterValueChangedCallback(evt => {
            var sprite = evt.newValue as Sprite;
            onChange(sprite);
            preview.image = sprite ? AssetPreview.GetAssetPreview(sprite) : null;
        });

        box.Add(preview);
        box.Add(field);
        rightPane.Add(box);
    }

    private void DrawSpriteListRow(VisualElement parent, Sprite value, System.Action<Sprite> onChange, System.Action onDelete)
    {
        var box = new Box();
        box.style.flexDirection = FlexDirection.Row;
        box.style.marginBottom = 5;
        box.style.backgroundColor = new Color(0.22f, 0.22f, 0.22f);
        box.style.borderBottomWidth = 1; box.style.borderBottomColor = Color.black;

        var preview = new Image();
        preview.style.width = 50; preview.style.height = 50;
        preview.scaleMode = ScaleMode.ScaleToFit;
        if (value != null) preview.image = AssetPreview.GetAssetPreview(value);
        box.Add(preview);

        var field = new ObjectField() { objectType = typeof(Sprite), value = value, style = { flexGrow = 1 } };
        field.RegisterValueChangedCallback(evt => {
            var s = evt.newValue as Sprite;
            onChange(s);
            preview.image = s ? AssetPreview.GetAssetPreview(s) : null;
        });
        box.Add(field);

        var delBtn = new Button(onDelete) { text = "X" };
        delBtn.style.backgroundColor = new Color(0.5f, 0.2f, 0.2f);
        box.Add(delBtn);

        parent.Add(box);
    }
}