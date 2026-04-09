using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using VNovelizer.Core.Diagnostics;


public enum E_UI_Layer
{ 
    Bottom,
    Left,
    Middle,
    Right,
    Top,
    System
}
public class UIManager : BaseManager<UIManager>
{
    public Dictionary<string, BasePanel> panelDic = new Dictionary<string, BasePanel>();
    
    private Transform Bottom;
    private Transform Top;
    private Transform Left;
    private Transform Middle;
    private Transform Right;
    private Transform System;

    public RectTransform canvas;

    private GameObject _canvasGameObject;
    //一个私有变量来记录 EventSystem GameObject
    private GameObject _eventSystemGameObject;
    
    //记录Canvas和EventSystem是否是动态创建的（需要销毁）
    private bool _isCanvasDynamicallyCreated = false;
    private bool _isEventSystemDynamicallyCreated = false;

    private static bool isListeningSceneLoad = false;
    
    //记录全局加载界面界面
    private LoadingProgressPanel _persistentLoadingPanel;
    private GameObject _persistentLoadingPanelGO;
    
    //test
    private static int _instanceCounter = 0;
    private int _instanceId;
    
    public UIManager()
    {
        //测试，查看UIManager的创建问题
        _instanceId = ++_instanceCounter;
        VNDebug.LogVerbose($"[UIManager] ctor, instanceId = {_instanceId}");
        
        // 构造函数不再初始化Canvas，留待Init方法调用
        
        // 监听场景加载事件
        if (!isListeningSceneLoad)
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            isListeningSceneLoad = true;
        }
    }
    /// <summary>
    /// 加载界面的预加载
    /// </summary>
    private void PreloadPersistentLoadingPanel()
    {
        // 已经缓存过，直接返回
        if (_persistentLoadingPanel != null && _persistentLoadingPanelGO != null)
            return;

        const string panelName = "LoadingProgressPanel";
        string loadPath = VNProjectConfig.Instance.UI_LoadingPath;

        // 1. 先检查当前场景里是否已经有运行时实例（防止重复）
        LoadingProgressPanel[] existingPanels =
            Object.FindObjectsByType<LoadingProgressPanel>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        if (existingPanels != null && existingPanels.Length > 0)
        {
            // 取第一个作为唯一常驻实例
            _persistentLoadingPanel = existingPanels[0];
            _persistentLoadingPanelGO = _persistentLoadingPanel.gameObject;

            // 如果有重复实例，销毁多余的
            for (int i = 1; i < existingPanels.Length; i++)
            {
                if (existingPanels[i] != null && existingPanels[i].gameObject != null)
                {
                    GameObject.Destroy(existingPanels[i].gameObject);
                }
            }

            // 确保是根对象
            _persistentLoadingPanelGO.transform.SetParent(null, false);

            // 放进 DontDestroyOnLoad
            GameObject.DontDestroyOnLoad(_persistentLoadingPanelGO);

            // 预加载阶段直接隐藏
            _persistentLoadingPanelGO.SetActive(false);

            VNDebug.LogVerbose("[UIManager] 复用现有 LoadingProgressPanel，并设为常驻隐藏对象");
            return;
        }

        // 2. 场景里没有，就通过 ResourcesManager 创建
        // 注意：ResourcesManager.Load<GameObject>() 已经会自动 Instantiate
        GameObject obj = ResourcesManager.GetInstance().Load<GameObject>(loadPath + "/" + panelName);
        if (obj == null)
        {
            Debug.LogError($"[UIManager] 无法加载全局 LoadingProgressPanel: {loadPath}/{panelName}");
            return;
        }

        // 确保是根对象
        obj.transform.SetParent(null, false);

        // 先拿脚本
        LoadingProgressPanel panel = obj.GetComponent<LoadingProgressPanel>();
        if (panel == null)
        {
            // 兼容脚本挂在子物体上的情况
            panel = obj.GetComponentInChildren<LoadingProgressPanel>(true);
        }

        if (panel == null)
        {
            Debug.LogError("[UIManager] LoadingProgressPanel 预制体上未找到 LoadingProgressPanel 组件！");
            GameObject.Destroy(obj);
            return;
        }

        // 标记为常驻
        GameObject.DontDestroyOnLoad(obj);

        _persistentLoadingPanel = panel;
        _persistentLoadingPanelGO = obj;

        // 预加载阶段直接隐藏，不调用 HideMe，避免初始逻辑被扰动
        _persistentLoadingPanelGO.SetActive(false);

        VNDebug.LogVerbose("[UIManager] 已预创建常驻 LoadingProgressPanel（隐藏）");
    }
        
    
    
    /// <summary>
    /// 场景加载完成回调
    /// </summary>
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        string sceneName = scene.name;
        
        // 如果是主菜单场景，先销毁动态创建的Canvas和EventSystem
        if (sceneName == "VNMainMenu" || sceneName == "VNMainMenuScene")
        {
            VNDebug.LogVerbose($"[UIManager] 检测到主菜单场景: {sceneName}，清理动态创建的UI对象...");
            
            // 销毁动态创建的Canvas
            if (_isCanvasDynamicallyCreated && _canvasGameObject != null)
            {
                VNDebug.LogVerbose("[UIManager] 销毁动态创建的Canvas");
                GameObject.Destroy(_canvasGameObject);
                _canvasGameObject = null;
                canvas = null;
                _isCanvasDynamicallyCreated = false;
            }
            
            // 销毁动态创建的EventSystem
            if (_isEventSystemDynamicallyCreated && _eventSystemGameObject != null)
            {
                VNDebug.LogVerbose("[UIManager] 销毁动态创建的EventSystem");
                GameObject.Destroy(_eventSystemGameObject);
                _eventSystemGameObject = null;
                _isEventSystemDynamicallyCreated = false;
            }



            // 清空面板字典
            panelDic.Clear();
            
            VNDebug.LogVerbose($"[UIManager] 主菜单场景初始化...");
            
            // 初始化UI系统（会检测场景中已存在的Canvas）
            Init();
            
            // 延迟一帧显示MainMenuPanel，确保所有组件都已初始化
            MonoManager.GetInstance().StartCoroutine(DelayedShowMainMenu());
        }
        else
        {
            // 延迟一帧执行，确保场景中的Camera已经初始化
            // MonoManager.GetInstance().StartCoroutine(DelayedSetupCanvasCamera());
            MonoManager.GetInstance().StartCoroutine(DelayedInitGameplayUI());
        }

        SetupCanvasCamera();
    }
    /// <summary>
    /// 协程，进入新场景清除应用，重新初始化
    /// </summary>
    /// <returns></returns>
    private IEnumerator DelayedInitGameplayUI()
    {
        yield return null; // 等新场景对象起来

        // 清掉旧层级引用，避免沿用上一场景已经失效的对象
        Bottom = null;
        Top = null;
        Left = null;
        Middle = null;
        Right = null;
        System = null;

        if (canvas == null || _canvasGameObject == null)
        {
            canvas = null;
            _canvasGameObject = null;
        }

        VNDebug.LogVerbose("[UIManager] 非主菜单场景，重新初始化 UIManager...");
        Init();
        SetupCanvasCamera();
    }
    
    
    /// <summary>
    /// 延迟设置Canvas的Camera引用（等待场景完全加载）
    /// </summary>
    private IEnumerator DelayedSetupCanvasCamera()
    {
        yield return null; // 等待一帧，确保场景中的Camera已经初始化
        SetupCanvasCamera();
    }

    /// <summary>
    /// 延迟显示主菜单面板
    /// </summary>
    private IEnumerator DelayedShowMainMenu()
    {
        yield return null; // 等待一帧
        
        // 检查MainMenuPanel是否已存在于场景中
        MainMenuPanel existingPanel = Object.FindFirstObjectByType<MainMenuPanel>();
        if (existingPanel != null)
        {
            VNDebug.LogVerbose("[UIManager] 场景中已存在MainMenuPanel，直接显示");
            existingPanel.ShowMe();
            
            // 如果面板不在字典中，添加到字典
            if (!panelDic.ContainsKey("MainMenuPanel"))
            {
                panelDic.Add("MainMenuPanel", existingPanel);
            }
        }
        else
        {
            // 场景中不存在，动态加载
            string mainMenuPath = VNProjectConfig.Instance != null 
                ? VNProjectConfig.Instance.UI_MainMenuPath 
                : "VNPrefabs/UI/MainMenu"; // 默认路径
            ShowPanel<MainMenuPanel>("MainMenuPanel", mainMenuPath, E_UI_Layer.Middle, null);
        }
    }
    
    /// <summary>
    /// 初始化UI系统，支持场景中已存在的Canvas或动态创建
    /// </summary>
    public void Init()
    {
        VNDebug.LogVerbose("[UIManager] 尝试创建UIManager");

        //检查是否已经创建，防止重复
        if (canvas != null && _canvasGameObject != null)
        {
            Debug.LogWarning("[UIManager] Canvas 已经存在，跳过重新创建。");
            RefreshLayerReferences();
            //即使Canvas已存在，也要重新设置Camera
            SetupCanvasCamera();
            return;
        }

        //首先检查场景中是否已存在Canvas
        // Canvas existingCanvas = Object.FindFirstObjectByType<Canvas>();
        Canvas existingCanvas = FindUsableSceneCanvas();
        // if (existingCanvas != null)
        // {
        //     VNDebug.LogVerbose($"[UIManager] Init 找到的 existingCanvas = {existingCanvas.name}, root = {existingCanvas.transform.root.name}");
        //     
        // }
        
        if (existingCanvas != null)
        {
            VNDebug.LogVerbose("[UIManager] 检测到场景中已存在Canvas，使用场景中的Canvas");
            _canvasGameObject = existingCanvas.gameObject;
            canvas = existingCanvas.transform as RectTransform;
            _isCanvasDynamicallyCreated = false; // 场景中的Canvas不是动态创建的
            
            // 找到所有层级
            RefreshLayerReferences();
            
            // 检查层级是否完整
            if (Middle == null)
            {
                Debug.LogWarning("[UIManager] 场景中的Canvas缺少Middle层级，尝试创建...");
                CreateMissingLayers();
            }
            
            //如果Canvas是ScreenSpaceCamera模式，设置Camera引用
            SetupCanvasCamera();
        }
        else
        {
            // 场景中不存在Canvas，动态创建
            VNDebug.LogVerbose("[UIManager] 场景中不存在Canvas，动态创建...");
            _canvasGameObject = ResourcesManager.GetInstance().Load<GameObject>("VNovelizerRes/VNPrefabs/UI/VNGamePlayCanvas");
            if (_canvasGameObject == null)
            {
                Debug.LogError("[UIManager] 无法加载 VNGamePlayCanvas 预制体！");
                return;
            }
            canvas = _canvasGameObject.transform as RectTransform;
            GameObject.DontDestroyOnLoad(_canvasGameObject);
            _isCanvasDynamicallyCreated = true; // 标记为动态创建
            
            // 找到所有层级
            RefreshLayerReferences();
            
            //如果Canvas是ScreenSpaceCamera模式，设置Camera引用
            SetupCanvasCamera();
        }

        //检查 EventSystem 是否已经存在，防止重复
        if (_eventSystemGameObject == null)
        {
            //首先检查场景中是否已存在EventSystem
            EventSystem existingEventSystem = Object.FindFirstObjectByType<EventSystem>();
            if (existingEventSystem != null)
            {
                VNDebug.LogVerbose("[UIManager] 检测到场景中已存在EventSystem，使用场景中的EventSystem");
                _eventSystemGameObject = existingEventSystem.gameObject;
                _isEventSystemDynamicallyCreated = false; // 场景中的EventSystem不是动态创建的
            }
            else
            {
                // 场景中不存在，动态创建
                _eventSystemGameObject = ResourcesManager.GetInstance().Load<GameObject>("VNovelizerRes/VNPrefabs/UI/EventSystem");
                if (_eventSystemGameObject == null)
                {
                    Debug.LogError("[UIManager] 无法加载 EventSystem 预制体！");
                    return;
                }
                GameObject.DontDestroyOnLoad(_eventSystemGameObject);
                _isEventSystemDynamicallyCreated = true; // 标记为动态创建
            }
        }
        
        PreloadPersistentLoadingPanel();
    }
    /// <summary>
    /// 查找主Canvas
    /// </summary>
    /// <returns></returns>
    private Canvas FindUsableSceneCanvas()
    {
        Canvas[] canvases = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        for (int i = 0; i < canvases.Length; i++)
        {
            Canvas c = canvases[i];
            if (c == null) continue;

            // 1) 跳过常驻 LoadingProgressPanel 自带的 Canvas
            if (_persistentLoadingPanelGO != null)
            {
                if (c.gameObject == _persistentLoadingPanelGO || c.transform.IsChildOf(_persistentLoadingPanelGO.transform))
                {
                    VNDebug.LogVerbose($"[UIManager] 跳过 LoadingProgressPanel Canvas: {c.name}");
                    continue;
                }
            }

            // 2) 跳过黑幕过场的 Canvas_FadeOverlay
            if (c.name == "Canvas_FadeOverlay")
            {
                VNDebug.LogVerbose($"[UIManager] 跳过 FadeOverlay Canvas: {c.name}");
                continue;
            }

            // 3) 优先使用真正的 VNGamePlayCanvas
            if (c.name == "VNGamePlayCanvas")
            {
                return c;
            }

            // 4) 或者至少要求它已经有完整的 UI 层级
            bool hasBottom = c.transform.Find("Bottom") != null;
            bool hasMiddle = c.transform.Find("Middle") != null;
            bool hasTop = c.transform.Find("Top") != null;
            bool hasSystem = c.transform.Find("System") != null;

            if (hasBottom && hasMiddle && hasTop && hasSystem)
            {
                return c;
            }
        }

        return null;
    }
    
    
    /// <summary>
    /// 刷新层级引用
    /// </summary>
    private void RefreshLayerReferences()
    {
        if (canvas == null) return;
        
        Bottom = canvas.Find("Bottom");
        Top = canvas.Find("Top");
        Left = canvas.Find("Left");
        Middle = canvas.Find("Middle");
        Right = canvas.Find("Right");
        System = canvas.Find("System");
        
        // 输出层级状态
        VNDebug.LogVerbose($"[UIManager] 层级状态 - Bottom: {Bottom != null}, Top: {Top != null}, Left: {Left != null}, " +
                  $"Middle: {Middle != null}, Right: {Right != null}, System: {System != null}");
    }
    
    /// <summary>
    /// 创建缺失的层级
    /// </summary>
    private void CreateMissingLayers()
    {
        if (canvas == null) return;
        
        // 创建缺失的层级
        if (Bottom == null) CreateLayer("Bottom");
        if (Top == null) CreateLayer("Top");
        if (Left == null) CreateLayer("Left");
        if (Middle == null) CreateLayer("Middle");
        if (Right == null) CreateLayer("Right");
        if (System == null) CreateLayer("System");
        
        // 刷新引用
        RefreshLayerReferences();
    }
    
    /// <summary>
    /// 创建单个层级
    /// </summary>
    private void CreateLayer(string layerName)
    {
        GameObject layerObj = new GameObject(layerName);
        layerObj.transform.SetParent(canvas);
        RectTransform rect = layerObj.AddComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        VNDebug.LogVerbose($"[UIManager] 已创建缺失的层级: {layerName}");
    }
    
    /// <summary>
    /// 设置Canvas的Camera引用（用于ScreenSpaceCamera模式）
    /// </summary>
    private void SetupCanvasCamera()
    {
        
        if (canvas == null)
        { 
            Debug.LogWarning("[UIManager] SetupCanvasCamera: canvas 为 null");
            return;
        } 
        
        Canvas canvasComponent = canvas.GetComponent<Canvas>();
        if (canvasComponent == null)
        {
            Debug.LogWarning("[UIManager] SetupCanvasCamera: Canvas 组件为 null");
            return;
        }
                
        // 如果Canvas是ScreenSpaceCamera模式，需要设置Camera
        if (canvasComponent.renderMode == RenderMode.ScreenSpaceCamera)
        {
            
            // 无论Camera引用是否为空，都重新查找并设置（场景跳转后Camera可能变化）
            Camera mainCamera = Camera.main;
            if (mainCamera == null)
            {
                VNDebug.LogVerbose("[UIManager] Camera.main 为 null，尝试查找场景中的第一个Camera");
                // 如果MainCamera标签不存在，尝试查找第一个Camera
                Camera[] cameras = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
                VNDebug.LogVerbose($"[UIManager] 找到 {cameras.Length} 个Camera");
                if (cameras.Length > 0)
                {
                    mainCamera = cameras[0];
                    VNDebug.LogVerbose($"[UIManager] 使用第一个Camera: {mainCamera.name}");
                }
            }
            else
            {
                VNDebug.LogVerbose($"[UIManager] 找到 Camera.main: {mainCamera.name}");
            }
            
            if (mainCamera != null)
            {
                canvasComponent.worldCamera = mainCamera;
            }
        }
        else
        {
            VNDebug.LogVerbose($"[UIManager] Canvas renderMode 不是 ScreenSpaceCamera，当前模式: {canvasComponent.renderMode}");
        }
    } 
    
    
