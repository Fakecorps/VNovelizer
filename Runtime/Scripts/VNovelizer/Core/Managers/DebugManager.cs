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
    private const string PREF_KEY_AUTO_PLAY = "Debug_Mode";

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

        // 2. 自动启动模式（由剧本管理器"试玩"按钮触发）
        if (PlayerPrefs.GetInt(PREF_KEY_AUTO_PLAY, 0) == 1)
        {
            // 立即清除标记，避免下次手动打开 VNDebugScene 时也自动启动
            PlayerPrefs.DeleteKey(PREF_KEY_AUTO_PLAY);
            PlayerPrefs.Save();

            string scriptName = PlayerPrefs.GetString(PREF_KEY_SCRIPT, "");
            if (!string.IsNullOrEmpty(scriptName))
            {
                Debug.Log($"[DebugManager] 自动启动试玩模式，剧本: {scriptName}");
                // 直接启动游戏（VNGamePlay 场景加载后会自动销毁 DebugPanel）
                VNManager.GetInstance().StartGame(scriptName, "");
            }
            else
            {
                Debug.LogWarning("[DebugManager] 自动启动模式已触发，但未找到剧本名，请手动输入后点击 Start");
            }
        }
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