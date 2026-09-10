using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VNovelizer.Core;
using VNovelizer.Core.Commands.Meta;

namespace VNovelizer.Editor.RowPerformanceEditor
{
    /// <summary>
    /// 参数候选值供给：把 <see cref="VNParamType"/> 的**动态取值域**解析为实际候选列表。
    ///
    /// <para>
    /// 这是「动态取值域用类型表达而非在特性里写死候选」这一决策的落地点：
    /// 命令类上只声明 <c>VNParamType.CharacterId</c>，实际有哪些角色由本类在
    /// 编辑期实时查询。新增角色 / 背景 / BGM 后无需改任何特性标注。
    /// </para>
    /// </summary>
    public static class ParamCandidateProvider
    {
        /// <summary>
        /// 取某个参数类型的候选值。返回 null 表示"该类型无固定候选，用自由输入"。
        /// </summary>
        public static List<string> GetCandidates(VNParamInfo param, string currentScriptName)
        {
            if (param == null) return null;

            switch (param.Type)
            {
                case VNParamType.Enum:
                    {
                        var opts = param.Options != null && param.Options.Length > 0
                            ? param.Options.ToList()
                            : null;
                        // 约定：Options 切分后首项为空串时，首项渲染为「（无）」占位。
                        // 用户选"无"→ 写入空字符串→ 命令解析器 TryParse("") 返回 false → 视作未指定
                        // （与「不写该参数」等价）。SetParamValue 的尾空裁剪会把 args 收缩。
                        if (opts != null && opts.Count > 0 && string.IsNullOrEmpty(opts[0]))
                        {
                            opts.RemoveAt(0);
                            const string kNoneLabel = "（无）";
                            if (!opts.Contains(kNoneLabel)) opts.Insert(0, kNoneLabel);
                        }
                        return opts;
                    }

                case VNParamType.Bool:
                    return new List<string> { "true", "false" };

                case VNParamType.SlotCode:
                    return new List<string> { "L", "ML", "M", "MR", "R" };

                case VNParamType.CharacterId:
                    return GetCharacterIds();

                case VNParamType.LineId:
                    return GetLineIds();

                case VNParamType.ScriptName:
                    return GetScriptNames();

                case VNParamType.SceneName:
                    return GetSceneNames();

                case VNParamType.FlagName:
                    return GetFlagNames();

                case VNParamType.BackgroundName:
                    return GetResourceNames(ResType.Background);

                case VNParamType.BgmName:
                    return GetResourceNames(ResType.BGM);

                case VNParamType.SfxName:
                    return GetResourceNames(ResType.SFX);

                case VNParamType.VoiceName:
                    return GetResourceNames(ResType.Voice);

                case VNParamType.VideoName:
                    return GetResourceNames(ResType.Video);

                case VNParamType.AnimName:
                    return GetVfxNames(true);

                case VNParamType.ParticleName:
                    return GetVfxNames(false);

                case VNParamType.GalleryCgName:
                    return GetCgGalleryNames();

                case VNParamType.GalleryMusicName:
                    return GetMusicGalleryNames();

                case VNParamType.GallerySceneName:
                    return GetSceneGalleryNames();

                // ScriptLineId 依赖前置 ScriptName 参数的值，由 InspectorBuilder 联动解析
                // （见 InspectorBuilder.ResolveCandidates → GetScriptLineIds）。
                default:
                    return null;
            }
        }

        /// <summary>
        /// 角色 ID 候选。用 <see cref="AssetDatabase"/> 而非 <c>Resources.LoadAll</c>——
        /// 本项目的角色资源可能注册在 Addressables 组中而不在 Resources 目录下，
        /// 后者会漏掉它们。AssetDatabase 覆盖工程内全部资产，编辑期可靠。
        /// </summary>
        private static List<string> GetCharacterIds()
        {
            var result = new List<string>();
            var guids = AssetDatabase.FindAssets("t:CharacterProfile");

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var profile = AssetDatabase.LoadAssetAtPath<CharacterProfile>(path);
                if (profile != null && !string.IsNullOrEmpty(profile.CharacterID) &&
                    !result.Contains(profile.CharacterID))
                    result.Add(profile.CharacterID);
            }

