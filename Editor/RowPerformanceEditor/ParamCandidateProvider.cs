using System;
using System.Collections.Generic;
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
                    return param.Options != null && param.Options.Length > 0
                        ? param.Options.ToList()
                        : null;

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

                // 背景 / 音频等依赖项目资源注册表，暂以自由输入 + 提示承载。
                // 待资源注册表提供统一查询入口后接入（不影响当前可用性：
                // 用户仍可手输，且校验器会提示资源不存在）。
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
    }
}
