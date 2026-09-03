using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace VNovelizer.Editor.RowPerformanceEditor
{
    /// <summary>
    /// IMGUI 实现的命令链代码编辑器（2026-08-31 重写；2026-09-01 修复 BUG 集）。
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
    /// <b>等宽字体 + EAW 字符宽度（2026-09-01 修复光标偏移）</b>：
    /// Unity 等宽字体（Cascadia Mono 等）不含中文，渲染时 fallback 到系统中文字体，
    /// <b>中文字符实际像素宽度 = 英文字符的 2 倍</b>。原版用 <c>x = 字符列 × 字符宽</c>
    /// 算坐标，CJK 之后所有字符 X 全部少算一列宽 → 越往后越偏。修复后按
    /// East Asian Width 计算每个字符的显示列宽（W/F = 2，其他 = 1），
    /// 坐标换算为"字符前累计显示宽度 × 字符宽"。成熟参考：<c>wcwidth</c>（POSIX 标准）。
    /// </para>
    ///
    /// <para>
    /// <b>自管 Undo 栈（2026-09-01 修复 Ctrl+Z 无效）</b>：
    /// 原版把 Ctrl+Z/Y 吞掉交给外部 GraphUndoStack 处理，但后者是图快照、且仅在
    /// 200ms 防抖后图变更时才记 → 纯文本编辑无对应快照。修复：编辑器自管文本
    /// undo 栈（双栈 <c>List&lt;Snapshot&gt;</c>，100 条上限），与图 undo 完全解耦。
    /// 合并策略：连续输入 &lt; 500ms 合并为一条。成熟参考：<c>imstb_textedit.h</c>。
    /// </para>
    ///
    /// <para><b>自动换行</b>：与截图中上方预览块的视觉效果对齐 ——
    /// 长命令链不会溢出到不可见区域，而是按编辑器宽度自动折行，
    /// 每行一条命令、按嵌套深度缩进、关键字着色，阅读体验接近 IDE。
    /// 2026-09-01：折行切点也按显示宽度计算（而非字符数）。</para>
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
        /// 2026-09-03：编辑器请求外部恢复 UIToolkit 焦点。
        ///
        /// <para><b>触发场景</b>：用户按 Tab（含 Shift+Tab）后。即使 IMGUI 内部
        /// <see cref="HandleKeyDown"/> 已 <c>e.Use()</c> 消化了 Tab 并插入 4 空格，
        /// UIToolkit 的 FocusController 仍会把焦点切到下一个 focusable element
        /// （出口段 IMGUIContainer），导致本编辑器视觉失焦、光标消失、后续键盘
        /// 事件不再派发到本编辑器。</para>
        ///
        /// <para><b>根因</b>：焦点切换发生在 UIToolkit 层，IMGUI 的 <c>e.Use()</c>
        /// 不影响 UIToolkit 焦点。现有三套拦截方案全部失效：
        /// <list type="bullet">
        /// <item><c>tabIndex = -1</c>：不影响 Tab 焦点切换</item>
        /// <item>BubbleUp <c>StopPropagation</c> + <c>container.Focus()</c>：BubbleUp
        ///   不会回到原 container（事件 target 已是新 container，冒泡路径不经过原 container）</item>
        /// <item>TrickleDown <c>PreventDefault</c>：对 Tab 焦点切换无效（焦点切换
        ///   不是 KeyDownEvent 的"默认行为"，PreventDefault 管不到）</item>
        /// <item>同步 <c>OnFocusRestoreRequested</c> + <c>container.Focus()</c>：在
        ///   IMGUI onGUIHandler 期间调用，被 UIToolkit DefaultAction 阶段覆盖
        ///   （焦点切换在 KeyDownEvent 的 DefaultAction 阶段执行，container.Focus()
        ///   在 AtTarget 阶段调用后又被 DefaultAction 切走）</item>
        /// </list></para>
        ///
        /// <para><b>成熟方案</b>：IMGUI 处理 Tab 之后，设置 <see cref="PendingRefocus"/>
        /// 标志。外部在 IMGUIContainer 的 <c>BlurEvent</c>（焦点切走后立即触发）
        /// 中检查此标志，若为 true 则 <c>schedule container.Focus()</c> 异步恢复
        /// 焦点。<c>BlurEvent</c> 在 DefaultAction 焦点切换完成之后才派发，所以
        /// 此时 <c>container.Focus()</c> 不会被覆盖。</para>
        /// </summary>
        public event Action OnFocusRestoreRequested;

        /// <summary>
        /// 2026-09-03：Tab 按下后请求外部恢复焦点的标志。
        ///
        /// <para>IMGUI <see cref="HandleKeyDown"/> 处理 Tab 之后置为 true。外部
        /// （<see cref="TextChainEditor"/>）在 IMGUIContainer 的 <c>BlurEvent</c>
        /// 回调中检查此标志：若为 true，说明焦点切走是 Tab 触发的，需要恢复；
        /// 清除标志后 <c>schedule container.Focus()</c> 把焦点取回。</para>
        ///
        /// <para>用标志而非直接在 <c>OnFocusRestoreRequested</c> 事件中恢复焦点，
        /// 是因为同步调用 <c>container.Focus()</c> 在 IMGUI onGUIHandler 期间会被
        /// UIToolkit DefaultAction 阶段覆盖（用户 2026-09-03 反馈验证）。必须等
        /// <c>BlurEvent</c>（焦点切走之后）才能可靠恢复。</para>
        /// </summary>
        public bool PendingRefocus;

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

        // 2026-09-01 v3：移除自动换行（RebuildRows / FindWrapLength / VisualRow）。
        // 改为按 _text 中的 '\n' 显式切行 —— 显示由 ChainSerializer.SerializeFormatted
        // 提供的格式化文本（含换行 + 4 空格缩进），编辑器不再做宽度折行。
        // 长行（如命令参数含长字符串）会溢出，但用户可手动加 '\n' 控制。

        private GUIStyle _textStyle;
        private GUIStyle _gutterStyle;
        private bool _stylesReady;

        private float _lineHeight = 16f;

        private const float GutterWidth = 38f;
        private const float PadLeft = 6f;
        private const float PadTop = 4f;

        // ---------------- 字符 advance 宽度缓存（2026-09-01 v2 修复） ----------------
        // v1 用 EAW "CJK=2 列" 估算 * _charWidth 算 X，仍偏 —— 因为 Cascadia Mono 12pt
        // 字符 advance ≠ YaHei 12pt 字符 advance 的整数倍（实测比例 ≈ 1 : 1.22），按 EAW
        // 假定的整数倍会系统性偏。同时 token 实测 advance 累加 ≠ n × _charWidth，导致
        // token 之间出现空档。修复：每行用 GUIStyle.CalcSize 实测每个字符 advance width
        // 缓存到 _lineAdvanceX[]，所有 X 计算（光标 / 选区 / token 排版 / 换行 / 鼠标
        // 命中）都查这张表，与 GUI.Label 实际渲染完全一致。GC 控制：缓存按行复用，
        // 切行 / 改字时才重测（RebuildLayout 设 _lineAdvanceValid=false）。

        private float[] _lineAdvanceX;   // _lineAdvanceX[i] = line 第 i 个字符的 advance 起点 X（像素）
        private string _lineAdvanceSrc;
        private int _lineAdvanceN;        // _lineAdvanceX 有效长度 = line.Length + 1
        private bool _lineAdvanceValid;
        private readonly GUIContent _advanceProbe = new GUIContent();

        /// <summary>
        /// 测 line 每个字符的 advance width 缓存到 _lineAdvanceX。
        /// 同 line 重复调用直接返回；切行 / 改字后 RebuildLayout 会置 _lineAdvanceValid=false 强制重测。
        /// </summary>
        private void EnsureLineAdvance(string line)
        {
            if (_lineAdvanceValid && _lineAdvanceSrc == line && _lineAdvanceX != null) return;
            int n = line.Length;
            if (_lineAdvanceX == null || _lineAdvanceX.Length < n + 1)
                _lineAdvanceX = new float[Mathf.Max(64, n + 1)];
            _lineAdvanceX[0] = 0f;
            for (int i = 0; i < n; i++)
            {
                _advanceProbe.text = line.Substring(i, 1);
                float cw = _textStyle.CalcSize(_advanceProbe).x;
                _lineAdvanceX[i + 1] = _lineAdvanceX[i] + cw;
            }
            _lineAdvanceSrc = line;
            _lineAdvanceN = n + 1;
            _lineAdvanceValid = true;
        }

        // ---------------- 撤销 / 重做（2026-09-01，参考 imstb_textedit.h） ----------------
        // 根因：原代码把 Ctrl+Z/Y 吞掉交给外部 GraphUndoStack 处理，但后者是图快照、
        // 且仅在 200ms 防抖后图变更时才记，**纯文本编辑根本无对应快照** → 用户输入
        // 字符后 Ctrl+Z 无效。修复：编辑器自管文本 undo 栈，与图 undo 完全解耦——
        // 文本快照即时入栈（不等防抖），撤销时文本先回退，200ms 后图自然重建。
        // 合并策略：与上次同向输入 < 500ms 合并为一条（避免每个字符一条）。

        private struct Snapshot
        {
            public string Text;
            public int Cursor;
            public int SelectAnchor;
        }

        private readonly List<Snapshot> _undo = new List<Snapshot>();
        private readonly List<Snapshot> _redo = new List<Snapshot>();
        private long _lastEditTicks;     // 上次编辑时间（用于合并窗口判定）
        private const int MaxHistory = 100;
        private const long MergeWindowTicks = 500 * TimeSpan.TicksPerMillisecond;

        /// <summary>编辑前入栈。连续输入（< 500ms）自动合并为一条。</summary>
        private void RecordSnapshot()
        {
            long now = DateTime.UtcNow.Ticks;
            if (_undo.Count > 0 && now - _lastEditTicks < MergeWindowTicks)
            {
                Snapshot top = _undo[_undo.Count - 1];
                // 合并条件：上次的 text 是当前 text 的前缀（纯连续插入），
                // 且光标位置正好相应后移 → 用户在继续输入，未中断。
                if (top.Text.Length <= _text.Length
                    && string.CompareOrdinal(_text, 0, top.Text, 0, top.Text.Length) == 0
                    && top.Cursor + (_text.Length - top.Text.Length) == _cursor)
                {
                    _lastEditTicks = now;
                    _redo.Clear();
                    return;
                }
            }

            _undo.Add(new Snapshot
            {
                Text = _text,
                Cursor = _cursor,
                SelectAnchor = _selectAnchor,
            });
            while (_undo.Count > MaxHistory) _undo.RemoveAt(0);
            _lastEditTicks = now;
            _redo.Clear();
        }

        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;

        /// <summary>外部可调用（如 GraphUndoStack 的 Undo 联动），未禁用时也安全。</summary>
        public void Undo()
        {
            if (_undo.Count == 0) return;
            Snapshot snap = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);

            _redo.Add(new Snapshot
            {
                Text = _text,
                Cursor = _cursor,
                SelectAnchor = _selectAnchor,
            });
            while (_redo.Count > MaxHistory) _redo.RemoveAt(0);

            _text = snap.Text;
            _cursor = snap.Cursor;
            _selectAnchor = snap.SelectAnchor;
            _layoutDirty = true;
            _lastEditTicks = 0;   // undo 后不立刻合并
            NotifyChanged();
        }

        public void Redo()
        {
            if (_redo.Count == 0) return;
            Snapshot snap = _redo[_redo.Count - 1];
            _redo.RemoveAt(_redo.Count - 1);

            _undo.Add(new Snapshot
            {
                Text = _text,
                Cursor = _cursor,
                SelectAnchor = _selectAnchor,
            });
            while (_undo.Count > MaxHistory) _undo.RemoveAt(0);

            _text = snap.Text;
            _cursor = snap.Cursor;
            _selectAnchor = snap.SelectAnchor;
            _layoutDirty = true;
            _lastEditTicks = 0;
            NotifyChanged();
        }

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

        // 2026-09-03：合二为一后 @Confirm: 标记与出口段的视觉区分
        // @Confirm: 标记行整行用醒目橙色（复用 ColHighlight）
        // 出口段所有行（含 @Confirm: 行及之后）加一层淡紫底色做视觉分组
        private static readonly Color ColConfirmSectionBg = new Color(0.5f, 0.3f, 0.8f, 0.06f);

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
                ResetHistory();  // 2026-09-01：外部赋值（如加载 CSV）不视为可撤销编辑
            }
        }

        /// <summary>外部赋值时不触发 OnTextChanged（用于规范化回填）。
        /// 2026-09-01：<b>不清空撤销栈</b>——图重建的回填（同一逻辑单元的语义化修正）不应
        /// 让用户的撤销失效。切行 / 加载 CSV 等真正"换上下文"的场景由 TextChainEditor
        /// 显式调用 <see cref="ResetHistory()"/>。</summary>
        public void SetTextWithoutNotify(string value)
        {
            string v = value ?? "";
            if (_text == v) return;
            _text = v;
            _layoutDirty = true;
            ClampCursor();
        }

        /// <summary>外部（如 TextChainEditor 切行 / 加载 CSV）显式清空撤销栈。</summary>
        public void ResetHistory()
        {
            _undo.Clear();
            _redo.Clear();
            _lastEditTicks = 0;
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

            // 2026-09-03：启用 IME，让输入法 hotkey（如 Ctrl+Space 切换中英文）工作。
            //
            // 根因：VnCodeEditorIMGUI 是自绘 IMGUI 控件，Unity 不会自动识别为文本输入
            // 控件（不像 EditorGUILayout.TextField 那样会被 Unity 标记为 TextField）。
            // IME 默认是 Auto 模式（"选中文本字段时启用 IME，其他情况禁用"），所以本
            // 编辑器获得焦点时 IME 仍是禁用状态 —— IMM/TSF 层不处理 Ctrl+Space 等
            // 输入法 hotkey，导致用户无法切换输入法。
            //
            // 修复：编辑器获得焦点时强制启用 IME（IMECompositionMode.On），失去焦点
            // 时恢复 Auto（避免影响其他窗口的 IME 行为）。启用 IME 后，IMM/TSF 层会
            // 正确处理 Ctrl+Space，触发输入法切换。
            //
            // 参考：Unity 官方文档 Input.imeCompositionMode —— "默认情况下，Unity 会在
            // 文本字段中启用 IME 组合，而在其他情况下禁用它。但是，当您想要实现自己的
            // 输入 GUI 时，可以强制启用 IME。"
            if (_hasFocus)
            {
                if (Input.imeCompositionMode != IMECompositionMode.On)
                    Input.imeCompositionMode = IMECompositionMode.On;
            }
            else if (wasFocus && Input.imeCompositionMode != IMECompositionMode.Auto)
            {
                Input.imeCompositionMode = IMECompositionMode.Auto;
            }

            if (_hasFocus && e.type == EventType.KeyDown)
                HandleKeyDown(e);

            // 键盘编辑可能刚改过文本 → 绘制前必须让"逻辑行"与"可视行"再次对齐。
            // 否则会出现 _rows 还是旧的、_lineStarts 已经是新的，导致行索引越界。
            EnsureLayout(rect.width - GutterWidth - PadLeft * 2);

            // ---- 绘制 ----
            DrawBackground(rect);
            DrawConfirmSectionBackground(rect);
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
            // 2026-09-01 v3：强制 padding = 0，否则 GUIStyle.CalcSize 返回的字符宽
            // 含 padding.left + padding.right，每字符累加多算 2×padding —— 光标 X
            // = 真实 advance + N×2×padding，越往后越偏右。token 间空档也由此引起。
            _textStyle.padding = new RectOffset(0, 0, 0, 0);
            _textStyle.margin = new RectOffset(0, 0, 0, 0);
            _textStyle.overflow = new RectOffset(0, 0, 0, 0);
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

            // 测量行高（仅 _lineHeight 用；字符宽度改为按行实测缓存 _lineAdvanceX[]）
            GUIContent probe = new GUIContent("M");
            _lineHeight = Mathf.Max(FontSize + 4f, _textStyle.CalcHeight(probe, 999f));
        }

        // ---------------- 布局 ----------------

        /// <summary>
        /// 保证逻辑行缓存是最新的。OnGUI 入口（常规）与事件处理之后（键盘刚改过文本时）调用。
        /// 2026-09-01 v3：移除自动换行，不再 RebuildRows；contentWidth 保留参数但不再使用。
        /// </summary>
        private void EnsureLayout(float contentWidth)
        {
            if (_layoutDirty) RebuildLayout();
            // 2026-09-01 v3：移除自动折行（RebuildRows / FindWrapLength / VisualRow 已删）
            // 编辑器按 _text 中的 '\n' 显式切行，行结构由 ChainSerializer.SerializeFormatted 提供
        }

        private void RebuildLayout()
        {
            _layoutDirty = false;
            _lineAdvanceValid = false;  // 2026-09-01 v2：每行实测缓存失效，下次绘制重测
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
            if (_lines.Count == 0) return 0;

            float localY = mouse.y - rect.y - PadTop + _scroll.y;
            int lineIdx = Mathf.FloorToInt(localY / _lineHeight);
            if (lineIdx < 0) lineIdx = 0;
            if (lineIdx >= _lines.Count) lineIdx = _lines.Count - 1;

            string line = _lines[lineIdx];
            int lineLen = line.Length;

            EnsureLineAdvance(line);

            float localX = mouse.x - rect.x - GutterWidth - PadLeft + _scroll.x;
            // 2026-09-01 v2：用 GUIStyle.CalcSize 实测的字符 advance 找最近列
            // 用 midX（字符像素中点）做距离判定，比右边界更符合 IDE 直觉
            float bestDist = float.MaxValue;
            int bestCol = 0;
            for (int i = 0; i <= lineLen; i++)
            {
                float charLeft = _lineAdvanceX[i];
                float charRight = (i < lineLen) ? _lineAdvanceX[i + 1] : charLeft + 1f;
                float midX = (charLeft + charRight) * 0.5f;
                float dist = Mathf.Abs(midX - localX);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestCol = i;
                }
            }

            return _lineStarts[lineIdx] + bestCol;
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
                    // 2026-09-01：撤销/重做由编辑器自管（与图 Undo 解耦）
                    case KeyCode.Z:
                        if (shift) Redo();
                        else Undo();
                        e.Use(); return;
                    case KeyCode.Y:
                        Redo();
                        e.Use(); return;
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
                    // 2026-09-01：IDE 习惯 —— Tab 插 4 空格而非 \t（避免渲染宽度差异），
                    // Shift+Tab 删前导 4 空格，选区多行按行首缩进
                    if (!ReadOnly)
                    {
                        if (shift)
                        {
                            int line = IndexToLine(_cursor);
                            int ls = _lineStarts[line];
                            int remove = 0;
                            while (remove < 4 && ls + remove < _text.Length && _text[ls + remove] == ' ')
                                remove++;
                            if (remove > 0) DeleteRange(ls, remove);
                        }
                        else if (HasSelection)
                        {
                            // 多行缩进：从最后一行往前插（避免索引偏移）
                            int firstLine = IndexToLine(SelStart);
                            int lastLine = IndexToLine(SelEnd);
                            RecordSnapshot();
                            for (int li = lastLine; li >= firstLine; li--)
                                _text = _text.Insert(_lineStarts[li], "    ");
                            _layoutDirty = true;
                            int delta = 4 * (lastLine - firstLine + 1);
                            _selectAnchor = _cursor = Mathf.Clamp(SelEnd + delta, 0, _text.Length);
                            NotifyChanged();
                        }
                        else
                        {
                            ReplaceSelection("    ");
                        }

                        // 2026-09-03：通知外部恢复 UIToolkit 焦点（双管齐下）。
                        //
                        // 第一道防线：同步调用 OnFocusRestoreRequested → container.Focus()
                        //   在 IMGUI onGUIHandler 期间尝试恢复焦点。若 UIToolkit 允许
                        //   在事件处理期间修改焦点状态，立即生效。但实测可能被 UIToolkit
                        //   DefaultAction 阶段覆盖（焦点切换在 KeyDownEvent 的 DefaultAction
                        //   阶段执行，container.Focus() 在 AtTarget 阶段调用后又被切走）。
                        //
                        // 第二道防线：设置 PendingRefocus = true，外部在 IMGUIContainer 的
                        //   BlurEvent（焦点切走后立即触发，在 DefaultAction 之后）中检查此标志，
                        //   schedule container.Focus() 异步恢复。BlurEvent 在焦点切换完成后
                        //   才派发，所以此时 container.Focus() 不会被 DefaultAction 覆盖。
                        //
                        // 详细原理见 OnFocusRestoreRequested 和 PendingRefocus 的文档。
                        PendingRefocus = true;
                        OnFocusRestoreRequested?.Invoke();
                    }
                    e.Use(); return;
                case KeyCode.Escape:
                    e.Use(); return;
            }

            // 可打印字符（含中文 —— IMGUI 的 IME 组合完成后走这里）
            // 2026-09-02：Ctrl/Command 修饰键按下的字符不处理 —— 让系统/Unity 处理
            // 快捷键（如 Ctrl+Space 切换输入法、Ctrl+C/V/X 已在上面处理）。
            // 根因：原代码不检测 Ctrl，Ctrl+Space 时 e.character == ' '，被当成空格
            // 插入到文本，输入法切换被吞。
            if (!ReadOnly && !e.control && !e.command
                && e.character != '\0' && !char.IsControl(e.character))
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
        /// 上下移动按逻辑行（2026-09-01 v3：移除自动换行后逻辑行=可视行）。
        /// </summary>
        private void MoveVertical(int dir, bool shift)
        {
            if (_lines.Count == 0) return;

            int line = IndexToLine(_cursor);
            int column = _cursor - _lineStarts[line];

            // 期望列：从长行移到短行再移回来时不丢列
            if (_desiredColumn >= 0) column = _desiredColumn;
            else _desiredColumn = column;

            int targetLine = Mathf.Clamp(line + dir, 0, _lines.Count - 1);
            int col = Mathf.Clamp(column, 0, _lines[targetLine].Length);
            int next = _lineStarts[targetLine] + col;

            if (!shift) _selectAnchor = next;
            _cursor = next;
        }

        /// <summary>Home / End：跳到当前逻辑行的行首 / 行尾。</summary>
        private void MoveToLineEdge(int dir, bool shift)
        {
            if (_lines.Count == 0) return;

            int line = IndexToLine(_cursor);
            int lineStartIdx = _lineStarts[line];

            int next = dir < 0
                ? lineStartIdx
                : lineStartIdx + _lines[line].Length;

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
            RecordSnapshot();  // 2026-09-01：编辑前入栈（与下次输入合并）
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

            RecordSnapshot();  // 2026-09-01：编辑前入栈
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

        /// <summary>
        /// 2026-09-03：绘制出口段底色（合二为一后 @Confirm: 标记行及之后所有行）。
        ///
        /// <para><b>规则</b>：找到第一个整行 trim 后等于 <c>@Confirm:</c>（大小写
        /// 不敏感）的行，从该行起到底部加淡紫底色，做视觉分组。@Confirm: 标记
        /// 必须独占一行（与用户确认的分隔规则一致）。</para>
        /// </summary>
        private void DrawConfirmSectionBackground(Rect rect)
        {
            if (_lines.Count == 0) return;

            int confirmLine = FindConfirmLine();
            if (confirmLine < 0) return;

            float y0 = rect.y + PadTop + confirmLine * _lineHeight - _scroll.y;
            float y1 = rect.y + PadTop + _lines.Count * _lineHeight - _scroll.y;

            if (y1 < rect.y || y0 > rect.yMax) return;

            float drawY0 = Mathf.Max(y0, rect.y);
            float drawY1 = Mathf.Min(y1, rect.yMax);

            EditorGUI.DrawRect(
                new Rect(rect.x + GutterWidth, drawY0, rect.width - GutterWidth, drawY1 - drawY0),
                ColConfirmSectionBg);
        }

        /// <summary>找到 @Confirm: 标记行索引（整行 trim 后等于 @Confirm:，大小写不敏感）。无则返回 -1。</summary>
        private int FindConfirmLine()
        {
            for (int i = 0; i < _lines.Count; i++)
            {
                if (_lines[i].Trim().Equals("@Confirm:",
                        StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }

        private void DrawGutter(Rect rect)
        {
            Rect gutter = new Rect(rect.x, rect.y, GutterWidth, rect.height);
            EditorGUI.DrawRect(gutter, ColGutterBg);
            EditorGUI.DrawRect(new Rect(gutter.xMax, rect.y, 1, rect.height),
                new Color(0.17f, 0.17f, 0.17f));

            // 2026-09-01 v3：移除自动换行后逻辑行=可视行，直接遍历 _lines
            int first = Mathf.Max(0, Mathf.FloorToInt(_scroll.y / _lineHeight) - 1);
            int last = Mathf.Min(_lines.Count - 1,
                first + Mathf.CeilToInt(rect.height / _lineHeight) + 2);

            for (int i = first; i <= last; i++)
            {
                float y = rect.y + PadTop + i * _lineHeight - _scroll.y;
                var r = new Rect(rect.x, y, GutterWidth - 6, _lineHeight);
                GUI.Label(r, (i + 1).ToString(), _gutterStyle);
            }
        }

        private void DrawCurrentLine(Rect rect)
        {
            if (!_hasFocus) return;
            int line = IndexToLine(_cursor);
            float y = rect.y + PadTop + line * _lineHeight - _scroll.y;
            if (y < rect.y - _lineHeight || y > rect.yMax) return;

            EditorGUI.DrawRect(
                new Rect(rect.x + GutterWidth, y, rect.width - GutterWidth, _lineHeight),
                ColCurrentLine);
        }

        private void DrawSelection(Rect rect)
        {
            if (!HasSelection) return;

            if (_lines.Count == 0) return;

            int selStart = SelStart, selEnd = SelEnd;

            // 只遍历选区覆盖的逻辑行区间，避免全量扫描
            int startLine = IndexToLine(selStart);
            int endLine = IndexToLine(selEnd);

            for (int line = startLine; line <= endLine; line++)
            {
                string lineStr = _lines[line];
                int lineStartIdx = _lineStarts[line];

                int a = Mathf.Max(selStart, lineStartIdx);
                int b = Mathf.Min(selEnd, lineStartIdx + lineStr.Length);
                if (b <= a) continue;

                EnsureLineAdvance(lineStr);

                // 2026-09-01 v2：按 GUIStyle.CalcSize 实测的字符 advance 算选区起止 X
                int aIdx = Mathf.Clamp(a - lineStartIdx, 0, lineStr.Length);
                int bIdx = Mathf.Clamp(b - lineStartIdx, 0, lineStr.Length);
                float x0 = rect.x + GutterWidth + PadLeft + _lineAdvanceX[aIdx] - _scroll.x;
                float x1 = rect.x + GutterWidth + PadLeft + _lineAdvanceX[bIdx] - _scroll.x;
                float y = rect.y + PadTop + line * _lineHeight - _scroll.y;

                if (y + _lineHeight < rect.y || y > rect.yMax) continue;

                EditorGUI.DrawRect(new Rect(x0, y, Mathf.Max(x1 - x0, 1f), _lineHeight),
                    ColSelection);
            }
        }

        private void DrawTokens(Rect rect)
        {
            if (_lines.Count == 0) return;

            int first = Mathf.Max(0, Mathf.FloorToInt(_scroll.y / _lineHeight) - 1);
            int last = Mathf.Min(_lines.Count - 1,
                first + Mathf.CeilToInt(rect.height / _lineHeight) + 2);

            // 裁剪：只绘制 gutter 右侧区域，防止文字溢出到行号栏
            GUI.BeginGroup(new Rect(rect.x + GutterWidth, rect.y,
                rect.width - GutterWidth, rect.height));

            for (int line = first; line <= last; line++)
            {
                string lineStr = _lines[line];
                float y = PadTop + line * _lineHeight - _scroll.y;
                bool isSelectedLine = LineMatchesSelection(line);

                EnsureLineAdvance(lineStr);

                float cursorX = PadLeft - _scroll.x;
                foreach (var token in _lineTokens[line])
                {
                    int a = token.Column;
                    int b = token.Column + token.Length;
                    if (b <= a) continue;

                    string slice = lineStr.Substring(a, b - a);
                    // 2026-09-01 v2：用 GUIStyle.CalcSize 实测 token 宽度（与 GUI.Label
                    // 实际渲染完全一致）—— 解决 token 间空档问题
                    float sliceW = _textStyle.CalcSize(new GUIContent(slice)).x;

                    // 裁剪
                    if (cursorX + sliceW > -200f && cursorX < rect.width + 200f)
                    {
                        Color c = isSelectedLine ? ColHighlight : token.Color;
                        var r = new Rect(cursorX, y, sliceW, _lineHeight);

                        var old = _textStyle.normal.textColor;
                        _textStyle.normal.textColor = c;
                        GUI.Label(r, slice, _textStyle);
                        _textStyle.normal.textColor = old;
                    }
                    cursorX += sliceW;
                }
            }

            GUI.EndGroup();
        }

        private void DrawCursor(Rect rect)
        {
            if (_lines.Count == 0) return;

            int line = IndexToLine(_cursor);
            string lineStr = _lines[line];

            EnsureLineAdvance(lineStr);

            // 2026-09-01 v2：按 GUIStyle.CalcSize 实测的字符 advance 算光标 X
            int col = Mathf.Clamp(_cursor - _lineStarts[line], 0, lineStr.Length);
            float cursorAdvance = _lineAdvanceX[col];

            float x = rect.x + GutterWidth + PadLeft + cursorAdvance - _scroll.x;
            float y = rect.y + PadTop + line * _lineHeight - _scroll.y;

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
            _scroll.x = 0f;   // 2026-09-01 v3：移除自动换行后仍不水平滚动（行结构由 SerializeFormatted 提供）
            _scroll.y = Mathf.Clamp(_scroll.y, 0f,
                Mathf.Max(0f, _lines.Count * _lineHeight - 40f));
        }

        public float ContentHeight => _lines.Count * _lineHeight + PadTop * 2;

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

            // 2026-09-03：@Confirm: 标记行整行用橙色着色（合二为一后进入段/出口段分隔标记）
            // 规则：整行 trim 后等于 @Confirm:（大小写不敏感，与 ScriptParser 一致）
            if (line.Trim().Equals("@Confirm:", StringComparison.OrdinalIgnoreCase))
            {
                tokens.Add(new Token
                {
                    Text = line,
                    Column = 0, Length = line.Length, Color = ColHighlight
                });
                return tokens;
            }

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

        // 2026-09-01 v3：VisualRow / RebuildRows / FindWrapLength / IndexToRow 全部移除
        // 编辑器按 _text 中的 '\n' 显式切行（与 _lines 一一对应），不再做宽度折行。
        // 行结构由 ChainSerializer.SerializeFormatted 提供（含换行 + 4 空格缩进）。
    }
}
