using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using VNovelizer.Core.Commands;
using UnityEngine.SceneManagement;
using UnityEngine.Events;
using VNovelizer.Core.API; // 引用 API 以便调用 ClearAllEffects

/// <summary>
/// 视觉小说核心管理器 (终极预演版)
/// </summary>
public class VNManager : BaseManager<VNManager>
{
    // === 核心数据 ===
    public List<StoryLine> StoryLines { get; private set; } = new List<StoryLine>();
    public Dictionary<string, int> LineIDIndexMap { get; private set; } = new Dictionary<string, int>();

    // 当前行索引
    public int CurrentLineIndex { get; set; } = -1;

    // --- 状态变量 ---
    private StoryLine lastLine = null;
    private string currentBG = null;
    private string currentBGM = null;
    private string currentScriptName;
    private string lastValidSpeaker = "";
    private string lastValidText = "";
    private string lastValidHeadProfile = ""; // 记录上一个有效的 HeadProfile
    private Dictionary<string, string> currentCharacters = new Dictionary<string, string>();
    private Dictionary<string, float> currentCharactersScaleX = new Dictionary<string, float>();

    // 【新增】特效状态追踪
    private HashSet<string> activeEffects = new HashSet<string>();

    // 游戏状态
    private bool isAutoPlaying = false;
    private bool isSkipping = false;
    private bool isTextDisplaying = false;
    
    // 【新增】回放模式相关变量
    private bool isReplayMode = false;
    private string replayEndLineID = "";
    private bool wasMainMenuVisibleBeforeReplay = false; // 记录回放前主菜单是否可见

    // 跨场景数据
    private string pendingScriptName;
    private string pendingLineID;
    private SaveData pendingSaveData; // 【新增】用于跨场景加载存档
    private SaveData currentLoadingSaveData; // 【新增】当前正在加载的存档数据
    private int currentLoadingTargetIndex; // 【新增】当前正在加载的目标行索引
    private bool isListeningSceneLoad = false;
    private UnityAction onGameStartedCallback; // 【新增】游戏启动完成后的回调

    // 配置
    private bool isVoiceEnabled = true;
    private bool isTextSpeedEnabled = true;

    // 协程
    private Coroutine _flowCoroutine;
    private Coroutine _autoPlayCoroutine;

