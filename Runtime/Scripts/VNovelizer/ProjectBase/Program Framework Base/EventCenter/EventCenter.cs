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
        if (eventDic.ContainsKey(name))//如果有该监听
        { 
            (eventDic[name] as EventInfo<T>).actions += action;//则添加到委托函数中
        }
        else
        {
            eventDic.Add(name, new EventInfo<T>(action));//如果没有，则创建新的监听到字典中
        }
    }

    //移出事件监听
    public void RemoveEventListener<T>(string name, UnityAction<T> action)
    {
        if (eventDic.ContainsKey(name))//如果有该监听
        {
            (eventDic[name] as EventInfo<T>).actions -= action;//则从委托函数中移除
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
                eInfo.actions.Invoke(info);
            }
        }
    }

    //无参添加事件监听
    public void AddEventListener(string name, UnityAction action)
    {
        if (eventDic.ContainsKey(name))//如果有该监听
        {
            (eventDic[name] as EventInfo).actions += action;//则添加到委托函数中
        }
        else
        {
            eventDic.Add(name, new EventInfo(action));//如果没有，则创建新的监听到字典中
        }
    }

    //无参移出事件监听
    public void RemoveEventListener(string name, UnityAction action)
    {
        if (eventDic.ContainsKey(name))//如果有该监听
        {
            (eventDic[name] as EventInfo).actions -= action;//则从委托函数中移除
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
                eInfo.actions.Invoke();
            }
        }
    }
    //清空事件中心
    public void Clear()
    { 
        eventDic.Clear(); 
    }

}
