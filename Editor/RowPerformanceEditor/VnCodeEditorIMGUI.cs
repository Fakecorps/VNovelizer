using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace VNovelizer.Editor.RowPerformanceEditor
{
    /// <summary>
    /// IMGUI 实现的命令链代码编辑器（2026-08-31 重写）。
    ///
    /// <para>
    /// <b>为什么放弃 UIToolkit 方案</b>：UIToolkit 的 <c>TextField</c> 是封闭黑盒，
    /// 不暴露光标索引 / 光标像素坐标 / 选区 / 行布局。在其上"叠加着色层"必然导致
    /// 光标不可见（USS 无 caret-color）、选区不可见、长行错位、滚动不同步。
    /// IMGUI 下一切自绘，上述问题全部消失。
    /// </para>
    ///
    /// <para>
    /// <b>不依赖 TextEditor 的 internal 方法</b>：<c>HandleKeyEvent</c> 等是 internal，
    /// 外部不可调用。因此本类<b>自己维护</b>文本 / 光标 / 选区，
    /// 只使用 TextEditor 的公开字段做状态同步。
    /// </para>
    ///
    /// <para>
    /// <b>等宽字体是正确性前提</b>：字符索引 → 像素坐标换算为一次乘法
    /// （<c>x = 列号 × 字符宽</c>），光标 / 选区 / 行号必然精确对齐。
    /// 比例字体下无法这样算，只能逐字累加测量。
    /// </para>
    ///
    /// <para><b>自动换行</b>：与截图中上方预览块的视觉效果对齐 ——
    /// 长命令链不会溢出到不可见区域，而是按编辑器宽度自动折行，
    /// 每行一条命令、按嵌套深度缩进、关键字着色，阅读体验接近 IDE。</para>
    /// </summary>
    public class VnCodeEditorIMGUI
    {
        // ---------------- 配置 ----------------

        public int FontSize = 12;
        public bool ReadOnly;

        /// <summary>画布选中节点的命令签名（如 showbg()）——匹配行整行高亮。</summary>
        public string SelectedSignature;

        /// <summary>文本被用户编辑（每次改动立即触发；外部做防抖）。</summary>
        public event Action<string> OnTextChanged;

        /// <summary>焦点从本编辑器移出。外部可在此做规范化回填 —— 打字期间不能回填。</summary>
        public event Action OnFocusLost;

        /// <summary>
        /// 是否持有键盘焦点。外部（如规范化回填）据此判断能否安全覆写文本：
        /// 用户正在编辑器里打字时覆写 = 光标乱跳 + 正在输入的字符被吃掉。
        /// </summary>
        public bool HasFocus => _hasFocus;

        // ---------------- 状态 ----------------

        private string _text = "";
        private int _cursor;        // 光标字符索引
        private int _selectAnchor;  // 选区锚点（= _cursor 表示无选区）

        private Vector2 _scroll;
        private int _controlId;
        private bool _hasFocus;

        // 期望列（上下移动时保持，跨越长短行不丢列）
        private int _desiredColumn = -1;

        // ---------------- 布局缓存 ----------------

        private readonly List<string> _lines = new List<string>();
        private readonly List<int> _lineStarts = new List<int>();
        private readonly List<List<Token>> _lineTokens = new List<List<Token>>();
        private bool _layoutDirty = true;

        // ---- 自动换行：逻辑行 → 可视行（2026-08-31）----
        // 逻辑行 = 按 '\n' 切分（编辑/光标索引以此为准，不受宽度影响）
        // 可视行 = 逻辑行按编辑器宽度折行后的结果（仅绘制与命中测试使用）
        private readonly List<VisualRow> _rows = new List<VisualRow>();
        private readonly List<int> _lineFirstRow = new List<int>();
        private bool _rowsDirty = true;
        private float _lastWrapWidth = -1f;

        private GUIStyle _textStyle;
        private GUIStyle _gutterStyle;
        private bool _stylesReady;

        private float _charWidth = 7.2f;
        private float _lineHeight = 16f;

        private const float GutterWidth = 38f;
        private const float PadLeft = 6f;
        private const float PadTop = 4f;

        // ---------------- 配色（与节点图/Inspector 深色主题一致） ----------------

        private static readonly Color ColBg = new Color(0.12f, 0.12f, 0.12f);
        private static readonly Color ColGutterBg = new Color(0.145f, 0.145f, 0.145f);
        private static readonly Color ColGutterText = new Color(0.43f, 0.43f, 0.43f);
        private static readonly Color ColPlain = new Color(0.85f, 0.85f, 0.85f);
        private static readonly Color ColCommand = new Color(0.435f, 0.722f, 0.91f);   // #6FB8E8
        private static readonly Color ColString = new Color(0.659f, 0.784f, 0.529f);   // #A8C887
        private static readonly Color ColNumber = new Color(0.91f, 0.784f, 0.498f);    // #E8C87F
        private static readonly Color ColOperator = new Color(0.48f, 0.48f, 0.48f);    // #7A7A7A
        private static readonly Color ColPunct = new Color(0.43f, 0.43f, 0.43f);
        private static readonly Color ColSelection = new Color(0.29f, 0.565f, 0.886f, 0.35f);
        private static readonly Color ColCursor = new Color(0.95f, 0.95f, 0.95f);
        private static readonly Color ColCurrentLine = new Color(1f, 1f, 1f, 0.04f);
        private static readonly Color ColHighlight = new Color(1f, 0.706f, 0.329f);    // #FFB454

        // ---------------- 公开 API ----------------

        public string Text
        {
            get => _text;
            set
            {
                string v = value ?? "";
                if (_text == v) return;
                _text = v;
                _layoutDirty = true;
                ClampCursor();
            }
        }

        /// <summary>外部赋值时不触发 OnTextChanged（用于规范化回填）。</summary>
        public void SetTextWithoutNotify(string value)
        {
            string v = value ?? "";
            if (_text == v) return;
            _text = v;
            _layoutDirty = true;
            ClampCursor();
        }

        // ---------------- 主绘制入口 ----------------

        /// <summary>在给定矩形内绘制编辑器。由 IMGUIContainer 的 onGUIHandler 调用。</summary>
        public void OnGUI(Rect rect)
        {
            EnsureStyles();
            EnsureLayout(rect.width - GutterWidth - PadLeft * 2);

            bool wasFocus = _hasFocus;
            _controlId = GUIUtility.GetControlID(FocusType.Keyboard, rect);

            Event e = Event.current;

            // 点击获取焦点
            if (e.type == EventType.MouseDown && rect.Contains(e.mousePosition))
            {
                GUIUtility.keyboardControl = _controlId;
                _hasFocus = true;
                HandleMouseDown(rect, e);
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && _hasFocus && _dragging)
            {
                HandleMouseDrag(rect, e);
                e.Use();
            }
            else if (e.type == EventType.MouseUp && _dragging)
            {
                _dragging = false;
                e.Use();
            }
            else if (e.type == EventType.ScrollWheel && rect.Contains(e.mousePosition))
            {
                Scroll(e.delta * 14f);
                e.Use();
            }

            _hasFocus = GUIUtility.keyboardControl == _controlId;

            if (_hasFocus && e.type == EventType.KeyDown)
                HandleKeyDown(e);

            // 键盘编辑可能刚改过文本 → 绘制前必须让"逻辑行"与"可视行"再次对齐。
            // 否则会出现 _rows 还是旧的、_lineStarts 已经是新的，导致行索引越界。
            EnsureLayout(rect.width - GutterWidth - PadLeft * 2);

            // ---- 绘制 ----
            DrawBackground(rect);
            DrawCurrentLine(rect);
            DrawSelection(rect);
            DrawTokens(rect);
            DrawGutter(rect);
            if (_hasFocus) DrawCursor(rect);

            // 焦点环
            if (_hasFocus)
            {
                EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1), ColCommand);
                EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1, rect.width, 1), ColCommand);
                EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1, rect.height), ColCommand);
                EditorGUI.DrawRect(new Rect(rect.xMax - 1, rect.y, 1, rect.height), ColCommand);
            }

            if (wasFocus != _hasFocus)
            {
                _desiredColumn = -1;
                if (!_hasFocus) OnFocusLost?.Invoke();
            }
        }

        // ---------------- 样式 ----------------

        private void EnsureStyles()
        {
            if (_stylesReady) return;
            _stylesReady = true;

            _textStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = FontSize,
                richText = false,
                alignment = TextAnchor.UpperLeft,
                wordWrap = true,          // 2026-08-31：开启自动换行，长行不溢出
                clipping = TextClipping.Clip,
            };
            _textStyle.normal.textColor = ColPlain;
            _textStyle.active.textColor = ColPlain;
            _textStyle.focused.textColor = ColPlain;
            _textStyle.hover.textColor = ColPlain;

            _gutterStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = FontSize - 1,
                alignment = TextAnchor.UpperRight,
                wordWrap = false,
                clipping = TextClipping.Clip,
            };
            _gutterStyle.normal.textColor = ColGutterText;

            // 等宽字体：只赋给这两个局部 style，绝不碰 GUI.skin.font 全局状态
            Font mono = VnMonoFont.Get();
            if (mono != null)
            {
                _textStyle.font = mono;
                _gutterStyle.font = mono;
            }

            // 测量字符尺寸（等宽：任一字符等宽，用 'M' 代表）
            // 安全下限：字体未加载或测量异常时不会导致光标/文字完全错位
            GUIContent probe = new GUIContent("M");
            float measuredW = _textStyle.CalcSize(probe).x;
            _charWidth = Mathf.Max(7f, measuredW);   // 等宽字体 @12pt 通常 7-8px
            _lineHeight = Mathf.Max(FontSize + 4f, _textStyle.CalcHeight(probe, 999f));
        }

        // ---------------- 布局 ----------------

        /// <summary>
        /// 保证"逻辑行"与"可视行"都是最新的。两处调用点：
        /// OnGUI 入口（常规）与事件处理之后（键盘刚改过文本时）。
        /// 可视行依赖可用宽度，面板拖拽变宽/变窄也要重算折行。
        /// </summary>
        private void EnsureLayout(float contentWidth)
        {
            if (_layoutDirty) RebuildLayout();
            if (_rowsDirty || Mathf.Abs(contentWidth - _lastWrapWidth) > 0.5f)
                RebuildRows(contentWidth);
        }

        private void RebuildLayout()
        {
            _layoutDirty = false;
            _rowsDirty = true;          // 文本变了 → 可视行需重算
            _lines.Clear();
            _lineStarts.Clear();
            _lineTokens.Clear();

            if (string.IsNullOrEmpty(_text))
            {
                _lines.Add("");
                _lineStarts.Add(0);
                _lineTokens.Add(new List<Token>());
                return;
            }

            int start = 0;
            for (int i = 0; i <= _text.Length; i++)
            {
                if (i == _text.Length || _text[i] == '\n')
                {
                    string line = _text.Substring(start, i - start);
                    _lines.Add(line);
                    _lineStarts.Add(start);
                    _lineTokens.Add(Tokenize(line, start));
                    start = i + 1;
                }
            }
        }

        // 索引 ↔ 行/列
        private int IndexToLine(int index)
        {
            for (int i = _lineStarts.Count - 1; i >= 0; i--)
                if (index >= _lineStarts[i]) return i;
            return 0;
        }

        private int IndexToColumn(int index)
        {
            int line = IndexToLine(index);
            return index - _lineStarts[line];
        }

        private int PosToIndex(int line, int column)
        {
            if (line < 0) return 0;
            if (line >= _lines.Count) return _text.Length;
            int col = Mathf.Clamp(column, 0, _lines[line].Length);
            return _lineStarts[line] + col;
        }

        // 屏幕坐标 → 字符索引（按可视行命中）
        private int ScreenToIndex(Rect rect, Vector2 mouse)
        {
            if (_rows.Count == 0) return 0;

            float localY = mouse.y - rect.y - PadTop + _scroll.y;
            int row = Mathf.FloorToInt(localY / _lineHeight);
            if (row < 0) row = 0;
            if (row >= _rows.Count) row = _rows.Count - 1;

            var vr = _rows[row];

            float localX = mouse.x - rect.x - GutterWidth - PadLeft + _scroll.x;
            int column = Mathf.RoundToInt(localX / _charWidth);
            int rowLen = vr.ColEnd - vr.ColStart;
            if (column < 0) column = 0;
            if (column > rowLen) column = rowLen;

            return _lineStarts[vr.Line] + vr.ColStart + column;
        }

        // ---------------- 鼠标 ----------------

        private bool _dragging;

        private void HandleMouseDown(Rect rect, Event e)
        {
            if (e.button != 0) return;

            int index = ScreenToIndex(rect, e.mousePosition);

            if (e.clickCount >= 2)  // 双击选词
            {
                SelectWordAt(index);
            }
            else
            {
                // Shift+点击：保持锚点不动，只把光标移过去 → 扩展/收缩选区
                // 直接点击：锚点与光标一起落到点击处 → 取消选区
                if (!e.shift) _selectAnchor = index;
                _cursor = index;
            }

            _dragging = true;
            _desiredColumn = -1;
        }

        private void HandleMouseDrag(Rect rect, Event e)
        {
            _cursor = ScreenToIndex(rect, e.mousePosition);
        }

        // ---------------- 键盘 ----------------

        private void HandleKeyDown(Event e)
        {
            bool shift = e.shift;
            bool ctrl = e.control || e.command;

            // --- 复制 / 剪切 / 粘贴 / 全选 ---
            if (ctrl)
            {
                switch (e.keyCode)
                {
                    case KeyCode.A: SelectAll(); e.Use(); return;
                    case KeyCode.C: DoCopy(); e.Use(); return;
                    case KeyCode.X: DoCopy(); DeleteSelection(); e.Use(); return;
                    case KeyCode.V: DoPaste(); e.Use(); return;
                    case KeyCode.Z: e.Use(); return;   // 由外部 Undo 栈处理
                    case KeyCode.Y: e.Use(); return;
                }
            }

            switch (e.keyCode)
            {
                case KeyCode.LeftArrow:
                    MoveHorizontal(-1, shift, ctrl); e.Use(); return;
                case KeyCode.RightArrow:
                    MoveHorizontal(1, shift, ctrl); e.Use(); return;
                case KeyCode.UpArrow:
                    MoveVertical(-1, shift); e.Use(); return;
                case KeyCode.DownArrow:
                    MoveVertical(1, shift); e.Use(); return;
                case KeyCode.Home:
                    MoveToLineEdge(-1, shift); e.Use(); return;
                case KeyCode.End:
                    MoveToLineEdge(1, shift); e.Use(); return;
                case KeyCode.PageUp:
                    MoveVertical(-10, shift); e.Use(); return;
                case KeyCode.PageDown:
                    MoveVertical(10, shift); e.Use(); return;
                case KeyCode.Backspace:
                    if (HasSelection) DeleteSelection();
                    else if (_cursor > 0) { _cursor--; DeleteRange(_cursor, 1); }
                    e.Use(); return;
                case KeyCode.Delete:
                    if (HasSelection) DeleteSelection();
                    else DeleteRange(_cursor, 1);
                    e.Use(); return;
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    if (!ReadOnly) { ReplaceSelection("\n"); }
                    e.Use(); return;
                case KeyCode.Tab:
                    if (!ReadOnly) { ReplaceSelection("\t"); }
                    e.Use(); return;
                case KeyCode.Escape:
                    e.Use(); return;
            }

            // 可打印字符（含中文 —— IMGUI 的 IME 组合完成后走这里）
            if (!ReadOnly && e.character != '\0' && !char.IsControl(e.character))
            {
                ReplaceSelection(e.character.ToString());
                e.Use();
            }
        }

        private void MoveHorizontal(int dir, bool shift, bool byWord)
        {
            int next;
            if (byWord) next = dir < 0 ? FindWordStart(_cursor) : FindWordEnd(_cursor);
            else next = Mathf.Clamp(_cursor + dir, 0, _text.Length);

            if (!shift && HasSelection)
            {
                //  collapses selection to the edge
                next = dir < 0 ? SelStart : SelEnd;
                _selectAnchor = next;
            }
            else if (!shift) _selectAnchor = next;

            _cursor = next;
            _desiredColumn = -1;
        }

        /// <summary>
        /// 上下移动按<b>可视行</b>而非逻辑行 —— 自动换行后，
        /// 一个逻辑行可能占多行，按逻辑行走会"跳过"折出来的行，手感是错的。
        /// </summary>
        private void MoveVertical(int dir, bool shift)
        {
            if (_rows.Count == 0) return;

            int row = IndexToRow(_cursor);
            var vr = _rows[row];
            int column = _cursor - _lineStarts[vr.Line] - vr.ColStart;

            // 期望列：从长行移到短行再移回来时不丢列
            if (_desiredColumn >= 0) column = _desiredColumn;
            else _desiredColumn = column;

            int targetRow = Mathf.Clamp(row + dir, 0, _rows.Count - 1);
            var tvr = _rows[targetRow];
            int col = Mathf.Clamp(column, 0, tvr.ColEnd - tvr.ColStart);
            int next = _lineStarts[tvr.Line] + tvr.ColStart + col;

            if (!shift) _selectAnchor = next;
            _cursor = next;
        }

        /// <summary>Home / End：跳到当前<b>可视行</b>的行首 / 行尾（而非整个逻辑行）。</summary>
        private void MoveToLineEdge(int dir, bool shift)
        {
            if (_rows.Count == 0) return;

            int row = IndexToRow(_cursor);
            var vr = _rows[row];
            int lineStartIdx = _lineStarts[vr.Line];

            int next = dir < 0
                ? lineStartIdx + vr.ColStart
                : lineStartIdx + vr.ColEnd;

            if (!shift) _selectAnchor = next;
            _cursor = next;
            _desiredColumn = -1;
        }

        // ---------------- 选区 ----------------

        public bool HasSelection => _cursor != _selectAnchor;
        public int SelStart => Mathf.Min(_cursor, _selectAnchor);
        public int SelEnd => Mathf.Max(_cursor, _selectAnchor);

        private void SelectAll()
        {
            _selectAnchor = 0;
            _cursor = _text.Length;
        }

        private void SelectWordAt(int index)
        {
            _selectAnchor = FindWordStart(index);
            _cursor = FindWordEnd(index);
        }

        private int FindWordStart(int index)
        {
            index = Mathf.Clamp(index, 0, _text.Length);
            while (index > 0 && IsWordChar(_text[index - 1])) index--;
            return index;
        }

        private int FindWordEnd(int index)
        {
            index = Mathf.Clamp(index, 0, _text.Length);
            while (index < _text.Length && IsWordChar(_text[index])) index++;
            return index;
        }

        private static bool IsWordChar(char c)
        {
            return char.IsLetterOrDigit(c) || c == '_';
        }

        // ---------------- 编辑 ----------------

        private void ReplaceSelection(string insert)
        {
            if (HasSelection) DeleteSelection();
            InsertAt(_cursor, insert);
            _cursor += insert.Length;
            _selectAnchor = _cursor;
        }

        private void DeleteSelection()
        {
            if (!HasSelection) return;
            DeleteRange(SelStart, SelEnd - SelStart);
        }

        private void InsertAt(int index, string s)
        {
            if (ReadOnly) return;
            _text = _text.Insert(index, s);
            _layoutDirty = true;
            NotifyChanged();
        }

        private void DeleteRange(int index, int length)
        {
            if (ReadOnly) return;
            index = Mathf.Clamp(index, 0, _text.Length);
            length = Mathf.Clamp(length, 0, _text.Length - index);
            if (length <= 0) return;

            _text = _text.Remove(index, length);
            _layoutDirty = true;
            _selectAnchor = _cursor = Mathf.Clamp(index, 0, _text.Length);
            NotifyChanged();
        }

        private void ClampCursor()
        {
            _cursor = Mathf.Clamp(_cursor, 0, _text.Length);
            _selectAnchor = Mathf.Clamp(_selectAnchor, 0, _text.Length);
        }

        private void NotifyChanged() => OnTextChanged?.Invoke(_text);

        // ---------------- 剪贴板 ----------------

        private void DoCopy()
        {
            if (!HasSelection) return;
            EditorGUIUtility.systemCopyBuffer = _text.Substring(SelStart, SelEnd - SelStart);
        }

        private void DoPaste()
        {
            string paste = EditorGUIUtility.systemCopyBuffer;
            if (string.IsNullOrEmpty(paste)) return;
            ReplaceSelection(paste);
        }

        // ---------------- 绘制 ----------------

        private void DrawBackground(Rect rect)
        {
            EditorGUI.DrawRect(rect, ColBg);
        }

        private void DrawGutter(Rect rect)
        {
            Rect gutter = new Rect(rect.x, rect.y, GutterWidth, rect.height);
            EditorGUI.DrawRect(gutter, ColGutterBg);
            EditorGUI.DrawRect(new Rect(gutter.xMax, rect.y, 1, rect.height),
                new Color(0.17f, 0.17f, 0.17f));

            // 只绘制可见行；续行（软折行的后续行）不画行号，避免行号与逻辑行错位
            int first = Mathf.Max(0, Mathf.FloorToInt(_scroll.y / _lineHeight) - 1);
            int last = Mathf.Min(_rows.Count - 1,
                first + Mathf.CeilToInt(rect.height / _lineHeight) + 2);

            for (int i = first; i <= last; i++)
            {
                if (_rows[i].ColStart != 0) continue;
                float y = rect.y + PadTop + i * _lineHeight - _scroll.y;
                var r = new Rect(rect.x, y, GutterWidth - 6, _lineHeight);
                GUI.Label(r, (_rows[i].Line + 1).ToString(), _gutterStyle);
            }
        }

        private void DrawCurrentLine(Rect rect)
        {
            if (!_hasFocus) return;
            int row = IndexToRow(_cursor);
            float y = rect.y + PadTop + row * _lineHeight - _scroll.y;
            if (y < rect.y - _lineHeight || y > rect.yMax) return;

            EditorGUI.DrawRect(
                new Rect(rect.x + GutterWidth, y, rect.width - GutterWidth, _lineHeight),
                ColCurrentLine);
        }

        private void DrawSelection(Rect rect)
        {
            if (!HasSelection) return;

            if (_rows.Count == 0) return;

            int selStart = SelStart, selEnd = SelEnd;

            // 只遍历选区覆盖的可视行区间，避免全量扫描
            int startRow = IndexToRow(selStart);
            int endRow = IndexToRow(selEnd);

            for (int row = startRow; row <= endRow; row++)
            {
                var vr = _rows[row];
                int lineStartIdx = _lineStarts[vr.Line];

                int a = Mathf.Max(selStart, lineStartIdx + vr.ColStart);
                int b = Mathf.Min(selEnd, lineStartIdx + vr.ColEnd);
                if (b <= a) continue;

                float x0 = rect.x + GutterWidth + PadLeft +
                           (a - lineStartIdx - vr.ColStart) * _charWidth - _scroll.x;
                float x1 = rect.x + GutterWidth + PadLeft +
                           (b - lineStartIdx - vr.ColStart) * _charWidth - _scroll.x;
                float y = rect.y + PadTop + row * _lineHeight - _scroll.y;

                if (y + _lineHeight < rect.y || y > rect.yMax) continue;

                EditorGUI.DrawRect(new Rect(x0, y, Mathf.Max(x1 - x0, 1f), _lineHeight),
                    ColSelection);
            }
        }

        private void DrawTokens(Rect rect)
        {
            if (_rows.Count == 0) return;

            int first = Mathf.Max(0, Mathf.FloorToInt(_scroll.y / _lineHeight) - 1);
            int last = Mathf.Min(_rows.Count - 1,
                first + Mathf.CeilToInt(rect.height / _lineHeight) + 2);

            // 裁剪：只绘制 gutter 右侧区域，防止文字溢出到行号栏
            GUI.BeginGroup(new Rect(rect.x + GutterWidth, rect.y,
                rect.width - GutterWidth, rect.height));

            for (int row = first; row <= last; row++)
            {
                var vr = _rows[row];
                float y = PadTop + row * _lineHeight - _scroll.y;
                bool isSelectedLine = LineMatchesSelection(vr.Line);

                foreach (var token in _lineTokens[vr.Line])
                {
                    // token 与本可视行的列区间求交
                    int a = Mathf.Max(token.Column, vr.ColStart);
                    int b = Mathf.Min(token.Column + token.Length, vr.ColEnd);
                    if (b <= a) continue;

                    float x = PadLeft + (a - vr.ColStart) * _charWidth - _scroll.x;
                    if (x > rect.width + 200f || x + (b - a) * _charWidth < -200f)
                        continue;

                    string slice = _lines[vr.Line].Substring(a, b - a);
                    Color c = isSelectedLine ? ColHighlight : token.Color;
                    var r = new Rect(x, y, (b - a) * _charWidth + 2f, _lineHeight);

                    var old = _textStyle.normal.textColor;
                    _textStyle.normal.textColor = c;
                    GUI.Label(r, slice, _textStyle);
                    _textStyle.normal.textColor = old;
                }
            }

            GUI.EndGroup();
        }

        private void DrawCursor(Rect rect)
        {
            if (_rows.Count == 0) return;

            int row = IndexToRow(_cursor);
            var vr = _rows[row];
            int column = _cursor - _lineStarts[vr.Line] - vr.ColStart;

            float x = rect.x + GutterWidth + PadLeft + column * _charWidth - _scroll.x;
            float y = rect.y + PadTop + row * _lineHeight - _scroll.y;

            if (y < rect.y - _lineHeight || y > rect.yMax) return;
            if (x < rect.x + GutterWidth - 1f || x > rect.xMax + 1f) return;

            EditorGUI.DrawRect(new Rect(x, y, 2f, _lineHeight), ColCursor);
        }

        /// <summary>该行是否为画布选中节点对应的命令行。</summary>
        private bool LineMatchesSelection(int line)
        {
            if (string.IsNullOrEmpty(SelectedSignature)) return false;
            if (line < 0 || line >= _lines.Count) return false;
            return _lines[line].IndexOf(SelectedSignature, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // 滚动（供外部滚轮事件调用）
        public void Scroll(Vector2 delta)
        {
            _scroll += delta;
            _scroll.x = 0f;   // 自动换行模式不需要水平滚动
            _scroll.y = Mathf.Clamp(_scroll.y, 0f,
                Mathf.Max(0f, _rows.Count * _lineHeight - 40f));
        }

        public float ContentHeight => _rows.Count * _lineHeight + PadTop * 2;

        // ---------------- 词法分析 ----------------

        private struct Token
        {
            public string Text;
            public int Column;   // 行内起始列
            public int Length;
            public Color Color;
        }

        /// <summary>
        /// 行内词法着色。字符级扫描（不依赖 AST 解析）——
        /// 用户键入半成品（语法不完整）时也能正确着色，不会因解析失败而整行失效。
        /// </summary>
        private static List<Token> Tokenize(string line, int lineStartIndex)
        {
            var tokens = new List<Token>();
            if (string.IsNullOrEmpty(line)) return tokens;

            int i = 0;
            while (i < line.Length)
            {
                char c = line[i];

                // 空白（连续空白合成一个 token，减少绘制调用）
                if (c == ' ' || c == '\t')
                {
                    int start = i;
                    while (i < line.Length && (line[i] == ' ' || line[i] == '\t')) i++;
                    tokens.Add(new Token
                    {
                        Text = line.Substring(start, i - start),
                        Column = start, Length = i - start, Color = ColPlain
                    });
                    continue;
                }

                // 字符串
                if (c == '"')
                {
                    int start = i;
                    i++;
                    while (i < line.Length && line[i] != '"')
                    {
                        if (line[i] == '\\') i++;
                        i++;
                    }
                    if (i < line.Length) i++;  // 闭合引号
                    tokens.Add(new Token
                    {
                        Text = line.Substring(start, i - start),
                        Column = start, Length = i - start, Color = ColString
                    });
                    continue;
                }

                // 标识符 / 命令名
                if (char.IsLetter(c) || c == '_')
                {
                    int start = i;
                    while (i < line.Length &&
                           (char.IsLetterOrDigit(line[i]) || line[i] == '_')) i++;

                    // 后面紧跟 '(' → 命令名；否则是参数值
                    int j = i;
                    while (j < line.Length && line[j] == ' ') j++;
                    bool isCommand = j < line.Length && line[j] == '(';

                    tokens.Add(new Token
                    {
                        Text = line.Substring(start, i - start),
                        Column = start, Length = i - start,
                        Color = isCommand ? ColCommand : ColPlain
                    });
                    continue;
                }

                // 数字（含负号、小数）
                if (char.IsDigit(c) ||
                    (c == '-' && i + 1 < line.Length && char.IsDigit(line[i + 1])))
                {
                    int start = i;
                    if (c == '-') i++;
                    while (i < line.Length &&
                           (char.IsDigit(line[i]) || line[i] == '.')) i++;
                    tokens.Add(new Token
                    {
                        Text = line.Substring(start, i - start),
                        Column = start, Length = i - start, Color = ColNumber
                    });
                    continue;
                }

                // 操作符（& -> 等）
                if (c == '&' || c == '-' || c == '>' || c == '|')
                {
                    tokens.Add(new Token
                    {
                        Text = c.ToString(), Column = i, Length = 1, Color = ColOperator
                    });
                    i++;
                    continue;
                }

                // 括号 / 逗号 / 冒号 / @
                if (c == '(' || c == ')' || c == '[' || c == ']' ||
                    c == ',' || c == ':' || c == '@')
                {
                    tokens.Add(new Token
                    {
                        Text = c.ToString(), Column = i, Length = 1, Color = ColPunct
                    });
                    i++;
                    continue;
                }

                // 其他
                tokens.Add(new Token
                {
                    Text = c.ToString(), Column = i, Length = 1, Color = ColPlain
                });
                i++;
            }

            return tokens;
        }

        // ---------------- 可视行（自动换行） ----------------

        /// <summary>一条可视行：逻辑行 li 的 [ColStart, ColEnd) 列区间。</summary>
        private struct VisualRow
        {
            public int Line;
            public int ColStart;
            public int ColEnd;
        }

        /// <summary>
        /// 按可用宽度把逻辑行折叠成可视行。宽度变化或文本变化时重算。
        /// 等宽字体下按字符数折行即可精确，无需逐字测量。
        /// </summary>
        private void RebuildRows(float width)
        {
            _rowsDirty = false;
            _lastWrapWidth = width;
            _rows.Clear();
            _lineFirstRow.Clear();

            int maxCols = Mathf.Max(8, Mathf.FloorToInt(width / _charWidth));

            for (int li = 0; li < _lines.Count; li++)
            {
                _lineFirstRow.Add(_rows.Count);
                string line = _lines[li];

                if (line.Length == 0)
                {
                    _rows.Add(new VisualRow { Line = li, ColStart = 0, ColEnd = 0 });
                    continue;
                }

                int col = 0;
                while (col < line.Length)
                {
                    int take = FindWrapLength(line, col, maxCols);
                    _rows.Add(new VisualRow { Line = li, ColStart = col, ColEnd = col + take });
                    col += take;
                }
            }
        }

        /// <summary>
        /// 从 start 起决定本行容纳多少字符。优先在词边界（空白）断开，
        /// 单个超长"词"则退化为在标点处断开，都没有才硬切 —— 保证不会死循环。
        /// </summary>
        private static int FindWrapLength(string line, int start, int maxCols)
        {
            int remaining = line.Length - start;
            if (remaining <= maxCols) return remaining;

            int end = start + maxCols;

            // 1) 优先词边界（空白留在行尾，下一行从非空白开始）
            for (int i = end; i > start; i--)
            {
                if (line[i] == ' ' || line[i] == '\t')
                    return i + 1 - start;
            }

            // 2) 无空白的超长片段：在标点后断开（cmd(a,b,c) 里逗号是天然断点）
            for (int i = end; i > start; i--)
            {
                char c = line[i];
                if (c == ',' || c == '&' || c == '(' || c == ')' ||
                    c == '[' || c == ']' || c == '-')
                    return i + 1 - start;
            }

            // 3) 硬切（等宽下不会超宽，只是断点难看）
            return maxCols;
        }

        /// <summary>字符索引 → 所属可视行序号。</summary>
        private int IndexToRow(int index)
        {
            if (_rows.Count == 0) return 0;

            int line = IndexToLine(index);
            int col = index - _lineStarts[line];

            int first = _lineFirstRow[line];
            int last = (line + 1 < _lineFirstRow.Count ? _lineFirstRow[line + 1] : _rows.Count) - 1;
            if (last < first) return first;

            for (int r = first; r <= last; r++)
            {
                if (col <= _rows[r].ColEnd) return r;   // 软换行边界归上一行行尾
            }
            return last;
        }
    }
}
