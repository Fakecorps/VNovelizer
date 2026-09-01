using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace VNovelizer.Editor.RowPerformanceEditor
{
    /// <summary>
    /// 可拖拽的竖向分隔条（2026-08-31 新增）。
    ///
    /// <para>
    /// 挂在相邻两列之间拖动，改变 <c>target</c> 那一列的宽度。
    /// 视觉上是 2px 细线，但命中区域有 7px（细线左右各留出热区），
    /// 避免像 1px 边框那样"永远抓不住"。
    /// </para>
    ///
    /// <para>
    /// <b>invert 语义</b>：分隔条在 target <i>左侧</i>时（target 是右列），
    /// 往右拖应当让 target 变窄，故 <c>invert = true</c>；
    /// 分隔条在 target <i>右侧</i>时（target 是左列），往右拖 target 变宽，用默认 false。
    /// </para>
    ///
    /// <para>
    /// <b>为什么不用 VisualElement.CapturePointer</b>：该 API 并非所有 Unity 版本都提供
    /// （本项目目标版本上不可用，会报 CS0103）。这里改为把 down / move / up 三个回调
    /// 统一挂在面板根元素上并走 TrickleDown 派发 —— 既能在指针移出分隔条后继续收到事件，
    /// 又保证三个事件由同一元素派发、<c>e.position</c> 处于同一坐标系，位移差才正确。
    /// </para>
    ///
    /// <para><b>宽度持久化</b>：传入 <c>prefsKey</c> 后，宽度写入 EditorPrefs，
    /// 重开窗口保持用户布局。</para>
    /// </summary>
    public class VnColumnResizer : VisualElement
    {
        private readonly VisualElement _target;
        private readonly float _minWidth;
        private readonly float _maxWidth;
        private readonly bool _invert;
        private readonly string _prefsKey;
        private readonly VisualElement _line;

        private VisualElement _root;
        private bool _dragging;
        private float _startPointerX;
        private float _startWidth;

        /// <param name="target">宽度被改变的那一列。</param>
        /// <param name="minWidth">最小宽度。</param>
        /// <param name="maxWidth">最大宽度。</param>
        /// <param name="invert">分隔条是否位于 target 左侧。</param>
        /// <param name="prefsKey">持久化键；传 null 则不持久化。</param>
        /// <param name="defaultWidth">初始 / 无存档时的宽度。</param>
        public VnColumnResizer(VisualElement target, float minWidth, float maxWidth,
            bool invert, string prefsKey, float defaultWidth)
        {
            _target = target;
            _minWidth = minWidth;
            _maxWidth = maxWidth;
            _invert = invert;
            _prefsKey = prefsKey;

            AddToClassList("vn-col-resizer");

            // 2px 可见细线 + 左右热区（由 USS 的 padding 撑出命中宽度）
            _line = new VisualElement();
            _line.AddToClassList("vn-col-resizer-line");
            Add(_line);

            ApplyWidth(LoadWidth(defaultWidth));

            RegisterCallback<AttachToPanelEvent>(OnAttachToPanel);
            RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);
        }

        // ---------------- 事件挂载 ----------------

        private void OnAttachToPanel(AttachToPanelEvent e)
        {
            _root = panel?.visualTree;
            if (_root == null) return;

            _root.RegisterCallback<PointerDownEvent>(OnRootDown, TrickleDown.TrickleDown);
            _root.RegisterCallback<PointerMoveEvent>(OnRootMove, TrickleDown.TrickleDown);
            _root.RegisterCallback<PointerUpEvent>(OnRootUp, TrickleDown.TrickleDown);
        }

        private void OnDetachFromPanel(DetachFromPanelEvent e)
        {
            if (_root == null) return;

            _root.UnregisterCallback<PointerDownEvent>(OnRootDown, TrickleDown.TrickleDown);
            _root.UnregisterCallback<PointerMoveEvent>(OnRootMove, TrickleDown.TrickleDown);
            _root.UnregisterCallback<PointerUpEvent>(OnRootUp, TrickleDown.TrickleDown);
            _root = null;
        }

        /// <summary>本次事件是否落在本分隔条（或其内部细线）上。</summary>
        private bool IsHit(EventBase e)
        {
            var ve = e.target as VisualElement;
            while (ve != null)
            {
                if (ve == this) return true;
                ve = ve.parent;
            }
            return false;
        }

        // ---------------- 拖拽 ----------------

        private void OnRootDown(PointerDownEvent e)
        {
            // 点在别处：顺手清掉可能残留的拖拽状态（例如上一次在窗口外松开了鼠标，
            // PointerUpEvent 没送达 —— 下一次点击即可恢复）。
            if (!IsHit(e))
            {
                if (_dragging) EndDrag();
                return;
            }

            if (e.button != 0) return;

            _dragging = true;
            _startPointerX = e.position.x;
            _startWidth = _target.resolvedStyle.width;

            AddToClassList("vn-col-resizer--active");
            e.StopPropagation();   // 阻止画布等下方元素把这次按下当作自己的操作
        }

        private void OnRootMove(PointerMoveEvent e)
        {
            if (!_dragging) return;

            float delta = e.position.x - _startPointerX;
            if (_invert) delta = -delta;

            ApplyWidth(Mathf.Clamp(_startWidth + delta, _minWidth, _maxWidth));

            e.StopPropagation();
        }

        private void OnRootUp(PointerUpEvent e)
        {
            if (!_dragging) return;
            EndDrag();
            e.StopPropagation();
        }

        private void EndDrag()
        {
            _dragging = false;
            RemoveFromClassList("vn-col-resizer--active");
            SaveWidth(_target.resolvedStyle.width);
        }

        // ---------------- 宽度存取 ----------------

        private float LoadWidth(float fallback)
        {
            if (string.IsNullOrEmpty(_prefsKey)) return fallback;
            if (!EditorPrefs.HasKey(_prefsKey)) return fallback;

            float w = EditorPrefs.GetFloat(_prefsKey, fallback);
            // 存档可能来自旧版本（范围变了）——夹回当前有效区间
            return Mathf.Clamp(w, _minWidth, _maxWidth);
        }

        private void ApplyWidth(float w)
        {
            _target.style.width = w;
            // 手动设宽的侧栏必须禁止被挤压，否则窗口变窄时 flex 会覆盖用户拖出的宽度
            _target.style.flexShrink = 0;
            _target.style.flexGrow = 0;
        }

        private void SaveWidth(float w)
        {
            if (string.IsNullOrEmpty(_prefsKey)) return;
            EditorPrefs.SetFloat(_prefsKey, w);
        }
    }
}
