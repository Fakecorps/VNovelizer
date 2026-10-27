using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VNovelizer.Core.Theater
{
    /// <summary>
    /// 文字面板的绑定组件：显式声明「段落挂载容器」与「段落模板」。
    ///
    /// 程序只按这里的引用工作——不按对象名查找、不自动改动用户布局；
    /// 引用缺失时打警告并就地自愈一次（保证可用，与面板 Prefab 编辑互不干扰）。
    /// 段落实例 = 克隆 ParagraphTemplate（模板对象会被禁用并排除出布局），
    /// 正文 TMP 由 TemplateText 的层级路径映射到克隆实例；TemplateSpeaker 可选。
    /// </summary>
    [DisallowMultipleComponent]
    public class TextActorView : MonoBehaviour
    {
        [Tooltip("段落实例的父节点（应挂 VerticalLayoutGroup）")]
        public RectTransform TextContainer;

        [Tooltip("段落模板（每次追加时克隆；可为空 = 使用内置最小模板）。模板会被禁用并排除出布局。")]
        public RectTransform ParagraphTemplate;

        [Tooltip("段落模板内的正文 TMP（与 ParagraphTemplate 配套；空则由模板自动查找第一个 TMP）")]
        public TMP_Text TemplateText;

        [Tooltip("段落模板内的说话者 TMP（可选；仅当角色开启「显示说话者」时写入，否则该对象被禁用）")]
        public TMP_Text TemplateSpeaker;

        [Tooltip("裁剪超出 TextContainer 的段落；关闭时由自定义面板负责处理溢出")]
        public bool ClipOverflow = true;

        // 模板内目标组件的相对层级路径（克隆后按同一路径定位对应组件）
        private readonly List<int> _templateTextPath = new List<int>();
        private readonly List<int> _templateSpeakerPath = new List<int>();
        private readonly List<GameObject> _paragraphInstances = new List<GameObject>();

        /// <summary>确保容器与模板可用（缺失时自愈一次并打警告）。幂等。</summary>
        public void EnsureStructure()
        {
            if (TextContainer == null)
            {
                Debug.LogWarning($"[TextActorView] {name}: 未指定 TextContainer，已自动创建全屏垂直布局容器");
                var go = new GameObject("TextContainer", typeof(RectTransform));
                TextContainer = go.GetComponent<RectTransform>();
                TextContainer.SetParent(transform, false);
                TextContainer.anchorMin = Vector2.zero;
                TextContainer.anchorMax = Vector2.one;
                TextContainer.offsetMin = new Vector2(24f, 24f);
                TextContainer.offsetMax = new Vector2(-24f, -24f);
                var layout = go.AddComponent<VerticalLayoutGroup>();
                layout.childAlignment = TextAnchor.UpperLeft;
                layout.spacing = 8f;
                layout.childControlWidth = true;
                layout.childControlHeight = false;
                layout.childForceExpandWidth = true;
                layout.childForceExpandHeight = false;
            }

            if (ParagraphTemplate == null)
            {
                Debug.LogWarning($"[TextActorView] {name}: 未指定段落模板，已创建内置最小模板（TMP 正文）");
                TextActorDefaultPanelBuilder.EnsureBuiltinTemplate(this);
            }
            else if (TemplateText == null)
            {
                TemplateText = ParagraphTemplate.GetComponentInChildren<TMP_Text>(true);
                if (TemplateText == null)
                {
                    Debug.LogWarning($"[TextActorView] {name}: 段落模板内找不到 TMP 正文，改用内置最小模板");
                    ParagraphTemplate = null;
                    TextActorDefaultPanelBuilder.EnsureBuiltinTemplate(this);
                }
            }

            if (ClipOverflow && TextContainer.GetComponent<RectMask2D>() == null)
                TextContainer.gameObject.AddComponent<RectMask2D>();

            // 模板是「克隆源」：不参与布局、不渲染
            if (ParagraphTemplate != null)
                ParagraphTemplate.gameObject.SetActive(false);

            RebuildPaths();
        }

        /// <summary>重建模板内正文/说话者组件的层级路径缓存（结构变化后调用）</summary>
        private void RebuildPaths()
        {
            _templateTextPath.Clear();
            _templateSpeakerPath.Clear();
            if (ParagraphTemplate == null) return;
            _templateTextPath.AddRange(BuildPath(ParagraphTemplate, TemplateText != null ? TemplateText.transform : null));
            _templateSpeakerPath.AddRange(BuildPath(ParagraphTemplate, TemplateSpeaker != null ? TemplateSpeaker.transform : null));
        }

        private static List<int> BuildPath(RectTransform root, Transform target)
        {
            var path = new List<int>();
            if (target == null) return path;
            Transform t = target;
            while (t != null && t != root)
            {
                path.Insert(0, t.GetSiblingIndex());
                t = t.parent;
            }
            return path;
        }

        private static Transform ResolvePath(Transform root, List<int> path)
        {
            Transform current = root;
            foreach (int idx in path)
            {
                if (idx < 0 || idx >= current.childCount) return null;
                current = current.GetChild(idx);
            }
            return current;
        }

        /// <summary>创建一段落落实例并写入内容（克隆段落模板）。返回正文 TMP（可能为 null）。</summary>
        public TMP_Text CreateParagraph(string text, string speaker)
        {
            if (ParagraphTemplate == null || TextContainer == null || TemplateText == null) return null;
            if (!TemplateText.transform.IsChildOf(ParagraphTemplate))
            {
                Debug.LogError($"[TextActorView] {name}: TemplateText 必须属于 ParagraphTemplate 的层级");
                return null;
            }

            GameObject go = Instantiate(ParagraphTemplate.gameObject, TextContainer);
            go.transform.SetAsLastSibling();
            _paragraphInstances.Add(go);

            TMP_Text textTmp = ResolvePath(go.transform, _templateTextPath)?.GetComponent<TMP_Text>();
            if (textTmp == null) textTmp = go.GetComponentInChildren<TMP_Text>(true);

            if (textTmp != null)
            {
                if (textTmp.font == null) textTmp.font = TextActorDefaultPanelBuilder.LoadDefaultFont();
                textTmp.text = text ?? "";
                textTmp.maxVisibleCharacters = 99999;
            }

            if (TemplateSpeaker != null && TemplateSpeaker != TemplateText &&
                TemplateSpeaker.transform.IsChildOf(ParagraphTemplate) &&
                !TemplateText.transform.IsChildOf(TemplateSpeaker.transform))
            {
                Transform speakerNode = ResolvePath(go.transform, _templateSpeakerPath);
                var speakerTmp = speakerNode != null ? speakerNode.GetComponent<TMP_Text>() : null;
                if (speakerTmp != null)
                {
                    speakerTmp.text = speaker ?? "";
                    speakerTmp.raycastTarget = false;
                    speakerTmp.gameObject.SetActive(!string.IsNullOrEmpty(speaker));
                }
            }

            if (textTmp != null)
            {
                textTmp.raycastTarget = false;
                textTmp.gameObject.SetActive(true);
            }
            go.SetActive(true);
            LayoutRebuilder.ForceRebuildLayoutImmediate(TextContainer);
            if (textTmp != null) textTmp.ForceMeshUpdate();
            return textTmp;
        }

        /// <summary>清空全部段落实例（模板对象除外）</summary>
        public void ClearParagraphs()
        {
            foreach (var paragraph in _paragraphInstances)
                DestroyParagraph(paragraph);
            _paragraphInstances.Clear();
        }

        /// <summary>移除最旧的段落实例（最大段落数裁剪）</summary>
        public void RemoveOldestParagraph()
        {
            if (_paragraphInstances.Count == 0) return;
            var paragraph = _paragraphInstances[0];
            _paragraphInstances.RemoveAt(0);
            DestroyParagraph(paragraph);
        }

        private static void DestroyParagraph(GameObject paragraph)
        {
            if (paragraph == null) return;
            paragraph.SetActive(false);
            paragraph.transform.SetParent(null, false);
            if (Application.isPlaying) Destroy(paragraph);
            else DestroyImmediate(paragraph);
        }
    }
}