//     /// <summary>
// /// 确保存在一个全局常驻的Canvas，用于放置 DontDestroyOnLoad 的UI
// /// </summary>
// private void EnsurePersistentUICanvas()
// {
//     if (_persistentCanvas != null)
//     {
//         return;
//     }
//
//     GameObject root = new GameObject("VNGlobalPersistentCanvas");
//     Canvas canvasComp = root.AddComponent<Canvas>();
//     canvasComp.renderMode = RenderMode.ScreenSpaceOverlay;
//     canvasComp.sortingOrder = 5000; // 保证盖在普通UI上面
//
//     CanvasScaler scaler = root.AddComponent<CanvasScaler>();
//     scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
//     scaler.referenceResolution = new Vector2(1920, 1080);
//     scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
//     scaler.matchWidthOrHeight = 0.5f;
//
//     root.AddComponent<GraphicRaycaster>();
//
//     _persistentCanvas = root.GetComponent<RectTransform>();
//     GameObject.DontDestroyOnLoad(root);
//
//     GameObject systemLayer = new GameObject("System");
//     systemLayer.transform.SetParent(_persistentCanvas, false);
//
//     RectTransform layerRect = systemLayer.AddComponent<RectTransform>();
//     layerRect.anchorMin = Vector2.zero;
//     layerRect.anchorMax = Vector2.one;
//     layerRect.offsetMin = Vector2.zero;
//     layerRect.offsetMax = Vector2.zero;
//
//     _persistentSystemLayer = systemLayer.transform;
//
//     VNDebug.LogVerbose("[UIManager] 已创建全局常驻UI根节点 VNGlobalPersistentCanvas");
// }

