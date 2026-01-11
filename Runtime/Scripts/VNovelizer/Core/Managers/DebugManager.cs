using UnityEngine;
using UnityEngine.UI;
using TMPro; // 记得引用 TMP

public class DebugManager : MonoBehaviour
{
    [SerializeField] private Button startBtn;
    [SerializeField] private TMP_InputField scriptInput;
    [SerializeField] private TMP_InputField lineIdInput;

    // 定义 Key，防止手写出错
    private const string PREF_KEY_SCRIPT = "Debug_LastScriptName";
    private const string PREF_KEY_LINEID = "Debug_LastLineID";

    void Start()
    {
        // 1. 回显上次的输入
        if (scriptInput != null)
            scriptInput.text = PlayerPrefs.GetString(PREF_KEY_SCRIPT, "Test101"); // 可以设置个默认值

        if (lineIdInput != null)
            lineIdInput.text = PlayerPrefs.GetString(PREF_KEY_LINEID, "");

        // 绑定按钮事件
        if (startBtn != null)
            startBtn.onClick.AddListener(OnStartDebug);
        Debug.Log(Application.persistentDataPath);
    }

    private void OnStartDebug()
    {
        string scriptName = scriptInput.text.Trim();
        string lineID = lineIdInput.text.Trim();

        if (string.IsNullOrEmpty(scriptName))
        {
            Debug.LogError("请输入剧本名！");
            return;
        }

        // 2. 保存当前输入，方便下次不用重填
        PlayerPrefs.SetString(PREF_KEY_SCRIPT, scriptName);
        PlayerPrefs.SetString(PREF_KEY_LINEID, lineID);
        PlayerPrefs.Save(); // 强制写入磁盘

        // 启动游戏逻辑
        VNManager.GetInstance().StartGame(scriptName, lineID);
    }
}