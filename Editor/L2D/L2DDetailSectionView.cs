using System;
using System.Collections.Generic;
using System.Linq;
using Live2D.Cubism.Framework.Expression;
using Live2D.Cubism.Framework.MotionFade;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VNovelizer.L2D;

namespace VNovelizer.L2D.EditorTools
{
    /// <summary>
    /// L2D 角色详情面板专属区块（核心角色编辑器 Tab 区第一页）：
    /// 3 个官方资产拖拽框 + 表情列表（ID → .exp3 资产）+ 动作列表（ID → .anim + Kind + Loop）+ 实时校验。
    /// 所有变更即时 SetDirty；结构类变更（增删行）经 onRebuild 触发面板重建。
    /// </summary>
    public class L2DDetailSectionView : VisualElement
    {
        private readonly L2DCharacterProfile profile;
        private readonly Action onRebuild;

        private Label validationLabel;

        public L2DDetailSectionView(L2DCharacterProfile profile, Action onRebuild)
        {
            this.profile = profile;
            this.onRebuild = onRebuild;
            style.flexGrow = 1;
            Build();
        }

        private void Build()
        {
            Add(BuildCardLabel("模型资源（SDK 导入产物）"));
            Add(BuildAssetFields());
            Add(BuildCardLabel("表情配置（ID → .exp3 表情资产）"));
            Add(BuildExpressionSection());
            Add(BuildCardLabel("动作配置（ID → .anim 动作）"));
            Add(BuildMotionSection());
            Add(BuildValidationSection());
            RefreshValidation();
        }

        // =========================================================
        //                      模型资源
        // =========================================================
        private VisualElement BuildAssetFields()
        {
            var box = new VisualElement();
            box.style.marginBottom = 12;

            var prefabField = new ObjectField("模型 Prefab")
            {
                objectType = typeof(GameObject),
                value = profile.ModelPrefab,
                allowSceneObjects = false
            };
            prefabField.style.marginBottom = 5;
            prefabField.RegisterValueChangedCallback(evt =>
            {
                profile.ModelPrefab = evt.newValue as GameObject;
                EditorUtility.SetDirty(profile);
            });
            box.Add(prefabField);

            var exprListField = new ObjectField("表情总表 ExpressionList")
            {
                objectType = typeof(CubismExpressionList),
                value = profile.ExpressionList,
                allowSceneObjects = false
            };
            exprListField.style.marginBottom = 5;
            exprListField.RegisterValueChangedCallback(evt =>
            {
                profile.ExpressionList = evt.newValue as CubismExpressionList;
                EditorUtility.SetDirty(profile);
                RefreshValidation();
            });
            box.Add(exprListField);

            var fadeListField = new ObjectField("动作总表 FadeMotionList")
            {
                objectType = typeof(CubismFadeMotionList),
                value = profile.FadeMotionList,
                allowSceneObjects = false
            };
            fadeListField.style.marginBottom = 5;
            fadeListField.RegisterValueChangedCallback(evt =>
            {
                profile.FadeMotionList = evt.newValue as CubismFadeMotionList;
                EditorUtility.SetDirty(profile);
            });
            box.Add(fadeListField);

            return box;
        }