/// <summary>
/// 预创建全局 LoadingProgressPanel，默认隐藏，且不随场景销毁
/// </summary>
// private void PreloadGlobalLoadingPanel()
// {
//     if (_hasPreloadedGlobalLoadingPanel)
//     {
//         return;
//     }
//
//     EnsurePersistentUICanvas();
//
//     const string panelName = "LoadingProgressPanel";
//     if (persistentPanelDic.ContainsKey(panelName))
//     {
//         _hasPreloadedGlobalLoadingPanel = true;
//         return;
//     }
//
//     string loadPath = VNProjectConfig.Instance != null
//         ? VNProjectConfig.Instance.UI_LoadingPath
//         : "VNovelizerRes/VNPrefabs/UI/Loading";
//
//     GameObject prefab = ResourcesManager.GetInstance().Load<GameObject>(loadPath + "/" + panelName);
//     if (prefab == null)
//     {
//         Debug.LogError($"[UIManager] 无法预加载全局面板: {loadPath}/{panelName}");
//         return;
//     }
//
//     GameObject obj = GameObject.Instantiate(prefab, _persistentSystemLayer, false);
//
//     RectTransform rect = obj.transform as RectTransform;
//     if (rect != null)
//     {
//         rect.anchorMin = Vector2.zero;
//         rect.anchorMax = Vector2.one;
//         rect.offsetMin = Vector2.zero;
//         rect.offsetMax = Vector2.zero;
//         rect.localScale = Vector3.one;
//         rect.localPosition = Vector3.zero;
//     }
//
//     LoadingProgressPanel panel = obj.GetComponent<LoadingProgressPanel>();
//     if (panel == null)
//     {
//         Debug.LogError("[UIManager] 预加载失败：LoadingProgressPanel 预制体上没有 LoadingProgressPanel 组件");
//         GameObject.Destroy(obj);
//         return;
//     }
//
//     persistentPanelDic[panelName] = panel;
//
//     // 默认隐藏
//     panel.HideMe();
//     obj.SetActive(false);
//
//     _hasPreloadedGlobalLoadingPanel = true;
//
//     VNDebug.LogVerbose("[UIManager] 已预创建全局 LoadingProgressPanel（隐藏，DontDestroyOnLoad）");
// }