            result.Sort();
            return result.Count > 0 ? result : null;
        }

        /// <summary>
        /// 取指定角色的分组名候选（<c>VNParamType.CharacterGroup</c> 用）。
        /// 依赖已选定的角色 ID——这是"参数间联动"的典型场景。
        /// </summary>
        public static List<string> GetCharacterGroups(string characterId)
        {
            if (string.IsNullOrEmpty(characterId)) return null;

            var profile = FindProfile(characterId);
            if (profile == null) return null;

            var result = new List<string>();
            foreach (var group in profile.ElementSpriteGroups)
                if (group != null && !string.IsNullOrEmpty(group.Group))
                    result.Add(group.Group);

            return result.Count > 0 ? result : null;
        }

        /// <summary>
        /// 取指定角色 + 分组下的表情名候选（<c>VNParamType.Emotion</c> 用）。
        /// </summary>
        public static List<string> GetEmotions(string characterId, string groupName)
        {
            if (string.IsNullOrEmpty(characterId)) return null;

            var profile = FindProfile(characterId);
            if (profile == null) return null;

            var result = new List<string>();
            foreach (var group in profile.ElementSpriteGroups)
            {
                if (group == null) continue;
                if (!string.IsNullOrEmpty(groupName) && group.Group != groupName) continue;

                foreach (var sprite in group.Sprites)
                    if (sprite != null && !string.IsNullOrEmpty(sprite.Element) &&
                        !result.Contains(sprite.Element))
                        result.Add(sprite.Element);
            }

            return result.Count > 0 ? result : null;
        }

