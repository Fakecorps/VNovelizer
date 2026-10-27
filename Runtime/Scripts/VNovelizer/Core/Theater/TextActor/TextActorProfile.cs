using UnityEngine;

namespace VNovelizer.Core.Theater
{
    /// <summary>
    /// 文字角色配置：以 UGUI 面板 + 段落模板承载正文的角色类型（TextActor）。
    ///
    /// 剧本在 Character 列填写本角色 ID 登台，当前行 Text 以「新增段落」的形式逐字播放到
    /// TextContainer（VerticalLayoutGroup），旧段落保留——用于模拟"整屏文字"的视觉小说呈现
    /// （通常配合手动隐藏对话框使用）。跨行继承：登台后槽位留空继续存在并接收正文，
    /// hideTextActor(posID) 关闭并清空。
    ///
    /// 字体/字号/颜色/背景图片/装饰等视觉样式全部在面板 Prefab 内编辑，不在此重复配置。
    /// </summary>
    [CreateAssetMenu(fileName = "TextActorProfile", menuName = "VNovelizer/TextActor Profile")]
    public class TextActorProfile : CharacterProfile
    {
        public override bool IsSpriteBased => false;

        [Header("文字面板")]
        [Tooltip("面板 Prefab（根节点挂 TextActorView，内含 TextContainer 与段落模板）。留空时运行时自动使用内置默认面板。")]
        public GameObject PanelPrefab;

        [Tooltip("正文显示方式：关 = 逐字打字（速度跟随全局设置）；开 = 瞬间全显")]
        public bool DirectDisplay = false;

        [Tooltip("打字速度覆盖（秒/字符）。< 0 = 跟随全局 TextSpeed 设置")]
        public float TextSpeedOverride = -1f;

        [Tooltip("段落内是否额外显示说话者姓名（本行 Speaker 列，经本地化解析后）")]
        public bool ShowSpeaker = false;

        [Tooltip("最大保留段落数（超出后移除最旧段落）。0 = 不限制")]
        public int MaxParagraphs = 0;
    }
}
