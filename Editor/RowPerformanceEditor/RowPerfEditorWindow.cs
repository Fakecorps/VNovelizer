using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VNovelizer.Core.Commands;
using VNovelizer.Core.Commands.Chain;
using VNovelizer.Core.Commands.Meta;

namespace VNovelizer.Editor.RowPerformanceEditor
{
    /// <summary>
    /// 行演出编辑器主窗口：三栏布局（命令面板 · 画布 · 属性检查器）。
    ///
    /// <para>
    /// <b>保存链路</b>（任一环失败即阻断，不写坏 CSV）：
    /// </para>
    /// <code>
    /// 图 → ChainGraphValidator（致命错误阻断）
    ///    → GraphToAst（SP 分解）
    ///    → ChainSerializer.SerializeAndVerify（含 ChainParser 反解析比对）
    ///    → 原子写 CSV（临时文件 + File.Replace）
    /// </code>
    /// </summary>
    public class RowPerfEditorWindow : EditorWindow
    {
        // ---- 数据 ----
        private string _csvPath;
        private string _scriptName;
        private List<CsvRow> _rows = new List<CsvRow>();
        private int _currentRowIndex = -1;
        private bool _isDirty;

        /// <summary>
        /// 当前 EntryGraph 是否为视图合成的模板图（Normal/Enhanced 行展开的系统命令节点）。
        /// 为 true 且用户未编辑（!_isDirty）时，保存跳过序列化——不把合成模板写进 Command 列。
        /// </summary>
        private bool _syntheticTemplate;

        // ---- 组件 ----
        private RowGraphView _graphView;
        private CommandPalette _palette;
        private InspectorBuilder _inspector;
        private readonly GraphUndoStack _undoStack = new GraphUndoStack();

        // ---- UI 引用 ----
        private Label _lineSummary;
        private Label _formBadge;
        private Label _statusForm;
        private Label _statusValidation;
        private Label _statusUndo;
        private Label _statusChain;
        private TextField _lineIdField;
        private Button _saveButton;
        private Button _resetButton;

        // ---- 校验状态 ----
        private ChainGraphValidationResult _entryValidation;
        private ChainGraphValidationResult _confirmValidation;

        /// <summary>CSV 的一行（只保留编辑器需要的字段 + 原始整行用于写回）</summary>
        private class CsvRow
        {
            public string Id;
            public string Speaker;
            public string HeadProfile;
            public string CharLeft;
            public string CharMidLeft;
            public string CharMid;
            public string CharMidRight;
            public string CharRight;
            public string Text;
            public string Background;
            public string Bgm;
            public string Voice;
            public string Command;        // 完整 Command 列（含 @Confirm: 段）
            public List<string> Cells;    // 原始单元格，写回时只替换 Command 列
        }

        /// <summary>
        /// CSV 列布局。剧本存在两种格式，**必须与 <c>ScriptParser</c> 保持一致**，
        /// 否则会读错列、甚至把 Command 写到 Note 列去：
        ///
        /// <list type="bullet">
        /// <item>14 列（当前）：ID Speaker HeadProfile CharL CharML CharM CharMR CharR Text BG BGM Voice <b>Command</b> Note</item>
        /// <item>12 列（旧）：ID Speaker HeadProfile CharL CharM CharR Text BG BGM Voice <b>Command</b> Note</item>
        /// </list>
        /// </summary>
        private struct CsvLayout
        {
            public bool IsWide;   // true = 14 列格式

            public int CommandIndex => IsWide ? 12 : 10;
            public int MinCellCount => IsWide ? 14 : 12;

            public static CsvLayout Detect(int cellCount)
            {
                return new CsvLayout { IsWide = cellCount >= 14 };
            }
        }

        private CsvLayout _layout;

        // ==================== 打开入口 ====================

        [MenuItem("VNovelizer/行演出编辑器", false, 210)]
        public static void Open()
        {
            var win = GetWindow<RowPerfEditorWindow>("行演出编辑器");
            win.minSize = new Vector2(1080, 560);
            win.Show();
        }

        /// <summary>从剧本管理器双击 Command 单元格打开，直达指定行。</summary>
        public static void OpenForRow(string csvPath, string lineId)
        {
            var win = GetWindow<RowPerfEditorWindow>("行演出编辑器");
            win.minSize = new Vector2(1080, 560);
            win.Show();
            win.LoadCsv(csvPath);
            win.GotoLineId(lineId);
        }

        // ==================== 生命周期 ====================