        private static CharacterProfile FindProfile(string characterId)
        {
            var guids = AssetDatabase.FindAssets("t:CharacterProfile");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var profile = AssetDatabase.LoadAssetAtPath<CharacterProfile>(path);
                if (profile != null && profile.CharacterID == characterId) return profile;
            }
            return null;
        }

        private static List<string> GetLineIds()
        {
            var manager = VNManager.GetInstance();
            if (manager.StoryLines == null || manager.StoryLines.Count == 0) return null;

            var result = new List<string>();
            foreach (var line in manager.StoryLines)
                if (!string.IsNullOrEmpty(line.ID)) result.Add(line.ID);

            return result.Count > 0 ? result : null;
        }

        private static List<string> GetScriptNames()
        {
            var guids = AssetDatabase.FindAssets("t:TextAsset");
            var result = new List<string>();

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) continue;
                string name = System.IO.Path.GetFileNameWithoutExtension(path);
                if (!result.Contains(name)) result.Add(name);
            }

            result.Sort();
            return result.Count > 0 ? result : null;
        }

        private static List<string> GetSceneNames()
        {
            var result = new List<string>();
            foreach (var scene in EditorBuildSettings.scenes)
            {
                if (!scene.enabled) continue;
                string name = System.IO.Path.GetFileNameWithoutExtension(scene.path);
                if (!string.IsNullOrEmpty(name)) result.Add(name);
            }
            return result.Count > 0 ? result : null;
        }

        /// <summary>标志名候选（来自工程内全部 FlagRegistry 资产，与 Flag 编辑器同源）</summary>
        private static List<string> GetFlagNames()
        {
            var result = new List<string>();
            var guids = AssetDatabase.FindAssets("t:FlagRegistry");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var registry = AssetDatabase.LoadAssetAtPath<FlagRegistry>(path);
                if (registry == null) continue;
                foreach (var def in registry.Definitions)
                    if (def != null && !string.IsNullOrEmpty(def.Name) && !result.Contains(def.Name))
                        result.Add(def.Name);
            }
            result.Sort();
            return result.Count > 0 ? result : null;
        }

        /// <summary>
        /// 查询工程内 Flag 注册表，返回 flag 的声明类型。
        /// 返回 false 表示未注册（兼容模式：类型未知，由调用方按宽松策略处理）。
        /// </summary>
        public static bool TryGetFlagType(string flagName, out FlagType type)
        {
            type = FlagType.Bool;
            if (string.IsNullOrEmpty(flagName)) return false;

            var guids = AssetDatabase.FindAssets("t:FlagRegistry");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var registry = AssetDatabase.LoadAssetAtPath<FlagRegistry>(path);
                if (registry == null) continue;
                var def = registry.Find(flagName);
                if (def != null)
                {
                    type = def.Type;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 背景 / BGM / 音效 / 语音 / 视频资源名候选（轻量版：只取逻辑名，不加载资产本体）。
        /// 与 ResourceAssetService.LoadAssets 同双模型语义：
        /// Addressables 托管模式枚举组内类别条目，文件夹模式扫描类别文件夹，
        /// 逻辑名（Excel 索引名）= 资源管理器窗口中用户看到的名称。
        /// </summary>
        private static List<string> GetResourceNames(ResType resType)
        {
            var result = new List<string>();

            // 视频始终是 StreamingAssets 原始文件（不经 Addressables）
            if (resType == ResType.Video)
            {
                string videoPath = ResourceAssetService.GetPathFromConfig(resType);
                if (!string.IsNullOrEmpty(videoPath) && Directory.Exists(videoPath))
                {
                    string[] videoExt = { ".mp4", ".mov", ".webm", ".avi", ".asf", ".wmv" };
                    foreach (string filePath in Directory.GetFiles(videoPath, "*.*", SearchOption.TopDirectoryOnly))
                    {
                        string ext = Path.GetExtension(filePath).ToLower();
                        if (!videoExt.Contains(ext)) continue;
                        string name = Path.GetFileNameWithoutExtension(filePath);
                        if (!string.IsNullOrEmpty(name) && !result.Contains(name)) result.Add(name);
                    }
                }
                result.Sort();
                return result.Count > 0 ? result : null;
            }

            // Addressables 托管模式：组内类别条目，逻辑名 = 地址尾段
            string category = VNAddressablesRegistrar.GetCategoryKey(resType);
            if (VNAddressablesRegistrar.IsManagedMode && !string.IsNullOrEmpty(category))
            {
                foreach (var entry in VNAddressablesRegistrar.GetCategoryEntries(category))
                {
                    string logicalName = GetLogicalName(entry.address, category);
                    if (!string.IsNullOrEmpty(logicalName) && !result.Contains(logicalName))
                        result.Add(logicalName);
                }
            }

            // 文件夹兜底（旧版模式 / 未分配资产）：逻辑名 = 文件名
            string folder = ResourceAssetService.GetPathFromConfig(resType);
            if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
            {
                string filter = UIElementBuilder.GetSearchFilter(resType);
                foreach (string guid in AssetDatabase.FindAssets(filter, new[] { folder }))
                {
                    string name = Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(guid));
                    if (!string.IsNullOrEmpty(name) && !result.Contains(name)) result.Add(name);
                }
            }

            result.Sort();
            return result.Count > 0 ? result : null;
        }

        /// <summary>
        /// 动画 / 粒子特效名候选（VFX 两类目录）：
        /// Addressables 托管模式枚举组内类别条目，文件夹模式扫描类别文件夹。
        /// </summary>
        private static List<string> GetVfxNames(bool anim)
        {
            var config = VNProjectConfig.Instance;
            if (config == null) return null;

            string category = anim ? config.AnimationPath : config.ParticalEffectPath;
            if (string.IsNullOrEmpty(category)) return null;

            var result = new List<string>();

            if (VNAddressablesRegistrar.IsManagedMode)
            {
                foreach (var entry in VNAddressablesRegistrar.GetCategoryEntries(category))
                {
                    string name = GetLogicalName(entry.address, category);
                    if (!string.IsNullOrEmpty(name) && !result.Contains(name)) result.Add(name);
                }
            }

            string folder = VNProjectPaths.ResourceKeyToFolder(category);
            if (!string.IsNullOrEmpty(folder) && AssetDatabase.IsValidFolder(folder))
            {
                foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { folder }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    string name = System.IO.Path.GetFileNameWithoutExtension(path);
                    if (!string.IsNullOrEmpty(name) && !result.Contains(name)) result.Add(name);
                }
            }

            result.Sort();
            return result.Count > 0 ? result : null;
        }

        /// <summary>地址尾段 → 逻辑名（与 ResourceAssetService.GetLogicalName 同规则）</summary>
        private static string GetLogicalName(string address, string category)
        {
            if (string.IsNullOrEmpty(address)) return null;
            string prefix = category + "/";
            if (!address.StartsWith(prefix, StringComparison.Ordinal)) return null;
            string name = address.Substring(prefix.Length);
            return string.IsNullOrEmpty(name) ? null : name;
        }

        private static List<string> GetCgGalleryNames()
        {
            var container = VNEditorResourceResolver.LoadByKey<CGDataContainer>(VNUIPrefabKeys.CGDataContainer);
            if (container == null) return null;
            var result = new List<string>();
            foreach (var cg in container.cgList)
                if (cg != null && !string.IsNullOrEmpty(cg.cgName) && !result.Contains(cg.cgName))
                    result.Add(cg.cgName);
            result.Sort();
            return result.Count > 0 ? result : null;
        }

        private static List<string> GetMusicGalleryNames()
        {
            var container = VNEditorResourceResolver.LoadByKey<MusicDataContainer>(VNUIPrefabKeys.MusicDataContainer);
            if (container == null) return null;
            var result = new List<string>();
            foreach (var music in container.musicList)
                if (music != null && !string.IsNullOrEmpty(music.name) && !result.Contains(music.name))
                    result.Add(music.name);
            result.Sort();
            return result.Count > 0 ? result : null;
        }

        private static List<string> GetSceneGalleryNames()
        {
            var container = VNEditorResourceResolver.LoadByKey<SceneDataContainer>(VNUIPrefabKeys.SceneDataContainer);
            if (container == null) return null;
            var result = new List<string>();
            foreach (var scene in container.sceneList)
                if (scene != null && !string.IsNullOrEmpty(scene.VNscriptID) && !result.Contains(scene.VNscriptID))
                    result.Add(scene.VNscriptID);
            result.Sort();
            return result.Count > 0 ? result : null;
        }

        /// <summary>
        /// 指定剧本的全部行 ID 候选（<c>VNParamType.ScriptLineId</c> 用，跨剧本 startId）。
        /// 直接按文件路径读取 CSV 并复用 ScriptParser 的引号感知拆分（跳过标题行），
        /// 不依赖运行时资源链（Addressables/Resources），编辑器非播放模式下也可用。
        /// </summary>
        public static List<string> GetScriptLineIds(string scriptName)
        {
            if (string.IsNullOrEmpty(scriptName)) return null;

            string csvPath = null;
            var guids = AssetDatabase.FindAssets("t:TextAsset");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(System.IO.Path.GetFileNameWithoutExtension(path), scriptName,
                    StringComparison.Ordinal))
                {
                    csvPath = path;
                    break;
                }
            }
            if (string.IsNullOrEmpty(csvPath)) return null;

            var csv = AssetDatabase.LoadAssetAtPath<TextAsset>(csvPath);
            if (csv == null || string.IsNullOrEmpty(csv.text)) return null;

            var result = new List<string>();
            string[] lines = ScriptParser.SplitCSVLines(csv.text);
            bool first = true;
            foreach (string line in lines)
            {
                string l = line.Trim();
                if (string.IsNullOrEmpty(l)) continue;
                if (first) { first = false; continue; } // 跳过标题行

                string[] cols = ScriptParser.SplitCSV(l);
                if (cols.Length == 0) continue;
                string id = cols[0].Trim();
                if (!string.IsNullOrEmpty(id) && !result.Contains(id)) result.Add(id);
            }
            return result.Count > 0 ? result : null;
        }
    }
}
