using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using VNovelizer.Core.Diagnostics;

/// <summary>
/// UI 层级（v2）——排序带即枚举值：sortingOrder = (int)Layer + Order。
/// 仅四档：场景级全屏 / 常规覆盖层 / 弹窗 / 加载。
/// （旧六层 Bottom/Left/Middle/Right/Top/System 已废弃：Left/Right/Bottom 从未被使用，
///   其余三档语义归并为 Scene/Overlay/Popup/Loading。）
/// </summary>
public enum EUILayer
{
    Scene = 10,     // 全屏场景级面板（Gameplay / MainMenu / Gallery）
    Overlay = 20,   // 常规覆盖层（Pause / SaveLoad / Settings / History / Choice）
    Popup = 30,     // 模态弹窗（Confirm）——必须压过一切 Overlay
    Loading = 40,   // 全局加载条——压过一切
}

/// <summary>
/// 面板规格：注册表条目。面板的键、层级、排序、生命周期策略集中声明，
/// 调用方只需要类型（UIManager.Show&lt;T&gt;()），不再传路径/层级字符串。
/// </summary>
public sealed class PanelSpec
{
    /// <summary>面板名（须与 prefab 名及面板类名一致，作为注册键）</summary>
    public string Name;

    /// <summary>
    /// 自定义路径解析器（目录，不含面板名）：兼容用户经 RegisterPanel 注册的
    /// 自定义面板。内置面板不再使用（PrefabKey 即完整 fallback 地址）。
    /// </summary>
    public Func<string> PathResolver;

    /// <summary>
    /// 模板键（VNUIPrefabKeys 常量）：Show 时先查 VNProjectConfig"八、UI 模板覆写"，
    /// 用户指派了自定义模板则直接实例化引用；否则按本键（= 完整 fallback 地址）
    /// 经资源服务链加载包内默认模板。
    /// </summary>
    public string PrefabKey;

    /// <summary>层级（决定 sortingOrder 的基数带）</summary>
    public EUILayer Layer = EUILayer.Scene;

    /// <summary>层内排序偏移（sortingOrder = Layer + Order）</summary>
    public int Order = 0;

    /// <summary>常驻面板：跨场景存活（DontDestroyOnLoad），Hide 只隐藏不销毁</summary>
    public bool Persistent = false;

    /// <summary>
    /// 加载进度权重（&gt; 0 时 Show&lt;T&gt; 会向 LoadingProgressManager 注册一条
    /// 对应权重任务，加载完成后 Complete。VNManager 通过此机制跟踪"UI 加载"
    /// 在 LoadingProgressPanel 总进度中的占比；默认 0 = 不注册（大多数面板不进入加载流程）。
    /// </summary>
    public float LoadingTaskWeight = 0f;
}

/// <summary>
/// UIManager v2 —— 一面板一 Canvas 架构。
///
/// 设计契约：
/// - 无中心画布：每个面板 prefab 根节点自带 Canvas(Overlay)+CanvasScaler+GraphicRaycaster，
///   实例化为场景根对象；缺失组件时自动补挂（过渡期容错 + 警告）；
/// - 排序：sortingOrder = (int)Layer + Order，由注册表集中分配，取代层级树；
/// - 注册表：所有面板（含引擎内置 11 个）统一在 PanelSpec 声明元数据，
///   用户自定义面板经 RegisterPanel 接入；
/// - 生命周期：场景切换时 HideAll() 显式清理（Persistent 除外）；
/// - API：泛型驱动（Show&lt;T&gt;/Hide&lt;T&gt;/Get&lt;T&gt;），调用方零字符串、零路径、零层级知识。
/// </summary>
public class UIManager : BaseManager<UIManager>
{
    /// <summary>已实例化面板（键 = PanelSpec.Name）</summary>
    private readonly Dictionary<string, BasePanel> _panels = new Dictionary<string, BasePanel>();

    /// <summary>面板根对象（键 = PanelSpec.Name）——销毁/常驻都以根为准（脚本可能不在根节点的过渡期容错）</summary>
    private readonly Dictionary<string, GameObject> _panelRoots = new Dictionary<string, GameObject>();

    /// <summary>面板注册表（键 = PanelSpec.Name）</summary>
    private readonly Dictionary<string, PanelSpec> _specs = new Dictionary<string, PanelSpec>();

