using UnityEngine;

namespace VNovelizer.Core.Diagnostics
{
    /// <summary>
    /// 仅在 Editor 或 Development Build 中输出详细日志；Release 玩家包中调用会被编译器剔除（Conditional）。
    /// </summary>
    public static class VNDebug
    {
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        public static void LogVerbose(string message)
        {
            Debug.Log(message);
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        public static void LogVerboseWarning(string message)
        {
            Debug.LogWarning(message);
        }
    }
}
