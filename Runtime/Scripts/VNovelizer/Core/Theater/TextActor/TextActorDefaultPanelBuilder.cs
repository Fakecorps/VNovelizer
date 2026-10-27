using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VNovelizer.Core.Theater
{
    /// <summary>
    /// 内置默认文字面板构造器：面板 Prefab 未指定时由运行时兜底构建；
    /// 编辑器「生成内置默认面板预制体」按钮复用同一构造器（运行/编辑两域一致，避免双实现漂移）。
    /// 构造出的结构：
    /// <code>
    /// VNTextActorPanel (TextActorView + Image 半透明底 + CanvasGroup 由 TextActor 补挂)
    /// └── TextContainer (VerticalLayoutGroup)
    ///     └── ParagraphTemplate (TMP 正文 + ContentSizeFitter；克隆源，运行时不显示)
    /// </code>
    /// </summary>
    public static class TextActorDefaultPanelBuilder
    {
        /// <summary>包内默认正文字体（思源黑体 SDF）的资源键（= 包内资产相对路径，Addressables 注册键）</summary>
        public const string DefaultFontKey = "VNovelizerRes/Fonts/TMPFonts/SiYuan-Black-Normal SDF";

        /// <summary>
        /// 内置默认正文字体：包内思源黑体 SDF（资源链加载）→ TMP 全局默认字体兜底。
        /// 编辑器生成预制体时请显式传 fontOverride（VNEditorResourceResolver 解析，不依赖运行时资源链）。
        /// </summary>
        public static TMP_FontAsset LoadDefaultFont()
        {
            var font = VNResourceService.Load<TMP_FontAsset>(DefaultFontKey);
            return font != null ? font : TMP_Settings.defaultFontAsset;
        }

        /// <summary>
        /// 构建内置默认面板。
        /// 根节点版式（与内置预制体默认值一致）：全屏拉伸 + Left/Right=250、Top/Bottom=150 内缩。
        /// </summary>
        public static GameObject Build(TextActorProfile profile, TMP_FontAsset fontOverride = null)
        {
            var root = new GameObject("VNTextActorPanel", typeof(RectTransform));
            var view = root.AddComponent<TextActorView>();

            // 全屏拉伸 + 内缩（用户可在预制体里修改锚点/尺寸）
            var rootRect = root.GetComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            // stretch 约定：offsetMin=(Left, Bottom)，offsetMax=(-Right, -Top)——Top 显示为 -offsetMax.y
            rootRect.offsetMin = new Vector2(250f, 150f);   // Left=250 / Bottom=150
            rootRect.offsetMax = new Vector2(-250f, -150f); // Right=250 / Top=150

            // 半透明底：保证正文叠在任意画面上可读；用户可在预制体中自行替换/移除
            var bg = root.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.35f);
            bg.raycastTarget = false;

            var containerGo = new GameObject("TextContainer", typeof(RectTransform));
            var container = containerGo.GetComponent<RectTransform>();
            container.SetParent(root.transform, false);
            container.anchorMin = Vector2.zero;
            container.anchorMax = Vector2.one;
            container.offsetMin = new Vector2(32f, 32f);
            container.offsetMax = new Vector2(-32f, -32f);
            containerGo.AddComponent<RectMask2D>();
            var layout = containerGo.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.spacing = 70f;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            view.TextContainer = container;
            EnsureBuiltinTemplate(view, fontOverride ?? LoadDefaultFont());
            view.EnsureStructure();
            return root;
        }

        /// <summary>
        /// 创建内置最小段落模板（TMP 正文 + ContentSizeFitter 自动高度）并接线到
        /// <paramref name="view"/>。面板 Prefab 缺模板时由 <see cref="TextActorView.EnsureStructure"/>
        /// 自愈调用一次。
        /// </summary>
        public static void EnsureBuiltinTemplate(TextActorView view, TMP_FontAsset font = null)
        {
            if (view == null) return;

            var go = new GameObject("ParagraphTemplate", typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(view.TextContainer, false);
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = "";
            tmp.fontSize = 75f;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.TopLeft;
            tmp.enableWordWrapping = true;
            tmp.extraPadding = true;
            tmp.raycastTarget = false;
            tmp.font = font != null ? font : LoadDefaultFont();

            var fitter = go.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            view.ParagraphTemplate = rt;
            view.TemplateText = tmp;
            view.TemplateSpeaker = null;
        }
    }
}