    public VNManager()
    {
        if (!isListeningSceneLoad)
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            isListeningSceneLoad = true;
        }
    }

    /// <summary>
    /// 启动游戏（会切换到VNGamePlay场景）
    /// </summary>
    /// <param name="scriptFileName">剧本文件名</param>
    /// <param name="startLineID">起始行ID（可选）</param>
    /// <param name="onGameStarted">游戏启动完成后的回调函数（可选）</param>
    public void StartGame(string scriptFileName, string startLineID = "", UnityAction onGameStarted = null)
    {
        this.pendingScriptName = scriptFileName;
        this.pendingLineID = startLineID;
        this.onGameStartedCallback = onGameStarted;

        if (SceneManager.GetActiveScene().name != "VNGamePlay")
        {
            SceneManager.LoadScene("VNGamePlay");
        }
        else
        {
            RunGameLogic();
        }
    }

    /// <summary>
    /// 在当前场景中启动游戏（不切换场景）
    /// 会在当前场景检查并创建Canvas
    /// </summary>
    /// <param name="scriptFileName">剧本文件名</param>
    /// <param name="startLineID">起始行ID（可选）</param>
    /// <param name="onGameStarted">游戏启动完成后的回调函数（可选）</param>
    public void StartGameOnScene(string scriptFileName, string startLineID = "", UnityAction onGameStarted = null)
    {
        this.pendingScriptName = scriptFileName;
        this.pendingLineID = startLineID;
        this.onGameStartedCallback = onGameStarted;

        // 确保UIManager已初始化，这样会检查并创建Canvas
        UIManager.GetInstance().Init();
        
        // 直接运行游戏逻辑，不切换场景
        RunGameLogic();
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == "VNGamePlay")
        {
            // 优先处理存档加载
            if (pendingSaveData != null)
            {
                ContinueGameInternal(pendingSaveData);
                pendingSaveData = null;
            }
            // 然后处理新游戏
            else if (!string.IsNullOrEmpty(pendingScriptName))
            {
                RunGameLogic();
            }
        }
    }

    private void RunGameLogic()
    {
        Debug.Log($"[VNManager] RunGameLogic 开始。剧本: {pendingScriptName}, 目标行: {pendingLineID}");

        InitializeManager();
        
        // 【新增】显示加载进度面板
        ShowLoadingPanelAndStartGame();
    }
    
    /// <summary>
    /// 显示加载面板并开始游戏加载流程
    /// </summary>
    private void ShowLoadingPanelAndStartGame()
    {
        // 1. 显示加载进度面板
        UIManager.GetInstance().ShowPanel<LoadingProgressPanel>(
            "LoadingProgressPanel",
            VNProjectConfig.Instance.UI_LoadingPath,
            E_UI_Layer.System,
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
            Debug.Log($"[VNManager] 检测到新剧本或首次启动，已清空历史记录。旧剧本: {previousScriptName}, 新剧本: {pendingScriptName}");
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

        // 2. 计算目标行索引 (暂不预演，只算位置)
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
        
        // 3. 显示 UI (异步过程，UIManager会自动注册并跟踪进度)
        if (StoryLines.Count > 0)
        {
            // UIManager会自动注册任务 "ui_VNGameplayPanel"，我们只需要等待它完成
            UIManager.GetInstance().ShowPanel<VNGameplayPanel>("VNGameplayPanel", VNProjectConfig.Instance.UI_VNGamePlayPath, E_UI_Layer.Middle, (panel) =>
            {
                // UI加载完成，UIManager会自动完成任务
                // 注意：这里不立即执行游戏逻辑，等待OnGameLoadingCompleted回调
            });
        }
        else
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
        }
    }
    
    /// <summary>
    /// 游戏加载完成回调
    /// </summary>
    private void OnGameLoadingCompleted()
    {
        LoadingProgressManager progressManager = LoadingProgressManager.GetInstance();
        progressManager.OnAllTasksCompleted -= OnGameLoadingCompleted;
        
        // 隐藏加载面板
        UIManager.GetInstance().HidePanel("LoadingProgressPanel");
        
        // 清理加载任务
        progressManager.ClearAllTasks();
        
        // 延迟一帧，确保UI完全初始化
        MonoManager.GetInstance().StartCoroutine(DelayedStartGameplay());
    }
    
    /// <summary>
    /// 延迟启动游戏逻辑（确保UI完全初始化）
    /// </summary>
    private System.Collections.IEnumerator DelayedStartGameplay()
    {
        yield return null; // 等待一帧
        
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
                targetIndex = 0;
            }
        }
        
        // 获取游戏面板
        VNGameplayPanel gameplayPanel = UIManager.GetInstance().GetPanel<VNGameplayPanel>("VNGameplayPanel");
        if (gameplayPanel == null)
        {
            Debug.LogError("[VNManager] 无法获取VNGameplayPanel，游戏启动失败");
            if (onGameStartedCallback != null)
            {
                onGameStartedCallback.Invoke();
                onGameStartedCallback = null;
            }
            yield break;
        }
        
        // 【修复】确保游戏状态设置为 Gameplay（场景回放时需要）
        GameStateManager.GetInstance().SetState(GameState.Gameplay);
        
        // A. 强力清理 UI 现场
        VNAPI.ClearAllEffects(); // 确保 EffectLayer 是空的
        EventCenter.GetInstance().EventTrigger("HideCharacter", "Left");
        EventCenter.GetInstance().EventTrigger("HideCharacter", "Mid");
        EventCenter.GetInstance().EventTrigger("HideCharacter", "Right");

        // 【修复】快进到目标行，如果遇到 choice 命令则停止
        bool encounteredChoice = false;
        if (targetIndex > 0)
        {
            Debug.Log($"[VNManager] UI就绪，开始预演至索引: {targetIndex}");
            encounteredChoice = FastForwardToLine(targetIndex);
        }

        // C. 设置当前行（如果遇到 choice，FastForwardToLine 已经设置了 CurrentLineIndex，不需要覆盖）
        if (!encounteredChoice)
        {
            CurrentLineIndex = targetIndex;
        }

        // D. 同步立绘显示 (FastForward 更新了 currentCharacters 数据，现在应用到 UI)
        foreach (var kvp in currentCharacters)
        {
            string[] parts = kvp.Value.Split('_');
            if (parts.Length >= 2)
            {
                Dictionary<string, string> info = new Dictionary<string, string>
                {
                    { "position", kvp.Key }, { "characterID", parts[0] }, { "emotion", parts[1] }
                };
                EventCenter.GetInstance().EventTrigger("ShowCharacter", info);
                
                // 同步翻转状态：如果该位置有保存的翻转状态，立即应用到UI
                string posCode = NormalizePositionCode(kvp.Key);
                if (currentCharactersScaleX.ContainsKey(posCode))
                {
                    float scaleX = currentCharactersScaleX[posCode];
                    RectTransform charRect = VNAPI.GetCharRect(posCode);
                    if (charRect != null)
                    {
                        Vector3 scale = charRect.localScale;
                        scale.x = scaleX;
                        charRect.localScale = scale;
                        Debug.Log($"[VNManager] 同步位置 {kvp.Key}({posCode}) 的翻转状态: {scaleX}");
                    }
                }
            }
        }

        // E. 同步背景显示
        if (!string.IsNullOrEmpty(currentBG))
        {
            EventCenter.GetInstance().EventTrigger("ChangeBackground", currentBG);
        }

        // F. 正式播放
        PlayCurrentLine();

        // G. 【新增】调用游戏启动完成回调
        if (onGameStartedCallback != null)
        {
            onGameStartedCallback.Invoke();
            onGameStartedCallback = null; // 调用后清空，避免重复调用
        }
    }
    
    private void RunGameLogic_OLD()
    {
        // 此方法已废弃，保留作为参考
        Debug.Log($"[VNManager] RunGameLogic 开始。剧本: {pendingScriptName}, 目标行: {pendingLineID}");

        InitializeManager();
        
        // 【新增】跨剧本加载时清空历史记录（新游戏或切换剧本）
        // 在设置新剧本名之前，检查是否是切换剧本
        string previousScriptName = this.currentScriptName;
        bool isNewScript = string.IsNullOrEmpty(previousScriptName) || previousScriptName != pendingScriptName;
        
        if (isNewScript)
        {
            // 新游戏或切换剧本，清空历史记录
            ClearHistoryLog();
            Debug.Log($"[VNManager] 检测到新剧本或首次启动，已清空历史记录。旧剧本: {previousScriptName}, 新剧本: {pendingScriptName}");
        }
        
        this.currentScriptName = pendingScriptName;

        // 1. 加载剧本数据 (纯数据操作)
        CommandManager.GetInstance().ExecuteCommand($"loadscript({pendingScriptName})");
        
        ResetState();

        // 2. 计算目标行索引 (暂不预演，只算位置)
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
        // 3. 显示 UI (异步过程)
        if (StoryLines.Count > 0)
        {
            UIManager.GetInstance().ShowPanel<VNGameplayPanel>("VNGameplayPanel", VNProjectConfig.Instance.UI_VNGamePlayPath, E_UI_Layer.Middle, (panel) =>
            {
                // 【修复】确保游戏状态设置为 Gameplay（场景回放时需要）
                GameStateManager.GetInstance().SetState(GameState.Gameplay);
                
                // A. 强力清理 UI 现场
                VNAPI.ClearAllEffects(); // 确保 EffectLayer 是空的
                EventCenter.GetInstance().EventTrigger("HideCharacter", "Left");
                EventCenter.GetInstance().EventTrigger("HideCharacter", "Mid");
                EventCenter.GetInstance().EventTrigger("HideCharacter", "Right");

                // 【修复】快进到目标行，如果遇到 choice 命令则停止
                bool encounteredChoice = false;
                if (targetIndex > 0)
                {
                    Debug.Log($"[VNManager] UI就绪，开始预演至索引: {targetIndex}");
                    encounteredChoice = FastForwardToLine(targetIndex);
                }

                // C. 设置当前行（如果遇到 choice，FastForwardToLine 已经设置了 CurrentLineIndex，不需要覆盖）
                if (!encounteredChoice)
                {
                    CurrentLineIndex = targetIndex;
                }

                // D. 同步立绘显示 (FastForward 更新了 currentCharacters 数据，现在应用到 UI)
                foreach (var kvp in currentCharacters)
                {
                    string[] parts = kvp.Value.Split('_');
                    if (parts.Length >= 2)
                    {
                        Dictionary<string, string> info = new Dictionary<string, string>
                        {
                            { "position", kvp.Key }, { "characterID", parts[0] }, { "emotion", parts[1] }
                        };
                        EventCenter.GetInstance().EventTrigger("ShowCharacter", info);
                        
                        // 同步翻转状态：如果该位置有保存的翻转状态，立即应用到UI
                        string posCode = NormalizePositionCode(kvp.Key);
                        if (currentCharactersScaleX.ContainsKey(posCode))
                        {
                            float scaleX = currentCharactersScaleX[posCode];
                            RectTransform charRect = VNAPI.GetCharRect(posCode);
                            if (charRect != null)
                            {
                                Vector3 scale = charRect.localScale;
                                scale.x = scaleX;
                                charRect.localScale = scale;
                                Debug.Log($"[VNManager] 同步位置 {kvp.Key}({posCode}) 的翻转状态: {scaleX}");
                            }
                        }
                    }
                }

                // E. 同步背景显示
                if (!string.IsNullOrEmpty(currentBG))
                {
                    EventCenter.GetInstance().EventTrigger("ChangeBackground", currentBG);
                }

                // F. 正式播放
                PlayCurrentLine();

                // G. 【新增】调用游戏启动完成回调
                if (onGameStartedCallback != null)
                {
                    onGameStartedCallback.Invoke();
                    onGameStartedCallback = null; // 调用后清空，避免重复调用
                }
            });
        }
        else
        {
            Debug.LogError("[VNManager] 剧本加载失败，无法启动游戏。");
            // 即使失败也调用回调，让用户知道启动失败
            if (onGameStartedCallback != null)
            {
                onGameStartedCallback.Invoke();
                onGameStartedCallback = null;
            }
        }

        // 清理参数
        pendingScriptName = null;
        pendingLineID = null;
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

        // 【Bug修复】清理音效列表，防止场景切换时引用已销毁的对象
        MusicManager.GetInstance().ClearAllSFX();

        CommandManager.GetInstance().Init();
        EventCenter.GetInstance().AddEventListener("TypingFinished", OnTypingFinished);
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
        lastValidSpeaker = "";
        lastValidText = "";
        lastValidHeadProfile = "";
        currentCharacters.Clear();
        activeEffects.Clear();
        VNAPI.ClearAllEffects(); // 物理清空特效
        currentCharactersScaleX.Clear();
        isVoiceEnabled = true;
        lastLine = null;
    }
    
    /// <summary>
    /// 清空历史记录（用于新游戏或跨剧本加载）
    /// </summary>
    private void ClearHistoryLog()
    {
        GlobalDataManager.GetInstance().ClearHistoryLog();
        Debug.Log("[VNManager] 已清空历史记录");
    }

    /// <summary>
    /// 全量状态预演 (核心逻辑)
    /// </summary>
    /// <summary>
    /// 快进到目标行
    /// </summary>
    /// <param name="targetIndex">目标行索引</param>
    /// <returns>如果遇到 choice 命令返回 true，否则返回 false</returns>
    public bool FastForwardToLine(int targetIndex)
    {
        ResetState();
        VNAPI.ClearAllEffects(); // 物理清空
        activeEffects.Clear();
        if (targetIndex <= 0) return false;

        bool encounteredChoice = false;

        // 模拟运行
        for (int i = 0; i < targetIndex; i++)
        {
            if (i >= StoryLines.Count) break;
            StoryLine line = StoryLines[i];

            // 【修复】检查是否包含 choice 命令，如果包含则停止快进
            if (!string.IsNullOrEmpty(line.Command) && ContainsChoiceCommand(line.Command))
            {
                // 遇到选项命令，停止快进，设置当前行索引为包含 choice 的行
                CurrentLineIndex = i;
                encounteredChoice = true;
                Debug.Log($"[VNManager] 快进过程中遇到选项命令，停止在第 {i} 行 (ID: {line.ID})");
                
                // 先应用当前行的状态（背景、立绘、BGM等）
                if (!string.IsNullOrEmpty(line.Background)) currentBG = line.Background;
                if (!string.IsNullOrEmpty(line.BGM))
                {
                    if (line.BGM == "stop") currentBGM = "";
                    else if (line.BGM != "pause" && line.BGM != "resume") currentBGM = line.BGM;
                }
                SimulateCharacterUpdate("Left", line.CharLeft);
                SimulateCharacterUpdate("Mid", line.CharMid);
                SimulateCharacterUpdate("Right", line.CharRight);
                if (line.Voice == "false") isVoiceEnabled = false;
                else if (!string.IsNullOrEmpty(line.Voice)) isVoiceEnabled = true;
                if (!string.IsNullOrEmpty(line.Speaker)) lastValidSpeaker = line.Speaker;
                if (!string.IsNullOrEmpty(line.Text)) lastValidText = line.Text;
                if (!string.IsNullOrEmpty(line.HeadProfile)) lastValidHeadProfile = line.HeadProfile;
                lastLine = line;
                
                // 先应用其他命令（不包括 choice）
                string otherCommands = ExtractNonChoiceCommands(line.Command);
                if (!string.IsNullOrEmpty(otherCommands))
                {
                    CommandManager.GetInstance().SimulateCommands(otherCommands);
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
            SimulateCharacterUpdate("Mid", line.CharMid);
            SimulateCharacterUpdate("Right", line.CharRight);

            // 4. 语音
            if (line.Voice == "false") isVoiceEnabled = false;
            else if (!string.IsNullOrEmpty(line.Voice)) isVoiceEnabled = true;

            // 5. Command 模拟 (特效、Flags 等)
            if (!string.IsNullOrEmpty(line.Command))
            {
                CommandManager.GetInstance().SimulateCommands(line.Command);
            }

            if (!string.IsNullOrEmpty(line.Speaker)) lastValidSpeaker = line.Speaker;
            if (!string.IsNullOrEmpty(line.Text)) lastValidText = line.Text;
            
            // 更新 lastValidHeadProfile（包括 "hide"）
            if (!string.IsNullOrEmpty(line.HeadProfile))
            {
                lastValidHeadProfile = line.HeadProfile;
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
        
        // 分割命令（使用 & 分隔符）
        string[] commands = commandString.Split('&');
        System.Collections.Generic.List<string> nonChoiceCommands = new System.Collections.Generic.List<string>();
        
        foreach (string cmd in commands)
        {
            string trimmedCmd = cmd.Trim();
            if (string.IsNullOrEmpty(trimmedCmd)) continue;
            
            // 检查是否是 choice 命令
            int startIndex = trimmedCmd.IndexOf('(');
            if (startIndex > 0)
            {
                string cmdName = trimmedCmd.Substring(0, startIndex).Trim().ToLower();
                if (cmdName != "choice")
                {
                    nonChoiceCommands.Add(trimmedCmd);
                }
            }
            else
            {
                // 没有括号的命令也保留
                if (!trimmedCmd.ToLower().StartsWith("choice"))
                {
                    nonChoiceCommands.Add(trimmedCmd);
                }
            }
        }
        
        // 重新组合命令字符串
        return string.Join("&", nonChoiceCommands);
    }

    private void SimulateCharacterUpdate(string pos, string data)
    {
        if (string.IsNullOrEmpty(data)) return;

        // 标准化位置代码：内部统一使用 "Left"/"Mid"/"Right" 存储角色数据
        // 但翻转状态使用 "L"/"M"/"R" 存储
        string normalizedPos = pos; // 角色数据保持原格式
        string normalizedPosCode = NormalizePositionCode(pos); // 翻转状态使用标准化格式

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

    // 位置代码转换工具函数
    private string NormalizePositionCode(string pos)
    {
        if (string.IsNullOrEmpty(pos)) return pos;
        string upper = pos.ToUpper();
        if (upper == "LEFT" || upper == "L") return "L";
        if (upper == "MID" || upper == "MIDDLE" || upper == "M") return "M";
        if (upper == "RIGHT" || upper == "R") return "R";
        return pos; // 未知格式，原样返回
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

        Debug.Log($"[VNManager] 自动恢复特效: {commandString}");
    }

    public void SetScriptData(List<StoryLine> lines, Dictionary<string, int> idMap, string scriptName)
    {
        this.StoryLines = lines;
        this.LineIDIndexMap = idMap;
        this.CurrentLineIndex = 0;
        this.lastLine = null;
        this.currentScriptName = scriptName;
    }

    /// <summary>
    /// 继续游戏（加载存档）
    /// </summary>
    public void ContinueGame(SaveData saveData)
    {
        // 【核心修复】先检查场景，如果不在VNGamePlay场景，先加载场景
        if (SceneManager.GetActiveScene().name != "VNGamePlay")
        {
            // 保存存档数据，等待场景加载完成后再恢复
            pendingSaveData = saveData;
            SceneManager.LoadScene("VNGamePlay");
            return;
        }
        
        // 如果已经在VNGamePlay场景，直接执行
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
        UIManager.GetInstance().ShowPanel<LoadingProgressPanel>(
            "LoadingProgressPanel",
            VNProjectConfig.Instance.UI_LoadingPath,
            E_UI_Layer.System,
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
            Debug.Log($"[VNManager] 已恢复 {saveData.HistoryLog.Count} 条历史记录");
        }
        else
        {
            // 如果存档中没有历史记录，清空当前的历史记录（防止残留）
            GlobalDataManager.GetInstance().ClearHistoryLog();
        }

        // 恢复标志
        if (saveData.Flags != null)
        {
            GlobalDataManager.GetInstance().GetGlobalData().Flags = new Dictionary<string, bool>(saveData.Flags);
        }
        
        if (saveData.IntFlags != null)
        {
            GlobalDataManager.GetInstance().GetGlobalData().IntFlags = new Dictionary<string, int>(saveData.IntFlags);
        }
        
        if (saveData.StringFlags != null)
        {
            GlobalDataManager.GetInstance().GetGlobalData().StringFlags = new Dictionary<string, string>(saveData.StringFlags);
        }

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
            UIManager.GetInstance().ShowPanel<VNGameplayPanel>("VNGameplayPanel", VNProjectConfig.Instance.UI_VNGamePlayPath, E_UI_Layer.Middle, (panel) =>
            {
                // UI加载完成，UIManager会自动完成任务
                // 注意：这里不立即执行游戏逻辑，等待OnContinueGameLoadingCompleted回调
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
    /// 继续游戏加载完成回调
    /// </summary>
    private void OnContinueGameLoadingCompleted()
    {
        LoadingProgressManager progressManager = LoadingProgressManager.GetInstance();
        progressManager.OnAllTasksCompleted -= OnContinueGameLoadingCompleted;
        
        // 隐藏加载面板
        UIManager.GetInstance().HidePanel("LoadingProgressPanel");
        
        // 清理加载任务
        progressManager.ClearAllTasks();
        
        // 延迟一帧，确保UI完全初始化
        MonoManager.GetInstance().StartCoroutine(DelayedContinueGameplay());
    }
    
    /// <summary>
    /// 延迟继续游戏逻辑（确保UI完全初始化）
    /// </summary>
    private System.Collections.IEnumerator DelayedContinueGameplay()
    {
        yield return null; // 等待一帧
        
        // 获取游戏面板
        VNGameplayPanel gameplayPanel = UIManager.GetInstance().GetPanel<VNGameplayPanel>("VNGameplayPanel");
        if (gameplayPanel == null)
        {
            Debug.LogError("[VNManager] 无法获取VNGameplayPanel，继续游戏失败");
            currentLoadingSaveData = null;
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
    
    /// <summary>
    /// 从存档恢复游戏状态（UI已准备好）
    /// </summary>
    private void RestoreGameStateFromSave(SaveData saveData, int targetIndex)
    {
        // 清理UI现场
        VNAPI.ClearAllEffects();
        EventCenter.GetInstance().EventTrigger("HideCharacter", "Left");
        EventCenter.GetInstance().EventTrigger("HideCharacter", "Mid");
        EventCenter.GetInstance().EventTrigger("HideCharacter", "Right");

        // 【修复】如果目标行索引大于0，需要预演到该位置，如果遇到 choice 命令则停止
        bool encounteredChoice = false;
        if (targetIndex > 0)
        {
            Debug.Log($"[VNManager] 从存档恢复，预演至索引: {targetIndex}");
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
            EventCenter.GetInstance().EventTrigger("ChangeBackground", currentBG);
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

        // 设置当前行索引并播放
        CurrentLineIndex = targetIndex;
        PlayCurrentLine();
    }

    private void PlayCurrentLine()
    {
        if (CurrentLineIndex < 0 || CurrentLineIndex >= StoryLines.Count)
        {
            // 【新增】如果超出范围，检查是否是回放模式
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

        // 【新增】恢复所有被修改的角色 Transform 到默认值（setchartrans 命令不继承）
        // 【新增】恢复对话文本的默认颜色和大小（t_color 和 t_size 命令不继承）
        var gameplayPanel = UIManager.GetInstance().GetPanel<VNGameplayPanel>("VNGameplayPanel");
        if (gameplayPanel != null)
        {
            gameplayPanel.RestoreDefaultCharTransforms();
            gameplayPanel.RestoreDefaultTextProperties();
        }

        StoryLine currentLine = StoryLines[CurrentLineIndex];
        ApplyInheritance(currentLine);
        lastLine = currentLine;

        UpdateVisualState(currentLine);
        UpdateAudioState(currentLine);
        UpdateDialogue(currentLine);

        GlobalDataManager.GetInstance().AddReadLineID(currentLine.ID);

        if (!string.IsNullOrEmpty(currentLine.Command))
        {
            _flowCoroutine = MonoManager.GetInstance().StartCoroutine(ExecuteActionsAndContinue(currentLine.Command));
        }
        else
        {
            CheckAndTriggerAutoPlay();
        }
    }

    private IEnumerator ExecuteActionsAndContinue(string actionString)
    {
        int preIndex = CurrentLineIndex;
        yield return CommandManager.GetInstance().ExecuteCommandsAsync(actionString);
        _flowCoroutine = null;

        // 【修复】检查游戏状态，如果是 Choice 状态，不应该继续前进或触发自动播放
        GameStateManager stateManager = GameStateManager.GetInstance();
        if (stateManager != null && stateManager.CurrentState == GameState.Choice)
        {
            // 在 Choice 状态下，等待玩家选择，不继续前进
            Debug.Log("[VNManager] 命令执行完成，当前处于 Choice 状态，停止继续前进");
            yield return null;
        }

        if (CurrentLineIndex != preIndex) PlayCurrentLine();
        else CheckAndTriggerAutoPlay();
    }

    private void CheckAndTriggerAutoPlay()
    {
        // 【修复】检查游戏状态，如果是 Choice 状态，不应该触发自动播放
        GameStateManager stateManager = GameStateManager.GetInstance();
        if (stateManager != null && stateManager.CurrentState == GameState.Choice)
        {
            // 在 Choice 状态下，等待玩家选择，不触发自动播放
            return;
        }
        
        bool isBusy = isTextDisplaying || CommandManager.GetInstance().IsRunning || _flowCoroutine != null;
        if (isAutoPlaying && !isBusy && _autoPlayCoroutine == null)
        {
            float delay = GlobalDataManager.GetInstance().GetGlobalData().AutoSpeed;
            _autoPlayCoroutine = MonoManager.GetInstance().StartCoroutine(AutoPlayCountdown(delay));
        }
    }

    private IEnumerator AutoPlayCountdown(float delay)
    {
        yield return new WaitForSeconds(delay);
        _autoPlayCoroutine = null;
        CurrentLineIndex++;
        PlayCurrentLine();
    }

    public void NextLine()
    {
        // 【修复】检查游戏状态，如果是 Choice 状态，不应该继续前进
        GameStateManager stateManager = GameStateManager.GetInstance();
        if (stateManager != null && stateManager.CurrentState == GameState.Choice)
        {
            // 在 Choice 状态下，等待玩家选择，不继续前进
            Debug.Log("[VNManager] 当前处于 Choice 状态，等待玩家选择，暂停前进");
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
            CheckAndTriggerAutoPlay();
            return;
        }

        if (isTextDisplaying)
        {
            EventCenter.GetInstance().EventTrigger("DisplayAllText");
            return;
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

        CurrentLineIndex++;
        PlayCurrentLine();
    }

    private void ApplyInheritance(StoryLine currentLine)
    {
        if (string.IsNullOrEmpty(currentLine.Speaker))
        {
            currentLine.Speaker = lastValidSpeaker;
        }
        else
        {
            lastValidSpeaker = currentLine.Speaker;
        }

        if (string.IsNullOrEmpty(currentLine.Text))
        {
            currentLine.Text = lastValidText;
        }
        else
        {
            lastValidText = currentLine.Text;
        }
        
        // HeadProfile 继承：如果当前行为空，继承上一个有效的 HeadProfile（包括 "hide"）
        if (string.IsNullOrEmpty(currentLine.HeadProfile))
        {
            if (!string.IsNullOrEmpty(lastValidHeadProfile))
            {
                currentLine.HeadProfile = lastValidHeadProfile;
            }
        }
        else
        {
            // 更新 lastValidHeadProfile（包括 "hide"）
            lastValidHeadProfile = currentLine.HeadProfile;
        }

        // 2. 视觉/音频继承 (依赖当前 Manager 状态)
        // 如果当前行没填，就用 Manager 现在的状态填回去
        if (string.IsNullOrEmpty(currentLine.Background))
            currentLine.Background = this.currentBG;

        if (string.IsNullOrEmpty(currentLine.CharLeft) && currentCharacters.ContainsKey("Left"))
            currentLine.CharLeft = currentCharacters["Left"];
        if (string.IsNullOrEmpty(currentLine.CharMid) && currentCharacters.ContainsKey("Mid"))
            currentLine.CharMid = currentCharacters["Mid"];
        if (string.IsNullOrEmpty(currentLine.CharRight) && currentCharacters.ContainsKey("Right"))
            currentLine.CharRight = currentCharacters["Right"];

        // 3. 语音继承
        // 逻辑：没填->自动生成；填false->关；填其他->开
        if (string.IsNullOrEmpty(currentLine.Voice))
        {
            if (!isVoiceEnabled)
            {
                currentLine.Voice = "";
            }
            else
            {
                // 只有当有 ID 时才自动生成，防止空行报错
                if (!string.IsNullOrEmpty(currentLine.ID))
                    currentLine.Voice = Path.GetDirectoryName(currentLine.ID) + "/" + currentLine.ID + ".mp3";
            }
        }
        else if (currentLine.Voice.ToLower() == "false")
        {
            isVoiceEnabled = false;
            currentLine.Voice = "";
        }
        else
        {
            isVoiceEnabled = true; // 有明确设置语音文件名，则开启
        }
    }

    private void UpdateVisualState(StoryLine currentLine)
    {
        if (!string.IsNullOrEmpty(currentLine.Background) && currentLine.Background != "hide" && currentLine.Background != "black")
        {
            currentBG = currentLine.Background;
            EventCenter.GetInstance().EventTrigger("ChangeBackground", currentLine.Background);
        }
        else if (currentLine.Background == "black")
        {
            currentBG = "black";
            EventCenter.GetInstance().EventTrigger("ChangeBackground", "black");
        }
        else if (currentLine.Background == "hide")
        {
            currentBG = "hide";
            EventCenter.GetInstance().EventTrigger("HideBackground");
        }

        UpdateCharacter("Left", currentLine.CharLeft);
        UpdateCharacter("Mid", currentLine.CharMid);
        UpdateCharacter("Right", currentLine.CharRight);
    }

    private void UpdateCharacter(string position, string charData)
    {
        if (charData == "hide")
        {
            EventCenter.GetInstance().EventTrigger("HideCharacter", position);
            if (this.currentCharacters.ContainsKey(position))
                this.currentCharacters.Remove(position);
            // 隐藏时不清除翻转状态，保持状态以便后续恢复
        }
        else if (!string.IsNullOrEmpty(charData))
        {
            string[] parts = charData.Split('_');
            if (parts.Length >= 2)
            {
                this.currentCharacters[position] = charData;
                Dictionary<string, string> info = new Dictionary<string, string>
                {
                    { "position", position }, { "characterID", parts[0] }, { "emotion", parts[1] }
                };
                EventCenter.GetInstance().EventTrigger("ShowCharacter", info);
                
                // 如果该位置有保存的翻转状态，立即应用到UI
                string posCode = NormalizePositionCode(position);
                if (currentCharactersScaleX.ContainsKey(posCode))
                {
                    float scaleX = currentCharactersScaleX[posCode];
                    RectTransform charRect = VNAPI.GetCharRect(posCode);
                    if (charRect != null)
                    {
                        Vector3 scale = charRect.localScale;
                        scale.x = scaleX;
                        charRect.localScale = scale;
                        Debug.Log($"[VNManager] 应用位置 {position}({posCode}) 的翻转状态: {scaleX}");
                    }
                }
            }
        }
    }

    private void UpdateAudioState(StoryLine currentLine)
    {
        if (!string.IsNullOrEmpty(currentLine.BGM))
        {
            if (currentLine.BGM == "stop") { MusicManager.GetInstance().StopBGM(); currentBGM = ""; }
            else if (currentLine.BGM == "pause") MusicManager.GetInstance().PauseBGM();
            else if (currentLine.BGM == "resume") MusicManager.GetInstance().PlayBGM(currentBGM);
            else { MusicManager.GetInstance().PlayBGM(currentLine.BGM); currentBGM = currentLine.BGM; }
        }

        // 【Bug修复】优化语音加载，添加空值和路径检查
        if (!string.IsNullOrEmpty(currentLine.Voice))
        {
            // 检查VoiceManager是否已初始化
            if (VoiceManager.GetInstance() != null)
            {
                // 检查语音路径是否有效（不包含无效字符）
                string voicePath = currentLine.Voice.Trim();
                if (!string.IsNullOrEmpty(voicePath) && !voicePath.Contains("://"))
                {
                    VoiceManager.GetInstance().PlayVoice(voicePath);
                }
                else
                {
                    Debug.LogWarning($"[VNManager] 无效的语音路径: {voicePath}");
                }
            }
            else
            {
                Debug.LogWarning("[VNManager] VoiceManager未初始化，无法播放语音");
            }
        }
    }

    private void UpdateDialogue(StoryLine currentLine)
    {
        Dictionary<string, string> info = new Dictionary<string, string>
        {
            { "speaker", currentLine.Speaker }, { "text", currentLine.Text }
        };
        EventCenter.GetInstance().EventTrigger("UpdateDialogue", info);
        
        // 更新 HeadProfile（无论是否为 "hide" 都会触发事件）
        Dictionary<string, string> headProfileInfo = new Dictionary<string, string>
        {
            { "headProfile", string.IsNullOrEmpty(currentLine.HeadProfile) ? "hide" : currentLine.HeadProfile },
            { "speaker", currentLine.Speaker }
        };
        EventCenter.GetInstance().EventTrigger("UpdateHeadProfile", headProfileInfo);

        isTextDisplaying = true;
        AddHistoryEntry(currentLine.Speaker, currentLine.Text, currentLine.Voice);
    }

    public void UpdateCurrentBG_OnlyData(string bgName)
    {
        this.currentBG = bgName;
    }

    public void ToggleAutoPlay()
    {
        isAutoPlaying = !isAutoPlaying;
        EventCenter.GetInstance().EventTrigger("ToggleAutoPlay", isAutoPlaying);
        CheckAndTriggerAutoPlay();
    }

    public void ToggleSkip()
    {
        isSkipping = !isSkipping;
        EventCenter.GetInstance().EventTrigger("ToggleSkip", isSkipping);
    }

    public void SaveGame(int slotIndex)
    {
        SaveData saveData = new SaveData();
        saveData.ScriptFileName = this.currentScriptName;
        saveData.LineID = lastLine != null ? lastLine.ID : "";
        saveData.CurrentBG = this.currentBG;
        saveData.CurrentBGM = this.currentBGM;
        saveData.Characters = new Dictionary<string, string>(this.currentCharacters);
        saveData.CharacterScaleX = new Dictionary<string, float>(this.currentCharactersScaleX);
        saveData.Flags = new Dictionary<string, bool>(GlobalDataManager.GetInstance().GetGlobalData().Flags);
        saveData.IntFlags = new Dictionary<string, int>(GlobalDataManager.GetInstance().GetGlobalData().IntFlags);
        saveData.StringFlags = new Dictionary<string, string>(GlobalDataManager.GetInstance().GetGlobalData().StringFlags);
        saveData.ActiveEffects = new List<string>(this.activeEffects); // 保存特效
        
        // 保存历史记录
        List<HistoryEntry> historyLog = GlobalDataManager.GetInstance().GetHistoryLog();
        if (historyLog != null)
        {
            saveData.HistoryLog = new List<HistoryEntry>(historyLog);
            Debug.Log($"[VNManager] 保存了 {historyLog.Count} 条历史记录");
            
            // 验证历史记录数据
            if (historyLog.Count > 0)
            {
                var firstEntry = historyLog[0];
                Debug.Log($"[VNManager] 第一条历史记录示例 - Speaker: {firstEntry?.Speaker ?? "null"}, Text: {firstEntry?.Text?.Substring(0, Mathf.Min(20, firstEntry.Text?.Length ?? 0)) ?? "null"}");
            }
        }
        else
        {
            saveData.HistoryLog = new List<HistoryEntry>();
            Debug.LogWarning("[VNManager] 历史记录为null，已初始化为空列表");
        }
        
        saveData.SaveTime = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        saveData.ScreenshotPath = SaveManager.GetInstance().SaveCachedScreenshot(slotIndex);

        SaveManager.GetInstance().SaveGame(slotIndex, saveData);
    }

    private void AddHistoryEntry(string speaker, string text, string voiceID)
    {
        GlobalDataManager.GetInstance().AddHistoryLog(speaker, text, voiceID);
        HistoryEntry entry = new HistoryEntry(speaker, text, voiceID);
        EventCenter.GetInstance().EventTrigger("AddHistoryEntry", entry);
    }

    public void ExecuteChoiceCommand(string command)
    {
        if (!string.IsNullOrEmpty(command))
            MonoManager.GetInstance().StartCoroutine(ExecuteActionsAndContinue(command));
        else
            PlayCurrentLine();
    }

    public bool IsAutoPlaying() { return isAutoPlaying; }
    public bool IsSkipping() { return isSkipping; }
    public bool IsTextDisplaying() { return isTextDisplaying; }

    public void SetConfig(string key, string value)
    {
        switch (key.ToLower())
        {
            case "voice": isVoiceEnabled = value.ToLower() == "true"; break;
            case "textspeed": isTextSpeedEnabled = value.ToLower() == "true"; break;
        }
    }
    #region API供外部调用
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
        // 需要同时检查 "L"/"M"/"R" 和 "Left"/"Mid"/"Right" 两种格式
        if (currentCharacters.ContainsKey(normalized))
            return currentCharacters[normalized];
        // 如果 normalized 是 "L"，也检查 "Left"
        if (normalized == "L" && currentCharacters.ContainsKey("Left"))
            return currentCharacters["Left"];
        if (normalized == "M" && currentCharacters.ContainsKey("Mid"))
            return currentCharacters["Mid"];
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
        Debug.Log($"[VNManager] 记录主菜单状态: {wasMainMenuVisibleBeforeReplay}");
        
        // 复用 StartGameOnScene 逻辑，但带上回放标记
        StartGameOnScene(scriptName, startID, () =>
        {
            Debug.Log($"[VNManager] 场景回放已启动: {scriptName}, 从 {startID} 到 {endID}");
        });
    }
    
    /// <summary>
    /// 结束场景回放
    /// </summary>
    private void EndReplay()
    {
        Debug.Log("[VNManager] 场景回放结束，开始清理状态");
        

        ResetReplayState();

        isReplayMode = false;
        replayEndLineID = "";

        PrimeTween.Tween.StopAll();
        VNAPI.ClearAllEffects();
        PoolManager.GetInstance().Clear();

        // 关闭游戏面板
        UIManager.GetInstance().HidePanel("VNGameplayPanel");
        
        // 【修复2】重新显示画廊面板（如果之前被隐藏了）
        GalleryPanel galleryPanel = UIManager.GetInstance().GetPanel<GalleryPanel>("GalleryPanel");
        if (galleryPanel != null)
        {
            // 面板已存在，直接显示
            galleryPanel.gameObject.SetActive(true);
            galleryPanel.SwitchPage(GalleryPanel.GalleryPage.Scene);
        }
        else
        {
            // 面板不存在，重新加载
            string galleryPath = VNProjectConfig.Instance != null 
                ? VNProjectConfig.Instance.UI_GalleryPath 
                : "VNPrefabs/UI/Gallery";
            if (string.IsNullOrEmpty(galleryPath)) galleryPath = "VNPrefabs/UI/Gallery";
            
            UIManager.GetInstance().ShowPanel<GalleryPanel>("GalleryPanel", galleryPath, E_UI_Layer.Middle, (panel) =>
            {
                // 切换到场景回放页面
                if (panel != null)
                {
                    panel.SwitchPage(GalleryPanel.GalleryPage.Scene);
                }
            });
        }
        
        // 【修复3】恢复主菜单面板（如果回放前是可见的）
        Debug.Log($"[VNManager] 检查是否需要恢复主菜单: wasMainMenuVisibleBeforeReplay = {wasMainMenuVisibleBeforeReplay}");
        if (wasMainMenuVisibleBeforeReplay)
        {
            MainMenuPanel mainMenuPanel = UIManager.GetInstance().GetPanel<MainMenuPanel>("MainMenuPanel");
            if (mainMenuPanel != null)
            {
                mainMenuPanel.gameObject.SetActive(true);
                Debug.Log("[VNManager] 主菜单面板已恢复显示");
            }
            else
            {
                Debug.LogWarning("[VNManager] 主菜单面板不存在，无法恢复");
            }
            wasMainMenuVisibleBeforeReplay = false; // 重置标志
        }
        else
        {
            Debug.Log("[VNManager] 回放前主菜单不可见，不恢复");
        }
    }
    
    /// <summary>
    /// 重置场景回放状态（清理所有回放产生的状态和效果）
    /// </summary>
    private void ResetReplayState()
    {
        Debug.Log("[VNManager] 开始重置场景回放状态");
        
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
        EventCenter.GetInstance().EventTrigger("HideCharacter", "Left");
        EventCenter.GetInstance().EventTrigger("HideCharacter", "Mid");
        EventCenter.GetInstance().EventTrigger("HideCharacter", "Right");
        currentCharacters.Clear();
        currentCharactersScaleX.Clear();
        
        // 6. 重置背景（可选：设置为黑色或隐藏）
        // EventCenter.GetInstance().EventTrigger("ChangeBackground", "black");
        currentBG = "";
        
        // 7. 重置游戏状态变量
        isAutoPlaying = false;
        isSkipping = false;
        isTextDisplaying = false;
        isVoiceEnabled = true;
        lastLine = null;
        lastValidSpeaker = "";
        lastValidText = "";
        lastValidHeadProfile = "";
        
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
        
        Debug.Log("[VNManager] 场景回放状态重置完成");
    }
    #endregion
}