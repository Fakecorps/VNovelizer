using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using VNovelizer.Core.Commands;
using UnityEngine.SceneManagement;
using UnityEngine.Events;
using VNovelizer.Core.API; // 引用 API 以便调用 ClearAllEffects
using VNovelizer.Core.Localization;
using VNovelizer.Core;
using VNovelizer.Core.Diagnostics;
using VNovelizer.Core.Compat;
using VNovelizer.Core.Theater;

/// <summary>
/// 视觉小说核心管理器 (终极预演版)
/// </summary>
public class VNManager : BaseManager<VNManager>
{
    // === 核心数据 ===
    public List<StoryLine> StoryLines { get; private set; } = new List<StoryLine>();
    public Dictionary<string, int> LineIDIndexMap { get; private set; } = new Dictionary<string, int>();

    // === [Flag 扩展] 快进预演跳转请求 ===
    // 由 jump/jumpif/jumpifnot 的 Simulate 写入目标行索引，FastForwardToLine 在每行模拟后消费
    public int? PendingJumpIndex { get; set; }
    // 由 loadscript/loadscriptif/loadscriptifnot 的 Simulate 写入 (剧本名, 起始行ID)，快进循环据此切换数据源
    public (string scriptName, string startID)? PendingScriptSwitch { get; set; }

    // 当前行索引
    public int CurrentLineIndex { get; set; } = -1;

    // --- 状态变量 ---
    private StoryLine lastLine = null;
    private string currentBG = null;
    private string currentBGM = null;
    private string currentScriptName;
    private Dictionary<string, string> currentCharacters = new Dictionary<string, string>();
    private Dictionary<string, float> currentCharactersScaleX = new Dictionary<string, float>();
    /// <summary>读档后首帧播放：CSV 立绘列为空时，使用存档恢复的 currentCharacters，避免误清空槽位。</summary>
    private bool _usePersistedCharacterSlotsWhenCsvCharCellsEmpty;

    private readonly Dictionary<string, string> _dialogueEventScratch = new Dictionary<string, string>(2);
    private readonly Dictionary<string, string> _headProfileEventScratch = new Dictionary<string, string>(2);

    // 【新增】特效状态追踪
    private HashSet<string> activeEffects = new HashSet<string>();
    
    //【26-3-19新增】游戏界面加载回调
    private bool isGameplayPanelLoadCallbackFired = false;

    // 游戏状态
    private bool isAutoPlaying = false;
    private bool isSkipping = false;
    private bool isTextDisplaying = false;
    
    // 【新增】回放模式相关变量
    private bool isReplayMode = false;
    private string replayEndLineID = "";
    private bool wasMainMenuVisibleBeforeReplay = false; // 记录回放前主菜单是否可见

    // 启动参数（StartGame → RunGameLogic 之间传递）
    private string pendingScriptName;
    private string pendingLineID;
    private SaveData currentLoadingSaveData; // 当前正在加载的存档数据
    private int currentLoadingTargetIndex;   // 当前正在加载的目标行索引
    private bool isListeningSceneLoad = false;
    private UnityAction onGameStartedCallback; // 游戏启动完成后的回调

    // 配置
    private bool isVoiceEnabled = true;
    private bool isTextSpeedEnabled = true;

    // === 自动存档 ===
    private int autoSaveLineCounter = 0;        // 行数计数器（每 N 行触发）
    private Coroutine _autoSaveCoroutine;       // 防重入：上一次自动保存协程未结束时跳过新触发
    private static bool _autoSaveConfigEnsured = false; // 是否已从 SaveLoadPanel 预制体加载过配置

    // 协程
    private Coroutine _flowCoroutine;
    private Coroutine _autoPlayCoroutine;

    //(3-29)文本行间转场
    private bool _advanceAfterCommandsRequested = false;

    // [Confirm 出口] 当前行 @Confirm: 出口段是否已被消费（防止出口被打断后重复执行；进入新行时复位）
    private bool _confirmExitConsumed;

