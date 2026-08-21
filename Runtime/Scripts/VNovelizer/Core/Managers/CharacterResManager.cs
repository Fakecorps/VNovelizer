using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// 角色资源管理器 (终极防呆版)
/// </summary>
public class CharacterResManager : BaseManager<CharacterResManager>
{
    private Dictionary<string, CharacterProfile> characterProfiles = new Dictionary<string, CharacterProfile>();
    private bool isInitialized = false;

    /// <summary>
    /// 初始化
    /// </summary>
    public void Init()
    {
        if (isInitialized) return; // 防止重复加载

        characterProfiles.Clear();
        LoadAllCharacterProfiles();
        isInitialized = true;
    }

    /// <summary>
    /// 加载所有角色配置
    /// </summary>
    private void LoadAllCharacterProfiles()
    {
        string loadPath = VNProjectConfig.Instance.CharacterResPath;
        // 经资源服务链批量加载（Addressables 按类别 Label / Resources 按文件夹）
        IList<CharacterProfile> profileList = VNResourceService.LoadAll<CharacterProfile>(loadPath);

        if (profileList == null || profileList.Count == 0)
        {
            // 角色不是必需资产：未配置任何角色是合法状态（无角色剧也能正常跑）
            // 不再视为错误——改为 info 提示与创建引导
            Debug.Log($"[CharacterResManager] 未找到任何角色 Profile（类别 {loadPath}）。" +
                      $"如需添加角色：使用 VNovelizer → 角色编辑器 → 新建角色（位置自选）。");
            return;
        }

        Debug.Log($"【系统】在 {loadPath} 下发现了 {profileList.Count} 个配置文件");

        foreach (CharacterProfile profile in profileList)
        {
            if (profile != null)
            {
                // 这里的 .Trim() 非常关键，防止Inspector里填写的ID带有空格
                string cleanID = profile.CharacterID.Trim();

                if (string.IsNullOrEmpty(cleanID))
                {
                    Debug.LogError($"【配置错误】文件 '{profile.name}' 的 ID 是空的！");
                    continue;
                }

                if (!characterProfiles.ContainsKey(cleanID))
                {
                    characterProfiles[cleanID] = profile;
                    Debug.Log($"✅ 成功登记角色: [{cleanID}] (对应文件: {profile.name})");
                }
                else
                {
                    Debug.LogWarning($"⚠️ 重复的角色ID: {cleanID}，已跳过。");
                }
            }
        }
    }

    /// <summary>
    /// 获取角色配置
    /// </summary>
    public CharacterProfile GetCharacterProfile(string characterID)
    {
        // 1. 自动去空格，防止 CSV 里有隐形空格
        string cleanID = characterID.Trim();

        // 2. 懒加载：如果字典是空的，说明之前没Init，现在立刻补救
        if (characterProfiles.Count == 0)
        {
            Debug.LogWarning("⚠️ 检测到角色字典为空，尝试自动执行初始化...");
            Init();
        }

        // 3. 尝试获取
        if (characterProfiles.ContainsKey(cleanID))
        {
            return characterProfiles[cleanID];
        }

        // 4. 如果还是找不到，打印出所有已知的ID，方便对比
        string loadedIDs = string.Join(", ", characterProfiles.Keys);
        Debug.LogError($"❌ 【致命错误】找不到角色ID: '{cleanID}' (原始请求: '{characterID}')\n" +
                       $"ℹ️ 当前内存中已加载的ID列表: [{loadedIDs}]\n" +
                       $"👉 请检查 Inspector 里的 CharacterID 是否和 CSV 里的完全一致（注意大小写）");

        return null;
    }

    /// <summary>
    /// 静默获取角色配置（不打印错误日志，用于 SpeakerBox 等可选功能）
    /// </summary>
    /// <param name="characterID">角色ID</param>
    /// <returns>角色配置，如果找不到则返回 null</returns>
    public CharacterProfile TryGetCharacterProfile(string characterID)
    {
        // 1. 自动去空格，防止 CSV 里有隐形空格
        string cleanID = characterID?.Trim() ?? "";
        
        if (string.IsNullOrEmpty(cleanID))
        {
            return null;
        }

        // 2. 懒加载：如果字典是空的，说明之前没Init，现在立刻补救
        if (characterProfiles.Count == 0)
        {
            Init();
        }

        // 3. 静默查找，不打印错误
        if (characterProfiles.ContainsKey(cleanID))
        {
            return characterProfiles[cleanID];
        }

        return null;
    }

    // ... 下面的代码保持不变 ...

    public Sprite GetCharacterSprite(string characterID, string group, string element)
    {
        CharacterProfile profile = GetCharacterProfile(characterID);
        if (profile != null)
        {
            return profile.GetEmotionSprite(group, element);
        }
        return null;
    }

}