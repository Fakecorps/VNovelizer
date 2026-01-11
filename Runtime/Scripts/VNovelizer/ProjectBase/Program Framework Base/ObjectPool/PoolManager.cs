using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class poolData
{
    public List<GameObject> poolList;//缓存池对象列表
    public GameObject fatherObj;//缓存池父物体

    public poolData(GameObject obj, GameObject poolObj)
    {
        //给缓存值创建父对象，并作为Pool对象的子物体
        //用于将对象在Hierarchy中分组排列，优化开发体验
        fatherObj = new GameObject(obj.name);
        fatherObj.transform.SetParent(poolObj.transform, false);
        poolList = new List<GameObject>();
        PushObj(obj);
    }

    public void PushObj(GameObject obj)
    { 
        // 【Bug修复】检查对象是否有效（使用 Unity 的 == 运算符，它会检查对象是否已被销毁）
        if (obj == null)
        {
            Debug.LogWarning("[poolData] 尝试推回空对象到缓存池");
            return;
        }
        
        // 【Bug修复】尝试访问 transform 来验证对象是否真的有效
        // 如果对象已被销毁，访问 transform 会抛出 MissingReferenceException
        try
        {
            Transform objTransform = obj.transform;
            if (objTransform == null)
            {
                Debug.LogWarning("[poolData] 对象的 transform 为 null，对象可能已被销毁");
                return;
            }
            
            //将对象放回缓存池
            poolList.Add(obj);
            //设置父对象
            objTransform.SetParent(fatherObj.transform, false);
            //失活
            obj.SetActive(false);
        }
        catch (MissingReferenceException)
        {
            Debug.LogWarning("[poolData] 对象已被销毁，无法推回缓存池");
            return;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[poolData] 推回对象到缓存池时发生错误: {e.Message}");
            return;
        }
    }

    public GameObject GetObj()
    {
        // 【Bug修复】清理所有已销毁的对象引用
        poolList.RemoveAll(obj => obj == null);
        
        // 如果列表为空，返回 null
        if (poolList.Count == 0)
        {
            return null;
        }
        
        // 取出第一个有效对象
        GameObject obj = poolList[0];
        poolList.RemoveAt(0);
        
        // 【Bug修复】再次检查对象是否有效（防止在 RemoveAll 和这里之间被销毁）
        if (obj == null)
        {
            Debug.LogWarning("[poolData] 获取到的对象已被销毁，返回 null");
            return null;
        }
        
        // 激活
        try
        {
            obj.SetActive(true);
            //设置父对象
            obj.transform.SetParent(null, false);
        }
        catch (MissingReferenceException)
        {
            Debug.LogWarning("[poolData] 对象在激活时已被销毁");
            return null;
        }
        
        return obj;
    }
}
public class PoolManager : BaseManager<PoolManager>
{
    //缓存池容器
    public Dictionary<string,poolData>poolDic = new Dictionary<string, poolData>();

    private GameObject poolObj;

    public void GetObj(string name,UnityAction<GameObject> callback)
    {
        // 【Bug修复】如果对象池存在且有对象，尝试获取
        if (poolDic.ContainsKey(name) && poolDic[name].poolList.Count > 0)
        {
            GameObject obj = poolDic[name].GetObj();
            // 如果获取到的对象有效，直接返回
            if (obj != null)
            {
                callback(obj);
                return;
            }
            // 如果对象无效（已被销毁），继续执行下面的异步加载逻辑
        }
        
        // 对象池不存在、为空或返回了无效对象时，通过异步加载资源
        ResourcesManager.GetInstance().LoadAsync<GameObject>(name, (o) =>
        { 
            if (o != null)
            {
                o.name = name;
                callback(o);
            }
            else
            {
                Debug.LogError($"[PoolManager] 无法加载资源: {name}");
                callback(null);
            }
        });
    }

    public void PushObj(string name, GameObject obj)
    {
        // 【Bug修复】检查对象是否有效，防止已销毁的对象被推回对象池
        if (obj == null)
        {
            Debug.LogWarning($"[PoolManager] 尝试推回空对象到对象池: {name}");
            return;
        }
        
        if (poolObj == null)
        {
            poolObj = new GameObject("Pool");
        }

        if (poolDic.ContainsKey(name))
        {
            poolDic[name].PushObj(obj);
        }
        else
        {
            poolDic.Add(name, new poolData(obj, poolObj));
        }
    }
    public void Clear()//清空缓存池，主要用在切换场景时
    { 
        poolDic.Clear();
        poolObj = null;

    }
}