    /// <summary>
    /// 正在异步加载中的面板 → 等待回调队列（键 = PanelSpec.Name）。
    /// 面板 prefab 经资源服务链异步加载，同一面板在加载完成前被再次 Show 时
    /// 若不去重就会实例化出多份（表现为对话框重影、输入被上层面板吞掉）。
    /// </summary>
    private readonly Dictionary<string, List<Action<BasePanel>>> _pendingShows =
        new Dictionary<string, List<Action<BasePanel>>>();

    private GameObject _eventSystemGameObject;
    private static bool _isListeningSceneLoad;
    private RectTransform _effectLayer;

    /// <summary>是否已完成 Init（供面板自检初始化时序）</summary>
    public bool IsInitialized { get; private set; } = false;

    // ------------------------------------------------------------------
    // 初始化
    // ------------------------------------------------------------------

    public void Init()
    {
        // 内置注册表只建一次：Init 被 StartGame/ContinueGame/各面板 Awake 反复调用，
        // 若每次都重注册，用户经 Register 覆盖内置面板的自定义规格会被静默覆盖回默认值。
        if (_specs.Count == 0) RegisterBuiltinPanels();

        EnsureEventSystem();

        if (!_isListeningSceneLoad)
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            _isListeningSceneLoad = true;
        }

