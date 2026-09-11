using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace VNovelizer.Editor.L2DSupport
{
    /// <summary>
    /// Live2D 支持激活器：检测 Live2D Cubism SDK 是否已导入，自动管理 VN_L2D 脚本定义符号。
    ///
    /// VN_L2D 是插件内 L2D 程序集（VNovelizer.L2D.Runtime / VNovelizer.L2D.Editor）的
    /// 编译门控（asmdef defineConstraints）——SDK 在 → L2D 代码参与编译；
    /// SDK 不在 → 门控程序集整体不编译，插件其余部分零影响。
    ///
    /// Live2D SDK 是 Assets 导入式（非 UPM 包），asmdef versionDefines 无法探测其存在，
    /// 因此符号必须由本激活器管理。本文件位于 VNovelizer.Editor 程序集（恒编译、零 Live2D 依赖）。
    /// </summary>
    [InitializeOnLoad]
    public static class L2DSupportActivator
    {
        /// <summary>L2D 程序集编译门控符号</summary>
        public const string DefineSymbol = "VN_L2D";

        /// <summary>SDK 默认导入目录（官方 unitypackage 落点）</summary>
        private const string SdkFolder = "Assets/Live2D/Cubism";

        /// <summary>SDK 程序集名（目录兜底扫描用；用户改放其他目录时仍能命中）</summary>
        private const string SdkAsmdefName = "Live2D.Cubism";

        // 资产变动防抖（导入/删除 SDK 大批文件时避免每文件触发一次全组扫描）
        private static bool _refreshScheduled;

        static L2DSupportActivator()
        {
            EditorApplication.delayCall += SyncDefineSymbol;
            EditorApplication.projectChanged += OnProjectChanged;
        }

        private static void OnProjectChanged()
        {
            if (_refreshScheduled) return;
            _refreshScheduled = true;
            EditorApplication.delayCall += () =>
            {
                _refreshScheduled = false;
                SyncDefineSymbol();
            };
        }

        /// <summary>检测 Live2D Cubism SDK 是否已导入</summary>
        public static bool IsSdkInstalled()
        {
            if (Directory.Exists(SdkFolder)) return true;

            // 兜底：按程序集名扫描 asmdef（用户可能将 SDK 放到自定义目录）
            string[] guids = AssetDatabase.FindAssets("t:AssemblyDefinitionAsset");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path)) continue;
                if (Path.GetFileNameWithoutExtension(path) == SdkAsmdefName) return true;
            }
            return false;
        }

        /// <summary>
        /// 同步 VN_L2D 符号到全部构建目标组：SDK 在 → 添加；不在 → 移除。
        /// 仅在符号实际变化时写回，避免无谓刷脏与重编译。
        /// </summary>
        public static void SyncDefineSymbol()
        {
            bool installed = IsSdkInstalled();
            bool changed = false;

            foreach (BuildTargetGroup group in System.Enum.GetValues(typeof(BuildTargetGroup)))
            {
                if (group == BuildTargetGroup.Unknown) continue;

                string current;
                try
                {
                    current = PlayerSettings.GetScriptingDefineSymbolsForGroup(group);
                }
                catch (System.Exception)
                {
                    continue; // 个别废弃目标组可能抛异常，跳过
                }

                var defines = new List<string>(current.Split(';'));
                bool has = defines.Contains(DefineSymbol);

                if (installed && !has)
                {
                    defines.Add(DefineSymbol);
                    changed = true;
                }
                else if (!installed && has)
                {
                    defines.Remove(DefineSymbol);
                    changed = true;
                }
                else
                {
                    continue; // 该组无变化，不写回
                }

                PlayerSettings.SetScriptingDefineSymbolsForGroup(group, string.Join(";", defines));
            }

            if (changed)
            {
                Debug.Log(installed
                    ? $"[L2DSupportActivator] 检测到 Live2D Cubism SDK，已启用 VN_L2D（Live2D 支持已激活）"
                    : "[L2DSupportActivator] 未检测到 Live2D Cubism SDK，已移除 VN_L2D（Live2D 支持停用）");
            }
        }
    }
}