        // =========================================================
        //                      表情配置
        // =========================================================
        private VisualElement BuildExpressionSection()
        {
            var box = new VisualElement();
            box.style.marginBottom = 12;
            box.style.paddingTop = 6;
            box.style.paddingBottom = 6;
            box.style.paddingLeft = 8;
            box.style.paddingRight = 8;
            box.style.backgroundColor = GalleryTheme.Hex(GalleryTheme.BgCard);
            box.style.borderTopLeftRadius = 4;
            box.style.borderTopRightRadius = 4;
            box.style.borderBottomLeftRadius = 4;
            box.style.borderBottomRightRadius = 4;

            profile.Expressions = profile.Expressions ?? new List<L2DExpressionEntry>();

            for (int i = 0; i < profile.Expressions.Count; i++)
            {
                int index = i; // 闭包捕获
                var entry = profile.Expressions[i];
                box.Add(BuildExpressionRow(entry, index));
            }

            var btnRow = new VisualElement();
            btnRow.style.flexDirection = FlexDirection.Row;
            btnRow.style.marginTop = 4;

            var addBtn = new Button(() =>
            {
                profile.Expressions.Add(new L2DExpressionEntry { ID = "" });
                EditorUtility.SetDirty(profile);
                onRebuild?.Invoke();
            }) { text = "+ 添加表情" };
            GalleryStyles.ApplyButton(addBtn, GalleryTheme.AccentDim, true);
            addBtn.style.fontSize = 10;
            addBtn.style.width = 80;
            btnRow.Add(addBtn);

            var autofillBtn = new Button(() =>
            {
                int filled = 0;
                foreach (var e in profile.Expressions)
                {
                    if (e != null && e.Data != null && string.IsNullOrEmpty(e.ID))
                    {
                        e.ID = e.Data.name; // 资产名（如 exp_01）作为默认 ID
                        filled++;
                    }
                }
                if (filled > 0)
                {
                    EditorUtility.SetDirty(profile);
                    onRebuild?.Invoke();
                }
            }) { text = "用资产名填充空 ID" };
            GalleryStyles.ApplyButton(autofillBtn, GalleryTheme.BgCard, false);
            autofillBtn.style.fontSize = 10;
            autofillBtn.style.width = 120;
            autofillBtn.style.marginLeft = 6;
            btnRow.Add(autofillBtn);

            box.Add(btnRow);
            return box;
        }

        private VisualElement BuildExpressionRow(L2DExpressionEntry entry, int index)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 4;

            var idField = new TextField { value = entry.ID ?? "", tooltip = "表情 ID（立绘列情绪串 / l2dexp 命令引用）" };
            idField.style.width = 110;
            idField.style.flexShrink = 0;
            idField.style.marginRight = 6;
            idField.RegisterValueChangedCallback(evt =>
            {
                entry.ID = evt.newValue;
                EditorUtility.SetDirty(profile);
                RefreshValidation();
            });
            row.Add(idField);

            var dataField = new ObjectField
            {
                objectType = typeof(CubismExpressionData),
                value = entry.Data,
                allowSceneObjects = false
            };
            dataField.style.flexGrow = 1;
            dataField.style.flexShrink = 1;
            dataField.style.minWidth = 80;
            dataField.RegisterValueChangedCallback(evt =>
            {
                entry.Data = evt.newValue as CubismExpressionData;
                if (entry.Data != null && string.IsNullOrEmpty(entry.ID))
                    entry.ID = entry.Data.name; // 拖入资产时自动填 ID（可再改）
                EditorUtility.SetDirty(profile);
                RefreshValidation();
                onRebuild?.Invoke();
            });
            row.Add(dataField);

            var delBtn = new Button(() =>
            {
                profile.Expressions.Remove(entry);
                EditorUtility.SetDirty(profile);
                onRebuild?.Invoke();
            }) { text = "×" };
            delBtn.style.width = 24;
            delBtn.style.marginLeft = 6;
            delBtn.style.flexShrink = 0;
            delBtn.style.backgroundColor = GalleryTheme.Transparent_Color;
            delBtn.style.color = GalleryTheme.Hex(GalleryTheme.Danger);
            row.Add(delBtn);

            return row;
        }