        private void CreateGUI()
        {
            var root = rootVisualElement;
            root.AddToClassList("vn-root");

            var uss = AssetDatabase.LoadAssetAtPath<StyleSheet>(FindStyleSheetPath());
            if (uss != null) root.styleSheets.Add(uss);

            BuildToolbar(root);

            var main = new VisualElement();
            main.AddToClassList("vn-main");
            root.Add(main);

            var paletteRoot = new VisualElement();
            main.Add(paletteRoot);
            _palette = new CommandPalette(paletteRoot);
            _palette.OnRequestCreateNode += HandleCreateNode;
            _palette.OnRequestCreateForkJoin += HandleCreateForkJoin;

            _graphView = new RowGraphView();
            _graphView.style.flexGrow = 1;
            _graphView.OnGraphChanged += HandleGraphChanged;
            _graphView.OnRequestPromotion += HandlePromotionRequest;
            _graphView.OnRequestCreateNodeAt += (cmd, isConfirm, pos) => HandleCreateNode(cmd, isConfirm, pos);
            main.Add(_graphView);

            var inspectorRoot = new VisualElement();
            main.Add(inspectorRoot);
            _inspector = new InspectorBuilder(inspectorRoot);
            _inspector.OnValueChanged += HandleGraphChanged;
            _inspector.OnRequestJumpToColumn += HandleJumpToColumn;
            _inspector.OnChainTextChanged += HandleChainTextChanged;

            // 先订阅 _graphView 事件再初始化显示——避免初始化 Show 期间
            // 因事件竞态而错过初次选中通知
            _graphView.OnNodeSelected += node => _inspector.Show(node);

            BuildStatusBar(root);

            root.RegisterCallback<KeyDownEvent>(HandleShortcuts);

            if (string.IsNullOrEmpty(_csvPath)) TryAutoSelectScript();
            else RefreshAll();
        }

