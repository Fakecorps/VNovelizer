using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor.Events;

public class InputManager : BaseManager<InputManager>
{

    private bool isInputStart = false;

    public InputManager()
    {
        MonoManager.GetInstance().AddUpdateListener(Update);
    }


    public void StartInputCheck(bool isOpen)
    {
        isInputStart = isOpen;
    }
    private void CheckKeyCode(KeyCode Key)
    {
        if (Input.GetKeyDown(Key))
        {
            EventCenter.GetInstance().EventTrigger("某键按下", Key);
        }
        if (Input.GetKeyUp(Key))
        { 
            EventCenter.GetInstance().EventTrigger("某键抬起", Key);
        }
    }
    public void Update()
    {
        if (!isInputStart)
        {
            return;
        }
        CheckKeyCode(KeyCode.Space);
    }
}