        // =========================================================
        //                      动作配置
        // =========================================================
        private VisualElement BuildMotionSection()
        {
            var box = new VisualElement();
            box.style.marginBottom = 12;
            box.style.paddingTop = 6;
            box.style.paddingBottom = 6;
            box.style.paddingLeft = 8;
            box.style.paddingRight = 8;
            box.style.backgroundColor = GalleryTheme.Hex(GalleryTheme.BgCard);
            box.style.borderTopLeftRadius = 4;
            box.style.borderTopRightRadius = 4;
            box.style.borderBottomLeftRadius = 4;
            box.style.borderBottomRightRadius = 4;

            profile.Motions = profile.Motions ?? new List<L2DMotionEntry>();

            for (int i = 0; i < profile.Motions.Count; i++)
            {
                box.Add(BuildMotionRow(profile.Motions[i]));
            }

            var addBtn = new Button(() =>
            {
                profile.Motions.Add(new L2DMotionEntry { ID = "" });
                EditorUtility.SetDirty(profile);
                onRebuild?.Invoke();
            }) { text = "+ 添加动作" };
            GalleryStyles.ApplyButton(addBtn, GalleryTheme.AccentDim, true);
            addBtn.style.fontSize = 10;
            addBtn.style.width = 80;
            addBtn.style.marginTop = 4;
            box.Add(addBtn);

            return box;
        }

        private VisualElement BuildMotionRow(L2DMotionEntry entry)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 4;

            var idField = new TextField { value = entry.ID ?? "", tooltip = "动作 ID（l2dmotion 命令引用）" };
            idField.style.width = 90;
            idField.style.flexShrink = 0;
            idField.style.marginRight = 6;
            idField.RegisterValueChangedCallback(evt =>
            {
                entry.ID = evt.newValue;
                EditorUtility.SetDirty(profile);
                RefreshValidation();
            });
            row.Add(idField);

            var clipField = new ObjectField
            {
                objectType = typeof(AnimationClip),
                value = entry.Clip,
                allowSceneObjects = false
            };
            clipField.style.flexGrow = 1;
            clipField.style.flexShrink = 1;
            clipField.style.minWidth = 60;
            clipField.RegisterValueChangedCallback(evt =>
            {
                entry.Clip = evt.newValue as AnimationClip;
                if (entry.Clip != null && string.IsNullOrEmpty(entry.ID))
                    entry.ID = entry.Clip.name; // 拖入 clip 时自动填 ID（可再改）
                EditorUtility.SetDirty(profile);
                RefreshValidation();
                onRebuild?.Invoke();
            });
            row.Add(clipField);

            var kindField = new EnumField(entry.Kind);
            kindField.style.width = 84;
            kindField.style.flexShrink = 0;
            kindField.style.marginLeft = 6;
            kindField.RegisterValueChangedCallback(evt =>
            {
                entry.Kind = (L2DMotionKind)evt.newValue;
                if (entry.Kind == L2DMotionKind.Idle) entry.Loop = true; // Idle 必须循环
                EditorUtility.SetDirty(profile);
                RefreshValidation();
                onRebuild?.Invoke();
            });
            row.Add(kindField);

            var loopToggle = new Toggle("循环") { value = entry.Loop };
            loopToggle.style.width = 56;
            loopToggle.style.flexShrink = 0;
            loopToggle.style.marginLeft = 6;
            loopToggle.RegisterValueChangedCallback(evt =>
            {
                entry.Loop = evt.newValue;
                EditorUtility.SetDirty(profile);
                RefreshValidation();
            });
            row.Add(loopToggle);

            var delBtn = new Button(() =>
            {
                profile.Motions.Remove(entry);
                EditorUtility.SetDirty(profile);
                onRebuild?.Invoke();
            }) { text = "×" };
            delBtn.style.width = 24;
            delBtn.style.marginLeft = 6;
            delBtn.style.flexShrink = 0;
            delBtn.style.backgroundColor = GalleryTheme.Transparent_Color;
            delBtn.style.color = GalleryTheme.Hex(GalleryTheme.Danger);
            row.Add(delBtn);

