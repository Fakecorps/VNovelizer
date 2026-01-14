using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.SceneManagement;


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

    public UIManager()
    {
        // 构造函数不再初始化Canvas，留待Init方法调用
        
        // 监听场景加载事件
        if (!isListeningSceneLoad)
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            isListeningSceneLoad = true;
        }
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
            Debug.Log($"[UIManager] 检测到主菜单场景: {sceneName}，清理动态创建的UI对象...");
            
            // 销毁动态创建的Canvas
            if (_isCanvasDynamicallyCreated && _canvasGameObject != null)
            {
                Debug.Log("[UIManager] 销毁动态创建的Canvas");
                GameObject.Destroy(_canvasGameObject);
                _canvasGameObject = null;
                canvas = null;
                _isCanvasDynamicallyCreated = false;
            }
            
            // 销毁动态创建的EventSystem
            if (_isEventSystemDynamicallyCreated && _eventSystemGameObject != null)
            {
                Debug.Log("[UIManager] 销毁动态创建的EventSystem");
                GameObject.Destroy(_eventSystemGameObject);
                _eventSystemGameObject = null;
                _isEventSystemDynamicallyCreated = false;
            }



            // 清空面板字典
            panelDic.Clear();
            
            Debug.Log($"[UIManager] 主菜单场景初始化...");
            
            // 初始化UI系统（会检测场景中已存在的Canvas）
            Init();
            
            // 延迟一帧显示MainMenuPanel，确保所有组件都已初始化
            MonoManager.GetInstance().StartCoroutine(DelayedShowMainMenu());
        }
        else
        {
            // 延迟一帧执行，确保场景中的Camera已经初始化
            MonoManager.GetInstance().StartCoroutine(DelayedSetupCanvasCamera());
        }

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
            Debug.Log("[UIManager] 场景中已存在MainMenuPanel，直接显示");
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
        Canvas existingCanvas = Object.FindFirstObjectByType<Canvas>();
        if (existingCanvas != null)
        {
            Debug.Log("[UIManager] 检测到场景中已存在Canvas，使用场景中的Canvas");
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
            Debug.Log("[UIManager] 场景中不存在Canvas，动态创建...");
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
                Debug.Log("[UIManager] 检测到场景中已存在EventSystem，使用场景中的EventSystem");
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
        Debug.Log($"[UIManager] 层级状态 - Bottom: {Bottom != null}, Top: {Top != null}, Left: {Left != null}, " +
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
        Debug.Log($"[UIManager] 已创建缺失的层级: {layerName}");
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
                Debug.Log("[UIManager] Camera.main 为 null，尝试查找场景中的第一个Camera");
                // 如果MainCamera标签不存在，尝试查找第一个Camera
                Camera[] cameras = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
                Debug.Log($"[UIManager] 找到 {cameras.Length} 个Camera");
                if (cameras.Length > 0)
                {
                    mainCamera = cameras[0];
                    Debug.Log($"[UIManager] 使用第一个Camera: {mainCamera.name}");
                }
            }
            else
            {
                Debug.Log($"[UIManager] 找到 Camera.main: {mainCamera.name}");
            }
            
            if (mainCamera != null)
            {
                canvasComponent.worldCamera = mainCamera;
            }
        }
        else
        {
            Debug.Log($"[UIManager] Canvas renderMode 不是 ScreenSpaceCamera，当前模式: {canvasComponent.renderMode}");
        }
    }

    public void ShowPanel<T>(string panelName,string loadPath, E_UI_Layer layer, UnityAction<T> callBack) where T : BasePanel
    {
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
        if (panelDic.ContainsKey(panelName))
        {
            // 【修复】如果面板已存在，也需要完成任务，避免进度条卡住
            if (progressManager.GetTaskProgress(uiTaskID) >= 0)
            {
                // 任务已注册，直接完成它
                progressManager.CompleteTask(uiTaskID);
            }
            
            panelDic[panelName].ShowMe();
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

                Transform father = null;
                switch (layer)
                {
                    case E_UI_Layer.Bottom:
                        father = Bottom;
                        break;
                    case E_UI_Layer.Middle:
                        father = Middle;
                        break;
                    case E_UI_Layer.Right:
                        father = Right;
                        break;
                    case E_UI_Layer.Left:
                        father = Left;
                        break;
                    case E_UI_Layer.Top:
                        father = Top;
                        break;
                    case E_UI_Layer.System:
                        father = System;
                        break;
                }

                // 检查UI层级是否存在
                if (father == null)
                {
                    Debug.LogError($"[UIManager] UI层级 {layer} 未找到，请检查Canvas预制体结构");
                    GameObject.Destroy(obj);
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
    public void HidePanel(string panelName)
    {
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

    public T GetPanel<T>(string panelName) where T : BasePanel
    {
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