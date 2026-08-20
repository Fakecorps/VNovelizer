using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// MVP - Presenter：负责所有业务逻辑。
/// 持有 3 个数据容器 + 剪贴板 + 选中状态 + 模式状态 + 选中记忆 + 待确认删除。
/// 所有写操作走 Undo + SetDirty + SaveAssetIfDirty。
/// </summary>
public class GalleryEditorPresenter
{
    public enum Mode { CG, Music, Scene }

    // --- 数据容器 ---
    public CGDataContainer CgContainer { get; private set; }
    public MusicDataContainer MusicContainer { get; private set; }
    public SceneDataContainer SceneContainer { get; private set; }

    // --- 当前状态 ---
    public Mode CurrentMode { get; private set; } = Mode.CG;
    public object SelectedItem { get; private set; }
    public string SearchText { get; set; } = "";

    // --- 选中记忆 ---
    private readonly Dictionary<Mode, int> lastSelection = new Dictionary<Mode, int>();

    // --- 待确认删除（内联删除按钮"再点一次"语义） ---
    public object PendingDeleteItem { get; private set; }
    public double PendingDeleteTime { get; private set; }
    private const double PendingDeleteTimeout = 3.0;

    // --- 剪贴板 ---
    public object ClipboardItem { get; private set; }
    public Mode ClipboardMode { get; private set; }

    // --- 视图回调 ---
    public System.Action OnDataChanged;
    public System.Action<Mode> OnModeChanged;
    public System.Action<object> OnSelectionChanged;
    public System.Action<string> OnToast;

    // --- AssetPreview 缓存 ---
    private readonly Dictionary<int, Texture2D> previewCache = new Dictionary<int, Texture2D>();

    public void LoadAll()
    {
        // 经编辑器资源键解析器加载（与运行时同键空间：Addressables 地址 → 旧版 Resources → 包内默认）
        string cgPath = "VNovelizerRes/GalleryContent/CG/CGDataContainer";
        if (VNProjectConfig.Instance != null && !string.IsNullOrEmpty(VNProjectConfig.Instance.CG_DataPath))
            cgPath = VNProjectConfig.Instance.CG_DataPath + "/CGDataContainer";
        CgContainer = VNEditorResourceResolver.LoadByKey<CGDataContainer>(cgPath);

        string musicPath = "VNovelizerRes/GalleryContent/Music/MusicDataContainer";
        if (VNProjectConfig.Instance != null && !string.IsNullOrEmpty(VNProjectConfig.Instance.Music_DataPath))
            musicPath = VNProjectConfig.Instance.Music_DataPath + "/MusicDataContainer";
        MusicContainer = VNEditorResourceResolver.LoadByKey<MusicDataContainer>(musicPath);

        string scenePath = "VNovelizerRes/GalleryContent/Scene/SceneDataContainer";
        if (VNProjectConfig.Instance != null && !string.IsNullOrEmpty(VNProjectConfig.Instance.Scene_DataPath))
            scenePath = VNProjectConfig.Instance.Scene_DataPath + "/SceneDataContainer";
        SceneContainer = VNEditorResourceResolver.LoadByKey<SceneDataContainer>(scenePath);

        previewCache.Clear();
    }

    public void ClearPreviewCache() => previewCache.Clear();

    public Texture2D GetCachedPreview(Sprite sprite)
    {
        if (sprite == null) return null;
        int id = sprite.GetInstanceID();
        if (previewCache.TryGetValue(id, out var tex)) return tex;

        var preview = AssetPreview.GetAssetPreview(sprite);
        if (preview == null)
            preview = AssetPreview.GetMiniThumbnail(sprite) as Texture2D;

        if (preview != null)
            previewCache[id] = preview;

        return preview;
    }

    // =========================================================
    //                      数据源
    // =========================================================
    public IList GetSourceList(Mode mode)
    {
        if (mode == Mode.CG && CgContainer != null) return CgContainer.cgList;
        if (mode == Mode.Music && MusicContainer != null) return MusicContainer.musicList;
        if (mode == Mode.Scene && SceneContainer != null) return SceneContainer.sceneList;
        return new List<object>();
    }

    public ScriptableObject GetCurrentContainer()
    {
        if (CurrentMode == Mode.CG) return CgContainer;
        if (CurrentMode == Mode.Music) return MusicContainer;
        return SceneContainer;
    }

    // =========================================================
    //                      模式切换
    // =========================================================
    public void SwitchMode(Mode mode)
    {
        CurrentMode = mode;
        SelectedItem = null;
        PendingDeleteItem = null;
        OnModeChanged?.Invoke(mode);
    }

    public void SetSelected(object item, int index)
    {
        SelectedItem = item;
        if (item != null) lastSelection[CurrentMode] = index;
        PendingDeleteItem = null;
        OnSelectionChanged?.Invoke(item);
    }

