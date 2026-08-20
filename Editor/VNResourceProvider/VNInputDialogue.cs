using UnityEditor;
using UnityEngine;

/// <summary>
/// 轻量文本输入对话框（模态）。用于资源管理器的逻辑名重命名等场景。
/// 用法：VNInputDialogue.Show("重命名", "新名称", name => { ... });
/// </summary>
public class VNInputDialogue : EditorWindow
{
    private string _value;
    private string _message;
    private string _okButton;
    private System.Action<string> _onOk;

    /// <summary>弹出模态输入框。onOk 在点击确定（值非空）时回调。</summary>
    public static void Show(string title, string message, string initialValue, string okButton, System.Action<string> onOk)
    {
        var window = CreateInstance<VNInputDialogue>();
        window.titleContent = new GUIContent(title);
        window._value = initialValue ?? "";
        window._message = message ?? "";
        window._okButton = string.IsNullOrEmpty(okButton) ? "确定" : okButton;
        window._onOk = onOk;
        window.minSize = new Vector2(420, 120);
        window.maxSize = new Vector2(420, 120);
        window.ShowModalUtility();
    }

    private void OnGUI()
    {
        EditorGUILayout.Space(8);
        if (!string.IsNullOrEmpty(_message))
        {
            EditorGUILayout.HelpBox(_message, MessageType.None);
        }
        GUI.SetNextControlName("InputField");
        _value = EditorGUILayout.TextField(_value);
        EditorGUI.FocusTextInControl("InputField");

        EditorGUILayout.Space(8);
        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("取消", GUILayout.Width(80))) Close();
        if (GUILayout.Button(_okButton, GUILayout.Width(80)) || 
            (Event.current.isKey && Event.current.keyCode == KeyCode.Return))
        {
            var callback = _onOk;
            var value = _value != null ? _value.Trim() : "";
            Close();
            if (callback != null && !string.IsNullOrEmpty(value)) callback(value);
        }
        EditorGUILayout.EndHorizontal();
    }
}