        IsInitialized = true;
        VNDebug.LogVerbose("[UIManager] v2 初始化完成（一面板一 Canvas，注册面板数: " + _specs.Count + "）");
    }

    /// <summary>引擎内置面板注册。用户自定义面板经 RegisterPanel 追加。</summary>
    private void RegisterBuiltinPanels()
    {
        // --- Scene 层（全屏场景级） ---
        Register(new PanelSpec
        {
            Name = "VNGameplayPanel",
            Layer = EUILayer.Scene,
            Order = 0,
            LoadingTaskWeight = 0.6f,   // VNManager 注册 ui_VNGameplayPanel 任务，加载进度跟踪
            PrefabKey = VNUIPrefabKeys.VNGameplayPanel
        });
        Register(new PanelSpec
        {
            Name = "MainMenuPanel",
            Layer = EUILayer.Scene,
            Order = 0,
            PrefabKey = VNUIPrefabKeys.MainMenuPanel
        });
        Register(new PanelSpec
        {
            Name = "GalleryPanel",
            Layer = EUILayer.Scene,
            Order = 1,
            PrefabKey = VNUIPrefabKeys.GalleryPanel
        });

        // --- Overlay 层（常规覆盖） ---
        Register(new PanelSpec
        {
            Name = "PausePanel",
            Layer = EUILayer.Overlay,
            Order = 0,
            PrefabKey = VNUIPrefabKeys.PausePanel
        });
        Register(new PanelSpec
        {
            Name = "HistoryPanel",
            Layer = EUILayer.Overlay,
            Order = 1,
            PrefabKey = VNUIPrefabKeys.HistoryPanel
        });
        Register(new PanelSpec
        {
            Name = "SaveLoadPanel",
            Layer = EUILayer.Overlay,
            Order = 2,
            PrefabKey = VNUIPrefabKeys.SaveLoadPanel
        });
        Register(new PanelSpec
        {
            Name = "SettingsPanel",
            Layer = EUILayer.Overlay,
            Order = 3,
            PrefabKey = VNUIPrefabKeys.SettingsPanel
        });
        Register(new PanelSpec
        {
            Name = "ChoicePanel",
            Layer = EUILayer.Overlay,
            Order = 5,
            PrefabKey = VNUIPrefabKeys.ChoicePanel
        });

        // --- Popup 层（模态弹窗） ---
        Register(new PanelSpec
        {
            Name = "ConfirmPanel",
            Layer = EUILayer.Popup,
            Order = 0,
            PrefabKey = VNUIPrefabKeys.ConfirmPanel
        });

        // --- Loading 层（全局常驻） ---
        Register(new PanelSpec
        {
            Name = "LoadingProgressPanel",
            Layer = EUILayer.Loading,
            Order = 0,
            Persistent = true,
            PrefabKey = VNUIPrefabKeys.LoadingProgressPanel
        });
    }

    /// <summary>注册/覆盖面板规格（用户自定义面板入口）。同名视为覆盖（便于用户替换内置面板）。</summary>
    public void Register(PanelSpec spec)
    {
        if (spec == null || string.IsNullOrEmpty(spec.Name))
        {
            Debug.LogError("[UIManager] Register: spec 与 Name 不能为空");
            return;
        }

        bool overwrite = _specs.ContainsKey(spec.Name);
        _specs[spec.Name] = spec;

        if (overwrite)
            VNDebug.LogVerbose($"[UIManager] 面板规格覆盖: {spec.Name} (Layer={spec.Layer}, Order={spec.Order})");
    }

    /// <summary>查询面板规格（不存在返回 null）</summary>
    public PanelSpec GetSpec(string panelName)
    {
        return _specs.TryGetValue(panelName, out var spec) ? spec : null;
    }

    // ------------------------------------------------------------------
    // 显示 / 隐藏
    // ------------------------------------------------------------------

    /// <summary>
    /// 显示面板（幂等：已存在则直接 ShowMe 并回调；加载中则并入等待队列）。
    /// 未注册的面板会报错——新面板必须先经 Register 声明元数据。
    /// </summary>
    public void Show<T>(Action<T> onReady = null) where T : BasePanel
    {
        string name = typeof(T).Name;

        // 幂等：已实例化且存活
        if (_panels.TryGetValue(name, out var existing) && existing != null && existing.gameObject != null)
        {
            if (!existing.gameObject.activeSelf) existing.gameObject.SetActive(true);
            existing.ShowMe();

            // 面板已就绪：对应的加载进度任务（VNManager 预注册的 "ui_面板名"）直接完成。
            // 否则 WaitLoadingQueue 会因任务永不 Complete 而白等满 120 帧超时
            // （读档/换剧本时 GameplayPanel 已存在的常见路径）。
            var earlySpec = GetSpec(name);
            if (earlySpec != null && earlySpec.LoadingTaskWeight > 0f)
            {
                var pm = LoadingProgressManager.GetInstance();
                string taskId = "ui_" + name;
                if (pm != null && pm.GetTask(taskId) != null) // 无预注册任务时跳过（避免"任务不存在"警告）
                    pm.CompleteTask(taskId);
            }

            onReady?.Invoke(existing as T);
            return;
        }
        _panels.Remove(name); // 清理已销毁的残留键

        // 加载中：并入等待队列，绝不重复实例化
        if (_pendingShows.TryGetValue(name, out var waiting))
        {
            if (onReady != null) waiting.Add(p => onReady(p as T));
            return;
        }

        var spec = GetSpec(name);
        if (spec == null)
        {
            Debug.LogError($"[UIManager] 面板 {name} 未注册——请先 UIManager.GetInstance().Register(new PanelSpec{{...}}) 声明路径与层级");
            return;
        }

        // fallback 地址：
        // - 自定义面板（RegisterPanel）：PathResolver 返回目录 + 面板名；
        // - 内置面板：PrefabKey 本身即完整默认地址（键 = 包内默认资源路径）。
        string fullPath;
        if (spec.PathResolver != null)
        {
            string dir = spec.PathResolver();
            if (string.IsNullOrEmpty(dir))
            {
                Debug.LogError($"[UIManager] 面板 {name} 的路径解析失败（检查 PathResolver）");
                return;
            }
            fullPath = dir + "/" + name;
        }
        else
        {
            fullPath = !string.IsNullOrEmpty(spec.PrefabKey) ? spec.PrefabKey : name;
        }

        // 注册加载进度任务（仅指定了权重的大面板；VNManager 后续通过 LoadingProgressPanel 等待完成）。
        // 注意：VNManager（StartGameLoading/ContinueGameLoading）会预注册同名任务 "ui_面板名"，
        // 已存在时复用（不重复注册、不重置进度），仅在加载完成时 CompleteTask。
        string loadingTaskId = null;
        if (spec.LoadingTaskWeight > 0f && LoadingProgressManager.GetInstance() != null)
        {
            var progress = LoadingProgressManager.GetInstance();
            loadingTaskId = "ui_" + name;
            if (progress.GetTask(loadingTaskId) == null)
            {
                progress.RegisterTask(loadingTaskId, $"加载界面: {name}", spec.LoadingTaskWeight);
                progress.UpdateTaskProgress(loadingTaskId, 0.1f);
            }
        }

        var pending = new List<Action<BasePanel>>();
        if (onReady != null) pending.Add(p => onReady(p as T));
        _pendingShows[name] = pending;

        // 模板覆写优先：用户指派了自定义模板 → 直接实例化引用（零加载零寻址）；
        // 未指派 → 按 fallback 路径经资源服务链加载包内默认模板（Addressables → Resources）。
        // 注意：VNUIPrefabs 返回 prefab 本体，此处统一 Instantiate。
        var loadOp = VNUIPrefabs.LoadAsync(spec.PrefabKey, fullPath);
        loadOp.Completed += op => OnPanelPrefabLoaded(op.Asset, name, typeof(T), spec, fullPath, loadingTaskId);
    }

    /// <summary>面板 prefab 就绪回调：实例化 + Canvas 契约 + 入表 + 激活（覆写/默认模板共用）</summary>
    private void OnPanelPrefabLoaded(GameObject prefab, string name, Type panelType, PanelSpec spec, string fullPath, string loadingTaskId)
    {
        _pendingShows.TryGetValue(name, out var waiters);
        _pendingShows.Remove(name);

        // 加载期间面板已被 HidePanel/HideAll 作废（等待队列被移除）：丢弃加载结果，
        // 不再实例化——否则场景切换后加载完成的面板会凭空冒出来。
        if (waiters == null)
        {
            VNDebug.LogVerbose($"[UIManager] 面板 {name} 的 Show 已在加载期间被作废，丢弃加载结果");
            return;
        }

        if (prefab == null)
        {
            Debug.LogError($"[UIManager] 面板 {name} 加载失败: {fullPath}");
            return;
        }

        GameObject obj = UnityEngine.Object.Instantiate(prefab);

        // 脚本查找：契约要求在根节点；过渡期允许子节点（警告提示修 prefab）
        BasePanel panel = obj.GetComponent(panelType) as BasePanel;
        if (panel == null)
        {
            panel = obj.GetComponentInChildren(panelType, true) as BasePanel;
            if (panel != null)
                Debug.LogWarning($"[UIManager] 面板 {name} 的脚本不在 prefab 根节点（契约要求根节点自带 Canvas+脚本），已在子节点找到。建议将脚本移至根节点");
        }
        if (panel == null)
        {
            Debug.LogError($"[UIManager] 面板 {name} 加载成功但未找到面板组件: {fullPath}");
            UnityEngine.Object.Destroy(obj);
            return;
        }

        // 面板根 = 加载实例根（Canvas 契约与销毁都以根为准，避免脚本在子节点时残留 prefab 拆片）
        GameObject go = obj;
        go.name = name;

        // 根对象化（脱离任何父级）+ 常驻面板跨场景
        go.transform.SetParent(null, false);
        if (spec.Persistent) UnityEngine.Object.DontDestroyOnLoad(go);

        EnsureCanvasContract(go, spec);

        _panels[name] = panel;
        _panelRoots[name] = go;
        if (!go.activeSelf) go.SetActive(true);
        panel.ShowMe();

        // 完成加载进度任务
        if (loadingTaskId != null && LoadingProgressManager.GetInstance() != null)
            LoadingProgressManager.GetInstance().CompleteTask(loadingTaskId);

        VNDebug.LogVerbose($"[UIManager] Show {name} (Layer={spec.Layer}, sortingOrder={(int)spec.Layer + spec.Order})");

        // waiters 必非 null（为 null 时已在方法开头作为"已作废"丢弃）
        for (int i = 0; i < waiters.Count; i++)
        {
            try { waiters[i]?.Invoke(panel); }
            catch (Exception e) { Debug.LogError($"[UIManager] 面板 {name} 的 onReady 回调异常: {e}"); }
        }
    }

    /// <summary>
    /// 隐藏面板。常规面板销毁（现状语义）；Persistent 面板仅隐藏不销毁。
    /// </summary>
    public void Hide<T>() where T : BasePanel
    {
        HidePanel(typeof(T).Name);
    }

    /// <summary>按名隐藏（内部及少量旧调用点使用）</summary>
    public void HidePanel(string panelName)
    {
        // 加载中被要求隐藏：作废等待队列，避免加载完成后又冒出来
        _pendingShows.Remove(panelName);

        if (!_panels.TryGetValue(panelName, out var panel) || panel == null) return;

        var spec = GetSpec(panelName);
        GameObject root = GetPanelRoot(panelName);

        panel.HideMe();

        if (spec != null && spec.Persistent)
        {
            if (root != null) root.SetActive(false);
            VNDebug.LogVerbose($"[UIManager] Hide(常驻) {panelName}");
            return;
        }

        _panels.Remove(panelName);
        _panelRoots.Remove(panelName);
        if (root != null) UnityEngine.Object.Destroy(root);
        VNDebug.LogVerbose($"[UIManager] Hide(销毁) {panelName}");
    }

    /// <summary>获取面板根对象（不存在返回 null）</summary>
    public GameObject GetPanelRoot(string panelName)
    {
        if (_panelRoots.TryGetValue(panelName, out var root) && root != null) return root;
        // 兜底：脚本就在根上的标准契约下，panel.gameObject 即根
        return _panels.TryGetValue(panelName, out var panel) && panel != null ? panel.gameObject : null;
    }

    /// <summary>
    /// 隐藏全部面板（场景切换时由 OnSceneLoaded 自动调用）。
    /// Persistent 面板仅隐藏；其余销毁并清字典。
    /// </summary>
    public void HideAll()
    {
        // 在飞行中的 Show 一并作废：否则场景切换后加载完成的面板会凭空实例化出来
        _pendingShows.Clear();

        var toRemove = new List<string>();
        foreach (var kv in _panels)
        {
            if (kv.Value == null) { toRemove.Add(kv.Key); continue; }

            var spec = GetSpec(kv.Key);
            bool persistent = spec != null && spec.Persistent;
            GameObject root = GetPanelRoot(kv.Key);
            kv.Value.HideMe();

            if (persistent)
            {
                if (root != null) root.SetActive(false);
            }
            else
            {
                if (root != null) UnityEngine.Object.Destroy(root);
                toRemove.Add(kv.Key);
            }
        }
        foreach (var key in toRemove)
        {
            _panels.Remove(key);
            _panelRoots.Remove(key);
        }
    }

    // ------------------------------------------------------------------
    // 查询
    // ------------------------------------------------------------------

    /// <summary>获取已实例化面板（不存在返回 null；与旧 GetPanel 语义一致）</summary>
    public T Get<T>() where T : BasePanel
    {
        return _panels.TryGetValue(typeof(T).Name, out var panel) && panel != null ? panel as T : null;
    }

    /// <summary>获取已实例化面板（显式空检查风格）</summary>
    public bool TryGet<T>(out T panel) where T : BasePanel
    {
        panel = Get<T>();
        return panel != null;
    }

    /// <summary>按名获取（内部及少量旧调用点使用）</summary>
    public BasePanel GetPanel(string panelName)
    {
        return _panels.TryGetValue(panelName, out var panel) && panel != null ? panel : null;
    }

    /// <summary>面板是否已实例化且存活</summary>
    public bool IsShown<T>() where T : BasePanel
    {
        return Get<T>() != null;
    }

    // ------------------------------------------------------------------
    // Canvas 契约（一面板一 Canvas）
    // ------------------------------------------------------------------

    /// <summary>
    /// 确保 panel 根对象满足 Canvas 契约：
    /// Canvas(Overlay) + CanvasScaler(1920x1080, Shrink) + GraphicRaycaster + 根 RectTransform stretch 铺满。
    /// 缺失组件自动补挂并警告（prefab 过渡期容错）；sortingOrder 按注册表写入。
    /// </summary>
    private void EnsureCanvasContract(GameObject panelRoot, PanelSpec spec)
    {
        GameObject go = panelRoot;
        var rect = go.transform as RectTransform;

        var canvas = go.GetComponent<Canvas>();
        if (canvas == null)
        {
            canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            Debug.LogWarning($"[UIManager] 面板 {spec.Name} 的 prefab 缺少 Canvas，已自动补挂。请按契约重构 prefab（根节点自带 Canvas/CanvasScaler/GraphicRaycaster）");
        }
        else if (canvas.renderMode != RenderMode.ScreenSpaceOverlay)
        {
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        }

        var scaler = go.GetComponent<CanvasScaler>();
        if (scaler == null)
        {
            scaler = go.AddComponent<CanvasScaler>();
            Debug.LogWarning($"[UIManager] 面板 {spec.Name} 的 prefab 缺少 CanvasScaler，已自动补挂（1920x1080 / ScaleWithScreenSize / Shrink）");
        }
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Shrink;

        if (go.GetComponent<GraphicRaycaster>() == null)
            go.AddComponent<GraphicRaycaster>();

        // 契约执行：面板根必须 stretch 铺满，否则 prefab 里的锚点残留会让整屏 UI 偏移
        if (rect != null)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        canvas.sortingOrder = (int)spec.Layer + spec.Order;
        canvas.overrideSorting = false; // 根 Canvas 的 sortingOrder 直接生效
    }

    /// <summary>
    /// UI 特效层（引擎自建，取代旧"用户 prefab 内 EffectLayer"）。
    ///
    /// 结构：VN_EffectCanvas (Overlay, sortingOrder=5) / EffectLayer (stretch 铺满)
    /// 层级语义：盖住剧场画面（剧场相机），位于 Gameplay 对话框(10)之下——
    /// 与旧结构（EffectLayer 在 panel 内 UIRoot 之前）视觉层级一致。
    /// 幂等懒创建；与剧场根一致 DontDestroyOnLoad（引擎场景无关，
    /// 特效对象由命令层经 PoolManager 自行回收）。
    /// </summary>
    public Transform GetEffectLayerRoot()
    {
        if (_effectLayer != null) return _effectLayer;

        var canvasGo = new GameObject("VN_EffectCanvas");
        UnityEngine.Object.DontDestroyOnLoad(canvasGo);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5; // 剧场之上、Scene 层面板(10)之下
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Shrink;

        var layerGo = new GameObject("EffectLayer");
        _effectLayer = layerGo.AddComponent<RectTransform>();
        _effectLayer.SetParent(canvasGo.transform, false);
        _effectLayer.anchorMin = Vector2.zero;
        _effectLayer.anchorMax = Vector2.one;
        _effectLayer.offsetMin = Vector2.zero;
        _effectLayer.offsetMax = Vector2.zero;

        return _effectLayer;
    }

    // ------------------------------------------------------------------
    // EventSystem / 场景生命周期
    // ------------------------------------------------------------------

    private void EnsureEventSystem()
    {
        if (_eventSystemGameObject != null) return;

        EventSystem existing = UnityEngine.Object.FindFirstObjectByType<EventSystem>();
        if (existing != null)
        {
            _eventSystemGameObject = existing.gameObject;
            return;
        }

        // 三层兜底（确保任意启动时序下 EventSystem 都能就绪）：
        // 1. 模板覆写字段（用户自定义 EventSystem prefab）
        // 2. 程序化创建（最可靠：不依赖 Addressables 初始化时序，UI 输入立刻可用）
        // 3. Addressables / Resources 加载包内 prefab（如自定义组件如 InputSystemUIInputModule）
        GameObject prefab = VNUIPrefabs.Load(VNUIPrefabKeys.EventSystem, VNUIPrefabKeys.EventSystem);
        if (prefab != null)
        {
            _eventSystemGameObject = UnityEngine.Object.Instantiate(prefab);
        }
        else
        {
            // 程序化兜底：StartGame 早期调用时 Addressables 初始化未就绪，此处直接创建
            _eventSystemGameObject = new GameObject("EventSystem");
            _eventSystemGameObject.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            _eventSystemGameObject.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
            _eventSystemGameObject.AddComponent<StandaloneInputModule>();
#endif
        }
        UnityEngine.Object.DontDestroyOnLoad(_eventSystemGameObject);
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // 显式清理：面板是场景根对象，切场景必须主动回收（替代旧"销毁中心画布连带清理"）
        HideAll();
    }

    /// <summary>主菜单显示入口（面板规格已在内置注册表声明，此处仅显示）</summary>
    public void ShowMainMenu()
    {
        Show<MainMenuPanel>();
    }

    // 注意：此处不设终结器。BaseManager 单例与 SceneManager.sceneLoaded 订阅同为进程级生命周期，
    // 且终结器运行在 GC 线程上——在其中调用 Unity API（SceneManager 事件解绑）属未定义行为。
}
