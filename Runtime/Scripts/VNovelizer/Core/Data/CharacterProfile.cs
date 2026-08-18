using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// 角色配置文件
/// 立绘/头像采用二维分组管理：分组名（如服装 uniform）→ 表情名 → Sprite。
/// 剧本中的引用格式为 CharacterID#分组名#表情名（如 Amy#uniform#Smile）。
/// </summary>
[CreateAssetMenu(fileName = "CharacterProfile", menuName = "VNovelizer/CharacterProfile")]
public class CharacterProfile : ScriptableObject, ISerializationCallbackReceiver
{
    /// <summary>默认分组名（未分类立绘归入此组）</summary>
    public const string DefaultGroupName = "Default";

    // 角色ID（唯一标识）
    public string CharacterID;

    // 立绘资源（二维：分组 → 表情）
    public List<ElementSpriteGroup> ElementSpriteGroups = new List<ElementSpriteGroup>();

    // 头像资源（二维：分组 → 表情）
    public List<ElementSpriteGroup> HeadSpriteGroups = new List<ElementSpriteGroup>();

    // 【旧版一维数据】仅用于反序列化旧资产并自动迁移，勿再直接使用
    [HideInInspector] public List<ElementSprite> ElementSprites = new List<ElementSprite>();
    [HideInInspector] public List<ElementSprite> HeadSprites = new List<ElementSprite>();

    public Sprite SpeakerBox; // 姓名框资源
    public Sprite HeadFrame; // 头像边框资源

    [Header("立绘显示设置")]
    [Tooltip("立绘缩放比例，1.0为原始大小")]
    public float scale = 1.0f;

    [Tooltip("立绘位置偏移量（相对于原始位置）")]
    public Vector2 offset = Vector2.zero;

    // =========================================================
    //              旧资产自动迁移（ISerializationCallbackReceiver）
    // =========================================================
    public void OnAfterDeserialize()
    {
        // 加载旧资产时：一维列表非空且分组为空 → 自动转入默认分组
        if ((ElementSpriteGroups == null || ElementSpriteGroups.Count == 0) &&
            ElementSprites != null && ElementSprites.Count > 0)
        {
            MigrateLegacyToList(ElementSprites, ElementSpriteGroups);
        }
        if ((HeadSpriteGroups == null || HeadSpriteGroups.Count == 0) &&
            HeadSprites != null && HeadSprites.Count > 0)
        {
            MigrateLegacyToList(HeadSprites, HeadSpriteGroups);
        }

        // 兜底：确保分组容器非空（新建角色也有默认分组）
        if (ElementSpriteGroups == null) ElementSpriteGroups = new List<ElementSpriteGroup>();
        if (HeadSpriteGroups == null) HeadSpriteGroups = new List<ElementSpriteGroup>();
    }

    public void OnBeforeSerialize()
    {
        // 迁移完成后清空旧一维数据，保存时即从资产中移除旧字段内容
        if (ElementSpriteGroups != null && ElementSpriteGroups.Count > 0 &&
            ElementSprites != null && ElementSprites.Count > 0)
        {
            ElementSprites.Clear();
        }
        if (HeadSpriteGroups != null && HeadSpriteGroups.Count > 0 &&
            HeadSprites != null && HeadSprites.Count > 0)
        {
            HeadSprites.Clear();
        }
    }

    private static void MigrateLegacyToList(List<ElementSprite> legacy, List<ElementSpriteGroup> groups)
    {
        var defaultGroup = new ElementSpriteGroup { Group = DefaultGroupName };
        foreach (var item in legacy)
        {
            if (item != null && !string.IsNullOrEmpty(item.Element))
                defaultGroup.Sprites.Add(item);
        }
        groups.Add(defaultGroup);
        Debug.Log($"[CharacterProfile] '{defaultGroup.Group}' 分组已从旧版一维列表自动迁移 {defaultGroup.Sprites.Count} 项（保存资产后生效）");
    }

    // =========================================================
    //                      查询 API（新：二维）
    // =========================================================
    /// <summary>
    /// 根据分组名 + 情绪名获取对应的立绘
    /// </summary>
    public Sprite GetEmotionSprite(string group, string element)
    {
        return FindSpriteInGroups(ElementSpriteGroups, group, element, "立绘");
    }

    /// <summary>
    /// 根据分组名 + 情绪名获取对应的头像
    /// </summary>
    public Sprite GetHeadSprite(string group, string element)
    {
        return FindSpriteInGroups(HeadSpriteGroups, group, element, "头像");
    }

    private Sprite FindSpriteInGroups(List<ElementSpriteGroup> groups, string group, string element, string kindLabel)
    {
        if (string.IsNullOrEmpty(element))
        {
            Debug.LogError($"[CharacterProfile] Emotion is null or empty for character '{CharacterID}' ({kindLabel})");
            return null;
        }

        string targetGroup = string.IsNullOrEmpty(group) ? DefaultGroupName : group;

        if (groups != null)
        {
            foreach (var g in groups)
            {
                if (g == null || g.Group != targetGroup) continue;

                foreach (var es in g.Sprites)
                {
                    if (es != null && es.Element == element)
                    {
                        if (es.Sprite != null) return es.Sprite;
                        Debug.LogError($"[CharacterProfile] {kindLabel} '{targetGroup}/{element}' 的 Sprite 为空 (character '{CharacterID}')");
                        return null;
                    }
                }
                // 找到分组但组内无该表情
                Debug.LogError($"[CharacterProfile] {kindLabel}表情 '{element}' 在分组 '{targetGroup}' 中不存在 (character '{CharacterID}')。" +
                               $"可用: [{string.Join(", ", g.Sprites.Where(s => s != null).Select(s => s.Element))}]");
                return null;
            }
        }

        Debug.LogError($"[CharacterProfile] 找不到{kindLabel}分组 '{targetGroup}' (character '{CharacterID}')。" +
                       $"已有分组: [{(groups != null ? string.Join(", ", groups.Where(x => x != null).Select(x => x.Group)) : "")}]");
        return null;
    }

    /// <summary>
    /// 获取指定名称的分组；不存在则新建（供编辑器使用）
    /// </summary>
    public static ElementSpriteGroup GetOrAddGroup(List<ElementSpriteGroup> groups, string groupName)
    {
        string name = string.IsNullOrEmpty(groupName) ? DefaultGroupName : groupName;
        var found = groups.FirstOrDefault(g => g != null && g.Group == name);
        if (found != null) return found;

        var created = new ElementSpriteGroup { Group = name };
        groups.Add(created);
        return created;
    }
}

/// <summary>
/// 立绘/头像的二维分组容器：分组名 → 表情列表
/// </summary>
[System.Serializable]
public class ElementSpriteGroup
{
    public string Group; // 分组名（如 uniform / Default）
    public List<ElementSprite> Sprites = new List<ElementSprite>(); // 组内表情列表
}

/// <summary>
/// 情绪和对应立绘的映射
/// </summary>
[System.Serializable]
public class ElementSprite
{
    public string Element; // 情绪名称
    public Sprite Sprite;  // 对应立绘
}