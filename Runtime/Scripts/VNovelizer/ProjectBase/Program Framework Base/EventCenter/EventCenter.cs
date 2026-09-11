using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public interface IEventInfo
{ 

}

public class EventInfo<T> : IEventInfo
{ 
    public UnityAction<T> actions;
    public EventInfo(UnityAction<T> action)
    {
        actions += action;
    }
}

public class EventInfo: IEventInfo
{
    public UnityAction actions;
    public EventInfo(UnityAction action)
    {
        actions += action;
    }
}

public class EventCenter : BaseManager<EventCenter>
{
    private Dictionary<string,IEventInfo> eventDic = new Dictionary<string, IEventInfo>();

    //添加事件监听
    public void AddEventListener<T>(string name, UnityAction<T> action) 
    {
        if (eventDic.TryGetValue(name, out var info))//如果有该监听
        { 
            // 【Fix-40】签名冲突保护：同名事件先以另一种签名注册时 as 转换失败返回 null，
            // 直接解引用会 NRE；此处显式报错并忽略本次监听（触发侧已有 as 判空，静默不触发）。
            if (info is EventInfo<T> typed) { typed.actions += action; return; }
            Debug.LogError($"[EventCenter] 事件 '{name}' 已按其他签名注册（泛型/非泛型混用），类型冲突，忽略本次监听");
            return;
        }
        eventDic.Add(name, new EventInfo<T>(action));//如果没有，则创建新的监听到字典中
    }

    //移出事件监听
    public void RemoveEventListener<T>(string name, UnityAction<T> action)
    {
        if (eventDic.TryGetValue(name, out var info))//如果有该监听
        {
            // 【Fix-40】同上：as 失败时静默返回而非 NRE
            if (info is EventInfo<T> typed && typed.actions != null)
                typed.actions -= action;//则从委托函数中移除
        }
    }
    //添加事件触发
    public void EventTrigger<T>(string name, T info)
    {
        if (eventDic.ContainsKey(name))
        {
            // 尝试转换
            EventInfo<T> eInfo = eventDic[name] as EventInfo<T>;

            if (eInfo != null && eInfo.actions != null)
            {
                // 【Fix-41】逐个隔离调用：任一监听者抛异常不再中断其余监听者
                var invocations = eInfo.actions.GetInvocationList();
                for (int i = 0; i < invocations.Length; i++)
                {
                    try
                    {
                        ((UnityAction<T>)invocations[i]).Invoke(info);
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogError($"[EventCenter] 事件 '{name}' 监听者异常: {e}");
                    }
                }
            }
        }
    }

    //无参添加事件监听
    public void AddEventListener(string name, UnityAction action)
    {
        if (eventDic.TryGetValue(name, out var info))//如果有该监听
        {
            // 【Fix-40】签名冲突保护（无参版）
            if (info is EventInfo typed) { typed.actions += action; return; }
            Debug.LogError($"[EventCenter] 事件 '{name}' 已按其他签名注册（泛型/非泛型混用），类型冲突，忽略本次监听");
            return;
        }
        eventDic.Add(name, new EventInfo(action));//如果没有，则创建新的监听到字典中
    }

    //无参移出事件监听
    public void RemoveEventListener(string name, UnityAction action)
    {
        if (eventDic.TryGetValue(name, out var info))//如果有该监听
        {
            // 【Fix-40】同上：as 失败时静默返回而非 NRE
            if (info is EventInfo typed && typed.actions != null)
                typed.actions -= action;//则从委托函数中移除
        }
    }
    //无参触发事件
    public void EventTrigger(string name)
    {
        if (eventDic.ContainsKey(name))
        {
            // 尝试转换
            EventInfo eInfo = eventDic[name] as EventInfo;

            if (eInfo != null && eInfo.actions != null)
            {
                // 【Fix-41】逐个隔离调用：任一监听者抛异常不再中断其余监听者
                var invocations = eInfo.actions.GetInvocationList();
                for (int i = 0; i < invocations.Length; i++)
                {
                    try
                    {
                        ((UnityAction)invocations[i]).Invoke();
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogError($"[EventCenter] 事件 '{name}' 监听者异常: {e}");
                    }
                }
            }
        }
    }
    //清空事件中心
    public void Clear()
    { 
        eventDic.Clear(); 
    }

}
