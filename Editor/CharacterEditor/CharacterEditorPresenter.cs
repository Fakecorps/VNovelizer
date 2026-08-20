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

    /// <summary>
    /// 注册角色资产进 Addressables（键 = 角色类别前缀/角色ID）。
    /// 运行时 CharacterResManager 按 CharacterResPath 的类别 Label 批量检索——Label 由注册写入。
    /// 未初始化 Addressables 的项目（旧版兼容模式）自动跳过。
    /// </summary>
    private static void RegisterCharacterAddress(string assetPath, string characterId)
    {
        string category = VNProjectConfig.Instance != null
            ? VNProjectConfig.Instance.CharacterResPath
            : "VNovelizerRes/Characters";
        VNAddressablesRegistrar.RegisterAssetAtPath(assetPath, $"{category}/{characterId}");
    }

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

        var seenPaths = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

        // Addressables 托管模式：类别 Label 注册条目（角色 SO 可保存在项目内任意位置，
        // 只要注册过就出现在列表里——物理位置与索引无关）
        string category = VNProjectConfig.Instance != null ? VNProjectConfig.Instance.CharacterResPath : null;
        if (!string.IsNullOrEmpty(category) && VNAddressablesRegistrar.IsManagedMode)
        {
            foreach (var entry in VNAddressablesRegistrar.GetCategoryEntries(category))
            {
                var p = AssetDatabase.LoadAssetAtPath<CharacterProfile>(entry.AssetPath);
                if (p != null && seenPaths.Add(entry.AssetPath)) AllProfiles.Add(p);
            }
        }

        // 类别文件夹扫描（默认落点 + 未注册资产的兜底，与旧行为一致）
        string[] guids = AssetDatabase.FindAssets("t:CharacterProfile", new[] { CHARACTER_PATH });
        foreach (string guid in guids)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            if (!seenPaths.Add(assetPath)) continue;
            var p = AssetDatabase.LoadAssetAtPath<CharacterProfile>(assetPath);
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

        // 保存位置由用户自选（SaveFilePanelInProject 限定项目内）：
        // 物理位置与运行时索引无关（注册走 Addressables 地址/Label），默认落点仍是角色类别目录
        string path = EditorUtility.SaveFilePanelInProject(
            "新建角色", "NewCharacter", "asset",
            "选择角色资产（CharacterProfile）的保存位置。\n文件名 = 角色ID（剧本 Speaker / CharLeft 等列引用的名字），\n保存在项目内任意位置均可。",
            CHARACTER_PATH);
        if (string.IsNullOrEmpty(path)) return; // 用户取消

        if (AssetDatabase.LoadAssetAtPath<Object>(path) != null)
        {
            if (!EditorUtility.DisplayDialog("已存在", $"目标位置已有同名资产：\n{path}\n\n是否覆盖？", "覆盖", "取消"))
                return;
        }

        string characterId = Path.GetFileNameWithoutExtension(path);
        CharacterProfile newProfile = ScriptableObject.CreateInstance<CharacterProfile>();
        newProfile.CharacterID = characterId;
        // 新建角色自带默认分组
        newProfile.ElementSpriteGroups.Add(new ElementSpriteGroup { Group = CharacterProfile.DefaultGroupName });
        newProfile.HeadSpriteGroups.Add(new ElementSpriteGroup { Group = CharacterProfile.DefaultGroupName });

        AssetDatabase.CreateAsset(newProfile, path);
        AssetDatabase.SaveAssets();

        // 创建即注册进 Addressables（键 = 类别前缀/角色ID，与运行时 LoadAll 的 Label 检索一致；
        // 未初始化 Addressables 的项目自动跳过，靠 Resources 兜底）
        RegisterCharacterAddress(path, characterId);

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

                // 复制出的新角色同样注册进 Addressables
                RegisterCharacterAddress(newPath, copy.CharacterID);

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