    public int GetLastSelection(Mode mode)
    {
        return lastSelection.TryGetValue(mode, out int idx) ? idx : -1;
    }

    // =========================================================
    //                      CRUD
    // =========================================================
    public void CreateNew()
    {
        var container = GetCurrentContainer();
        if (container == null) return;
        Undo.RecordObject(container, "Gallery: Add Item");

        object newItem;
        int newIndex;

        if (CurrentMode == Mode.CG)
        {
            var cg = new CGData($"New_CG_{CgContainer.cgList.Count + 1}");
            CgContainer.AddCGData(cg);
            newItem = cg;
            newIndex = CgContainer.cgList.Count - 1;
        }
        else if (CurrentMode == Mode.Music)
        {
            var music = new VNMusic($"New_Music_{MusicContainer.musicList.Count + 1}");
            MusicContainer.AddMusic(music);
            newItem = music;
            newIndex = MusicContainer.musicList.Count - 1;
        }
        else
        {
            var scene = new VNScene($"New_Scene_{SceneContainer.sceneList.Count + 1}");
            SceneContainer.AddScene(scene);
            newItem = scene;
            newIndex = SceneContainer.sceneList.Count - 1;
        }

        EditorUtility.SetDirty(container);
        AssetDatabase.SaveAssetIfDirty(container);

        SelectedItem = newItem;
        lastSelection[CurrentMode] = newIndex;

        OnDataChanged?.Invoke();
        OnSelectionChanged?.Invoke(newItem);
        OnToast?.Invoke("已新建");
    }

    public void RenameSelected(string newName)
    {
        if (SelectedItem == null) return;
        var container = GetCurrentContainer();
        if (container == null) return;
        Undo.RecordObject(container, "Gallery: Rename");
        ApplyName(SelectedItem, newName);
        EditorUtility.SetDirty(container);
        AssetDatabase.SaveAssetIfDirty(container);
        OnDataChanged?.Invoke();
    }

    public void CopySelected()
    {
        if (SelectedItem == null) return;
        ClipboardItem = SelectedItem;
        ClipboardMode = CurrentMode;
        OnToast?.Invoke("已复制");
    }

    public void Paste()
    {
        if (ClipboardItem == null || ClipboardMode != CurrentMode) return;
        var container = GetCurrentContainer();
        if (container == null) return;
        Undo.RecordObject(container, "Gallery: Paste");

        if (CurrentMode == Mode.CG && ClipboardItem is CGData srcCg)
        {
            var copy = new CGData(srcCg.cgName + "_copy")
            {
                isUnlocked = srcCg.isUnlocked,
                lockedSprite = srcCg.lockedSprite,
                sprites = new List<Sprite>(srcCg.sprites)
            };
            CgContainer.AddCGData(copy);
        }
        else if (CurrentMode == Mode.Music && ClipboardItem is VNMusic srcM)
        {
            var copy = new VNMusic(srcM.name + "_copy")
            {
                isUnlocked = srcM.isUnlocked,
                picture = srcM.picture,
                music = srcM.music
            };
            MusicContainer.AddMusic(copy);
        }
        else if (CurrentMode == Mode.Scene && ClipboardItem is VNScene srcS)
        {
            var copy = new VNScene(srcS.VNscriptID + "_copy")
            {
                ScriptName = srcS.ScriptName,
                StartLineID = srcS.StartLineID,
                EndLineID = srcS.EndLineID,
                LockedSprite = srcS.LockedSprite,
                UnLockedSprite = srcS.UnLockedSprite,
                isUnLocked = srcS.isUnLocked
            };
            SceneContainer.AddScene(copy);
        }

        EditorUtility.SetDirty(container);
        AssetDatabase.SaveAssetIfDirty(container);
        OnDataChanged?.Invoke();
        OnToast?.Invoke("已粘贴");
    }

    public void Move(int index, int dir)
    {
        var list = GetSourceList(CurrentMode);
        int target = index + dir;
        if (target < 0 || target >= list.Count) return;
        var container = GetCurrentContainer();
        if (container == null) return;
        Undo.RecordObject(container, "Gallery: Reorder");
        var tmp = list[index];
        list[index] = list[target];
        list[target] = tmp;
        EditorUtility.SetDirty(container);
        AssetDatabase.SaveAssetIfDirty(container);
        OnDataChanged?.Invoke();
    }

