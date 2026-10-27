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
    private ResourceReferenceScanResult _scan;
    private string _subtitle;
    private bool _includePotential;

    public static void Show(string title, string subtitle, List<ScriptReferenceScanner.ScriptResourceReference> references,
        List<string> errors = null)
    {
        ResourceReferenceScanResult scan = errors == null ? null : new ResourceReferenceScanResult
        {
            References = references ?? new List<ScriptReferenceScanner.ScriptResourceReference>(),
            Errors = errors,
        };
        Show(title, subtitle, references, scan);
    }

    public static void Show(string title, string subtitle, ResourceReferenceScanResult scan)
    {
        Show(title, subtitle, scan?.References, scan);
    }

    public static void Show(string title, string subtitle, List<ScriptReferenceScanner.ScriptResourceReference> references,
        ResourceReferenceScanResult scan)
    {
        var wnd = GetWindow<ResourceRefReportWindow>();
        wnd.titleContent = new GUIContent(title);
        wnd._references = references ?? new List<ScriptReferenceScanner.ScriptResourceReference>();
        wnd._scan = scan;
        wnd._subtitle = subtitle ?? "";
        wnd._includePotential = false;
        wnd.minSize = new Vector2(800, 360);
        wnd.Rebuild();
    }

    public void CreateGUI() => Rebuild();

    private void Rebuild()
    {
        var root = rootVisualElement;
        root.Clear();
        root.style.paddingTop = 8;
        root.style.paddingLeft = 8;
        root.style.paddingRight = 8;
        root.style.paddingBottom = 8;

        if (_references == null) _references = new List<ScriptReferenceScanner.ScriptResourceReference>();
        int potentialCount = _references.Count(r => r.Potential);
        var header = MakeLabel($"共 {_references.Count - potentialCount} 处明确引用、{potentialCount} 处潜在依赖" +
            (string.IsNullOrEmpty(_subtitle) ? "" : $" — {_subtitle}"));
        header.style.fontSize = 13;
        header.style.unityFontStyleAndWeight = FontStyle.Bold;
        header.style.marginBottom = 8;
        root.Add(header);

        string integrity = _scan == null ? "未提供扫描完整性；此列表不能证明没有其他引用。" :
            _scan.IsComplete ? "扫描完整。可加载状态按注册地址 / Resources 键及资产类型检查，不启动播放、不验证打包结果。" :
            $"扫描不完整（{_scan.Errors.Count} 项错误）；未列出不代表没有引用，不应继续破坏性操作。";
        var status = MakeLabel(integrity);
        status.style.color = _scan != null && _scan.IsComplete ? ResourceStyles.TextSecondary : new Color(0.9f, 0.45f, 0.15f);
        status.style.marginBottom = 6;
        root.Add(status);

        if (potentialCount > 0)
        {
            var toggle = new Toggle("显示按行 ID 自动推导的潜在配音依赖（可选，默认不算缺失）");
            toggle.SetValueWithoutNotify(_includePotential);
            toggle.RegisterValueChangedCallback(evt => { _includePotential = evt.newValue; Rebuild(); });
            root.Add(toggle);
        }

        var scroll = new ScrollView(ScrollViewMode.Vertical);
        scroll.style.flexGrow = 1;
        root.Add(scroll);
        if (_scan != null)
            foreach (string error in _scan.Errors)
            {
                var errorLabel = MakeLabel(error);
                errorLabel.style.color = new Color(0.95f, 0.35f, 0.3f);
                errorLabel.style.marginBottom = 6;
                scroll.Add(errorLabel);
            }

        var ordered = _references.Where(r => _includePotential || !r.Potential)
            .OrderBy(r => r.ScriptName, System.StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.LineNo).ToList();
        if (ordered.Count == 0)
        {
            scroll.Add(MakeLabel(_scan != null && !_scan.IsComplete ?
                "目前没有匹配记录，但扫描不完整，无法判定没有引用。" : "当前筛选没有匹配的引用。"));
            return;
        }
        scroll.Add(MakeRow("剧本", "行 ID", "行号", "类别", "资源名", "来源", true));
        foreach (var r in ordered)
        {
            var card = new VisualElement();
            card.style.marginBottom = 8;
            card.Add(MakeRow(r.ScriptName, r.LineId, r.LineNo.ToString(),
                UIElementBuilder.GetTypeDisplayName(r.Type), r.Name, r.Source, false));
            string availability = r.IsLoadable ? "可加载" : r.Potential ? "未找到可选配音" : "不可加载 / 未找到目标资源";
            var state = MakeLabel((r.Potential ? "潜在依赖 · " : "明确引用 · ") + availability);
            state.style.color = r.IsLoadable ? new Color(0.3f, 0.7f, 0.4f) : new Color(0.95f, 0.5f, 0.25f);
            card.Add(state);
            card.Add(MakeLabel("加载键：" + (r.ResourceKey ?? "未完成检查")));
            card.Add(MakeLabel("资源路径：" + (r.AssetPath ?? "未找到")));
            card.Add(MakeLabel("CSV：" + (r.CsvPath ?? r.ScriptName) + " · 第 " + r.LineNo + " 行"));
            var actions = new VisualElement();
            actions.style.flexDirection = FlexDirection.Row;
            var locateResource = new Button(() => Locate(r.AssetPath)) { text = "定位资源" };
            locateResource.SetEnabled(!string.IsNullOrEmpty(r.AssetPath));
            actions.Add(locateResource);
            var locateCsv = new Button(() => Locate(r.CsvPath)) { text = "定位 CSV" };
            locateCsv.SetEnabled(!string.IsNullOrEmpty(r.CsvPath));
            actions.Add(locateCsv);
            actions.Add(new Button(() => EditorGUIUtility.systemCopyBuffer =
                $"{r.ScriptName}\t{r.LineId}\t{r.LineNo}\t{r.Type}\t{r.Name}\t{r.Source}\t{r.ResourceKey}\t{r.AssetPath}\t{r.CsvPath}") { text = "复制完整记录" });
            card.Add(actions);
            scroll.Add(card);
        }
    }

    private VisualElement MakeRow(string script, string lineId, string lineNo, string type, string name, string source, bool isHeader)
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.minHeight = 24;
        row.style.paddingLeft = 6;
        row.style.paddingRight = 6;
        row.style.marginBottom = 1;
        row.style.borderBottomWidth = 1;
        row.style.borderBottomColor = isHeader ? Color.black * 0.5f : Color.black * 0.15f;

        void AddCell(string text, float pct, bool bold = false)
        {
            var l = MakeLabel(text);
            l.style.width = Length.Percent(pct);
            l.style.flexShrink = 0;
            l.style.fontSize = 11;
            l.style.color = isHeader ? ResourceStyles.TextPrimary : ResourceStyles.TextSecondary;
            if (isHeader || bold) l.style.unityFontStyleAndWeight = FontStyle.Bold;
            row.Add(l);
        }

        AddCell(script, 14);
        AddCell(lineId, 14);
        AddCell(lineNo, 6);
        AddCell(type, 10);
        AddCell(name, 32, !isHeader);
        AddCell(source, 24);
        return row;
    }

    private static Label MakeLabel(string text)
    {
        var label = new Label(text ?? "") { tooltip = text ?? "" };
        label.style.whiteSpace = WhiteSpace.Normal;
        label.style.flexShrink = 1;
        label.style.minWidth = 0;
        label.AddManipulator(new ContextualMenuManipulator(evt =>
            evt.menu.AppendAction("复制完整文本", _ => EditorGUIUtility.systemCopyBuffer = text ?? "")));
        return label;
    }

    private static void Locate(string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        string normalized = path.Replace('\\', '/');
        string projectRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..")).Replace('\\', '/').TrimEnd('/') + "/";
        if (normalized.StartsWith(projectRoot, System.StringComparison.OrdinalIgnoreCase))
            normalized = normalized.Substring(projectRoot.Length);
        var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(normalized);
        if (asset != null)
        {
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }
        else if (System.IO.File.Exists(path))
            EditorUtility.RevealInFinder(System.IO.Path.GetFullPath(path));
    }
}
