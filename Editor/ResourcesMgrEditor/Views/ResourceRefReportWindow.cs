using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 资源引用报告窗口：以表格列出剧本引用记录（缺失引用检查 / 删除影响面详情）。
/// 只读展示，无编辑能力。
/// </summary>
public class ResourceRefReportWindow : EditorWindow
{
    private List<ScriptReferenceScanner.ScriptResourceReference> _references;
    private string _subtitle;

    /// <summary>打开报告窗口（自动命名聚焦）</summary>
    public static void Show(string title, string subtitle, List<ScriptReferenceScanner.ScriptResourceReference> references)
    {
        var wnd = GetWindow<ResourceRefReportWindow>();
        wnd.titleContent = new GUIContent(title);
        wnd._references = references ?? new List<ScriptReferenceScanner.ScriptResourceReference>();
        wnd._subtitle = subtitle ?? "";
        wnd.minSize = new Vector2(720, 320);
        wnd.Rebuild();
    }

    private void Rebuild()
    {
        var root = rootVisualElement;
        root.Clear();
        root.style.paddingTop = 8;
        root.style.paddingLeft = 8;
        root.style.paddingRight = 8;
        root.style.paddingBottom = 8;

        var header = new Label($"共 {_references.Count} 处引用" + (string.IsNullOrEmpty(_subtitle) ? "" : $" — {_subtitle}"));
        header.style.fontSize = 13;
        header.style.unityFontStyleAndWeight = FontStyle.Bold;
        header.style.marginBottom = 8;
        root.Add(header);

        if (_references.Count == 0)
        {
            var empty = new Label("没有匹配的引用。");
            empty.style.color = Color.gray;
            root.Add(empty);
            return;
        }

        var scroll = new ScrollView(ScrollViewMode.Vertical);
        scroll.style.flexGrow = 1;
        root.Add(scroll);

        // 表头
        var headerRow = MakeRow("剧本", "行 ID", "行号", "类别", "资源名", "来源", true);
        scroll.Add(headerRow);

        // 按 剧本名 → 行号 排序
        var ordered = _references
            .OrderBy(r => r.ScriptName, System.StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.LineNo)
            .ToList();
        foreach (var r in ordered)
            scroll.Add(MakeRow(r.ScriptName, r.LineId, r.LineNo.ToString(),
                UIElementBuilder.GetTypeDisplayName(r.Type), r.Name, r.Source, false));
    }

    private VisualElement MakeRow(string script, string lineId, string lineNo, string type, string name, string source, bool isHeader)
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.height = 24;
        row.style.paddingLeft = 6;
        row.style.paddingRight = 6;
        row.style.marginBottom = 1;
        row.style.borderBottomWidth = 1;
        row.style.borderBottomColor = isHeader ? Color.black * 0.5f : Color.black * 0.15f;

        void AddCell(string text, float pct, bool bold = false)
        {
            var l = new Label(text);
            l.style.width = Length.Percent(pct);
            l.style.flexShrink = 0;
            l.style.overflow = Overflow.Hidden;
            l.style.textOverflow = TextOverflow.Ellipsis;
            l.style.fontSize = 11;
            l.style.color = isHeader ? ResourceStyles.TextPrimary : ResourceStyles.TextSecondary;
            if (isHeader || bold) l.style.unityFontStyleAndWeight = FontStyle.Bold;
            row.Add(l);
        }

        AddCell(script, 16);
        AddCell(lineId, 16);
        AddCell(lineNo, 7);
        AddCell(type, 10);
        AddCell(name, 31, !isHeader);
        AddCell(source, 20);
        return row;
    }
}