    /// <summary>
    /// 内联删除：首次调用标记 pending，二次调用（3秒内同对象）才真正删除。
    /// </summary>
    public bool DeleteSelected()
    {
        if (SelectedItem == null) return false;
        var container = GetCurrentContainer();
        if (container == null) return false;

        if (PendingDeleteItem == SelectedItem
            && EditorApplication.timeSinceStartup - PendingDeleteTime < PendingDeleteTimeout)
        {
            Undo.RecordObject(container, "Gallery: Delete");
            DoDelete(SelectedItem);
            EditorUtility.SetDirty(container);
            AssetDatabase.SaveAssetIfDirty(container);
            PendingDeleteItem = null;
            SelectedItem = null;
            OnSelectionChanged?.Invoke(null);
            OnDataChanged?.Invoke();
            OnToast?.Invoke("已删除");
            return true;
        }

        PendingDeleteItem = SelectedItem;
        PendingDeleteTime = EditorApplication.timeSinceStartup;
        OnSelectionChanged?.Invoke(SelectedItem);
        OnToast?.Invoke("再次点击删除按钮以确认");
        return false;
    }

    /// <summary>
    /// 通过右键菜单直接删除，弹原生确认框。
    /// </summary>
    public void DeleteWithDialog(object item)
    {
        if (item == null) return;
        string name = GetName(item);
        if (!EditorUtility.DisplayDialog("删除", $"确定删除 {name} 吗?", "是", "否")) return;
        var container = GetCurrentContainer();
        if (container == null) return;
        Undo.RecordObject(container, "Gallery: Delete");
        DoDelete(item);
        EditorUtility.SetDirty(container);
        AssetDatabase.SaveAssetIfDirty(container);
        if (SelectedItem == item)
        {
            SelectedItem = null;
            OnSelectionChanged?.Invoke(null);
        }
        OnDataChanged?.Invoke();
        OnToast?.Invoke("已删除");
    }

    private void DoDelete(object item)
    {
        if (CurrentMode == Mode.CG) CgContainer.RemoveCGData(item as CGData);
        else if (CurrentMode == Mode.Music) MusicContainer.RemoveMusic(item as VNMusic);
        else SceneContainer.RemoveScene(item as VNScene);
    }

    // =========================================================
    //                      CG 差分图
    // =========================================================
    public void AddSpriteAt(CGData cg, int index)
    {
        if (cg == null) return;
        Undo.RecordObject(CgContainer, "Gallery: Add Sprite");
        if (index < 0 || index > cg.sprites.Count) index = cg.sprites.Count;
        cg.sprites.Insert(index, null);
        EditorUtility.SetDirty(CgContainer);
        AssetDatabase.SaveAssetIfDirty(CgContainer);
        OnDataChanged?.Invoke();
        OnSelectionChanged?.Invoke(SelectedItem);
    }

    public void SetSpriteAt(CGData cg, int index, Sprite sprite)
    {
        if (cg == null || index < 0 || index >= cg.sprites.Count) return;
        Undo.RecordObject(CgContainer, "Gallery: Set Sprite");
        cg.sprites[index] = sprite;
        EditorUtility.SetDirty(CgContainer);
        AssetDatabase.SaveAssetIfDirty(CgContainer);
        // 不触发 OnSelectionChanged 避免详情页整体重建（性能优化）
        OnDataChanged?.Invoke();
    }

    public void RemoveSpriteAt(CGData cg, int index)
    {
        if (cg == null || index < 0 || index >= cg.sprites.Count) return;
        Undo.RecordObject(CgContainer, "Gallery: Remove Sprite");
        cg.sprites.RemoveAt(index);
        EditorUtility.SetDirty(CgContainer);
        AssetDatabase.SaveAssetIfDirty(CgContainer);
        OnDataChanged?.Invoke();
        OnSelectionChanged?.Invoke(SelectedItem);
    }

    public void PersistCurrentOrder()
    {
        var container = GetCurrentContainer();
        if (container == null) return;

        Undo.RecordObject(container, "Gallery: Reorder");
        EditorUtility.SetDirty(container);
        AssetDatabase.SaveAssetIfDirty(container);

        if (SelectedItem != null)
        {
            var list = GetSourceList(CurrentMode);
            for (int i = 0; i < list.Count; i++)
            {
                if (ReferenceEquals(list[i], SelectedItem))
                {
                    lastSelection[CurrentMode] = i;
                    break;
                }
            }
        }

        OnDataChanged?.Invoke();
    }

    public void PersistSpriteOrder(CGData cg)
    {
        if (cg == null || CgContainer == null) return;
        Undo.RecordObject(CgContainer, "Gallery: Move Sprite");
        EditorUtility.SetDirty(CgContainer);
        AssetDatabase.SaveAssetIfDirty(CgContainer);
        OnDataChanged?.Invoke();
    }

    public void AddSpritesFromDrag(CGData cg, Sprite[] sprites)
    {
        if (cg == null || sprites == null || sprites.Length == 0) return;
        Undo.RecordObject(CgContainer, "Gallery: Drag Add Sprites");
        cg.sprites.AddRange(sprites);
        EditorUtility.SetDirty(CgContainer);
        AssetDatabase.SaveAssetIfDirty(CgContainer);
        OnDataChanged?.Invoke();
        OnSelectionChanged?.Invoke(SelectedItem);
    }

