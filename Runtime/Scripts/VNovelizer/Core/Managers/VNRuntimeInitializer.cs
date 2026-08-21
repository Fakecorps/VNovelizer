using UnityEngine;

/// <summary>
/// VN 运行时初始化入口（场景无关自举）。
///
/// 对标通用 VN 引擎架构：场景无关、根对象常驻、任意场景可启动游戏。
///
/// 两种用法：
/// 1. 组件方式：挂到任意场景的任意物体上，Inspector 中填剧本名（可选行 ID 与自动开始）——
///    声明"此场景启动 VN 游戏"。StartGame 是场景无关的（引擎根对象按需自举并跨场景常驻），
///    因此任何场景都可以是游戏场景；
/// 2. 编辑器试玩自举：剧本管理器"试玩"按钮写入 PlayerPrefs 标记后进入 Play 模式，
///    <see cref="AutoPlayOnPlayMode"/> 在任意场景进入 Play 时检测标记并自动开始
///    （替代已删除的 VNDebugScene 工作流：任意场景按 Play 即可试玩）。
///
/// 上次的输入会经 PlayerPrefs 记忆（剧本名/行 ID）。
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("VNovelizer/VN Runtime Initializer")]
public class VNRuntimeInitializer : MonoBehaviour
{
    [Header("游戏入口（场景无关，任意场景可直接开始）")]
    [Tooltip("启动时加载的剧本名（CSV 文件名，不含扩展名）。留空则不自动开始")]
    [SerializeField] private string scriptName = "";

    [Tooltip("起始行 ID（可选，留空从剧本开头开始）")]
    [SerializeField] private string startLineID = "";

    [Tooltip("Play 后自动开始游戏（关闭则只在 Inspector 手动点击按钮启动）")]
    [SerializeField] private bool autoStartOnPlay = true;

    [Header("调试（持久记忆上次输入）")]
    private const string PREF_KEY_SCRIPT = "Debug_LastScriptName";
    private const string PREF_KEY_LINEID = "Debug_LastLineID";
    private const string PREF_KEY_AUTO_PLAY = "Debug_Mode";

    /// <summary>编辑器试玩自举：任意场景进入 Play 模式时检测剧本管理器的试玩标记</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoPlayOnPlayMode()
    {
#if UNITY_EDITOR
        if (PlayerPrefs.GetInt(PREF_KEY_AUTO_PLAY, 0) != 1) return;

        // 立即清除标记，避免下次手动 Play 也自动启动
        PlayerPrefs.DeleteKey(PREF_KEY_AUTO_PLAY);
        PlayerPrefs.Save();

        string scriptName = PlayerPrefs.GetString(PREF_KEY_SCRIPT, "");
        if (!string.IsNullOrEmpty(scriptName))
        {
            Debug.Log($"[VNRuntimeInitializer] 检测到试玩标记，自动启动剧本: {scriptName}");
            VNManager.GetInstance().StartGame(scriptName, PlayerPrefs.GetString(PREF_KEY_LINEID, ""));
        }
#endif
    }

    private void Start()
    {
        // 编辑器 Play 模式下：组件字段为空时回显上次输入（调试便利）
#if UNITY_EDITOR
        if (string.IsNullOrEmpty(scriptName))
        {
            string lastScript = PlayerPrefs.GetString(PREF_KEY_SCRIPT, "");
            if (!string.IsNullOrEmpty(lastScript)) scriptName = lastScript;
        }
#endif

        if (autoStartOnPlay && !string.IsNullOrEmpty(scriptName))
        {
            StartGame();
        }
    }

    /// <summary>按当前 Inspector 设置启动游戏（可由 UI 按钮等调用）</summary>
    public void StartGame()
    {
        if (string.IsNullOrEmpty(scriptName))
        {
            Debug.LogWarning("[VNRuntimeInitializer] 未指定剧本名，无法启动");
            return;
        }

#if UNITY_EDITOR
        PlayerPrefs.SetString(PREF_KEY_SCRIPT, scriptName);
        PlayerPrefs.SetString(PREF_KEY_LINEID, startLineID);
        PlayerPrefs.Save();
#endif

        VNManager.GetInstance().StartGame(scriptName, startLineID);
    }

    /// <summary>启动指定剧本（外部代码调用入口）</summary>
    public void StartGame(string script, string lineID = "")
    {
        scriptName = script;
        startLineID = lineID;
        StartGame();
    }
}
