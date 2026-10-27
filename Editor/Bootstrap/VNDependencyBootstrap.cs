using System;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace VNovelizer.Editor.Bootstrap
{
    [InitializeOnLoad]
    public static class VNDependencyBootstrap
    {
        private static readonly string StateKey = "VNovelizer.Dependencies:" + Application.dataPath + ":";
        private static double _nextCheck;
        private const double TimeoutSeconds = 180;

        static VNDependencyBootstrap()
        {
            _nextCheck = EditorApplication.timeSinceStartup + 2;
            EditorApplication.update += Update;
        }

        public static bool IsReady
        {
            get
            {
                var package = FindPrimeTween();
                return package != null && IsCompatibleVersion(package.version);
            }
        }

        public static string StatusMessage
        {
            get
            {
                if (IsReady) return "PrimeTween 已就绪，VNovelizer 可以正常使用。";
                string error = SessionState.GetString(StateKey + "Error", "");
                if (!string.IsNullOrEmpty(error)) return error;
                return SessionState.GetBool(StateKey + "Attempted", false)
                    ? "正在通过 Unity Package Manager 安装 PrimeTween，请等待下载与脚本编译完成。"
                    : "正在检查 VNovelizer 的依赖配置。";
            }
        }

        public static void Retry()
        {
            SessionState.SetBool(StateKey + "Attempted", false);
            SessionState.SetString(StateKey + "Error", "");
            _nextCheck = EditorApplication.timeSinceStartup + 1;
            EditorApplication.update -= Update;
            EditorApplication.update += Update;
        }

        [MenuItem("VNovelizer/依赖安装状态与重试", false, 2)]
        public static void ShowStatusWindow()
        {
            EditorWindow.GetWindow<VNDependencyStatusWindow>("VNovelizer 依赖");
        }

        private static void Update()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode ||
                EditorApplication.timeSinceStartup < _nextCheck)
                return;
            _nextCheck = EditorApplication.timeSinceStartup + 1;

            try
            {
                var installed = FindPrimeTween();
                if (installed != null)
                {
                    if (!IsCompatibleVersion(installed.version))
                    {
                        if (string.IsNullOrEmpty(SessionState.GetString(StateKey + "Error", "")))
                            Fail("当前 PrimeTween " + installed.version + " 低于最低支持版本 " +
                                VNDependencyManifest.PackageVersion + "。已保留项目版本，请更新后重试。");
                        return;
                    }
                    if (SessionState.GetBool(StateKey + "Attempted", false))
                        Debug.Log("[VNovelizer] PrimeTween " + installed.version + " 已就绪。");
                    SessionState.SetString(StateKey + "Error", "");
                    EditorApplication.update -= Update;
                    return;
                }

                if (!string.IsNullOrEmpty(SessionState.GetString(StateKey + "Error", ""))) return;
                if (!SessionState.GetBool(StateKey + "Attempted", false))
                {
                    BeginInstall();
                    return;
                }

                string started = SessionState.GetString(StateKey + "Started", "0");
                if (long.TryParse(started, out long ticks) && ticks > 0 &&
                    (DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc)).TotalSeconds > TimeoutSeconds)
                    Fail("PrimeTween 安装尚未完成。请查看 Package Manager / Console 的错误，确认注册源可访问后点击重试。" +
                        "\n现有注册源已保留；默认下载源为 " + VNDependencyManifest.RegistryUrl + "。");
            }
            catch (Exception exception)
            {
                Fail(exception.Message);
            }
        }

        private static void BeginInstall()
        {
            SessionState.SetBool(StateKey + "Attempted", true);
            SessionState.SetString(StateKey + "Started", DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture));
            string manifestPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Packages", "manifest.json"));
            VNDependencyManifest.Ensure(manifestPath);
            Debug.Log("[VNovelizer] 正在自动安装 PrimeTween " + VNDependencyManifest.PackageVersion +
                "。无需手动注册 npm；请等待 Package Manager 下载与编译完成。");
            Client.Resolve();
        }

        private static PackageInfo FindPrimeTween()
        {
            foreach (var package in PackageInfo.GetAllRegisteredPackages())
                if (package.name == VNDependencyManifest.PackageName) return package;
            return null;
        }

        private static bool IsCompatibleVersion(string version)
        {
            return VNDependencyManifest.IsCompatibleVersion(version);
        }

        private static void Fail(string message)
        {
            SessionState.SetBool(StateKey + "Attempted", true);
            SessionState.SetString(StateKey + "Error", message);
            Debug.LogWarning("[VNovelizer] 依赖安装未完成：" + message +
                "\n可通过 VNovelizer → 依赖安装状态与重试 继续安装。");
            if (!Application.isBatchMode) ShowStatusWindow();
        }
    }

    internal class VNDependencyStatusWindow : EditorWindow
    {
        private void OnGUI()
        {
            EditorGUILayout.LabelField("VNovelizer 依赖安装", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(VNDependencyBootstrap.StatusMessage,
                VNDependencyBootstrap.IsReady ? MessageType.Info : MessageType.Warning);
            EditorGUILayout.LabelField("首次安装会自动配置项目 Packages/manifest.json，并安装 PrimeTween " +
                VNDependencyManifest.PackageVersion + "。已有版本、注册源和其他配置不会被覆盖。", EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(EditorApplication.isCompiling || EditorApplication.isUpdating))
            {
                if (GUILayout.Button("重新检查并安装依赖")) VNDependencyBootstrap.Retry();
                if (GUILayout.Button("打开 Package Manager"))
                    EditorApplication.ExecuteMenuItem("Window/Package Manager");
            }
        }

        private void OnInspectorUpdate()
        {
            Repaint();
        }
    }
}