    // =========================================================
    //                      通用字段写入
    // =========================================================
    public void SetBool(string label, bool value)
    {
        if (SelectedItem == null) return;
        var container = GetCurrentContainer();
        if (container == null) return;
        Undo.RecordObject(container, "Gallery: " + label);
        if (SelectedItem is CGData cg) cg.isUnlocked = value;
        else if (SelectedItem is VNMusic m) m.isUnlocked = value;
        else if (SelectedItem is VNScene s) s.isUnLocked = value;
        EditorUtility.SetDirty(container);
        AssetDatabase.SaveAssetIfDirty(container);
        OnDataChanged?.Invoke();
    }

    public void SetSprite(string label, Sprite sprite)
    {
        if (SelectedItem == null) return;
        var container = GetCurrentContainer();
        if (container == null) return;
        Undo.RecordObject(container, "Gallery: " + label);
        if (SelectedItem is CGData cg) cg.lockedSprite = sprite;
        else if (SelectedItem is VNMusic m) m.picture = sprite;
        else if (SelectedItem is VNScene s)
        {
            if (label.Contains("Locked")) s.LockedSprite = sprite;
            else s.UnLockedSprite = sprite;
        }
        EditorUtility.SetDirty(container);
        AssetDatabase.SaveAssetIfDirty(container);
        OnDataChanged?.Invoke();
    }

    public void SetClip(AudioClip clip)
    {
        if (SelectedItem is VNMusic m)
        {
            Undo.RecordObject(MusicContainer, "Gallery: Set Clip");
            m.music = clip;
            EditorUtility.SetDirty(MusicContainer);
            AssetDatabase.SaveAssetIfDirty(MusicContainer);
            OnDataChanged?.Invoke();
        }
    }

    public void SetSceneField(string label, string value)
    {
        if (SelectedItem is VNScene s)
        {
            Undo.RecordObject(SceneContainer, "Gallery: " + label);
            if (label == "ScriptName") s.ScriptName = value;
            else if (label == "StartLineID") s.StartLineID = value;
            else if (label == "EndLineID") s.EndLineID = value;
            EditorUtility.SetDirty(SceneContainer);
            AssetDatabase.SaveAssetIfDirty(SceneContainer);
            OnDataChanged?.Invoke();
        }
    }

    // =========================================================
    //                      工具
    // =========================================================
    public string GetName(object item)
    {
        if (item is CGData cg) return string.IsNullOrEmpty(cg.cgName) ? "[未命名]" : cg.cgName;
        if (item is VNMusic m) return string.IsNullOrEmpty(m.name) ? "[未命名]" : m.name;
        if (item is VNScene s) return string.IsNullOrEmpty(s.VNscriptID) ? "[未命名]" : s.VNscriptID;
        return "[未命名]";
    }

    public Sprite GetThumbSprite(object item)
    {
        if (item is CGData cg)
        {
            if (cg.sprites != null)
            {
                for (int i = 0; i < cg.sprites.Count; i++)
                {
                    if (cg.sprites[i] != null)
                        return cg.sprites[i];
                }
            }

            return cg.lockedSprite;
        }

        if (item is VNMusic m) return m.picture;
        if (item is VNScene s) return s.UnLockedSprite != null ? s.UnLockedSprite : s.LockedSprite;
        return null;
    }

    public bool IsUnlocked(object item)
    {
        if (item is CGData cg) return cg.isUnlocked;
        if (item is VNMusic m) return m.isUnlocked;
        if (item is VNScene s) return s.isUnLocked;
        return false;
    }

    public string GetSubText(object item)
    {
        if (item is CGData cg) return $"{cg.sprites.Count} 张差分";
        if (item is VNMusic m) return m.music != null ? m.music.name : "无音频";
        if (item is VNScene s) return string.IsNullOrEmpty(s.ScriptName) ? "未关联剧本" : s.ScriptName;
        return "";
    }

    private void ApplyName(object item, string name)
    {
        if (item is CGData cg) cg.cgName = name;
        else if (item is VNMusic m) m.name = name;
        else if (item is VNScene s) s.VNscriptID = name;
    }

    public bool IsNameDuplicate(string name, object self)
    {
        if (string.IsNullOrEmpty(name)) return false;
        var list = GetSourceList(CurrentMode);
        int count = 0;
        foreach (var item in list)
        {
            if (item == self) continue;
            if (GetName(item) == name) count++;
        }
        return count > 0;
    }

    public bool IsCurrentPendingDelete()
    {
        if (SelectedItem == null) return false;
        return PendingDeleteItem == SelectedItem
            && EditorApplication.timeSinceStartup - PendingDeleteTime < PendingDeleteTimeout;
    }
}