        private static string FindStyleSheetPath()
        {
            var guids = AssetDatabase.FindAssets("RowPerfEditor t:StyleSheet");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith("RowPerfEditor.uss")) return path;
            }
            return "";
        }

        // ==================== 工具栏 ====================

        private void BuildToolbar(VisualElement root)
        {
            var bar = new VisualElement();
            bar.AddToClassList("vn-toolbar");

            var scriptButton = new Button(ShowScriptPicker) { text = "剧本…" };
            scriptButton.tooltip = "选择要编辑的剧本 CSV";
            bar.Add(scriptButton);

            bar.Add(MakeDivider());

            var prev = new Button(() => StepRow(-1)) { text = "◀" };
            prev.tooltip = "上一行";
            bar.Add(prev);

            _lineIdField = new TextField { value = "" };
            _lineIdField.style.width = 74;
            _lineIdField.tooltip = "输入行 ID 后回车跳转";
            _lineIdField.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                {
                    GotoLineId(_lineIdField.value);
                    evt.StopPropagation();
                }
            });
            bar.Add(_lineIdField);

            var next = new Button(() => StepRow(1)) { text = "下一行 >" };
            next.tooltip = "下一行";
            bar.Add(next);

            bar.Add(MakeDivider());

            var undo = new Button(PerformUndo) { text = "撤销" };
            undo.tooltip = "撤销 (Ctrl+Z)";
            bar.Add(undo);

            var redo = new Button(PerformRedo) { text = "重做" };
            redo.tooltip = "重做 (Ctrl+Y)";
            bar.Add(redo);

            var copy = new Button(CopyChain) { text = "复制" };
            copy.tooltip = "复制本行命令链 (Ctrl+C)——可粘到其他行、其他剧本，甚至发给同事";
            bar.Add(copy);

            var paste = new Button(PasteChain) { text = "粘贴" };
            paste.tooltip = "粘贴命令链到当前行 (Ctrl+V)";
            bar.Add(paste);

            var relayout = new Button(() => _graphView?.RelayoutAll()) { text = "整理布局" };
            relayout.tooltip = "整理布局：按执行顺序重新排布全部节点";
            bar.Add(relayout);

            _lineSummary = new Label("");
            _lineSummary.AddToClassList("vn-line-summary");
            bar.Add(_lineSummary);

            _formBadge = new Label("—");
            _formBadge.AddToClassList("vn-rowform-badge");
            bar.Add(_formBadge);

            _resetButton = new Button(ResetToTemplate) { text = "重置模板" };
            _resetButton.tooltip = "移除系统命令，恢复为数据列驱动的默认演出";
            bar.Add(_resetButton);

            _saveButton = new Button(SaveCurrentRow) { text = "保存到 CSV" };
            bar.Add(_saveButton);

            root.Add(bar);
        }

        private static VisualElement MakeDivider()
        {
            var d = new VisualElement();
            d.AddToClassList("vn-toolbar-divider");
            return d;
        }

        private void BuildStatusBar(VisualElement root)
        {
            var bar = new VisualElement();
            bar.AddToClassList("vn-statusbar");

            _statusForm = new Label("");
            bar.Add(_statusForm);

            _statusValidation = new Label("");
            bar.Add(_statusValidation);

            _statusUndo = new Label("");
            bar.Add(_statusUndo);

            _statusChain = new Label("");
            _statusChain.style.flexGrow = 1;
            bar.Add(_statusChain);

            root.Add(bar);
        }

        // ==================== CSV 载入 ====================

        private void ShowScriptPicker()
        {
            var paths = FindScriptCsvPaths();
            if (paths.Count == 0)
            {
                EditorUtility.DisplayDialog("没有找到剧本",
                    "工程中未找到剧本 CSV。请先用「剧本管理器」新建剧本并执行转换。", "好");
                return;
            }

            var menu = new GenericMenu();
            foreach (string path in paths)
            {
                string display = Path.GetFileNameWithoutExtension(path);
                string captured = path;
                menu.AddItem(new GUIContent(display), path == _csvPath, () =>
                {
                    if (!ConfirmDiscardIfDirty()) return;
                    LoadCsv(captured);
                    if (_rows.Count > 0) SelectRow(0);
                });
            }
            menu.ShowAsContext();
        }

        private static List<string> FindScriptCsvPaths()
        {
            var result = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:TextAsset"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                    result.Add(path);
            }
            result.Sort();
            return result;
        }

        private void TryAutoSelectScript()
        {
            var paths = FindScriptCsvPaths();
            if (paths.Count == 0)
            {
                _lineSummary.text = "未找到剧本 CSV —— 请先用「剧本管理器」新建并转换剧本";
                return;
            }

            LoadCsv(paths[0]);
            if (_rows.Count > 0) SelectRow(0);
        }

        /// <summary>载入 CSV。解析复用与运行时相同的列约定，避免两处格式理解不一致。</summary>
        public void LoadCsv(string csvPath)
        {
            _csvPath = csvPath;
            _scriptName = Path.GetFileNameWithoutExtension(csvPath);
            _rows.Clear();
            _currentRowIndex = -1;

            if (string.IsNullOrEmpty(csvPath) || !File.Exists(csvPath))
            {
                _lineSummary.text = "CSV 不存在：" + csvPath;
                return;
            }

            string[] lines = File.ReadAllLines(csvPath);
            bool layoutDetected = false;

            for (int i = 1; i < lines.Length; i++) // 跳过表头
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;

                var cells = ParseCsvLine(lines[i]);
                if (cells.Count == 0) continue;

                if (!layoutDetected)
                {
                    _layout = CsvLayout.Detect(cells.Count);
                    layoutDetected = true;
                }

                if (cells.Count < _layout.MinCellCount) continue; // 残缺行跳过，不猜测

                _rows.Add(_layout.IsWide ? ReadWideRow(cells) : ReadLegacyRow(cells));
            }

            if (!layoutDetected)
                _lineSummary.text = $"{_scriptName} — CSV 中没有数据行";
        }

        /// <summary>读 14 列格式（与 ScriptParser 的列序严格一致）。</summary>
        private static CsvRow ReadWideRow(List<string> c)
        {
            return new CsvRow
            {
                Id = Cell(c, 0),
                Speaker = Cell(c, 1),
                HeadProfile = Cell(c, 2),
                CharLeft = Cell(c, 3),
                CharMidLeft = Cell(c, 4),
                CharMid = Cell(c, 5),
                CharMidRight = Cell(c, 6),
                CharRight = Cell(c, 7),
                Text = Cell(c, 8),
                Background = Cell(c, 9),
                Bgm = Cell(c, 10),
                Voice = Cell(c, 11),
                Command = Cell(c, 12),
                Cells = c,
            };
        }

        /// <summary>读旧 12 列格式（无 CharML / CharMR 两列）。</summary>
        private static CsvRow ReadLegacyRow(List<string> c)
        {
            return new CsvRow
            {
                Id = Cell(c, 0),
                Speaker = Cell(c, 1),
                HeadProfile = Cell(c, 2),
                CharLeft = Cell(c, 3),
                CharMidLeft = "",
                CharMid = Cell(c, 4),
                CharMidRight = "",
                CharRight = Cell(c, 5),
                Text = Cell(c, 6),
                Background = Cell(c, 7),
                Bgm = Cell(c, 8),
                Voice = Cell(c, 9),
                Command = Cell(c, 10),
                Cells = c,
            };
        }

        private static string Cell(List<string> cells, int index)
            => index >= 0 && index < cells.Count ? cells[index] : "";

        /// <summary>引号感知的 CSV 行解析（与 ScriptParser 的规则一致）。</summary>
        private static List<string> ParseCsvLine(string line)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(line)) return result;

            var sb = new System.Text.StringBuilder();
            bool inQuote = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (inQuote)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                        else inQuote = false;
                    }
                    else sb.Append(c);
                    continue;
                }

                if (c == '"') { inQuote = true; continue; }
                if (c == ',') { result.Add(sb.ToString()); sb.Clear(); continue; }
                sb.Append(c);
            }

            result.Add(sb.ToString());
            return result;
        }

        // ==================== 行切换 ====================

        private void StepRow(int delta)
        {
            if (_rows.Count == 0) return;
            if (!ConfirmDiscardIfDirty()) return;

            int target = Mathf.Clamp(_currentRowIndex + delta, 0, _rows.Count - 1);
            if (target != _currentRowIndex) SelectRow(target);
        }

        public void GotoLineId(string lineId)
        {
            if (string.IsNullOrWhiteSpace(lineId) || _rows.Count == 0) return;
            if (!ConfirmDiscardIfDirty()) return;

            int index = _rows.FindIndex(r => r.Id == lineId.Trim());
            if (index < 0)
            {
                EditorUtility.DisplayDialog("未找到行", $"剧本中不存在行 ID：{lineId}", "好");
                return;
            }
            SelectRow(index);
        }

        private void SelectRow(int index)
        {
            if (index < 0 || index >= _rows.Count) return;

            _currentRowIndex = index;
            _isDirty = false;
            _undoStack.Clear(); // 撤销不跨行

            RefreshAll();
            PushUndoSnapshot("打开行");
        }

        // ==================== 图重建与刷新 ====================

        private void RefreshAll()
        {
            if (_graphView == null) return;

            var row = CurrentRow;
            if (row == null)
            {
                _graphView.Rebuild(new ChainGraph(), new ChainGraph());
                _inspector?.Show(null);
                UpdateHeaderAndStatus();
                return;
            }

            _lineIdField.SetValueWithoutNotify(row.Id);

            SplitCommandColumn(row.Command, out string entryText, out string confirmText);

            var form = RowPromotion.DetermineForm(row.Command);

            // 2026-08-27（用户 Q1）：默认演出彻底展开，删除折叠胶囊。
            // Normal 行 → 合成纯系统命令模板图；
            // Enhanced 行 → 合成"系统命令 -> 用户链"完整模板图；
            // Custom 行 → 原样渲染。
            // 用户一旦编辑合成图（HandleGraphChanged 置 dirty），保存即"提升"写入 Command 列。
            ChainGraph entryGraph;
            switch (form)
            {
                case RowForm.Normal:
                    entryGraph = BuildGraph(DefaultPerformanceTemplate.BuildText(null));
                    _syntheticTemplate = true;
                    break;
                case RowForm.Enhanced:
                    entryGraph = BuildGraph(DefaultPerformanceTemplate.BuildText(entryText));
                    _syntheticTemplate = true;
                    break;
                default:
                    entryGraph = BuildGraph(entryText);
                    _syntheticTemplate = false;
                    break;
            }

            var confirmGraph = BuildGraph(confirmText);

            _graphView.LineContext = BuildLineContext(row);
            _graphView.Rebuild(entryGraph, confirmGraph,
                GraphPosStore.LoadPositions(_csvPath, row.Id),
                templateCollapsed: false, showTemplate: false);

            // 同步链文本到 Inspector 文本页签（合成模板时显示完整模板文本）
            string entryDisplay = _syntheticTemplate
                ? DefaultPerformanceTemplate.BuildText(
                    form == RowForm.Enhanced ? entryText : null)
                : entryText;
            _inspector?.SetChainTexts(entryDisplay, confirmText);

            Validate();
            UpdateHeaderAndStatus();
        }

        private static ChainGraph BuildGraph(string chainText)
        {
            if (string.IsNullOrWhiteSpace(chainText)) return new ChainGraph();

            var parsed = ChainParser.Parse(chainText);
            return parsed.Root != null ? AstToGraph.Convert(parsed.Root) : new ChainGraph();
        }

        /// <summary>把 Command 列拆为进入段与出口段（与 ScriptParser 同规则，引号感知）。</summary>
        private static void SplitCommandColumn(string command, out string entry, out string confirm)
        {
            entry = command ?? "";
            confirm = "";
            if (string.IsNullOrEmpty(command)) return;

            int idx = IndexOfConfirmToken(command);
            if (idx < 0) return;

            entry = command.Substring(0, idx).Trim().TrimEnd('&').Trim();
            confirm = command.Substring(idx + "@confirm:".Length).Trim();
        }

        private static int IndexOfConfirmToken(string source)
        {
            const string token = "@confirm:";
            bool inQuote = false;

            for (int i = 0; i < source.Length; i++)
            {
                char c = source[i];
                if (inQuote)
                {
                    if (c == '\\') { i++; continue; }
                    if (c == '"') inQuote = false;
                    continue;
                }
                if (c == '"') { inQuote = true; continue; }

                if (c == '@' && i + token.Length <= source.Length &&
                    string.Compare(source, i, token, 0, token.Length,
                        StringComparison.OrdinalIgnoreCase) == 0)
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// 构造行上下文供模板胶囊显示数据列值。
        /// 这里用 CSV 原始值而非运行时解析值——编辑器不应依赖播放状态。
        /// </summary>
        private VNLineContext BuildLineContext(CsvRow row)
        {
            return new VNLineContext(
                lineID: row.Id,
                lineIndex: _currentRowIndex,
                speaker: row.Speaker,
                text: row.Text,
                headProfile: row.HeadProfile,
                background: row.Background,
                bgm: row.Bgm,
                voice: row.Voice,
                charLeft: row.CharLeft,
                charMidLeft: row.CharMidLeft,
                charMid: row.CharMid,
                charMidRight: row.CharMidRight,
                charRight: row.CharRight,
                isSimulating: false);
        }

        private CsvRow CurrentRow =>
            _currentRowIndex >= 0 && _currentRowIndex < _rows.Count ? _rows[_currentRowIndex] : null;

        private void UpdateHeaderAndStatus()
        {
            var row = CurrentRow;

            if (row == null)
            {
                _lineSummary.text = string.IsNullOrEmpty(_csvPath)
                    ? "未选择剧本"
                    : $"{_scriptName} — 无可编辑行";
                _formBadge.text = "—";
                _statusForm.text = "";
                _statusChain.text = "";
                return;
            }

            string speaker = string.IsNullOrEmpty(row.Speaker) ? "（旁白）" : row.Speaker;
            string text = row.Text ?? "";
            if (text.Length > 40) text = text.Substring(0, 40) + "…";

            _lineSummary.text = $"{_scriptName} · {row.Id}　{speaker}：「{text}」" +
                                (_isDirty ? "　●未保存" : "");

            var form = RowPromotion.DetermineForm(row.Command);
            _formBadge.text = "● " + RowPromotion.FormLabel(form);
            _formBadge.tooltip = RowPromotion.FormTooltip(form);
            _formBadge.RemoveFromClassList("vn-rowform-normal");
            _formBadge.RemoveFromClassList("vn-rowform-enhanced");
            _formBadge.RemoveFromClassList("vn-rowform-custom");
            _formBadge.AddToClassList(RowPromotion.FormStyleClass(form));

            _resetButton.SetEnabled(form == RowForm.Custom);

            _statusForm.text = "形态：" + RowPromotion.FormLabel(form);
            _statusUndo.text = _undoStack.CanUndo ? $"撤销栈 {_undoStack.Depth - 1}" : "";
            _statusChain.text = BuildChainPreviewText();

            UpdateValidationStatus();
        }

        private string BuildChainPreviewText()
        {
            if (_graphView == null) return "";

            string entry = SerializeGraphSafe(_graphView.EntryGraph);
            string confirm = SerializeGraphSafe(_graphView.ConfirmGraph);

            if (string.IsNullOrEmpty(entry) && string.IsNullOrEmpty(confirm))
                return "（本行无命令，使用默认演出）";

            string text = entry;
            if (!string.IsNullOrEmpty(confirm)) text += "  @Confirm:" + confirm;
            return text;
        }

        private static string SerializeGraphSafe(ChainGraph graph)
        {
            if (graph == null || graph.NodeCount == 0) return "";
            var converted = GraphToAst.Convert(graph);
            return converted.Success && converted.Root != null
                ? ChainSerializer.Serialize(converted.Root)
                : "(图结构待修正)";
        }

        // ==================== 校验 ====================

        private void Validate()
        {
            if (_graphView == null) return;

            bool entryHasChoice = GraphContainsCommand(_graphView.EntryGraph, "choice");

            _entryValidation = ChainGraphValidator.Validate(
                _graphView.EntryGraph, isConfirmSection: false);
            _confirmValidation = ChainGraphValidator.Validate(
                _graphView.ConfirmGraph, isConfirmSection: true,
                entrySectionHasChoice: entryHasChoice);

            _graphView.ApplyValidation(_entryValidation, _confirmValidation);
            UpdateValidationStatus();
        }

        private static bool GraphContainsCommand(ChainGraph graph, string commandName)
        {
            if (graph == null) return false;
            foreach (var node in graph.Nodes)
                if (node.Kind == ChainGraphNodeKind.Command &&
                    string.Equals(node.CommandName, commandName, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        private void UpdateValidationStatus()
        {
            if (_statusValidation == null) return;

            int fatal = (_entryValidation?.FatalCount ?? 0) + (_confirmValidation?.FatalCount ?? 0);
            int warn = (_entryValidation?.WarningCount ?? 0) + (_confirmValidation?.WarningCount ?? 0);

            if (fatal > 0)
            {
                _statusValidation.text = $"[X] {fatal} 个致命错误（无法保存）" +
                                         (warn > 0 ? $" · {warn} 警告" : "");
                _statusValidation.style.color = new Color(0.78f, 0.35f, 0.31f);
            }
            else if (warn > 0)
            {
                _statusValidation.text = $"[!] {warn} 个警告（可保存）";
                _statusValidation.style.color = new Color(0.78f, 0.63f, 0.24f);
            }
            else
            {
                _statusValidation.text = "[OK] 校验通过";
                _statusValidation.style.color = new Color(0.50f, 0.67f, 0.36f);
            }

            _saveButton?.SetEnabled(fatal == 0);
        }

        // ==================== 交互处理 ====================

        private void HandleCreateNode(string commandName, bool isConfirm, Vector2? position)
        {
            if (CurrentRow == null) return;

            // 有拖拽落点用落点，否则在泳道起始位置创建
            Vector2 pos = position ?? new Vector2(
                ChainAutoLayout.StartX + 200f + UnityEngine.Random.Range(0, 3) * 30f,
                isConfirm ? ChainAutoLayout.ConfirmLaneY : ChainAutoLayout.EntryLaneY);

            var info = CommandMetaReader.Get(commandName);
            string defaultArgs = BuildDefaultArgs(info);

            var view = _graphView.CreateCommandNode(commandName, defaultArgs, pos, isConfirm);
            _graphView.ClearSelection();
            _graphView.AddToSelection(view);
        }

        /// <summary>
        /// 用元数据的默认值预填参数——比留空更友好：
        /// 用户看到的是可运行的命令，而非需要逐项猜测的空壳。
        /// 支持隐式绑定的参数保持为空（那是"引用数据列"的语义）。
        /// </summary>
        private static string BuildDefaultArgs(VNCommandInfo info)
        {
            if (info == null || !info.HasMeta || info.Parameters.Count == 0) return "";

            var values = new List<string>();
            foreach (var p in info.Parameters)
            {
                if (p.ImplicitBinding) { values.Add(""); continue; }
                values.Add(p.Default ?? "");
            }

            while (values.Count > 0 && string.IsNullOrEmpty(values[values.Count - 1]))
                values.RemoveAt(values.Count - 1);

            return string.Join(info.ArgSeparator.ToString(), values);
        }

        private void HandleCreateForkJoin(bool isConfirm)
        {
            if (CurrentRow == null) return;

            Vector2 pos = new Vector2(
                ChainAutoLayout.StartX + 200f,
                isConfirm ? ChainAutoLayout.ConfirmLaneY : ChainAutoLayout.EntryLaneY);

            _graphView.CreateForkJoinPair(pos, isConfirm);
        }

        private void HandleGraphChanged()
        {
            _isDirty = true;
            Validate();
            UpdateHeaderAndStatus();
            PushUndoSnapshot("编辑");
        }

        /// <summary>
        /// 命令链文本被编辑（Inspector 文本页签）→ 解析 → 重建对应图。
        /// 文本与图是同一真值的两个视图（2026-08-27 用户需求 5）。
        /// </summary>
        private void HandleChainTextChanged(bool isConfirm, string newText)
        {
            if (_graphView == null) return;

            var newGraph = BuildGraph(newText);
            var positions = _graphView.CollectPositions();

            var entryGraph = isConfirm ? _graphView.EntryGraph : newGraph;
            var confirmGraph = isConfirm ? newGraph : _graphView.ConfirmGraph;

            // 进入段文本编辑 = 用户显式定制，清除合成模板标志
            if (!isConfirm) _syntheticTemplate = false;

            _graphView.Rebuild(entryGraph, confirmGraph, positions,
                templateCollapsed: false, showTemplate: false);

            // 用规范化序列化文本回填（文本与图互相校准）
            _inspector.SetChainTexts(
                SerializeGraphSafe(entryGraph),
                SerializeGraphSafe(confirmGraph));

            HandleGraphChanged(); // dirty + 校验 + 快照
        }

        private void HandlePromotionRequest()
        {
            var row = CurrentRow;
            if (row == null) return;

            var form = RowPromotion.DetermineForm(row.Command);
            if (form == RowForm.Custom) return; // 已是定制行

            if (!RowPromotion.ConfirmPromotion(form)) return;

            row.Command = RowPromotion.BuildPromotedText(row.Command);
            _isDirty = true;
            RefreshAll();
            PushUndoSnapshot("提升为定制行");
        }

        private void ResetToTemplate()
        {
            var row = CurrentRow;
            if (row == null) return;
            if (RowPromotion.DetermineForm(row.Command) != RowForm.Custom) return;

            if (!RowPromotion.ConfirmReset()) return;

            row.Command = RowPromotion.StripSystemCommands(row.Command);
            _isDirty = true;
            RefreshAll();
            PushUndoSnapshot("重置回模板");
        }

        private void HandleJumpToColumn(string column)
        {
            var row = CurrentRow;
            if (row == null || string.IsNullOrEmpty(column)) return;

            EditorUtility.DisplayDialog("数据列",
                $"行 {row.Id} 的 {column} 列当前值：\n\n" +
                (_graphView.LineContext?.GetColumn(column) ?? "(空)") + "\n\n" +
                "数据列请在 Excel 中编辑——Command 列归本编辑器，其余列归表格，" +
                "两者互不覆盖。", "好");
        }

        // ==================== 撤销 / 剪贴板 ====================

        private void PushUndoSnapshot(string label)
        {
            if (_graphView == null || CurrentRow == null) return;

            _undoStack.Push(new GraphUndoStack.Snapshot
            {
                EntryChainText = SerializeGraphSafe(_graphView.EntryGraph),
                ConfirmChainText = SerializeGraphSafe(_graphView.ConfirmGraph),
                Positions = _graphView.CollectPositions(),
                TemplateCollapsed = true,
                Label = label,
            });

            _statusUndo.text = _undoStack.CanUndo ? $"撤销栈 {_undoStack.Depth - 1}" : "";
        }

        private void PerformUndo()
        {
            var snapshot = _undoStack.Undo();
            if (snapshot == null) return;
            RestoreSnapshot(snapshot);
        }

        private void PerformRedo()
        {
            var snapshot = _undoStack.Redo();
            if (snapshot == null) return;
            RestoreSnapshot(snapshot);
        }

        private void RestoreSnapshot(GraphUndoStack.Snapshot snapshot)
        {
            var entryGraph = BuildGraph(snapshot.EntryChainText);
            var confirmGraph = BuildGraph(snapshot.ConfirmChainText);

            var row = CurrentRow;
            bool showTemplate = row == null ||
                                RowPromotion.DetermineForm(row.Command) != RowForm.Custom;

            _graphView.Rebuild(entryGraph, confirmGraph, snapshot.Positions,
                snapshot.TemplateCollapsed, showTemplate);

            _isDirty = true;
            Validate();
            UpdateHeaderAndStatus();
        }

        private void CopyChain()
        {
            if (_graphView == null) return;

            ChainClipboard.Copy(
                SerializeGraphSafe(_graphView.EntryGraph),
                SerializeGraphSafe(_graphView.ConfirmGraph));

            ShowNotification(new GUIContent("已复制命令链"));
        }

        private void PasteChain()
        {
            if (CurrentRow == null) return;

            if (!ChainClipboard.ValidatePasteContent(out string error))
            {
                EditorUtility.DisplayDialog("无法粘贴", error, "好");
                return;
            }

            ChainClipboard.TryPaste(out string entry, out string confirm);

            _graphView.Rebuild(BuildGraph(entry), BuildGraph(confirm), null, true,
                RowPromotion.DetermineForm(CurrentRow.Command) != RowForm.Custom);

            _isDirty = true;
            Validate();
            UpdateHeaderAndStatus();
            PushUndoSnapshot("粘贴命令链");
        }

        private void HandleShortcuts(KeyDownEvent evt)
        {
            if (!evt.ctrlKey && !evt.commandKey) return;

            switch (evt.keyCode)
            {
                case KeyCode.Z: PerformUndo(); evt.StopPropagation(); break;
                case KeyCode.Y: PerformRedo(); evt.StopPropagation(); break;
                case KeyCode.C: CopyChain(); evt.StopPropagation(); break;
                case KeyCode.V: PasteChain(); evt.StopPropagation(); break;
                case KeyCode.S: SaveCurrentRow(); evt.StopPropagation(); break;
            }
        }

        // ==================== 保存 ====================

        private void SaveCurrentRow()
        {
            var row = CurrentRow;
            if (row == null || string.IsNullOrEmpty(_csvPath)) return;

            // 1. 校验（致命错误阻断）
            Validate();
            if ((_entryValidation?.HasFatal ?? false) || (_confirmValidation?.HasFatal ?? false))
            {
                EditorUtility.DisplayDialog("无法保存",
                    "图中存在致命错误，请先修正标红的节点。\n\n" +
                    BuildIssueSummary(), "好");
                return;
            }

            // 2. 图 → AST → 文本（含幂等自校验）
            // 2026-08-27：合成模板图未被用户编辑时，保持原 Command 列文本不写回
            // （Normal/Enhanced 行展开的模板节点是视图合成物，不主动"提升"）。
            if (_syntheticTemplate && !_isDirty)
            {
                SavePositionsOnly(row);
                ShowNotification(new GUIContent("命令链未修改（展开的模板为视图合成），已保存节点位置"));
                return;
            }

            if (!TrySerialize(_graphView.EntryGraph, out string entryText, out string error) ||
                !TrySerialize(_graphView.ConfirmGraph, out string confirmText, out error))
            {
                EditorUtility.DisplayDialog("序列化失败", error, "好");
                return;
            }

            string newCommand = entryText;
            if (!string.IsNullOrWhiteSpace(confirmText))
            {
                if (!string.IsNullOrWhiteSpace(newCommand)) newCommand += "&";
                newCommand += "@Confirm:" + confirmText;
            }

            // 3. 未变更则不写回——存量剧本手写的排版格式得以完整保留
            if (row.Command == newCommand)
            {
                SavePositionsOnly(row);
                _isDirty = false;
                UpdateHeaderAndStatus();
                ShowNotification(new GUIContent("命令链无变化，已保存节点位置"));
                return;
            }

            row.Command = newCommand;

            if (!TryWriteCsv(out string writeError))
            {
                EditorUtility.DisplayDialog("写入失败", writeError, "好");
                return;
            }

            SavePositionsOnly(row);
            _isDirty = false;
            RefreshAll();
            ShowNotification(new GUIContent("已保存到 CSV"));
        }

        private void SavePositionsOnly(CsvRow row)
        {
            GraphPosStore.Save(_csvPath, row.Id, _graphView.CollectPositions(), true);
        }

        private static bool TrySerialize(ChainGraph graph, out string text, out string error)
        {
            text = "";
            error = null;

            if (graph == null || graph.NodeCount == 0) return true;

            var converted = GraphToAst.Convert(graph);
            if (!converted.Success)
            {
                error = "图结构无法分解为命令链：\n" + string.Join("\n", converted.Errors);
                return false;
            }
            if (converted.Root == null) return true;

            var serialized = ChainSerializer.SerializeAndVerify(converted.Root);
            if (!serialized.Success)
            {
                error = "序列化自校验失败（这属于内部错误，请反馈）：\n" +
                        string.Join("\n", serialized.Errors);
                return false;
            }

            text = serialized.Text;
            return true;
        }

        private string BuildIssueSummary()
        {
            var lines = new List<string>();
            foreach (var result in new[] { _entryValidation, _confirmValidation })
            {
                if (result == null) continue;
                foreach (var issue in result.Issues)
                    if (issue.Level == ChainGraphIssueLevel.Fatal)
                        lines.Add("· " + issue.Message);
            }
            return string.Join("\n", lines);
        }

        /// <summary>
        /// 原子写 CSV：写临时文件后替换，避免 <c>AutoExcelConverter</c> 的 2 秒轮询
        /// 读到半写文件。轮询恰好撞上写入的概率不高，但一旦发生就是数据损坏，
        /// 而原子写的成本只是多一次文件移动。
        /// </summary>
        private bool TryWriteCsv(out string error)
        {
            error = null;

            try
            {
                string[] original = File.ReadAllLines(_csvPath);
                if (original.Length == 0) { error = "CSV 为空"; return false; }

                var output = new List<string> { original[0] }; // 表头原样保留

                // 按行 ID 匹配而非按序号——载入时残缺行会被跳过，
                // 若按序号对应会把命令写到错误的行上。
                var byId = new Dictionary<string, CsvRow>();
                foreach (var row in _rows)
                    if (!string.IsNullOrEmpty(row.Id)) byId[row.Id] = row;

                for (int i = 1; i < original.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(original[i]))
                    {
                        output.Add(original[i]);
                        continue;
                    }

                    var cells = ParseCsvLine(original[i]);
                    string id = Cell(cells, 0);

                    if (!string.IsNullOrEmpty(id) && byId.TryGetValue(id, out var row))
                        output.Add(BuildCsvLine(row));
                    else
                        output.Add(original[i]); // 未识别的行原样保留，绝不丢内容
                }

                string tempPath = _csvPath + ".tmp";
                File.WriteAllLines(tempPath, output);

                if (File.Exists(_csvPath))
                {
                    // File.Replace 在同卷内是原子操作
                    File.Replace(tempPath, _csvPath, null);
                }
                else
                {
                    File.Move(tempPath, _csvPath);
                }

                AssetDatabase.ImportAsset(_csvPath);
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }
        }

        /// <summary>
        /// 重建一行 CSV：只替换 Command 列，其余单元格原样写回。
        /// 列索引取自 <see cref="_layout"/>——写错这里会把命令写进 Note 列，
        /// 是最需要防范的一类错误。
        /// </summary>
        private string BuildCsvLine(CsvRow row)
        {
            var cells = new List<string>(row.Cells);
            int cmdIndex = _layout.CommandIndex;

            while (cells.Count <= cmdIndex) cells.Add("");
            cells[cmdIndex] = row.Command ?? "";

            var parts = new List<string>();
            foreach (string cell in cells) parts.Add(EscapeCsv(cell));
            return string.Join(",", parts);
        }

        private static string EscapeCsv(string value)
        {
            value = value ?? "";
            bool needQuote = value.Contains(",") || value.Contains("\"") ||
                             value.Contains("\n") || value.Contains("\r");
            if (!needQuote) return value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        private bool ConfirmDiscardIfDirty()
        {
            if (!_isDirty) return true;

            int choice = EditorUtility.DisplayDialogComplex(
                "当前行有未保存的修改",
                "切换行会丢弃这些修改。要先保存吗？",
                "保存并切换", "取消", "放弃修改");

            switch (choice)
            {
                case 0: SaveCurrentRow(); return !_isDirty;
                case 2: _isDirty = false; return true;
                default: return false;
            }
        }
    }
}
