using System;
using UnityEngine;
using UnityEditor;

namespace VNovelizer.Editor.RowPerformanceEditor
{
    /// <summary>
    /// 等宽字体加载器（2026-08-31 新增）。
    ///
    /// <para>
    /// <b>为什么必须要等宽字体</b>：代码编辑器要把「字符索引」换算成「像素坐标」
    /// 来绘制光标、选区、行号。比例字体（Inter、思源黑体）每个字符宽度不同，
    /// 无法用 <c>x = 列号 × 字符宽</c> 计算，只能逐字累加测量（慢且易错位）。
    /// 等宽字体下所有字符等宽，换算变成一次乘法，光标/选区/行号必然精确对齐。
    /// </para>
    ///
    /// <para>
    /// <b>安全约束（重要）</b>：项目曾因 OS 字体链 + 全局 <c>GUI.skin.font</c> 注入
    /// 产生过大量异常。因此本加载器<b>只返回 Font 对象，绝不修改任何全局状态</b>——
    /// 调用方必须把返回值赋给<b>局部</b> <c>GUIStyle.font</c>，用完后不残留。
    /// </para>
    ///
    /// <para><b>加载优先级</b>：</para>
    /// <list type="number">
    /// <item>工程内打包的等宽字体（<c>Runtime/3rdParty/Fonts/</c>，离线可靠）</item>
    /// <item>系统已安装的等宽字体链（Cascadia Mono → Consolas → …）</item>
    /// <item>null（调用方回退到默认字体，功能降级但不崩溃）</item>
    /// </list>
    /// </summary>
    internal static class VnMonoFont
    {
        /// <summary>系统等宽字体链，按优先级排列（Windows / macOS 常见覆盖面）。</summary>
        private static readonly string[] OsFontChain =
        {
            "Cascadia Mono",     // Windows 11 自带，现代等宽，MIT 许可
            "Cascadia Code",
            "Consolas",          // Windows 经典等宽
            "Menlo",             // macOS
            "DejaVu Sans Mono",  // Linux
            "Lucida Console",
            "Courier New",
        };

        private static Font _cached;
        private static bool _resolved;

        /// <summary>
        /// 获取等宽字体。可能为 null（调用方必须判空并回退）。
        /// 结果会被缓存，只解析一次。
        /// </summary>
        public static Font Get()
        {
            if (_resolved) return _cached;

            _resolved = true;
            _cached = Resolve();

            if (_cached == null)
            {
                Debug.LogWarning(
                    "[VnMonoFont] 未能加载等宽字体，代码编辑器回退到默认字体 —— " +
                    "光标与缩进可能错位。建议向 Runtime/3rdParty/Fonts/ 放入 " +
                    "JetBrains Mono / Cascadia Mono 等 SIL/MIT 许可的等宽字体。");
            }

            return _cached;
        }

        private static Font Resolve()
        {
            // ① 工程内打包字体优先（离线可靠、跨机一致）
            var bundled = LoadBundled();
            if (bundled != null) return bundled;

            // ② 回退系统字体链
            return LoadFromOs();
        }

        /// <summary>
        /// 查找工程内打包的等宽字体。当前目录为空，放入字体文件即自动生效，
        /// 无需改代码（按文件名关键字匹配）。
        /// </summary>
        private static Font LoadBundled()
        {
            // 约定：字体放在 Runtime/3rdParty/Fonts/ 下，文件名含 mono/mono 关键字
            string[] guids;
            try
            {
                guids = AssetDatabase.FindAssets("t:Font");
            }
            catch (Exception)
            {
                return null;
            }

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path)) continue;
                if (path.IndexOf("3rdParty/Fonts", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                string name = System.IO.Path.GetFileNameWithoutExtension(path) ?? "";
                bool isMono = name.IndexOf("mono", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!isMono) continue;

                try
                {
                    var font = AssetDatabase.LoadAssetAtPath<Font>(path);
                    if (font != null) return font;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[VnMonoFont] 加载打包字体失败 {path}：{e.Message}");
                }
            }

            return null;
        }

        /// <summary>
        /// 从系统加载等宽字体。逐项尝试，取第一个可用项。
        /// 全程 try-catch —— 任一字体名不可用都不能让编辑器崩溃。
        /// </summary>
        private static Font LoadFromOs()
        {
            foreach (string fontName in OsFontChain)
            {
                try
                {
                    var font = Font.CreateDynamicFontFromOSFont(new[] { fontName }, 12);
                    if (font != null && font.name != null)
                        return font;
                }
                catch (Exception)
                {
                    // 该字体不可用，继续尝试下一个
                }
            }

            return null;
        }
    }
}