/// <summary>
/// 显示全局常驻面板
/// </summary>
// private bool TryShowPersistentPanel<T>(string panelName, UnityAction<T> callBack) where T : BasePanel
// {
//     if (!persistentPanelDic.ContainsKey(panelName))
//     {
//         return false;
//     }
//
//     BasePanel basePanel = persistentPanelDic[panelName];
//     if (basePanel == null || basePanel.gameObject == null)
//     {
//         persistentPanelDic.Remove(panelName);
//         return false;
//     }
//
//     basePanel.gameObject.SetActive(true);
//     basePanel.transform.SetAsLastSibling();
//     basePanel.ShowMe();
//
//     if (callBack != null)
//     {
//         callBack(basePanel as T);
//     }
//
//     return true;
// }

    public void ShowPanel<T>(string panelName,string loadPath, E_UI_Layer layer, UnityAction<T> callBack) where T : BasePanel
    {
        
        if (panelName == "LoadingProgressPanel")
        {
            if (_persistentLoadingPanel == null || _persistentLoadingPanelGO == null)
            {
                PreloadPersistentLoadingPanel();
            }

            if (_persistentLoadingPanel != null && _persistentLoadingPanelGO != null)
            {
                _persistentLoadingPanelGO.SetActive(true);
                _persistentLoadingPanel.ShowMe();

                if (callBack != null)
                    callBack(_persistentLoadingPanel as T);

                return;
            }

            Debug.LogError("[UIManager] 常驻 LoadingProgressPanel 显示失败");
            return;
        }

        
        
        // // 先尝试显示全局常驻面板（例如 LoadingProgressPanel）
        // if (TryShowPersistentPanel(panelName, callBack))
        // {
        //     return;
        // }
        
        // 确保Canvas已初始化
        if (canvas == null || Middle == null)
        {
            Debug.LogWarning("[UIManager] Canvas未初始化，正在初始化...");
            Init();
            
            // 初始化后再次检查
            if (canvas == null || Middle == null)
            {
                Debug.LogError("[UIManager] Canvas初始化失败，无法显示面板！");
                return;
            }
        }
        
        string uiTaskID = $"ui_{panelName}";
        LoadingProgressManager progressManager = LoadingProgressManager.GetInstance();
        
        // 检查面板是否已存在
        // if (panelDic.ContainsKey(panelName))
        // {
        //     // 【修复】如果面板已存在，也需要完成任务，避免进度条卡住
        //     if (progressManager.GetTaskProgress(uiTaskID) >= 0)
        //     {
        //         // 任务已注册，直接完成它
        //         progressManager.CompleteTask(uiTaskID);
        //     }
        //     
        //     panelDic[panelName].ShowMe();
        //     if (callBack != null)
        //     {
        //         callBack(panelDic[panelName] as T);
        //     }
        //     return;
        // }
        
        // 清理失效的旧引用
        if (panelDic.ContainsKey(panelName))
        {
            BasePanel existingPanel = panelDic[panelName];
            if (existingPanel == null || existingPanel.gameObject == null)
            {
                Debug.LogWarning($"[UIManager] 检测到失效的面板引用，移除: {panelName}");
                panelDic.Remove(panelName);
            }
        }

        // 检查是否真的已存在
        if (panelDic.ContainsKey(panelName))
        {
            if (progressManager.GetTaskProgress(uiTaskID) >= 0)
            {
                progressManager.CompleteTask(uiTaskID);
            }

            panelDic[panelName].ShowMe();

            VNDebug.LogVerbose($"[UIManager] 复用已有面板: {panelName}");

            if (callBack != null)
            {
                callBack(panelDic[panelName] as T);
            }
            return;
        }
        
        
        // 注册UI加载任务（如果任务已存在，不重复注册，只更新名称）
        bool taskExists = progressManager.GetTaskProgress(uiTaskID) >= 0;
        
        if (!taskExists)
        {
            // 任务不存在，注册新任务（权重会在ResourcesManager中设置，但这里先注册确保存在）
            progressManager.RegisterTask(uiTaskID, $"加载UI: {panelName}", 1f);
        }
        else
        {
            // 任务已存在，只更新名称（可能权重已在其他地方设置）
            progressManager.UpdateTaskName(uiTaskID, $"加载UI: {panelName}");
        }
        ResourcesManager.GetInstance().LoadAsync<GameObject>(
            loadPath +"/"+ panelName, 
            (obj) =>
            {
                
                // 异步加载完成后，再次检查面板是否已存在
                if (panelDic.ContainsKey(panelName))
                {
                    Debug.LogWarning($"[UIManager] 面板 {panelName} 在异步加载期间已被添加，销毁重复实例");
                    GameObject.Destroy(obj);
                    
                    // 显示已存在的面板
                    panelDic[panelName].ShowMe();
                    if (callBack != null)
                    {
                        callBack(panelDic[panelName] as T);
                    }
                    return;
                }
                
                // 检查资源是否加载成功
                if (obj == null)
                {
                    Debug.LogError($"[UIManager] 无法加载面板预制体: {loadPath}/{panelName}");
                    return;
                }

                // 确保Canvas已初始化（异步加载期间可能Canvas被销毁）
                if (canvas == null || Middle == null)
                {
                    Debug.LogWarning("[UIManager] Canvas在异步加载期间丢失，重新初始化...");
                    Init();
                    
                    if (canvas == null || Middle == null)
                    {
                        Debug.LogError("[UIManager] Canvas初始化失败，销毁面板对象");
                        GameObject.Destroy(obj);
                        return;
                    }
                }

                // Transform father = null;
                // switch (layer)
                // {
                //     case E_UI_Layer.Bottom:
                //         father = Bottom;
                //         break;
                //     case E_UI_Layer.Middle:
                //         father = Middle;
                //         break;
                //     case E_UI_Layer.Right:
                //         father = Right;
                //         break;
                //     case E_UI_Layer.Left:
                //         father = Left;
                //         break;
                //     case E_UI_Layer.Top:
                //         father = Top;
                //         break;
                //     case E_UI_Layer.System:
                //         father = System;
                //         break;
                // }
                //
                // // 检查UI层级是否存在
                // if (father == null)
                // {
                //     Debug.LogError($"[UIManager] UI层级 {layer} 未找到，请检查Canvas预制体结构");
                //     GameObject.Destroy(obj);
                //     return;
                // }
                
                Transform father = GetLayerFather(layer);
                
                if (father == null || canvas == null)
                {
                    Debug.LogWarning($"[UIManager] 异步创建 {panelName} 时发现父节点/Canvas失效，尝试重新初始化...");
                    Init();
                    father = GetLayerFather(layer);
                }

                if (father == null)
                {
                    Debug.LogError($"[UIManager] UI层级 {layer} 未找到，无法创建面板: {panelName}");
                    if (obj != null) GameObject.Destroy(obj);
                    return;
                }
                

                obj.transform.SetParent(father);
                obj.transform.localPosition = Vector3.zero;
                obj.transform.localScale = Vector3.one;

                RectTransform rect = obj.transform as RectTransform;
                if (rect != null)
                {
                    rect.offsetMin = Vector2.zero; // Left, Bottom = 0
                    rect.offsetMax = Vector2.zero;
                }

                //得到预设体的面板脚本
                T panel = obj.GetComponent<T>();
                if (panel == null)
                {
                    Debug.LogError($"[UIManager] 面板预制体 {panelName} 上未找到 {typeof(T).Name} 组件！");
                    GameObject.Destroy(obj);
                    return;
                }

                //将panel存进字典（使用TryAdd或先检查）
                if (!panelDic.ContainsKey(panelName))
                {
                    panelDic.Add(panelName, panel);
                }
                else
                {
                    Debug.LogWarning($"[UIManager] 面板 {panelName} 已存在于字典中，销毁重复实例");
                    GameObject.Destroy(obj);
                    return;
                }

                //显示面板（调用ShowMe，让面板执行初始化逻辑，如订阅事件等）
                panel.ShowMe();
                
                VNDebug.LogVerbose($"[UIManager] ShowPanel 成功: {panelName}, 父节点 = {father.name}, Canvas = {canvas.name}");
                
                //处理面板创建后的逻辑（在ShowMe之后调用，确保面板已完全初始化）
                if (callBack != null)
                {
                    callBack(panel);
                }
            },
            uiTaskID,  // 传递taskID，让ResourcesManager跟踪进度
            $"加载UI: {panelName}",  // 任务名称
            1f  // 权重（如果任务已存在，这个权重会被忽略）
        );
    }

    //隐藏面板
    // public void HidePanel(string panelName)
    // {
    //     if (panelDic.ContainsKey(panelName))
    //     {
    //         BasePanel panel = panelDic[panelName];
    //         if (panel != null && panel.gameObject != null)
    //         {
    //             GameObject.Destroy(panel.gameObject);
    //         }
    //         panelDic.Remove(panelName);
    //     }
    // }
    
    
    public void HidePanel(string panelName)
    {
        if (panelName == "LoadingProgressPanel")
        {
            if (_persistentLoadingPanel != null && _persistentLoadingPanelGO != null)
            {
                _persistentLoadingPanel.HideMe();
                _persistentLoadingPanelGO.SetActive(false);
            }
            return;
        }

        if (panelDic.ContainsKey(panelName))
        {
            BasePanel panel = panelDic[panelName];
            if (panel != null && panel.gameObject != null)
            {
                GameObject.Destroy(panel.gameObject);
            }
            panelDic.Remove(panelName);
        }
    }
    

    // public T GetPanel<T>(string panelName) where T : BasePanel
    // {
    //     //Testhere
    //     VNDebug.LogVerbose("[UIManager] current Panel List:");
    //     foreach (var pair in panelDic)
    //     {
    //         VNDebug.LogVerbose($"[UIManager]key = {pair.Key}, value = {pair.Value}");
    //     }
    //     
    //     if (panelDic.ContainsKey(panelName))
    //     {
    //         return panelDic[panelName] as T;
    //     }
    //     return null;
    // }
    
    public T GetPanel<T>(string panelName) where T : BasePanel
    {
        if (panelName == "LoadingProgressPanel")
        {
            return _persistentLoadingPanel as T;
        }

        if (panelDic.ContainsKey(panelName))
        {
            return panelDic[panelName] as T;
        }

        return null;
    }
    
    //获得对应层级的父对象
    public Transform GetLayerFather(E_UI_Layer layer)
    {
        switch (layer)
        {
            case E_UI_Layer.Bottom:
                return this.Bottom;
            case E_UI_Layer.Middle:
                return this.Middle;
            case E_UI_Layer.Right:
                return this.Right;
            case E_UI_Layer.Left:
                return this.Left;
            case E_UI_Layer.Top:
                return this.Top;
            case E_UI_Layer.System:
                return this.System;
        }
        return null;
    }

    //给控件添加自定义事件监听
    public static void AddCustomEventListener(UIBehaviour control, EventTriggerType type, UnityAction<BaseEventData> callback)
    { 
        EventTrigger trigger = control.gameObject.GetComponent<EventTrigger>();
        if (trigger == null)
        {
            trigger = control.gameObject.AddComponent<EventTrigger>();
        }
        EventTrigger.Entry entry = new EventTrigger.Entry();
        entry.eventID = type;
        entry.callback.AddListener((data) => 
        { 
            callback(data as BaseEventData);
        });

        trigger.triggers.Add(entry);
    }
}