    public VNManager()
    {
        if (!isListeningSceneLoad)
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            isListeningSceneLoad = true;
        }
    }

    /// <summary>
    /// 启动游戏（场景无关：在当前场景直接开始，不切换场景）。
    /// VN 层自带自举（剧场相机/EventSystem/AudioListener/BGM 音源/转场根对象均按需自动创建），
    /// 任意场景调用即可——引擎根对象自行 Root 化并跨场景常驻。
    /// </summary>
    /// <param name="scriptFileName">剧本文件名</param>
    /// <param name="startLineID">起始行ID（可选）</param>
    /// <param name="onGameStarted">游戏启动完成后的回调函数（可选）</param>
    public void StartGame(string scriptFileName, string startLineID = "", UnityAction onGameStarted = null)
    {
        this.pendingScriptName = scriptFileName;
        this.pendingLineID = startLineID;
        this.onGameStartedCallback = onGameStarted;

        // 确保UIManager已初始化，这样会检查并创建Canvas
        UIManager.GetInstance().Init();

        // 直接运行游戏逻辑，不切换场景
        RunGameLogic();
    }

    /// <summary>
    /// 在当前场景中启动游戏（不切换场景）——StartGame 的场景无关化别名（历史 API 兼容保留）。
    /// </summary>
    public void StartGameOnScene(string scriptFileName, string startLineID = "", UnityAction onGameStarted = null)
    {
        StartGame(scriptFileName, startLineID, onGameStarted);
    }

    /// <summary>
    /// 场景切换后恢复演出界面。
    ///
    /// 引擎是场景无关的：剧场根、EventSystem、常驻面板跨场景存活，但普通面板
    /// （含 VNGameplayPanel）会被 UIManager.HideAll 在场景加载时销毁。
    /// 用户经 loadscene 命令换场景后若不重建，界面会凭空消失且无法推进。
    ///
    /// 延后一帧执行：UIManager 也订阅了 sceneLoaded，两个回调的先后顺序取决于
    /// 订阅时机（不可依赖），延后一帧可确保在 HideAll 之后重建。
    /// </summary>
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // 未在演出中（未加载剧本 / 已回主菜单）：无需恢复
        if (StoryLines.Count == 0 || CurrentLineIndex < 0) return;

        MonoManager.GetInstance().StartCoroutine(RestoreGameplayUIAfterSceneLoad());
    }

    private IEnumerator RestoreGameplayUIAfterSceneLoad()
    {
        yield return null; // 等 UIManager.HideAll 完成

        if (StoryLines.Count == 0 || CurrentLineIndex < 0) yield break;
        if (UIManager.GetInstance().IsShown<VNGameplayPanel>()) yield break;

        UIManager.GetInstance().Show<VNGameplayPanel>(panel =>
        {
            // 重新广播当前行的视听状态（剧场演员仍在，但对话框是新实例）
            if (CurrentLineIndex >= 0 && CurrentLineIndex < StoryLines.Count)
            {
                var line = StoryLines[CurrentLineIndex];
                UpdateDialogue(line, ResolveLine(line));
            }
        });
    }

    private void RunGameLogic()
    {
        VNDebug.LogVerbose($"[VNManager] RunGameLogic 开始。剧本: {pendingScriptName}, 目标行: {pendingLineID}");

        InitializeManager();

        // [Flag 扩展] 新游戏：Save 作用域 Flag 复位为注册表默认值（Global 不动；兼容模式无操作）
        FlagService.GetInstance().ResetSaveScope();

        // 【新增】显示加载进度面板
        ShowLoadingPanelAndStartGame();
    }
    
    /// <summary>
    /// 显示加载面板并开始游戏加载流程
    /// </summary>
    private void ShowLoadingPanelAndStartGame()
    {
        // 1. 显示加载进度面板
        UIManager.GetInstance().Show<LoadingProgressPanel>(
            (loadingPanel) =>
            {
                // 加载面板显示成功后，开始加载流程
                StartGameLoading();
            }
        );
    }
    
    /// <summary>
    /// 开始游戏加载流程（带进度跟踪）
    /// </summary>
    private void StartGameLoading()
    {
        
        isGameplayPanelLoadCallbackFired = false;
        
        LoadingProgressManager progressManager = LoadingProgressManager.GetInstance();
        
        // 注册加载任务
        string scriptTaskID = "load_script";
        string uiTaskID = "ui_VNGameplayPanel"; // 使用UIManager自动注册的任务ID
        
        progressManager.RegisterTask(scriptTaskID, $"加载剧本: {pendingScriptName}", 0.4f); // 权重40%
        // 先注册UI任务（如果还没注册），设置正确的权重
        // UIManager在ShowPanel时会检查任务是否已存在，如果存在就不重复注册
        if (progressManager.GetTaskProgress(uiTaskID) < 0)
        {
            progressManager.RegisterTask(uiTaskID, "加载游戏界面", 0.6f); // 权重60%
        }
        else
        {
            // 如果已经注册，更新权重和名称
            var uiTask = progressManager.GetTask(uiTaskID);
            if (uiTask != null)
            {
                uiTask.Weight = 0.6f;
                uiTask.TaskName = "加载游戏界面";
                // 触发进度更新以刷新显示
                progressManager.UpdateTaskProgress(uiTaskID, uiTask.Progress);
            }
        }
        
        // 监听所有任务完成
        progressManager.OnAllTasksCompleted += OnGameLoadingCompleted;
        
        // 使用协程来加载，让进度更新有时间刷新UI
        MonoManager.GetInstance().StartCoroutine(LoadScriptWithProgress(scriptTaskID));
    }
    
   
    
    /// <summary>
    /// 游戏加载完成回调
    /// </summary>
    private void OnGameLoadingCompleted()
    {
        LoadingProgressManager progressManager = LoadingProgressManager.GetInstance();
        progressManager.OnAllTasksCompleted -= OnGameLoadingCompleted;

        // 不 ClearAllTasks：等待任务队列自然完成后再进入正式演出
        MonoManager.GetInstance().StartCoroutine(WaitLoadingQueueThenStartGameplay());
    }

    private void InitializeManager()
    {
        GlobalDataManager.GetInstance().Init();
        UIManager.GetInstance().Init();
        CharacterResManager.GetInstance().Init();
        ResourcesManager.GetInstance();
        EventCenter.GetInstance();
        MonoManager.GetInstance();
        MusicManager.GetInstance();
        VoiceManager.GetInstance();
        SaveManager.GetInstance();
        TheaterManager.GetInstance().Init(); // 剧场层（场景相机 + 演员容器）

        // 【Bug修复】清理音效列表，防止场景切换时引用已销毁的对象
        MusicManager.GetInstance().ClearAllSFX();

        CommandManager.GetInstance().Init();
        EventCenter.GetInstance().AddEventListener(VNGameEvents.TypingFinished, OnTypingFinished);
    }

    private void OnTypingFinished()
    {
        isTextDisplaying = false;
        CheckAndTriggerAutoPlay();
    }

    private void ResetState()
    {
        currentBG = "";
        currentBGM = "";
        // 【BGM 修复】不再在此停止 BGM：
        // MusicManager.PlayBGM 自带同名幂等检查（同名跳过播放），FastForwardToLine 结束时会
        // 按预演结果按需 PlayBGM/StopBGM。若在此先 Stop，currentPlayingBGM 被清空导致幂等失效，
        // 同名 BGM 会被"先停再从头播"——jump/jumpif 同剧本跳转时 BGM 无意义重启。
        currentCharacters.Clear();
        activeEffects.Clear();
        VNAPI.ClearAllEffects(); // 物理清空特效
        currentCharactersScaleX.Clear();
        isVoiceEnabled = true;
        lastLine = null;
        autoSaveLineCounter = 0; // 新剧本/预演重置行数计数，避免跨剧本残留计数
    }
    
    /// <summary>
    /// 清空历史记录（用于新游戏或跨剧本加载）
    /// </summary>
    private void ClearHistoryLog()
    {
        GlobalDataManager.GetInstance().ClearHistoryLog();
        VNDebug.LogVerbose("[VNManager] 已清空历史记录");
    }

    /// <summary>
    /// 退出演出、返回主菜单（场景无关：主菜单是面板，不切换场景）。
    ///
    /// 这是唯一的"回主菜单"入口——PausePanel 的退出按钮与 exit() 命令都经此，
    /// 保证清理动作不遗漏。此前两条路径各写一份，都漏掉了剧场清场，
    /// 导致回到主菜单后立绘与背景仍留在屏幕上。
    /// </summary>
    public void ReturnToMainMenu()
    {
        // 1. 中断演出：协程、命令、动画、特效、对象池
        if (_flowCoroutine != null)
        {
            MonoManager.GetInstance().StopCoroutine(_flowCoroutine);
            _flowCoroutine = null;
        }
        if (_autoPlayCoroutine != null)
        {
            MonoManager.GetInstance().StopCoroutine(_autoPlayCoroutine);
            _autoPlayCoroutine = null;
        }
        CommandManager.GetInstance().InterruptAll();
        AnimationCompat.StopAll();
        VNAPI.ClearAllEffects();
        PoolManager.GetInstance().Clear();

        // 2. 停止音频（BGM / SFX / 语音）
        MusicManager.GetInstance().StopBGM();
        MusicManager.GetInstance().ClearAllSFX();
        VoiceManager.GetInstance()?.StopVoice();

        // 3. 清空剧场（立绘、背景、相机）——否则主菜单会叠在演出画面上
        TheaterManager.GetInstance().ClearTheater();

        // 4. 复位演出状态（含 Time.timeScale，防止快进中退出后主菜单卡在加速状态）
        ResetState();
        isAutoPlaying = false;
        isSkipping = false;
        isTextDisplaying = false;
        isReplayMode = false;
        replayEndLineID = "";
        CurrentLineIndex = -1;
        Time.timeScale = 1f;

        // 5. 恢复状态机（暂停/设置等嵌套面板栈一并回到 Gameplay 基线）
        var stateManager = GameStateManager.GetInstance();
        if (stateManager != null && stateManager.CurrentState != GameState.Gameplay)
            stateManager.SetState(GameState.Gameplay);

        // 6. 切换界面
        UIManager.GetInstance().HidePanel("PausePanel");
        UIManager.GetInstance().HidePanel("VNGameplayPanel");
        UIManager.GetInstance().Show<MainMenuPanel>();

        Debug.Log("[VNManager] 已退出演出并返回主菜单");
    }

    /// <summary>
    /// 全量状态预演 (核心逻辑)
    /// </summary>
    /// <summary>
    /// 快进到目标行
    /// </summary>
    /// <param name="targetIndex">目标行索引</param>
    /// <param name="ignoreChoice">是否忽略 choice 命令（用于 jump 命令强制跳转）</param>
    /// <returns>如果遇到 choice 命令返回 true，否则返回 false</returns>
    public bool FastForwardToLine(int targetIndex, bool ignoreChoice = false, HashSet<string> visited = null)
    {
        ResetState();
        VNAPI.ClearAllEffects(); // 物理清空
        activeEffects.Clear();
        if (targetIndex <= 0)
        {
            // 无预演内容：按"重建结果为空"处理 BGM（等价于旧版 ResetState 内 StopBGM 的语义，
            // 供 loadscript 未指定起始行时停止上一个剧本的残留 BGM）
            MusicManager.GetInstance().StopBGM();
            return false;
        }

        // [Flag 扩展] 跳转防死循环：记录已进入的 (剧本, 行号)，跨递归共享
        if (visited == null) visited = new HashSet<string>();

        bool encounteredChoice = false;

        // 模拟运行
        for (int i = 0; i < targetIndex; i++)
        {
            if (i >= StoryLines.Count) break;
            StoryLine line = StoryLines[i];

            // [Flag 扩展] 同一位置二次进入 = 跳转环（jump/jumpif/loadscript 成环），报错终止预演
            if (!visited.Add(currentScriptName + "#" + i))
            {
                Debug.LogError($"[VNManager] 快进检测到跳转环：剧本 {currentScriptName} 第 {i} 行 (ID: {line.ID}) 被重复进入，已中止预演。请检查 jump/jumpif/loadscript 是否构成循环。");
                break;
            }

            // 【修复】检查是否包含 choice 命令，如果包含则停止快进（除非 ignoreChoice 为 true）
            if (!ignoreChoice && !string.IsNullOrEmpty(line.Command) && ContainsChoiceCommand(line.Command))
            {
                // 遇到选项命令，停止快进，设置当前行索引为包含 choice 的行
                CurrentLineIndex = i;
                encounteredChoice = true;
                VNDebug.LogVerbose($"[VNManager] 快进过程中遇到选项命令，停止在第 {i} 行 (ID: {line.ID})");
                
                // 先应用当前行的状态（背景、立绘、BGM等）
                if (!string.IsNullOrEmpty(line.Background)) currentBG = line.Background;
                if (!string.IsNullOrEmpty(line.BGM))
                {
                    if (line.BGM == "stop") currentBGM = "";
                    else if (line.BGM != "pause" && line.BGM != "resume") currentBGM = line.BGM;
                }
                SimulateCharacterUpdate("Left", line.CharLeft);
                SimulateCharacterUpdate("MidLeft", line.CharMid_Left);
                SimulateCharacterUpdate("Mid", line.CharMid);
                SimulateCharacterUpdate("MidRight", line.CharMid_Right);
                SimulateCharacterUpdate("Right", line.CharRight);
                if (line.Voice == "false") isVoiceEnabled = false;
                else if (!string.IsNullOrEmpty(line.Voice)) isVoiceEnabled = true;
                lastLine = line;
                
                // 先应用其他命令（不包括 choice）
                // [Flag 扩展] 模拟前复位跳转请求，模拟后按需消费
                PendingJumpIndex = null;
                PendingScriptSwitch = null;
                string otherCommands = ExtractNonChoiceCommands(line.Command);
                if (!string.IsNullOrEmpty(otherCommands))
                {
                    CommandManager.GetInstance().SimulateCommands(otherCommands);
                }

                // [Flag 扩展] choice 行的其它命令触发跳转/跨剧本切换时，跳转优先于停止
                if (PendingScriptSwitch != null && TryApplyPendingScriptSwitch(visited, out bool switchResult))
                {
                    return switchResult;
                }
                if (PendingJumpIndex != null)
                {
                    int jumpTarget = PendingJumpIndex.Value;
                    PendingJumpIndex = null;
                    if (jumpTarget >= 0 && jumpTarget < StoryLines.Count)
                    {
                        encounteredChoice = false;
                        i = jumpTarget - 1;
                        lastLine = line;
                        continue;
                    }
                    Debug.LogError($"[VNManager] 快进中跳转目标越界: index={jumpTarget}");
                }

                // 停止快进循环
                break;
            }

            // 1. 基础属性
            if (!string.IsNullOrEmpty(line.Background)) currentBG = line.Background;

            // 2. BGM (只记录状态，不播放)
            if (!string.IsNullOrEmpty(line.BGM))
            {
                if (line.BGM == "stop") currentBGM = "";
                else if (line.BGM != "pause" && line.BGM != "resume") currentBGM = line.BGM;
            }

            // 3. 立绘
            SimulateCharacterUpdate("Left", line.CharLeft);
            SimulateCharacterUpdate("MidLeft", line.CharMid_Left);
            SimulateCharacterUpdate("Mid", line.CharMid);
            SimulateCharacterUpdate("MidRight", line.CharMid_Right);
            SimulateCharacterUpdate("Right", line.CharRight);

            // 4. 语音
            if (line.Voice == "false") isVoiceEnabled = false;
            else if (!string.IsNullOrEmpty(line.Voice)) isVoiceEnabled = true;

            // 5. Command 模拟 (特效、Flags 等)
            // [Flag 扩展] 模拟前复位跳转请求；模拟后消费 jump/jumpif/loadscriptif 产生的快进跳转
            PendingJumpIndex = null;
            PendingScriptSwitch = null;
            if (!string.IsNullOrEmpty(line.Command))
            {
                CommandManager.GetInstance().SimulateCommands(line.Command);
            }

            // [Flag 扩展] 跨剧本切换优先：切换数据源后重定向预演（内层递归完成后其后处理已执行）
            if (PendingScriptSwitch != null && TryApplyPendingScriptSwitch(visited, out bool scriptSwitchResult))
            {
                return scriptSwitchResult;
            }

            // [Flag 扩展] 本剧本跳转：快进指针指向目标行（目标行仍会按需模拟）
            if (PendingJumpIndex != null)
            {
                int jumpIndex = PendingJumpIndex.Value;
                PendingJumpIndex = null;
                if (jumpIndex < 0 || jumpIndex >= StoryLines.Count)
                {
                    Debug.LogError($"[VNManager] 快进中跳转目标越界: index={jumpIndex}");
                }
                else
                {
                    i = jumpIndex - 1; // for 自增后指向目标行
                }
                lastLine = line;
                continue;
            }

            // [Confirm 出口] 快进假设用户已点击本行：enter 段未产生跳转时模拟出口段并消费其跳转。
            // （enter 段已跳转的行不视为"停留"，出口段跳过——与正向播放语义一致；
            //  targetIndex 行本身不在循环内，其出口段不会被模拟，读档后正确处于"等点击"状态）
            if (!string.IsNullOrEmpty(line.ConfirmCommands))
            {
                CommandManager.GetInstance().SimulateCommands(line.ConfirmCommands);

                if (PendingScriptSwitch != null && TryApplyPendingScriptSwitch(visited, out bool confirmSwitchResult))
                {
                    return confirmSwitchResult;
                }

                if (PendingJumpIndex != null)
                {
                    int confirmJumpIndex = PendingJumpIndex.Value;
                    PendingJumpIndex = null;
                    if (confirmJumpIndex < 0 || confirmJumpIndex >= StoryLines.Count)
                    {
                        Debug.LogError($"[VNManager] 快进中出口跳转目标越界: index={confirmJumpIndex}");
                    }
                    else
                    {
                        i = confirmJumpIndex - 1;
                    }
                }
            }

            lastLine = line;
        }

        // 预演结束，应用 BGM 和 特效（只有完全快进到目标行时才应用）
        // 注意：如果遇到 choice 命令，CurrentLineIndex 已经被设置为包含 choice 的行，此时不应用特效
        if (!encounteredChoice)
        {
            // 没有遇到 choice，正常快进到目标行，应用 BGM 和特效
            if (!string.IsNullOrEmpty(currentBGM))
                MusicManager.GetInstance().PlayBGM(currentBGM);
            else
                MusicManager.GetInstance().StopBGM();
             
            List<string> effectsToRestore = new List<string>(activeEffects);

            foreach (var effect in effectsToRestore)
            {
                RestoreEffect(effect);
            }
        }
        else
        {
            // 遇到 choice，只应用 BGM（因为已经处理了当前行的状态）
            if (!string.IsNullOrEmpty(currentBGM))
                MusicManager.GetInstance().PlayBGM(currentBGM);
            else
                MusicManager.GetInstance().StopBGM();
        }
        
        return encounteredChoice;
    }

    /// <summary>
    /// [Flag 扩展] 消费快进中的跨剧本切换请求（loadscript/loadscriptif/loadscriptifnot 的 Simulate 产生）。
    /// 切换成功时重定向预演到新剧本并返回 true（result 为内层递归结果）；请求无效/加载失败返回 false，调用方按原剧本继续。
    /// </summary>
    private bool TryApplyPendingScriptSwitch(HashSet<string> visited, out bool result)
    {
        result = false;
        if (PendingScriptSwitch == null) return false;

        var sw = PendingScriptSwitch.Value;
        PendingScriptSwitch = null;

        var scriptData = ScriptParser.Parse(sw.scriptName);
        if (scriptData == null || scriptData.Lines.Count == 0)
        {
            Debug.LogError($"[VNManager] 快进中加载剧本失败: {sw.scriptName}，按原剧本继续预演");
            return false;
        }

        SetScriptData(scriptData.Lines, scriptData.IDMap, sw.scriptName);

        int startIdx = 0;
        if (!string.IsNullOrEmpty(sw.startID))
        {
            if (!scriptData.IDMap.TryGetValue(sw.startID, out startIdx))
            {
                Debug.LogWarning($"[VNManager] 快进中 loadscript 起始行 {sw.startID} 在剧本 {sw.scriptName} 中不存在，将从第 0 行开始");
                startIdx = 0;
            }
        }

        // 递归预演新剧本（共享 visited 防跨剧本环 A→B→A；内层完成后其 BGM/特效后处理已执行）
        result = FastForwardToLine(startIdx, false, visited);
        return true;
    }

    /// <summary>
    /// 检查命令字符串中是否包含 choice 命令
    /// </summary>
    private bool ContainsChoiceCommand(string commandString)
    {
        if (string.IsNullOrEmpty(commandString)) return false;
        
        // 检查是否包含 choice( 命令（不区分大小写）
        string lowerCommand = commandString.ToLower();
        return lowerCommand.Contains("choice(");
    }

    /// <summary>
    /// 提取除了 choice 之外的其他命令
    /// </summary>
    private string ExtractNonChoiceCommands(string commandString)
    {
        if (string.IsNullOrEmpty(commandString)) return "";

        // 使用链式词法器切分（引号/括号感知），同时兼容旧 & 语法与链式语法（-> / []）
        // 注意：过滤后用 & 重连——该结果仅用于 Simulate（预演不关心时序，只关心最终状态）
        var tokens = VNovelizer.Core.Commands.Chain.ChainLexer.Tokenize(commandString, null);
        var nonChoiceCommands = new System.Collections.Generic.List<string>();

        foreach (var token in tokens)
        {
            if (token.Type != VNovelizer.Core.Commands.Chain.ChainTokenType.Command)
                continue; // 跳过 & -> [ ] 等操作符

            string text = token.Text;
            int startIndex = text.IndexOf('(');
            string cmdName = (startIndex > 0
                ? text.Substring(0, startIndex)
                : text).Trim().ToLower();

            if (cmdName != "choice")
                nonChoiceCommands.Add(text);
        }

        return string.Join("&", nonChoiceCommands);
    }

    private void SimulateCharacterUpdate(string pos, string data)
    {
        string normalizedPos = pos;
        string normalizedPosCode = NormalizePositionCode(pos);

        // 空槽：清除该位置（与运行时「空=隐藏」一致，避免快进后仍保留旧立绘状态）
        if (string.IsNullOrEmpty(data))
        {
            if (currentCharacters.ContainsKey(normalizedPos)) currentCharacters.Remove(normalizedPos);
            if (currentCharactersScaleX.ContainsKey(normalizedPosCode)) currentCharactersScaleX.Remove(normalizedPosCode);
            return;
        }

        if (data == "hide")
        {
            if (currentCharacters.ContainsKey(normalizedPos)) currentCharacters.Remove(normalizedPos);
            if (currentCharactersScaleX.ContainsKey(normalizedPosCode)) currentCharactersScaleX.Remove(normalizedPosCode);
        }
        else
        {
            // 如果是新角色，初始化翻转状态为默认值（朝右）
            if (!currentCharacters.ContainsKey(normalizedPos))
            {
                currentCharactersScaleX[normalizedPosCode] = 1f;
            }
            currentCharacters[normalizedPos] = data;
        }
    }

    /// <summary>
    /// 位置代码归一化（L/ML/M/MR/R）。
    ///
    /// 委托给 <see cref="TheaterManager.NormalizePosCode"/>，保证"槽位别名表"只有一份——
    /// 此前 VNManager 与 TheaterManager 各维护一份，新增槽位时极易漏改其中之一。
    /// 差异保留：本方法对未知输入返回原值（VNManager 的字典键容忍全名），
    /// 而剧场层返回 null（命令层需要据此报错）。
    /// </summary>
    private string NormalizePositionCode(string pos)
    {
        if (string.IsNullOrEmpty(pos)) return pos;
        return TheaterManager.NormalizePosCode(pos) ?? pos;
    }

    // 特效状态管理 API
    public void RegisterEffect(string name) { if (!activeEffects.Contains(name)) activeEffects.Add(name); }
    public void UnregisterEffect(string name) { if (activeEffects.Contains(name)) activeEffects.Remove(name); }
    public List<string> GetActiveEffects() { return new List<string>(activeEffects); }

    // 恢复特效 (物理生成)
    private void RestoreEffect(string effectName)
    {
        string commandString = $"playparticle({effectName})";
        CommandManager.GetInstance().ExecuteCommand(commandString);

        VNDebug.LogVerbose($"[VNManager] 自动恢复特效: {commandString}");
    }

    public void SetScriptData(List<StoryLine> lines, Dictionary<string, int> idMap, string scriptName)
    {
        this.StoryLines = lines;
        this.LineIDIndexMap = idMap;
        this.CurrentLineIndex = 0;
        this.lastLine = null;
        this.currentScriptName = scriptName;
    }

    public string GetCurrentScriptName()
    {
        return currentScriptName;
    }

    /// <summary>
    /// 继续游戏（加载存档，场景无关：当前场景直接恢复，不切换场景）
    /// </summary>
    public void ContinueGame(SaveData saveData)
    {
        // 确保引擎 UI 自举（任意场景恢复存档）
        UIManager.GetInstance().Init();
        ContinueGameInternal(saveData);
    }
    
    /// <summary>
    /// 继续游戏的内部实现（场景已准备好）
    /// </summary>
    private void ContinueGameInternal(SaveData saveData)
    {
        // 【新增】显示加载进度面板
        ShowLoadingPanelAndContinueGame(saveData);
    }
    
    /// <summary>
    /// 显示加载面板并继续游戏
    /// </summary>
    private void ShowLoadingPanelAndContinueGame(SaveData saveData)
    {
        // 1. 显示加载进度面板
        UIManager.GetInstance().Show<LoadingProgressPanel>(
            (loadingPanel) =>
            {
                // 加载面板显示成功后，开始加载流程
                ContinueGameLoading(saveData);
            }
        );
    }
    
    /// <summary>
    /// 继续游戏的加载流程（带进度跟踪）
    /// </summary>
    private void ContinueGameLoading(SaveData saveData)
    {
        isGameplayPanelLoadCallbackFired = false;
        
        LoadingProgressManager progressManager = LoadingProgressManager.GetInstance();
        
        // 注册加载任务
        string scriptTaskID = "load_script_continue";
        string uiTaskID = "ui_VNGameplayPanel"; // 使用UIManager自动注册的任务ID
        
        progressManager.RegisterTask(scriptTaskID, $"加载存档: {saveData.ScriptFileName}", 0.4f); // 权重40%
        // 先注册UI任务（如果还没注册），设置正确的权重
        if (progressManager.GetTaskProgress(uiTaskID) < 0)
        {
            progressManager.RegisterTask(uiTaskID, "加载游戏界面", 0.6f); // 权重60%
        }
        else
        {
            // 如果已经注册，更新权重和名称
            var uiTask = progressManager.GetTask(uiTaskID);
            if (uiTask != null)
            {
                uiTask.Weight = 0.6f;
                uiTask.TaskName = "加载游戏界面";
                // 触发进度更新以刷新显示
                progressManager.UpdateTaskProgress(uiTaskID, uiTask.Progress);
            }
        }
        
        // 监听所有任务完成
        progressManager.OnAllTasksCompleted += OnContinueGameLoadingCompleted;
        
        // 使用协程来加载，让进度更新有时间刷新UI
        MonoManager.GetInstance().StartCoroutine(LoadScriptForContinueWithProgress(scriptTaskID, saveData));
    }
    

    
    /// <summary>
    /// 继续游戏加载完成回调
    /// </summary>
    private void OnContinueGameLoadingCompleted()
    {
        LoadingProgressManager progressManager = LoadingProgressManager.GetInstance();
        progressManager.OnAllTasksCompleted -= OnContinueGameLoadingCompleted;
        
        // 隐藏加载面板
        // UIManager.GetInstance().HidePanel("LoadingProgressPanel");
        
        // 清理加载任务
        // progressManager.ClearAllTasks();
        
        // 延迟一帧，确保UI完全初始化
        MonoManager.GetInstance().StartCoroutine(WaitLoadingQueueThenContinueGameplay());
    }
    
 
    
    /// <summary>
    /// 从存档恢复游戏状态（UI已准备好）
    /// </summary>
    private void RestoreGameStateFromSave(SaveData saveData, int targetIndex)
    {
        // 清理UI现场
        VNAPI.ClearAllEffects();
        EventCenter.GetInstance().EventTrigger(VNGameEvents.HideCharacter, "Left");
        EventCenter.GetInstance().EventTrigger(VNGameEvents.HideCharacter, "MidLeft");
        EventCenter.GetInstance().EventTrigger(VNGameEvents.HideCharacter, "Mid");
        EventCenter.GetInstance().EventTrigger(VNGameEvents.HideCharacter, "MidRight");
        EventCenter.GetInstance().EventTrigger(VNGameEvents.HideCharacter, "Right");

        // 【修复】如果目标行索引大于0，需要预演到该位置，如果遇到 choice 命令则停止
        bool encounteredChoice = false;
        if (targetIndex > 0)
        {
            VNDebug.LogVerbose($"[VNManager] 从存档恢复，预演至索引: {targetIndex}");
            encounteredChoice = FastForwardToLine(targetIndex);
        }
        
        // 设置当前行（如果遇到 choice，FastForwardToLine 已经设置了 CurrentLineIndex，不需要覆盖）
        if (!encounteredChoice && targetIndex >= 0)
        {
            CurrentLineIndex = targetIndex;
        }

        // 恢复背景
        if (!string.IsNullOrEmpty(currentBG) && currentBG != "hide" && currentBG != "black")
        {
            EventCenter.GetInstance().EventTrigger(VNGameEvents.ChangeBackground, currentBG);
        }

        // 恢复BGM
        if (!string.IsNullOrEmpty(currentBGM))
        {
            MusicManager.GetInstance().PlayBGM(currentBGM);
        }

        // 恢复立绘
        Dictionary<string, string> charactersToRestore = new Dictionary<string, string>(saveData.Characters);
        currentCharacters.Clear();
        foreach (var kvp in charactersToRestore)
        {
            UpdateCharacter(kvp.Key, kvp.Value);
        }

        // 恢复特效（在UI准备好后）
        if (saveData.ActiveEffects != null)
        {
            foreach (var effect in saveData.ActiveEffects)
            {
                activeEffects.Add(effect);
                RestoreEffect(effect);
            }
        }

        // 设置当前行索引并播放（首帧允许用存档槽位补全 CSV 空立绘，与「无行际继承」不冲突：存档是显式快照）
        CurrentLineIndex = targetIndex;
        _usePersistedCharacterSlotsWhenCsvCharCellsEmpty = true;
        PlayCurrentLine();
    }

    private void PlayCurrentLine()
    {
        if (CurrentLineIndex < 0 || CurrentLineIndex >= StoryLines.Count)
        {
            _usePersistedCharacterSlotsWhenCsvCharCellsEmpty = false;

            if (isReplayMode)
            {
                EndReplay();
            }
            return;
        }

        if (_autoPlayCoroutine != null)
        {
            MonoManager.GetInstance().StopCoroutine(_autoPlayCoroutine);
            _autoPlayCoroutine = null;
        }


        var gameplayPanel = UIManager.GetInstance().Get<VNGameplayPanel>();
        if (gameplayPanel != null)
        {
            gameplayPanel.RestoreDefaultTextProperties();
        }

        StoryLine currentLine = StoryLines[CurrentLineIndex];

        // [Confirm 出口] 进入新行：出口段重新可用
        _confirmExitConsumed = false;

        var resolved = ResolveLine(currentLine);
        lastLine = currentLine;

        UpdateVisualState(resolved);
        UpdateCharacterSlots(currentLine);
        UpdateAudioState(resolved);
        UpdateDialogue(currentLine, resolved);

        GlobalDataManager.GetInstance().AddReadLineID(currentLine.ID);

        if (!string.IsNullOrEmpty(currentLine.Command))
        {
            ClearAdvanceAfterCommandsRequest();
            _flowCoroutine = MonoManager.GetInstance().StartCoroutine(ExecuteActionsAndContinue(currentLine.Command));
        }
        else
        {
            CheckAndTriggerAutoPlay();
        }

        _usePersistedCharacterSlotsWhenCsvCharCellsEmpty = false;

        // 自动存档：行数计数（快进预演不经过此处，天然不重复计数）
        TickAutoSaveOnLinePlayed();
    }

    private void CheckAndTriggerAutoPlay()
    {
        GameStateManager stateManager = GameStateManager.GetInstance();
        if (stateManager != null && stateManager.CurrentState == GameState.Choice)
        {
            // 在 Choice 状态下，等待玩家选择，不触发自动播放
            return;
        }
        
        // 检查打字机效果是否完成
        bool isTextTyping = false;
        var gameplayPanel = UIManager.GetInstance().Get<VNGameplayPanel>();
        if (gameplayPanel != null)
        {
            isTextTyping = gameplayPanel.IsTextTyping();
        }

        // 检查语音是否正在播放
        bool isVoicePlaying = false;
        if (VoiceManager.GetInstance() != null)
        {
            isVoicePlaying = VoiceManager.GetInstance().IsVoicePlaying();
        }
        
        // 只有当打字机效果完成、语音播放完毕、命令执行完毕、流程协程完毕时，才能触发自动播放
        bool isBusy = isTextDisplaying || isTextTyping || isVoicePlaying || 
                      CommandManager.GetInstance().IsRunning || _flowCoroutine != null;

        if (isAutoPlaying && !isBusy)
        {
            float delay = GlobalDataManager.GetInstance().GetGlobalData().AutoSpeed;
            VNDebug.LogVerbose($"[VNManager] 自动播放触发 - 延迟时间: {delay}秒");
            _autoPlayCoroutine = MonoManager.GetInstance().StartCoroutine(AutoPlayCountdown(delay));
        }
    }

    public void CheckAutoPlay()
    { 
        CheckAndTriggerAutoPlay();
    }
    

    public void NextLine()
    {
        AdvanceToNextLine(false);
    }

    public void NextLineWithoutAnimation()
    {
        AdvanceToNextLine(true);
    }

    private void AdvanceToNextLine(bool skipAnimations)
    {
        VNDebug.LogVerbose($"[VNManager] Max line" + StoryLines.Count);
        // 【修复】检查游戏状态，如果是 Choice 状态，不应该继续前进
        GameStateManager stateManager = GameStateManager.GetInstance();
        if (stateManager != null && stateManager.CurrentState == GameState.Choice)
        {
            // 在 Choice 状态下，等待玩家选择，不继续前进
            VNDebug.LogVerbose("[VNManager] 当前处于 Choice 状态，等待玩家选择，暂停前进");
            return;
        }
        
        bool isCmdRunning = CommandManager.GetInstance().IsRunning;
        bool isFlowRunning = _flowCoroutine != null;

        if (isCmdRunning || isFlowRunning)
        {
            if (_flowCoroutine != null)
            {
                MonoManager.GetInstance().StopCoroutine(_flowCoroutine);
                _flowCoroutine = null;
            }
            CommandManager.GetInstance().InterruptAll();
            if (!skipAnimations)
            {
                CheckAndTriggerAutoPlay();
                return;
            }
        }

        if (isTextDisplaying)
        {
            EventCenter.GetInstance().EventTrigger(VNGameEvents.DisplayAllText);

            if (!skipAnimations)
            {
                return;
            }
        }

        if (_autoPlayCoroutine != null)
        {
            MonoManager.GetInstance().StopCoroutine(_autoPlayCoroutine);
            _autoPlayCoroutine = null;
        }

        // 【新增】检查回放结束条件（在播放下一行之前检查上一行是否是结束行）
        if (isReplayMode && lastLine != null && !string.IsNullOrEmpty(replayEndLineID) && lastLine.ID == replayEndLineID)
        {
            EndReplay();
            return;
        }

        // [Confirm 出口] 本行声明了 @Confirm: 且未消费：点击不再直接推进，
        // 而是执行出口命令（出口内未产生跳转时按默认行为推进下一行）。
        // 出口执行期间再次点击会先走上方的打断逻辑（出口已标记消费，打断后下次点击按默认推进）。
        if (HasPendingConfirmExit)
        {
            if (!skipAnimations)
            {
                LaunchConfirmExit();
                return;
            }

            // skip 模式：出口段以模拟方式执行（跳转生效、演出不出），保证跳过时流程正确
            _confirmExitConsumed = true;
            CommandManager.GetInstance().SimulateCommands(lastLine.ConfirmCommands);
            if (PendingJumpIndex != null)
            {
                CurrentLineIndex = PendingJumpIndex.Value;
                PendingJumpIndex = null;
                PlayCurrentLineImmediately();
                return;
            }
            CurrentLineIndex++;
            PlayCurrentLineImmediately();
            return;
        }

        CurrentLineIndex++;

        if (skipAnimations)
        {
            PlayCurrentLineImmediately();
        }
        else
        {
            PlayCurrentLine();
        }
    }

    private void PlayCurrentLineImmediately()
    {
        if (CurrentLineIndex < 0 || CurrentLineIndex >= StoryLines.Count)
        {
            if (isReplayMode)
            {
                EndReplay();
            }
            return;
        }

        if (_autoPlayCoroutine != null)
        {
            MonoManager.GetInstance().StopCoroutine(_autoPlayCoroutine);
            _autoPlayCoroutine = null;
        }

        var gameplayPanel = UIManager.GetInstance().Get<VNGameplayPanel>();
        if (gameplayPanel != null)
        {
            gameplayPanel.RestoreDefaultTextProperties();
        }

        StoryLine currentLine = StoryLines[CurrentLineIndex];

        // [Confirm 出口] 进入新行：出口段重新可用
        _confirmExitConsumed = false;

        var resolved = ResolveLine(currentLine);
        lastLine = currentLine;

        UpdateVisualState(resolved);
        UpdateCharacterSlots(currentLine);
        UpdateAudioState(resolved);
        UpdateDialogue(currentLine, resolved);
        EventCenter.GetInstance().EventTrigger(VNGameEvents.DisplayAllText);

        GlobalDataManager.GetInstance().AddReadLineID(currentLine.ID);

        int preIndex = CurrentLineIndex;
        if (!string.IsNullOrEmpty(currentLine.Command))
        {
            CommandManager.GetInstance().SimulateCommands(currentLine.Command);
        }

        if (CurrentLineIndex != preIndex)
        {
            PlayCurrentLineImmediately();
        }
        else
        {
            CheckAndTriggerAutoPlay();
        }
    }

    /// <summary>
    /// 一行的"解析结果"：继承与自动补全后的最终取值。
    ///
    /// 为什么需要它：StoryLines 里的 StoryLine 是**共享且长期存活**的剧本数据。
    /// 旧实现把继承结果直接写回 currentLine.Background / currentLine.Voice，
    /// 等于把"当次播放时的运行时状态"永久烙进剧本行——同一行被二次经过时
    /// （jump 回跳、场景回放、读档到不同状态后再走到该行），继承来的旧背景/
    /// 旧语音路径会顶掉本应继承的新状态。解析结果与剧本数据必须分离。
    /// </summary>
    private struct ResolvedLine
    {
        public string Background;
        public string BGM;
        public string Voice;
        public string Speaker;
        public string Text;
        public string HeadProfile;
    }

    /// <summary>
    /// 计算本行的最终取值（不修改剧本数据）。
    /// - Background：空 = 继承当前状态；
    /// - BGM：按 CSV 原值（空 = 不动，由 UpdateAudioState 判定）；
    /// - Voice：空且启用语音时按 ID 自动生成路径；"false" 关闭语音开关；
    /// - Speaker / Text / HeadProfile：不继承，原样透传。
    /// </summary>
    private ResolvedLine ResolveLine(StoryLine currentLine)
    {
        var resolved = new ResolvedLine
        {
            Background = currentLine.Background,
            BGM = currentLine.BGM,
            Voice = currentLine.Voice,
            Speaker = currentLine.Speaker,
            Text = currentLine.Text,
            HeadProfile = currentLine.HeadProfile,
        };

        // 背景：空单元格沿用上一有效背景（唯一的继承列）
        if (string.IsNullOrEmpty(resolved.Background))
            resolved.Background = this.currentBG;

        // 语音：未填时按 isVoiceEnabled 自动生成路径（减轻配音表负担）
        // 逻辑：没填 -> 自动生成；填 false -> 关；填其他 -> 开
        if (string.IsNullOrEmpty(resolved.Voice))
        {
            if (!isVoiceEnabled)
            {
                resolved.Voice = "";
            }
            else if (!string.IsNullOrEmpty(currentLine.ID))
            {
                // 只有当有 ID 时才自动生成，防止空行报错
                string dir = Path.GetDirectoryName(currentLine.ID);
                resolved.Voice = string.IsNullOrEmpty(dir)
                    ? currentLine.ID + ".mp3"
                    : dir.Replace('\\', '/') + "/" + currentLine.ID + ".mp3";
            }
        }
        else if (resolved.Voice.Trim().ToLower() == "false")
        {
            isVoiceEnabled = false;
            resolved.Voice = "";
        }
        else
        {
            isVoiceEnabled = true; // 有明确设置语音文件名，则开启
        }

        return resolved;
    }

    private void UpdateVisualState(ResolvedLine line)
    {
        string bg = line.Background;
        if (!string.IsNullOrEmpty(bg) && bg != "hide" && bg != "black")
        {
            currentBG = bg;
            EventCenter.GetInstance().EventTrigger(VNGameEvents.ChangeBackground, bg);
        }
        else if (bg == "black")
        {
            currentBG = "black";
            EventCenter.GetInstance().EventTrigger(VNGameEvents.ChangeBackground, "black");
        }
        else if (bg == "hide")
        {
            currentBG = "hide";
            EventCenter.GetInstance().EventTrigger(VNGameEvents.HideBackground);
        }
    }

    /// <summary>五槽位立绘同步（读档首帧允许用存档槽位补空，见字段注释）</summary>
    private void UpdateCharacterSlots(StoryLine currentLine)
    {
        UpdateCharacter("Left", ResolveCharForSlot(currentLine.CharLeft, "Left"));
        UpdateCharacter("MidLeft", ResolveCharForSlot(currentLine.CharMid_Left, "MidLeft"));
        UpdateCharacter("Mid", ResolveCharForSlot(currentLine.CharMid, "Mid"));
        UpdateCharacter("MidRight", ResolveCharForSlot(currentLine.CharMid_Right, "MidRight"));
        UpdateCharacter("Right", ResolveCharForSlot(currentLine.CharRight, "Right"));
    }

    private string ResolveCharForSlot(string csvValue, string slotKey)
    {
        if (!string.IsNullOrEmpty(csvValue)) return csvValue;
        if (_usePersistedCharacterSlotsWhenCsvCharCellsEmpty &&
            currentCharacters.TryGetValue(slotKey, out var persisted) &&
            !string.IsNullOrEmpty(persisted))
            return persisted;
        return csvValue;
    }

    private void UpdateCharacter(string position, string charData)
    {
        // 空槽与 hide 等价：不继承上一行立绘，必须每行显式填写才会显示
        if (string.IsNullOrEmpty(charData) || charData == "hide")
        {
            EventCenter.GetInstance().EventTrigger(VNGameEvents.HideCharacter, position);
            this.currentCharacters.Remove(position);
            // 隐藏时不清除翻转状态，保持状态以便后续恢复
            return;
        }

        string[] parts = charData.Split('#');
        if (parts.Length != 3)
        {
            Debug.LogError($"[VNManager] 立绘格式错误: '{charData}' (位置 {position})。新格式为 CharacterID#分组#表情（如 Amy#uniform#Smile），旧格式 ID_表情 已不再支持");
            return;
        }

        this.currentCharacters[position] = charData;

        // 翻转状态在剧场层由 TheaterManager.OnShowCharacter 直接读取
        // VNManager.GetCharacterScaleX(posCode) 应用，此处只需广播登台事件。
        var info = new Dictionary<string, string>
        {
            { "position", position }, { "characterID", parts[0] }, { "group", parts[1] }, { "emotion", parts[2] }
        };
        EventCenter.GetInstance().EventTrigger(VNGameEvents.ShowCharacter, info);
    }

    private void UpdateAudioState(ResolvedLine line)
    {
        string bgm = line.BGM;
        if (!string.IsNullOrEmpty(bgm))
        {
            if (bgm == "stop") { MusicManager.GetInstance().StopBGM(); currentBGM = ""; }
            else if (bgm == "pause") MusicManager.GetInstance().PauseBGM();
            else if (bgm == "resume") MusicManager.GetInstance().PlayBGM(currentBGM);
            else if (bgm != currentBGM)
            {
                // 同名 BGM 不重播，避免行间断续
                MusicManager.GetInstance().PlayBGM(bgm);
                currentBGM = bgm;
            }
            else
            {
                VNDebug.LogVerbose($"[VNManager] BGM {bgm} 已在播放，跳过重复播放");
            }
        }

        if (string.IsNullOrEmpty(line.Voice)) return;

        if (VoiceManager.GetInstance() == null)
        {
            Debug.LogWarning("[VNManager] VoiceManager未初始化，无法播放语音");
            return;
        }

        // 语音路径合法性：不接受 URL 形式
        string voicePath = line.Voice.Trim();
        if (voicePath.Length > 0 && !voicePath.Contains("://"))
            VoiceManager.GetInstance().PlayVoice(voicePath);
        else
            Debug.LogWarning($"[VNManager] 无效的语音路径: {voicePath}");
    }

    private void UpdateDialogue(StoryLine currentLine, ResolvedLine line)
    {
        string finalSpeaker = line.Speaker;
        string finalText = line.Text;

        // 启用本地化：每行独立解析，不在行与行之间继承译文（空/缺失则按配置回退 CSV）
        if (VNLocalizationService.IsEnabled())
        {
            bool fallbackToCsv = VNProjectConfig.Instance != null && VNProjectConfig.Instance.FallbackToCsvWhenMissing;

            if (VNLocalizationService.TryGetSpeaker(currentScriptName, currentLine.ID, out var localizedSpeaker) && !string.IsNullOrEmpty(localizedSpeaker))
                finalSpeaker = localizedSpeaker;
            else
                finalSpeaker = fallbackToCsv ? currentLine.Speaker : "";

            if (VNLocalizationService.TryGetText(currentScriptName, currentLine.ID, out var localizedText) && !string.IsNullOrEmpty(localizedText))
                finalText = localizedText;
            else
                finalText = fallbackToCsv ? currentLine.Text : "";
        }

        _dialogueEventScratch.Clear();
        _dialogueEventScratch[VNGameEvents.KeySpeaker] = finalSpeaker;
        _dialogueEventScratch[VNGameEvents.KeyText] = finalText;
        EventCenter.GetInstance().EventTrigger(VNGameEvents.UpdateDialogue, _dialogueEventScratch);

        _headProfileEventScratch.Clear();
        _headProfileEventScratch[VNGameEvents.KeyHeadProfile] = string.IsNullOrEmpty(line.HeadProfile) ? "hide" : line.HeadProfile;
        _headProfileEventScratch[VNGameEvents.KeySpeaker] = finalSpeaker;
        EventCenter.GetInstance().EventTrigger(VNGameEvents.UpdateHeadProfile, _headProfileEventScratch);

        isTextDisplaying = true;
        AddHistoryEntry(finalSpeaker, finalText, line.Voice);
    }

    public void UpdateCurrentBG_OnlyData(string bgName)
    {
        this.currentBG = bgName;
    }

    public void ToggleAutoPlay()
    {
        isAutoPlaying = !isAutoPlaying;
        if (isAutoPlaying)
        {
            VNDebug.LogVerbose("[VNManager] 自动播放已开启");
        }
        else
        {
            VNDebug.LogVerbose("[VNManager] 自动播放已关闭");
        }
        EventCenter.GetInstance().EventTrigger(VNGameEvents.ToggleAutoPlay, isAutoPlaying);
        CheckAndTriggerAutoPlay();
    }

    public void ToggleSkip()
    {
        isSkipping = !isSkipping;
        EventCenter.GetInstance().EventTrigger(VNGameEvents.ToggleSkip, isSkipping);
    }

    public void SaveGame(int slotIndex)
    {
        SaveData saveData = BuildSaveData();

        saveData.SaveTime = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        saveData.ScreenshotPath = SaveManager.GetInstance().SaveCachedScreenshot(slotIndex);

        SaveManager.GetInstance().SaveGame(slotIndex, saveData);
    }

    /// <summary>
    /// 构建当前游戏状态的存档快照（手动档与自动档共用）
    /// </summary>
    private SaveData BuildSaveData()
    {
        SaveData saveData = new SaveData();
        saveData.ScriptFileName = this.currentScriptName;
        saveData.LineID = lastLine != null ? lastLine.ID : "";
        saveData.CurrentBG = this.currentBG;
        saveData.CurrentBGM = this.currentBGM;
        saveData.Characters = new Dictionary<string, string>(this.currentCharacters);
        saveData.CharacterScaleX = new Dictionary<string, float>(this.currentCharactersScaleX);
        // [Flag 扩展] 经 FlagService 导出快照（Save 作用域 + 兼容模式全量；Global 作用域不进存档）
        FlagService.GetInstance().ExportForSave(saveData);
        saveData.ActiveEffects = new List<string>(this.activeEffects); // 保存特效

        // 保存历史记录
        List<HistoryEntry> historyLog = GlobalDataManager.GetInstance().GetHistoryLog();
        if (historyLog != null)
        {
            saveData.HistoryLog = new List<HistoryEntry>(historyLog);
            VNDebug.LogVerbose($"[VNManager] 保存了 {historyLog.Count} 条历史记录");

            // 验证历史记录数据
            if (historyLog.Count > 0)
            {
                var firstEntry = historyLog[0];
                VNDebug.LogVerbose($"[VNManager] 第一条历史记录示例 - Speaker: {firstEntry?.Speaker ?? "null"}, Text: {firstEntry?.Text?.Substring(0, Mathf.Min(20, firstEntry.Text?.Length ?? 0)) ?? "null"}");
            }
        }
        else
        {
            saveData.HistoryLog = new List<HistoryEntry>();
            Debug.LogWarning("[VNManager] 历史记录为null，已初始化为空列表");
        }

        return saveData;
    }

    // ==================== 自动存档 ====================

    /// <summary>
    /// 触发自动存档（异步：等帧末截图后落盘；快照在触发时刻同步捕获以保证状态正确）
    /// </summary>
    /// <param name="reason">触发原因（日志用）</param>
    public void TriggerAutoSave(string reason)
    {
        EnsureAutoSaveConfigLoaded();
        if (!SaveManager.AutoSaveConfig.Enabled) return;
        if (lastLine == null) return;               // 无实际进度（如新游戏启动的 loadscript）不保存
        if (_autoSaveCoroutine != null) return;     // 上一次自动保存仍在进行，跳过本次

        // 关键：此处同步捕获快照。loadscript/jump 等命令在同帧内同步改写状态，
        // 协程等到帧末再构建会拿到切换后的错误状态。
        SaveData snapshot = BuildSaveData();

        VNDebug.LogVerbose($"[VNManager] 自动存档触发: {reason} (剧本: {currentScriptName}, 行: {snapshot.LineID})");
        _autoSaveCoroutine = MonoManager.GetInstance().StartCoroutine(AutoSaveWriteRoutine(snapshot));
    }

    /// <summary>
    /// 选项选择后触发自动存档（保存选择行进度，读档后重新弹出选项）
    /// </summary>
    public void TriggerAutoSaveOnChoice()
    {
        EnsureAutoSaveConfigLoaded();
        if (!SaveManager.AutoSaveConfig.Enabled || !SaveManager.AutoSaveConfig.OnChoice) return;
        TriggerAutoSave("选项选择");
    }

    /// <summary>
    /// 跨剧本切换(loadscript)前触发自动存档（保存上一个剧本的进度）
    /// </summary>
    public void TriggerAutoSaveOnScriptSwitch()
    {
        EnsureAutoSaveConfigLoaded();
        if (!SaveManager.AutoSaveConfig.Enabled || !SaveManager.AutoSaveConfig.OnScriptSwitch) return;
        if (lastLine == null) return; // 新游戏首次 loadscript 无进度可存
        TriggerAutoSave("跨剧本切换");
    }

    /// <summary>
    /// 自动存档落盘协程：等本行画面渲染完成后截图并写入自动档文件
    /// </summary>
    private IEnumerator AutoSaveWriteRoutine(SaveData snapshot)
    {
        yield return new WaitForEndOfFrame();

        if (snapshot != null)
        {
            SaveManager.GetInstance().CaptureCurrentScreen();
            snapshot.SaveTime = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            snapshot.ScreenshotPath = SaveManager.GetInstance().SaveCachedAutoScreenshot();
            SaveManager.GetInstance().SaveAutoGame(snapshot);
        }

        _autoSaveCoroutine = null;
    }

    /// <summary>
    /// 立即保存自动档（同步；供玩家在 Save 面板手动覆盖自动档时使用，
    /// 截图使用打开面板前缓存的画面）
    /// </summary>
    public void SaveAutoGameNow()
    {
        if (lastLine == null) return;

        SaveData saveData = BuildSaveData();
        saveData.SaveTime = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        saveData.ScreenshotPath = SaveManager.GetInstance().SaveCachedAutoScreenshot();

        SaveManager.GetInstance().SaveAutoGame(saveData);
    }

    /// <summary>
    /// 行数计数：每播放一行 +1，达到配置间隔即触发自动存档
    /// </summary>
    private void TickAutoSaveOnLinePlayed()
    {
        EnsureAutoSaveConfigLoaded();
        var cfg = SaveManager.AutoSaveConfig;
        if (!cfg.Enabled || cfg.EveryLines <= 0) return;

        autoSaveLineCounter++;
        if (autoSaveLineCounter >= cfg.EveryLines)
        {
            autoSaveLineCounter = 0;
            TriggerAutoSave($"每 {cfg.EveryLines} 行");
        }
    }

    /// <summary>
    /// 确保自动存档配置已从 SaveLoadPanel 加载：
    /// 面板可能从未实例化（自动保存触发早于面板首次打开），
    /// 此时加载面板预制体（不实例化）读取 Inspector 序列化值。
    /// </summary>
    private void EnsureAutoSaveConfigLoaded()
    {
        if (_autoSaveConfigEnsured) return;
        _autoSaveConfigEnsured = true;
        if (SaveManager.AutoSaveConfig.LoadedFromPanel) return;

        var prefab = VNUIPrefabs.Load(VNUIPrefabKeys.SaveLoadPanel, VNUIPrefabKeys.SaveLoadPanel);
        if (prefab != null)
        {
            var panel = prefab.GetComponent<SaveLoadPanel>();
            if (panel != null)
            {
                panel.PushAutoSaveConfigToManager();
                VNDebug.LogVerbose("[VNManager] 已从 SaveLoadPanel 预制体加载自动存档配置");
            }
        }
    }

    private void AddHistoryEntry(string speaker, string text, string voiceID)
    {
        GlobalDataManager.GetInstance().AddHistoryLog(speaker, text, voiceID);
        HistoryEntry entry = new HistoryEntry(speaker, text, voiceID);
        EventCenter.GetInstance().EventTrigger(VNGameEvents.AddHistoryEntry, entry);
    }

    public void ExecuteChoiceCommand(string command)
    {
        // [Confirm 出口] 选项命令即本行出口：choice 行声明的 @Confirm: 段不再执行（解析期已警告该写法）
        ConsumeConfirmExit();

        // 【并发防护】用户已做出选择，当前行的残余演出必须终止。
        // 场景：choice 与异步命令并行（如 [choice(...) & wait(3)]——choice 不在链尾的写法），
        // 用户在 wait 结束前点了选项 → 本方法启动新协程；若旧 _flowCoroutine 不停，
        // 它稍后结束时会检测 CurrentLineIndex != preIndex 再次触发 PlayCurrentLine，
        // 与新协程双重演出 / 行索引二次推进。
        if (_flowCoroutine != null)
        {
            MonoManager.GetInstance().StopCoroutine(_flowCoroutine);
            _flowCoroutine = null;
            CommandManager.GetInstance().InterruptAll();
        }

        if (!string.IsNullOrEmpty(command))
            _flowCoroutine = MonoManager.GetInstance().StartCoroutine(ExecuteActionsAndContinue(command));
        else
            PlayCurrentLine();
    }

    public bool IsAutoPlaying() { return isAutoPlaying; }
    public bool IsSkipping() { return isSkipping; }
    public bool IsTextDisplaying() { return isTextDisplaying; }

    /// <summary>[Confirm 出口] 将当前行的 @Confirm: 出口段标记为已消费（choice 选项即本行出口，出口段不再执行）</summary>
    public void ConsumeConfirmExit()
    {
        _confirmExitConsumed = true;
    }

    public void SetConfig(string key, string value)
    {
        switch (key.ToLower())
        {
            case "voice": isVoiceEnabled = value.ToLower() == "true"; break;
            case "textspeed": isTextSpeedEnabled = value.ToLower() == "true"; break;
        }
    }

    #region 协程区
    /// <summary>
    /// 加载等待协程
    /// </summary>
    /// <returns></returns>
    private IEnumerator WaitLoadingQueueThenContinueGameplay()
    {
        LoadingProgressManager progressManager = LoadingProgressManager.GetInstance();

        const string scriptTaskID = "load_script_continue";
        const string uiTaskID = "ui_VNGameplayPanel";
        const int maxWaitFrames = 120;

        VNGameplayPanel gameplayPanel = null;

        for (int i = 0; i < maxWaitFrames; i++)
        {
            float scriptProgress = progressManager.GetTaskProgress(scriptTaskID);
            float uiProgress = progressManager.GetTaskProgress(uiTaskID);

            bool scriptDone = scriptProgress >= 1f || scriptProgress < 0f;
            bool uiDone = uiProgress >= 1f || uiProgress < 0f;

            gameplayPanel = UIManager.GetInstance().Get<VNGameplayPanel>();
            bool panelReady =
                isGameplayPanelLoadCallbackFired &&
                gameplayPanel != null &&
                gameplayPanel.gameObject != null &&
                gameplayPanel.gameObject.activeInHierarchy;

            if (scriptDone && uiDone && panelReady)
            {
                VNDebug.LogVerbose($"[VNManager] 加载任务与 VNGameplayPanel 均已就绪（ContinueGame），等待了 {i + 1} 帧");
                break;
            }

            yield return null;
        }

        gameplayPanel = UIManager.GetInstance().Get<VNGameplayPanel>();
        if (gameplayPanel == null || gameplayPanel.gameObject == null || !gameplayPanel.gameObject.activeInHierarchy)
        {
            Debug.LogError("[VNManager] 无法获取VNGameplayPanel，继续游戏失败");

            UIManager.GetInstance().HidePanel("LoadingProgressPanel");
            progressManager.ClearAllTasks();
            currentLoadingSaveData = null;

            yield break;
        }

        UIManager.GetInstance().HidePanel("LoadingProgressPanel");
        progressManager.ClearAllTasks();

        yield return DelayedContinueGameplay();
    }
    
     /// <summary>
    /// 带进度更新的剧本加载协程
    /// </summary>
    private System.Collections.IEnumerator LoadScriptWithProgress(string scriptTaskID)
    {
        LoadingProgressManager progressManager = LoadingProgressManager.GetInstance();
        
        // 【新增】跨剧本加载时清空历史记录（新游戏或切换剧本）
        // 在设置新剧本名之前，检查是否是切换剧本
        string previousScriptName = this.currentScriptName;
        bool isNewScript = string.IsNullOrEmpty(previousScriptName) || previousScriptName != pendingScriptName;
        
        if (isNewScript)
        {
            // 新游戏或切换剧本，清空历史记录
            ClearHistoryLog();
            VNDebug.LogVerbose($"[VNManager] 检测到新剧本或首次启动，已清空历史记录。旧剧本: {previousScriptName}, 新剧本: {pendingScriptName}");
        }
        
        this.currentScriptName = pendingScriptName;

        // 1. 加载剧本数据 (纯数据操作，同步加载，但用协程分步更新进度)
        progressManager.UpdateTaskProgress(scriptTaskID, 0.1f); // 开始加载
        yield return null; // 等待一帧，让UI更新
        
        progressManager.UpdateTaskProgress(scriptTaskID, 0.3f); // 解析中
        yield return null; // 等待一帧，让UI更新
        
        CommandManager.GetInstance().ExecuteCommand($"loadscript({pendingScriptName})");
        progressManager.UpdateTaskProgress(scriptTaskID, 0.7f); // 加载中
        yield return null; // 等待一帧，让UI更新
        
        ResetState();
        progressManager.UpdateTaskProgress(scriptTaskID, 0.9f); // 即将完成
        yield return null; // 等待一帧，让UI更新
        
        progressManager.CompleteTask(scriptTaskID); // 剧本加载完成

        // 2. 剧本数据为空：直接判定加载失败并中止（先于行号解析——
        //    否则剧本没加载成功时会先抛出"找不到行号 ID"的次生报错，掩盖真正根因）
        if (StoryLines.Count <= 0)
        {
            Debug.LogError("[VNManager] 剧本加载失败，无法启动游戏。");

            // 清理并隐藏加载面板
            progressManager.OnAllTasksCompleted -= OnGameLoadingCompleted;
            progressManager.ClearAllTasks();
            UIManager.GetInstance().HidePanel("LoadingProgressPanel");

            // 调用失败回调
            if (onGameStartedCallback != null)
            {
                onGameStartedCallback.Invoke();
                onGameStartedCallback = null;
            }
            yield break;
        }

        // 3. 计算目标行索引 (暂不预演，只算位置)
        int targetIndex = 0;
        if (!string.IsNullOrEmpty(pendingLineID))
        {
            string cleanID = pendingLineID.Trim();
            if (LineIDIndexMap.ContainsKey(cleanID))
            {
                targetIndex = LineIDIndexMap[cleanID];
            }
            else
            {
                Debug.LogError($"[VNManager] 找不到指定的行号 ID: {cleanID}，将从头开始。");
                targetIndex = 0;
            }
        }

        // 4. 显示 UI (异步过程，UIManager会自动注册并跟踪进度)
        // UIManager会自动注册任务 "ui_VNGameplayPanel"，我们只需要等待它完成
        UIManager.GetInstance().Show<VNGameplayPanel>((panel) =>
        {
            isGameplayPanelLoadCallbackFired = true;
            VNDebug.LogVerbose("[VNManager] VNGameplayPanel 的 ShowPanel 回调已触发");
        });
    }
    

    private IEnumerator DelayedStartGameplay()
{
    VNGameplayPanel gameplayPanel = UIManager.GetInstance().Get<VNGameplayPanel>();

    // 如果还没拿到，强制再创建一次
    if (gameplayPanel == null)
    {
        Debug.LogWarning("[VNManager] DelayedStartGameplay 时未找到 VNGameplayPanel，尝试强制补建...");

        UIManager.GetInstance().Show<VNGameplayPanel>(
            (panel) =>
            {
                VNDebug.LogVerbose("[VNManager] VNGameplayPanel 强制补建回调成功（StartGame）");
            }
        );

        // 最多再等 30 帧
        for (int i = 0; i < 30; i++)
        {
            gameplayPanel = UIManager.GetInstance().Get<VNGameplayPanel>();
            if (gameplayPanel != null &&
                gameplayPanel.gameObject != null &&
                gameplayPanel.gameObject.activeInHierarchy)
            {
                VNDebug.LogVerbose($"[VNManager] 强制补建后成功获取 VNGameplayPanel（StartGame），等待了 {i + 1} 帧");
                break;
            }

            yield return null;
        }
    }
    
    //再尝试重新拿
    gameplayPanel = UIManager.GetInstance().Get<VNGameplayPanel>();
    if (gameplayPanel == null || gameplayPanel.gameObject == null || !gameplayPanel.gameObject.activeInHierarchy)
    {
        Debug.LogError("[VNManager] 无法获取VNGameplayPanel，游戏启动失败");

        UIManager.GetInstance().HidePanel("LoadingProgressPanel");
        LoadingProgressManager.GetInstance().ClearAllTasks();

        if (onGameStartedCallback != null)
        {
            onGameStartedCallback.Invoke();
            onGameStartedCallback = null;
        }

        yield break;
    }
    
    
    // 计算目标行索引
    int targetIndex = 0;
    if (!string.IsNullOrEmpty(pendingLineID))
    {
        string cleanID = pendingLineID.Trim();
        if (LineIDIndexMap.ContainsKey(cleanID))
        {
            targetIndex = LineIDIndexMap[cleanID];
        }
        else
        {
            // 静默归零会让"行 ID 打错"表现为"莫名从头开始"，必须报错
            Debug.LogError($"[VNManager] 找不到指定的行号 ID: {cleanID}，将从剧本开头开始播放");
            targetIndex = 0;
        }
    }

    // 确保游戏状态设置为 Gameplay
    GameStateManager.GetInstance().SetState(GameState.Gameplay);

    // 强力清理演出现场（特效 + 五槽立绘），避免上一次演出残留
    VNAPI.ClearAllEffects();
    EventCenter.GetInstance().EventTrigger(VNGameEvents.HideCharacter, "Left");
    EventCenter.GetInstance().EventTrigger(VNGameEvents.HideCharacter, "MidLeft");
    EventCenter.GetInstance().EventTrigger(VNGameEvents.HideCharacter, "Mid");
    EventCenter.GetInstance().EventTrigger(VNGameEvents.HideCharacter, "MidRight");
    EventCenter.GetInstance().EventTrigger(VNGameEvents.HideCharacter, "Right");

    // 快进到目标行，如果遇到 choice 命令则停止
    bool encounteredChoice = false;
    if (targetIndex > 0)
    {
        VNDebug.LogVerbose($"[VNManager] UI就绪，开始预演至索引: {targetIndex}");
        encounteredChoice = FastForwardToLine(targetIndex);
    }

    // 设置当前行（遇到 choice 时 FastForwardToLine 已写入正确索引，不可覆盖）
    if (!encounteredChoice)
    {
        CurrentLineIndex = targetIndex;
    }

    // 同步立绘显示：把 Simulate 阶段积累的槽位状态登台
    // （翻转/缩放由 TheaterManager.OnShowCharacter 读取 GetCharacterScaleX 应用，此处无需重复计算）
    foreach (var kvp in currentCharacters)
    {
        string[] parts = kvp.Value.Split('#');
        if (parts.Length != 3) continue;

        // 事件契约类型为 Dictionary<string,string>（EventCenter 按泛型类型分发），
        // 用 Dictionary<string,object> 触发会被静默丢弃
        var info = new Dictionary<string, string>
        {
            { "position", kvp.Key },
            { "characterID", parts[0] },
            { "group", parts[1] },
            { "emotion", parts[2] }
        };
        EventCenter.GetInstance().EventTrigger(VNGameEvents.ShowCharacter, info);
    }

    // 同步背景显示
    if (!string.IsNullOrEmpty(currentBG))
    {
        EventCenter.GetInstance().EventTrigger(VNGameEvents.ChangeBackground, currentBG);
    }

    // 正式播放
    PlayCurrentLine();

    // 启动完成回调
    if (onGameStartedCallback != null)
    {
        onGameStartedCallback.Invoke();
        onGameStartedCallback = null;
    }
}
    
    /// <summary>
    /// 带进度更新的继续游戏剧本加载协程
    /// </summary>
    private System.Collections.IEnumerator LoadScriptForContinueWithProgress(string scriptTaskID, SaveData saveData)
    {
        LoadingProgressManager progressManager = LoadingProgressManager.GetInstance();
        
        // 【Bug修复】清理pending变量，避免与新游戏逻辑冲突
        pendingScriptName = null;
        pendingLineID = null;
        
        // 【Bug修复】确保游戏状态是Gameplay
        if (GameStateManager.GetInstance().CurrentState != GameState.Gameplay && 
            GameStateManager.GetInstance().CurrentState != GameState.AutoPlay)
        {
            GameStateManager.GetInstance().SetState(GameState.Gameplay);
        }
        
        InitializeManager();

        // 1. 加载剧本数据
        progressManager.UpdateTaskProgress(scriptTaskID, 0.1f);
        yield return null; // 等待一帧，让UI更新
        
        progressManager.UpdateTaskProgress(scriptTaskID, 0.3f);
        yield return null; // 等待一帧，让UI更新
        
        var scriptData = ScriptParser.Parse(saveData.ScriptFileName);
        if (scriptData != null)
        {
            SetScriptData(scriptData.Lines, scriptData.IDMap, saveData.ScriptFileName);
            progressManager.UpdateTaskProgress(scriptTaskID, 0.7f);
            yield return null; // 等待一帧，让UI更新
        }
        else
        {
            Debug.LogError($"无法加载存档: {saveData.ScriptFileName}");
            progressManager.CompleteTask(scriptTaskID);
            progressManager.OnAllTasksCompleted -= OnContinueGameLoadingCompleted;
            progressManager.ClearAllTasks();
            UIManager.GetInstance().HidePanel("LoadingProgressPanel");
            yield break;
        }
        
        progressManager.UpdateTaskProgress(scriptTaskID, 0.9f);
        yield return null; // 等待一帧，让UI更新
        
        progressManager.CompleteTask(scriptTaskID);

        // 恢复游戏状态数据
        currentBG = saveData.CurrentBG;
        currentBGM = saveData.CurrentBGM;

        // 恢复特效状态（先清空，再恢复）
        VNAPI.ClearAllEffects();
        activeEffects.Clear();
        
        // 恢复历史记录（在恢复特效前）
        if (saveData.HistoryLog != null && saveData.HistoryLog.Count > 0)
        {
            GlobalDataManager.GetInstance().RestoreHistoryLog(saveData.HistoryLog);
            VNDebug.LogVerbose($"[VNManager] 已恢复 {saveData.HistoryLog.Count} 条历史记录");
        }
        else
        {
            // 如果存档中没有历史记录，清空当前的历史记录（防止残留）
            GlobalDataManager.GetInstance().ClearHistoryLog();
        }

        // [Flag 扩展] 恢复标志：经 FlagService 路由（Save 作用域 → 内存快照区；Global 不回退；兼容模式整体覆盖 GlobalData 保持旧行为）
        FlagService.GetInstance().ImportFromSave(saveData);

        // 恢复立绘数据
        currentCharactersScaleX.Clear();
        if (saveData.CharacterScaleX != null) 
        {
            this.currentCharactersScaleX = new Dictionary<string, float>(saveData.CharacterScaleX);
        }

        // 计算目标行索引
        int targetIndex = 0;
        if (!string.IsNullOrEmpty(saveData.LineID) && LineIDIndexMap.ContainsKey(saveData.LineID))
        {
            targetIndex = LineIDIndexMap[saveData.LineID];
        }
        
        // 保存到成员变量，供DelayedContinueGameplay使用
        currentLoadingSaveData = saveData;
        currentLoadingTargetIndex = targetIndex;

        // 2. 显示 UI (异步过程，UIManager会自动注册并跟踪进度)
        if (StoryLines.Count > 0)
        {
            UIManager.GetInstance().Show<VNGameplayPanel>((panel) =>
            {
                // UI加载完成，UIManager会自动完成任务
                // 注意：这里不立即执行游戏逻辑，等待OnContinueGameLoadingCompleted回调
                isGameplayPanelLoadCallbackFired = true;
                VNDebug.LogVerbose("[VNManager] VNGameplayPanel 的 ShowPanel 回调已触发（ContinueGame）");
            });
        }
        else
        {
            Debug.LogError("[VNManager] 剧本数据为空，无法继续游戏");
            
            // 清理并隐藏加载面板
            progressManager.OnAllTasksCompleted -= OnContinueGameLoadingCompleted;
            progressManager.ClearAllTasks();
            UIManager.GetInstance().HidePanel("LoadingProgressPanel");
            
            // 清理临时数据
            currentLoadingSaveData = null;
        }
    }
    
    /// <summary>
    /// 延迟继续游戏逻辑（确保UI完全初始化）
    /// </summary>
    private System.Collections.IEnumerator DelayedContinueGameplay()
    {
        // yield return null; // 等待一帧
        
        // 获取游戏面板
        VNGameplayPanel gameplayPanel = UIManager.GetInstance().Get<VNGameplayPanel>();
        if (gameplayPanel == null)
        {
            Debug.LogError("[VNManager] 无法获取VNGameplayPanel，继续游戏失败");
            // currentLoadingSaveData = null;
            yield break;
        }
        
        // 检查是否有保存的存档数据
        if (currentLoadingSaveData == null)
        {
            Debug.LogError("[VNManager] 存档数据丢失，继续游戏失败");
            yield break;
        }
        
        // 【修复】确保游戏状态设置为 Gameplay（加载存档时需要）
        GameStateManager.GetInstance().SetState(GameState.Gameplay);
        
        // 恢复游戏状态
        RestoreGameStateFromSave(currentLoadingSaveData, currentLoadingTargetIndex);
        
        // 清理临时数据
        currentLoadingSaveData = null;
    }
    
    private IEnumerator ExecuteActionsAndContinue(string actionString)
    {
        int preIndex = CurrentLineIndex;

        yield return CommandManager.GetInstance().ExecuteCommandsAsync(actionString);

        _flowCoroutine = null;

        bool shouldAdvanceAfterCommands = ConsumeAdvanceAfterCommandsRequest();

        GameStateManager stateManager = GameStateManager.GetInstance();
        if (stateManager != null && stateManager.CurrentState == GameState.Choice)
        {
            VNDebug.LogVerbose("[VNManager] 命令执行完成，当前处于 Choice 状态，停止继续前进");
            yield break;
        }

        // 如果命令过程中已经改了行号（例如 jump），优先播放新位置
        if (CurrentLineIndex != preIndex)
        {
            PlayCurrentLine();
            yield break;
        }

        // 如果某个命令登记了“命令全部执行完后自动前进”
        if (shouldAdvanceAfterCommands)
        {
            // [Confirm 出口] 命令驱动的推进（如 fadeBlackOut）同样经由出口段，与点击/AutoPlay 行为统一
            if (HasPendingConfirmExit)
            {
                _flowCoroutine = null; // 当前协程即将结束，让出句柄给出口协程
                LaunchConfirmExit();
                yield break;
            }
            CurrentLineIndex++;
            PlayCurrentLine();
            yield break;
        }

        CheckAndTriggerAutoPlay();
    }

    // ==================== [Confirm 出口] 行尾出口执行（@Confirm: 语法糖） ====================

    /// <summary>当前行是否声明了尚未消费的 @Confirm: 出口段</summary>
    private bool HasPendingConfirmExit =>
        !_confirmExitConsumed &&
        lastLine != null &&
        !string.IsNullOrEmpty(lastLine.ConfirmCommands);

    /// <summary>
    /// 启动当前行的出口协程（点击推进、AutoPlay、AdvanceAfterCommands 三个推进入口统一经由这里）。
    /// 出口段标记为已消费：出口执行期间被用户点击打断时，下一次点击按默认行为推进，不重复执行出口命令。
    /// </summary>
    private void LaunchConfirmExit()
    {
        if (lastLine == null || string.IsNullOrEmpty(lastLine.ConfirmCommands)) return;
        _confirmExitConsumed = true;
        ClearAdvanceAfterCommandsRequest();
        _flowCoroutine = MonoManager.GetInstance().StartCoroutine(ExecuteConfirmExit(lastLine.ConfirmCommands));
    }

    /// <summary>
    /// 执行 @Confirm: 出口命令：出口命令产生跳转/切换（jump/jumpif/loadscript 生效）时播放新位置；
    /// 未产生跳转时按默认行为推进下一行（等价于旧版 NextLine）。
    /// </summary>
    private IEnumerator ExecuteConfirmExit(string confirmCommands)
    {
        int preIndex = CurrentLineIndex;

        yield return CommandManager.GetInstance().ExecuteCommandsAsync(confirmCommands);

        _flowCoroutine = null;

        GameStateManager stateManager = GameStateManager.GetInstance();
        if (stateManager != null && stateManager.CurrentState == GameState.Choice)
        {
            // 出口段不应含 choice（解析期已报错拦截），防御性兜底：等待选择
            yield break;
        }

        if (CurrentLineIndex != preIndex)
        {
            PlayCurrentLine();
            yield break;
        }

        // 默认行为：推进到下一行
        CurrentLineIndex++;
        PlayCurrentLine();
    }

    private IEnumerator AutoPlayCountdown(float delay)
    {
        var gameplayPanel = UIManager.GetInstance().Get<VNGameplayPanel>();
        bool isTextTyping = false;
        bool isVoicePlaying = false;
        
        // 第一步：等待打字机效果和语音播放都完成（以慢的为准）
        VNDebug.LogVerbose("[VNManager] 自动播放等待中：等待打字机效果和语音播放完成...");
        while (true)
        {
            // 检查打字机效果
            if (gameplayPanel != null)
            {
                isTextTyping = gameplayPanel.IsTextTyping();
            }
            
            // 检查语音播放
            if (VoiceManager.GetInstance() != null)
            {
                isVoicePlaying = VoiceManager.GetInstance().IsVoicePlaying();
                VNDebug.LogVerbose("Voice: " + isVoicePlaying);
            }
            
            // 如果两者都完成，跳出循环
            if (!isTextTyping && !isVoicePlaying)
            {
                VNDebug.LogVerbose("[VNManager] 打字机效果和语音播放已完成，等待额外延迟后进入下一行");
                break;
            }
            
            // 等待一帧后继续检查
            yield return null;
        }
        
        // 第二步：等待AutoSpeed时间后进入下一行
        yield return new WaitForSeconds(delay);

        VNDebug.LogVerbose($"[VNManager] 自动播放进入下一行 (行索引: {CurrentLineIndex + 1})");
        _autoPlayCoroutine = null;

        // [Confirm 出口] 自动播放 = 机器替用户点击：有出口段时经由出口执行（未跳转则默认推进），与点击行为统一
        if (HasPendingConfirmExit)
        {
            LaunchConfirmExit();
            yield break;
        }

        CurrentLineIndex++;
        PlayCurrentLine();
    }
    
    private IEnumerator WaitLoadingQueueThenStartGameplay()
    {
        LoadingProgressManager progressManager = LoadingProgressManager.GetInstance();

        const string scriptTaskID = "load_script";
        const string uiTaskID = "ui_VNGameplayPanel";
        const int maxWaitFrames = 120;

        VNGameplayPanel gameplayPanel = null;

        for (int i = 0; i < maxWaitFrames; i++)
        {
            float scriptProgress = progressManager.GetTaskProgress(scriptTaskID);
            float uiProgress = progressManager.GetTaskProgress(uiTaskID);

            bool scriptDone = scriptProgress >= 1f || scriptProgress < 0f;
            bool uiDone = uiProgress >= 1f || uiProgress < 0f;

            gameplayPanel = UIManager.GetInstance().Get<VNGameplayPanel>();
            bool panelReady =
                isGameplayPanelLoadCallbackFired &&
                gameplayPanel != null &&
                gameplayPanel.gameObject != null &&
                gameplayPanel.gameObject.activeInHierarchy;

            if (scriptDone && uiDone && panelReady)
            {
                VNDebug.LogVerbose($"[VNManager] 加载任务与 VNGameplayPanel 均已就绪，等待了 {i + 1} 帧");
                break;
            }

            yield return null;
        }

        gameplayPanel = UIManager.GetInstance().Get<VNGameplayPanel>();
        if (gameplayPanel == null || gameplayPanel.gameObject == null || !gameplayPanel.gameObject.activeInHierarchy)
        {
            Debug.LogError("[VNManager] 加载任务已结束，但仍无法获取 VNGameplayPanel，游戏启动失败");

            UIManager.GetInstance().HidePanel("LoadingProgressPanel");
            progressManager.ClearAllTasks();

            if (onGameStartedCallback != null)
            {
                onGameStartedCallback.Invoke();
                onGameStartedCallback = null;
            }

            yield break;
        }

        UIManager.GetInstance().HidePanel("LoadingProgressPanel");
        progressManager.ClearAllTasks();

        yield return DelayedStartGameplay();
    }

    
    #endregion
    
    
    
    #region API供外部调用
    public void RequestAdvanceAfterCommands()
    {
        _advanceAfterCommandsRequested = true;
    }

    public void ClearAdvanceAfterCommandsRequest()
    {
        _advanceAfterCommandsRequested = false;
    }

    public bool ConsumeAdvanceAfterCommandsRequest()
    {
        bool result = _advanceAfterCommandsRequested;
        _advanceAfterCommandsRequested = false;
        return result;
    }
    public float GetCharacterScaleX(string posCode)
    {
        string normalized = NormalizePositionCode(posCode);
        if (currentCharactersScaleX.ContainsKey(normalized))
            return currentCharactersScaleX[normalized];
        return 1f; // 默认朝右
    }

    // 【新增】设置角色 ScaleX 的 API (供 Command 调用)
    public void SetCharacterScaleX(string posCode, float scaleX)
    {
        string normalized = NormalizePositionCode(posCode);
        currentCharactersScaleX[normalized] = scaleX;
    }

    // 获取角色数据 (方便 CharFlip.Simulate 内部获取当前 CharID_Emotion)
    public string GetCharacterData(string posCode)
    {
        string normalized = NormalizePositionCode(posCode);
        // 需要同时检查 "L"/"ML"/"M"/"MR"/"R" 和 "Left"/"MidLeft"/"Mid"/"MidRight"/"Right" 两种格式
        if (currentCharacters.ContainsKey(normalized))
            return currentCharacters[normalized];
        // 如果 normalized 是缩写，也检查全名
        if (normalized == "L" && currentCharacters.ContainsKey("Left"))
            return currentCharacters["Left"];
        if (normalized == "ML" && currentCharacters.ContainsKey("MidLeft"))
            return currentCharacters["MidLeft"];
        if (normalized == "M" && currentCharacters.ContainsKey("Mid"))
            return currentCharacters["Mid"];
        if (normalized == "MR" && currentCharacters.ContainsKey("MidRight"))
            return currentCharacters["MidRight"];
        if (normalized == "R" && currentCharacters.ContainsKey("Right"))
            return currentCharacters["Right"];
        return "";
    }
    
    /// <summary>
    /// 启动场景回放
    /// </summary>
    /// <param name="scriptName">剧本文件名</param>
    /// <param name="startID">开始行ID</param>
    /// <param name="endID">结束行ID</param>
    /// <param name="wasMainMenuVisible">回放前主菜单是否可见（可选，默认false）</param>
    public void StartSceneReplay(string scriptName, string startID, string endID, bool wasMainMenuVisible = false)
    {
        isReplayMode = true;
        replayEndLineID = endID;
        
        // 【修复】记录主菜单是否可见（用于回放结束后恢复）
        wasMainMenuVisibleBeforeReplay = wasMainMenuVisible;
        VNDebug.LogVerbose($"[VNManager] 记录主菜单状态: {wasMainMenuVisibleBeforeReplay}");
        
        // 复用 StartGameOnScene 逻辑，但带上回放标记
        StartGameOnScene(scriptName, startID, () =>
        {
            VNDebug.LogVerbose($"[VNManager] 场景回放已启动: {scriptName}, 从 {startID} 到 {endID}");
        });
    }
    
    /// <summary>
    /// 结束场景回放
    /// </summary>
    private void EndReplay()
    {
        VNDebug.LogVerbose("[VNManager] 场景回放结束，开始清理状态");
        

        ResetReplayState();

        isReplayMode = false;
        replayEndLineID = "";

        AnimationCompat.StopAll();
        VNAPI.ClearAllEffects();
        PoolManager.GetInstance().Clear();

        // 关闭游戏面板
        UIManager.GetInstance().HidePanel("VNGameplayPanel");
        
        // 【修复2】重新显示画廊面板（如果之前被隐藏了）
        GalleryPanel galleryPanel = UIManager.GetInstance().Get<GalleryPanel>();
        if (galleryPanel != null)
        {
            // 面板已存在，直接显示
            galleryPanel.gameObject.SetActive(true);
            galleryPanel.SwitchPage(GalleryPanel.GalleryPage.Scene);
        }
        else
        {
            // 面板不存在，重新加载
            UIManager.GetInstance().Show<GalleryPanel>((panel) =>
            {
                // 切换到场景回放页面
                if (panel != null)
                {
                    panel.SwitchPage(GalleryPanel.GalleryPage.Scene);
                }
            });
        }
        
        // 【修复3】恢复主菜单面板（如果回放前是可见的）
        VNDebug.LogVerbose($"[VNManager] 检查是否需要恢复主菜单: wasMainMenuVisibleBeforeReplay = {wasMainMenuVisibleBeforeReplay}");
        if (wasMainMenuVisibleBeforeReplay)
        {
            MainMenuPanel mainMenuPanel = UIManager.GetInstance().Get<MainMenuPanel>();
            if (mainMenuPanel != null)
            {
                mainMenuPanel.gameObject.SetActive(true);
                VNDebug.LogVerbose("[VNManager] 主菜单面板已恢复显示");
            }
            else
            {
                Debug.LogWarning("[VNManager] 主菜单面板不存在，无法恢复");
            }
            wasMainMenuVisibleBeforeReplay = false; // 重置标志
        }
        else
        {
            VNDebug.LogVerbose("[VNManager] 回放前主菜单不可见，不恢复");
        }
    }
    
    /// <summary>
    /// 重置场景回放状态（清理所有回放产生的状态和效果）
    /// </summary>
    private void ResetReplayState()
    {
        VNDebug.LogVerbose("[VNManager] 开始重置场景回放状态");
        
        // 1. 停止BGM
        MusicManager.GetInstance().StopBGM();
        currentBGM = "";
        
        // 2. 停止所有音效
        MusicManager.GetInstance().ClearAllSFX();
        
        // 3. 停止语音
        if (VoiceManager.GetInstance() != null)
        {
            VoiceManager.GetInstance().StopVoice();
        }
        
        // 4. 清理所有特效
        VNAPI.ClearAllEffects();
        activeEffects.Clear();
        
        // 5. 隐藏所有角色
        EventCenter.GetInstance().EventTrigger(VNGameEvents.HideCharacter, "Left");
        EventCenter.GetInstance().EventTrigger(VNGameEvents.HideCharacter, "MidLeft");
        EventCenter.GetInstance().EventTrigger(VNGameEvents.HideCharacter, "Mid");
        EventCenter.GetInstance().EventTrigger(VNGameEvents.HideCharacter, "MidRight");
        EventCenter.GetInstance().EventTrigger(VNGameEvents.HideCharacter, "Right");
        currentCharacters.Clear();
        currentCharactersScaleX.Clear();
        
        // 6. 重置背景（可选：设置为黑色或隐藏）
        // EventCenter.GetInstance().EventTrigger(VNGameEvents.ChangeBackground, "black");
        currentBG = "";
        
        // 7. 重置游戏状态变量
        isAutoPlaying = false;
        isSkipping = false;
        isTextDisplaying = false;
        isVoiceEnabled = true;
        lastLine = null;
        
        // 8. 停止所有协程
        if (_flowCoroutine != null)
        {
            MonoManager.GetInstance().StopCoroutine(_flowCoroutine);
            _flowCoroutine = null;
        }
        if (_autoPlayCoroutine != null)
        {
            MonoManager.GetInstance().StopCoroutine(_autoPlayCoroutine);
            _autoPlayCoroutine = null;
        }
        
        // 9. 中断所有命令
        CommandManager.GetInstance().InterruptAll();
        
        // 10. 恢复TimeScale（如果被快进修改了）
        Time.timeScale = 1f;
        
        VNDebug.LogVerbose("[VNManager] 场景回放状态重置完成");
    }
    #endregion
}
