using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;
using Unity.VisualScripting;
/// <summary>
/// 1.面板基类
/// 2.找到所有自己面板下的控件
/// 3.提供显示，隐藏的行为等功能
/// /// </summary>
public class BasePanel : MonoBehaviour
{
    private Dictionary<string,List<UIBehaviour>> controlDic = new Dictionary<string, List<UIBehaviour>>();
    protected virtual void Awake()
    {
        FindChildrenControl<Button>();
        FindChildrenControl<TMP_Text>();
        FindChildrenControl<Text>();
        FindChildrenControl<Image>();
        FindChildrenControl<Toggle>();
        FindChildrenControl<Slider>();
        FindChildrenControl<InputField>();
        FindChildrenControl<ScrollRect>();
    }

    protected virtual void OnEnable()
    { 
        
    }

    //得到对应控件
    protected T GetControl<T>(string controlName) where T : UIBehaviour
    {
        if (controlDic.ContainsKey(controlName))
        {
            for (int i = 0; i < controlDic[controlName].Count; ++i)
            {
                if (controlDic[controlName][i] is T)
                { 
                    return controlDic[controlName][i] as T;
                }
            }
        }
        return null;
    }

    protected virtual void OnButtonClick(string ButtonName)
    { 
        
    }

    protected virtual void OnValueChanged(string ToggleName,bool value)
    { 
        
    }

    private void FindChildrenControl<T>() where T : UIBehaviour
    {
        T[] controls = this.GetComponentsInChildren<T>(true);
        for (int i = 0; i < controls.Length; ++i)
        {
            string controlName = controls[i].gameObject.name;
            if (controlDic.ContainsKey(controlName))
            {
                controlDic[controlName].Add(controls[i]);
            }
            else
            {
                controlDic.Add(controlName, new List<UIBehaviour>() { controls[i] });
            }

            //按钮控件自动监听
            if (controls[i] is Button)
            {
                (controls[i] as Button).onClick.AddListener(() =>
                {
                    OnButtonClick(controlName);
                });
            }
            //选择框控件自动监听
            else if (controls[i] is Toggle)
            {
                (controls[i] as Toggle).onValueChanged.AddListener((value) =>
                { 
                    OnValueChanged(controlName,value);
                });
            }
        }
    }

    public virtual void ShowMe()
    { 
        
    }

    public virtual void HideMe()
    { 
        
    }
}
