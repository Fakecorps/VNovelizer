using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// MVP - Presenter：角色编辑器的业务逻辑层。
/// 负责角色数据的加载、筛选、CRUD 操作。
/// </summary>
public class CharacterEditorPresenter
{
    /// <summary>角色资产目录：工作区（新项目）或旧版 Resources 目录（存量项目），见 VNProjectPaths</summary>
    private static string CHARACTER_PATH => VNProjectPaths.CharactersFolder;

    // --- 数据 ---
    public List<CharacterProfile> AllProfiles { get; private set; } = new List<CharacterProfile>();
    public List<CharacterProfile> FilteredProfiles { get; private set; } = new List<CharacterProfile>();
    public CharacterProfile SelectedProfile { get; private set; }

    // --- 状态 ---
    public string SearchText { get; set; } = "";

    // --- 视图回调 ---
    public System.Action OnDataChanged;
    public System.Action<CharacterProfile> OnSelectionChanged;

    public void LoadAll(bool forceReload)
    {
        if (!forceReload && AllProfiles.Count > 0) return;

        AllProfiles.Clear();
        EnsureDirectory();

        string[] guids = AssetDatabase.FindAssets("t:CharacterProfile", new[] { CHARACTER_PATH });
        foreach (string guid in guids)
        {
            var p = AssetDatabase.LoadAssetAtPath<CharacterProfile>(AssetDatabase.GUIDToAssetPath(guid));
            if (p != null) AllProfiles.Add(p);
        }

        ApplyFilter();
    }

    private void EnsureDirectory()
    {
        if (!Directory.Exists(CHARACTER_PATH))
        {
            Directory.CreateDirectory(CHARACTER_PATH);
            AssetDatabase.Refresh();
        }
    }

    public void ApplyFilter()
    {
        if (string.IsNullOrEmpty(SearchText))
            FilteredProfiles = new List<CharacterProfile>(AllProfiles);
        else
            FilteredProfiles = AllProfiles
                .Where(p => p.CharacterID != null && p.CharacterID.ToLower().Contains(SearchText.ToLower()))
                .ToList();

        OnDataChanged?.Invoke();

        // 如果当前选中的不在过滤结果中，清除选中
        if (SelectedProfile != null && !FilteredProfiles.Contains(SelectedProfile))
        {
            SelectProfile(null);
        }
    }

    public void SelectProfile(CharacterProfile profile)
    {
        SelectedProfile = profile;
        OnSelectionChanged?.Invoke(profile);
    }

    public void CreateNewCharacter()
    {
        EnsureDirectory();

        string baseName = "NewCharacter";
        string path = AssetDatabase.GenerateUniqueAssetPath($"{CHARACTER_PATH}/{baseName}.asset");

        CharacterProfile newProfile = ScriptableObject.CreateInstance<CharacterProfile>();
        newProfile.CharacterID = Path.GetFileNameWithoutExtension(path);
        // 新建角色自带默认分组
        newProfile.ElementSpriteGroups.Add(new ElementSpriteGroup { Group = CharacterProfile.DefaultGroupName });
        newProfile.HeadSpriteGroups.Add(new ElementSpriteGroup { Group = CharacterProfile.DefaultGroupName });

        AssetDatabase.CreateAsset(newProfile, path);
        AssetDatabase.SaveAssets();

        LoadAll(true);

        // 选中新建的角色
        var created = AllProfiles.FirstOrDefault(p => p.CharacterID == newProfile.CharacterID);
        if (created != null)
        {
            SearchText = "";
            ApplyFilter();
            SelectProfile(created);
        }
    }

    public void DuplicateCharacter(CharacterProfile profile)
    {
        if (profile == null) return;
        string path = AssetDatabase.GetAssetPath(profile);
        string newPath = AssetDatabase.GenerateUniqueAssetPath(path);

        if (AssetDatabase.CopyAsset(path, newPath))
        {
            var copy = AssetDatabase.LoadAssetAtPath<CharacterProfile>(newPath);
            if (copy != null)
            {
                copy.CharacterID = Path.GetFileNameWithoutExtension(newPath);
                EditorUtility.SetDirty(copy);
                AssetDatabase.SaveAssets();
                LoadAll(true);
                SelectProfile(copy);
            }
        }
    }

    public void DeleteCharacter(CharacterProfile profile)
    {
        if (profile == null) return;
        if (EditorUtility.DisplayDialog("删除角色", $"确定要删除 {profile.CharacterID} 吗？\n此操作不可撤销。", "确定删除", "取消"))
        {
            string path = AssetDatabase.GetAssetPath(profile);
            AssetDatabase.DeleteAsset(path);
            SelectProfile(null);
            LoadAll(true);
        }
    }

    public void RenameCharacter(CharacterProfile profile, string newName)
    {
        if (profile == null || string.IsNullOrEmpty(newName)) return;
        if (profile.CharacterID == newName) return;

        string path = AssetDatabase.GetAssetPath(profile);
        string newPath = $"{CHARACTER_PATH}/{newName}.asset";
        string error = null;

        if (path != newPath)
        {
            error = AssetDatabase.RenameAsset(path, newName);
        }

        if (!string.IsNullOrEmpty(error))
        {
            Debug.LogWarning($"重命名失败: {error}");
            return;
        }

        profile.CharacterID = newName;
        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();
        LoadAll(true);
    }

    public string GetCharacterPath()
    {
        return CHARACTER_PATH;
    }
}
