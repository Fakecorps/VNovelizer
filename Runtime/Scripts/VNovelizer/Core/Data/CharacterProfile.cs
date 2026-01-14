using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// 角色配置文件
/// </summary>
[CreateAssetMenu(fileName = "CharacterProfile", menuName = "VNovelizer/CharacterProfile")]
public class CharacterProfile : ScriptableObject
{
    // 角色ID（唯一标识）
    public string CharacterID;
    
    // 立绘资源映射
    public List<ElementSprite> ElementSprites = new List<ElementSprite>();
    
    // 头像资源映射
    public List<ElementSprite> HeadSprites = new List<ElementSprite>();

    public Sprite SpeakerBox; // 姓名框资源
    public Sprite HeadFrame; // 头像边框资源
    /// <summary>
    /// 根据情绪名称获取对应的立绘
    /// </summary>
    /// <param name="element">情绪名称</param>
    /// <returns>对应的立绘Sprite，如果找不到则返回null</returns>
    public Sprite GetEmotionSprite(string element)
    {
        
        if (string.IsNullOrEmpty(element))
        {
            Debug.LogError($"Emotion is null or empty for character '{CharacterID}'");
            return null;
        }
        
        
        foreach (var emotionSprite in ElementSprites)
        {
            if (emotionSprite.Element == element)
            {
                if (emotionSprite.Sprite != null)
                {
                    return emotionSprite.Sprite;
                }
                else
                {
                    Debug.LogError($"  Sprite for emotion '{element}' is null for character '{CharacterID}'");
                    return null;
                }
            }
        }
        
        Debug.LogError($"  Emotion '{element}' not found for character '{CharacterID}'");
        return null;
    }
    
    /// <summary>
    /// 根据情绪名称获取对应的头像
    /// </summary>
    /// <param name="element">情绪名称</param>
    /// <returns>对应的头像Sprite，如果找不到则返回null</returns>
    public Sprite GetHeadSprite(string element)
    {
        if (string.IsNullOrEmpty(element))
        {
            Debug.LogError($"Emotion is null or empty for character '{CharacterID}' (HeadSprite)");
            return null;
        }
        
        foreach (var headSprite in HeadSprites)
        {
            if (headSprite.Element == element)
            {
                if (headSprite.Sprite != null)
                {
                    return headSprite.Sprite;
                }
                else
                {
                    Debug.LogError($"  HeadSprite for emotion '{element}' is null for character '{CharacterID}'");
                    return null;
                }
            }
        }
        
        Debug.LogError($"  HeadSprite emotion '{element}' not found for character '{CharacterID}'");
        return null;
    }

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