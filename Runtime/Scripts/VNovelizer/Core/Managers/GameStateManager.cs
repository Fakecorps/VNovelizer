using UnityEngine;
using System.Collections.Generic;

public enum GameState
{
    Gameplay,   // 正常游戏，可以点下一句
    History,    // 历史记录打开中
    SaveLoad,   // 存档读档打开中
    Settings,   // 设置打开中
    AutoPlay,   // 自动播放中 (虽然这也是一种 Gameplay，但有时需要区分)
    Choice,
    System,     // 系统菜单 (比如暂停)
    Pause       // 暂停菜单
}

/// <summary>
/// 状态对，用于保存状态和其对应的previousState
/// </summary>
public struct StatePair
{
    public GameState state;
    public GameState previousState;
    
    public StatePair(GameState s, GameState prev)
    {
        state = s;
        previousState = prev;
    }
}

public class GameStateManager : BaseManager<GameStateManager>
{
    private GameState currentState = GameState.Gameplay;

    // 记录上一个状态，方便关闭面板时恢复
    private GameState previousState = GameState.Gameplay;
    
    // 【新增】状态栈，用于管理嵌套的状态切换（如从Pause打开SaveLoad）
    // 保存状态对（state, previousState），以便完整恢复
    private Stack<StatePair> stateStack = new Stack<StatePair>();

    public GameState CurrentState => currentState;
    
    /// <summary>
    /// 检查状态栈是否为空
    /// </summary>
    public bool IsStateStackEmpty()
    {
        return stateStack.Count == 0;
    }

    /// <summary>
    /// 切换状态
    /// </summary>
    public void SetState(GameState newState)
    {
        if (currentState == newState) return;

        previousState = currentState;
        currentState = newState;

        Debug.Log($"[GameState] 切换状态: {previousState} -> {currentState}");

        // 可以在这里广播事件，通知所有 UI 更新交互状态
        EventCenter.GetInstance().EventTrigger("GameStateChanged", currentState);
    }
    
    /// <summary>
    /// 推送状态到栈（用于嵌套状态，如从Pause打开SaveLoad）
    /// </summary>
    public void PushState(GameState newState)
    {
        if (currentState == newState) return;
        
        // 将当前状态和其previousState一起压入栈（保存完整的状态信息）
        StatePair statePair = new StatePair(currentState, previousState);
        stateStack.Push(statePair);
        
        // 更新状态
        previousState = currentState;
        currentState = newState;
        
        Debug.Log($"[GameState] 推送状态: {previousState} -> {currentState} (栈深度: {stateStack.Count}, 保存的previousState: {statePair.previousState})");
        
        // 可以在这里广播事件，通知所有 UI 更新交互状态
        EventCenter.GetInstance().EventTrigger("GameStateChanged", currentState);
    }
    
    /// <summary>
    /// 从栈中弹出状态（用于关闭嵌套的面板）
    /// </summary>
    public void PopState()
    {
        if (stateStack.Count > 0)
        {
            // 从栈中弹出状态对，完整恢复状态信息
            StatePair poppedPair = stateStack.Pop();
            // 恢复之前的状态和其对应的previousState
            previousState = poppedPair.previousState; // 恢复原始的previousState（如Gameplay）
            currentState = poppedPair.state; // 恢复之前的状态（如Pause）
            
            Debug.Log($"[GameState] 弹出状态: {previousState} -> {currentState} (栈深度: {stateStack.Count})");
            
            // 可以在这里广播事件，通知所有 UI 更新交互状态
            EventCenter.GetInstance().EventTrigger("GameStateChanged", currentState);
        }
        else
        {
            // 栈为空，使用默认的RestoreState逻辑
            RestoreState();
        }
    }

    /// <summary>
    /// 恢复上一个状态 (通常用于关闭面板后)
    /// </summary>
    public void RestoreState()
    {
        SetState(previousState);
    }

    /// <summary>
    /// 检查是否可以进行游戏交互 (点击下一句)
    /// </summary>
    public bool CanInteractGameplay()
    {
        return currentState == GameState.Gameplay || currentState == GameState.AutoPlay;
    }
    
    /// <summary>
    /// 检查是否可以打开指定状态的面板
    /// 如果当前状态是其他面板状态（History、SaveLoad、Settings等），则不允许打开
    /// </summary>
    /// <param name="targetState">要打开的面板状态</param>
    /// <returns>是否可以打开</returns>
    public bool CanOpenPanel(GameState targetState)
    {
        // 如果目标状态是Gameplay或AutoPlay，总是允许（这些不是面板状态）
        if (targetState == GameState.Gameplay || targetState == GameState.AutoPlay)
            return true;
        
        // 如果当前状态已经是目标状态，允许切换（关闭/打开）
        if (currentState == targetState)
            return true;
        
        // 如果当前状态是Gameplay或AutoPlay，允许打开任何面板
        if (currentState == GameState.Gameplay || currentState == GameState.AutoPlay)
            return true;
        
        // 【新增】如果当前状态是Choice，允许打开Pause、SaveLoad和Settings面板
        if (currentState == GameState.Choice)
        {
            if (targetState == GameState.Pause || targetState == GameState.SaveLoad || targetState == GameState.Settings)
                return true;
        }
        
        // 【新增】如果当前状态是Pause，允许打开SaveLoad和Settings面板
        if (currentState == GameState.Pause)
        {
            if (targetState == GameState.SaveLoad || targetState == GameState.Settings)
                return true;
        }
        
        // 如果当前状态是其他面板状态（History、SaveLoad、Settings等），不允许打开新面板
        return false;
    }
    
    /// <summary>
    /// 检查当前是否在面板状态（非游戏状态）
    /// </summary>
    public bool IsInPanelState()
    {
        return currentState == GameState.History || 
               currentState == GameState.SaveLoad || 
               currentState == GameState.Settings ||
               currentState == GameState.Choice ||
               currentState == GameState.System ||
               currentState == GameState.Pause;
    }
}