            return row;
        }

        // =========================================================
        //                      校验
        // =========================================================
        private VisualElement BuildValidationSection()
        {
            validationLabel = new Label();
            validationLabel.style.whiteSpace = WhiteSpace.Normal;
            validationLabel.style.fontSize = 11;
            validationLabel.style.marginBottom = 8;
            return validationLabel;
        }

        /// <summary>配置校验（设计文档 §4.4）：错误红字、警告黄字，全部通过时隐藏</summary>
        private void RefreshValidation()
        {
            if (validationLabel == null) return;

            var errors = new List<string>();
            var warnings = new List<string>();

            var exprIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (profile.Expressions != null)
            {
                foreach (var e in profile.Expressions)
                {
                    if (e == null) continue;
                    if (string.IsNullOrWhiteSpace(e.ID)) errors.Add("存在空表情 ID");
                    else if (!exprIds.Add(e.ID)) errors.Add($"表情 ID 重复: {e.ID}");
                    if (e.Data == null) warnings.Add($"表情 '{e.ID}' 未配置资产");
                    else if (!IsInExpressionList(e.Data)) errors.Add($"表情 '{e.ID}' 的资产不在表情总表内");
                }
            }

            var motionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int idleCount = 0;
            if (profile.Motions != null)
            {
                foreach (var m in profile.Motions)
                {
                    if (m == null) continue;
                    if (string.IsNullOrWhiteSpace(m.ID)) errors.Add("存在空动作 ID");
                    else if (!motionIds.Add(m.ID)) errors.Add($"动作 ID 重复: {m.ID}");
                    if (m.Kind == L2DMotionKind.Idle)
                    {
                        idleCount++;
                        if (!m.Loop) errors.Add($"Idle 动作 '{m.ID}' 必须勾选循环");
                        if (m.Clip == null) errors.Add($"Idle 动作 '{m.ID}' 未配置 clip（播完无待机兜底）");
                    }
                    if (m.Clip != null && !HasInstanceIdEvent(m.Clip))
                        errors.Add($"动作 '{m.ID}' 的 clip 缺 InstanceId 事件（须为 SDK 导入生成的 .anim）");
                }
            }
            if (idleCount != 1) errors.Add($"Idle 动作必须恰好 1 个（当前 {idleCount} 个）");

            if (profile.ModelPrefab == null) errors.Add("未配置模型 Prefab");

            if (errors.Count == 0 && warnings.Count == 0)
            {
                validationLabel.style.display = DisplayStyle.None;
                return;
            }

            validationLabel.style.display = DisplayStyle.Flex;
            string text = "";
            if (errors.Count > 0)
                text += "✖ " + string.Join("；", errors);
            if (warnings.Count > 0)
                text += (text.Length > 0 ? "\n" : "") + "⚠ " + string.Join("；", warnings);
            validationLabel.text = text;
            validationLabel.style.color = errors.Count > 0
                ? GalleryTheme.Hex(GalleryTheme.Danger)
                : GalleryTheme.Hex(GalleryTheme.Warning);
        }

        private bool IsInExpressionList(CubismExpressionData data)
        {
            if (profile.ExpressionList == null || profile.ExpressionList.CubismExpressionObjects == null)
                return false;
            return profile.ExpressionList.CubismExpressionObjects.Contains(data);
        }

        private static bool HasInstanceIdEvent(AnimationClip clip)
        {
            // SDK 导入的 .anim 内烘焙 InstanceId 事件，用于匹配 fadeMotionList（SDK 源码契约）
            return clip.events != null && clip.events.Any(e => e.functionName == "InstanceId");
        }

        // =========================================================
        //                      辅助
        // =========================================================
        private Label BuildCardLabel(string text)
        {
            return new Label(text)
            {
                style =
                {
                    unityFontStyleAndWeight = FontStyle.Bold,
                    color = GalleryTheme.Hex(GalleryTheme.TextSecondary),
                    fontSize = 11,
                    marginTop = 4,
                    marginBottom = 6
                }
            };
        }
    }
}
