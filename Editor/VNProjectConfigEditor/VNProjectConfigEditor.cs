using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

#if true
using Alchemy.Inspector;
#endif

/// <summary>
/// VNProjectConfig 的自定义 Inspector。
///
/// 说明：本类接管 VNProjectConfig 的 Inspector 渲染。
///      在当前项目中，Alchemy 的 BoxGroupDrawer 无法正常显示子字段
///      （只显示分组标题，内容空白）。
///      本类绕开 Alchemy 渲染 BoxGroup，改用纯 UIElements 自绘分组卡片，
///      保证所有字段都能正常显示。
///      同时支持 [HideScriptField]、[Order]、[BoxGroup] 等 Alchemy 特性。
/// </summary>
[CustomEditor(typeof(VNProjectConfig))]
public class VNProjectConfigEditor : Editor
{
    public override VisualElement CreateInspectorGUI()
    {
        var root = new VisualElement
        {
            style =
            {
                paddingTop = 4f,
                paddingBottom = 4f,
                flexDirection = FlexDirection.Column,
            }
        };

        // m_Script 字段（除非带 [HideScriptField]）
        if (target.GetType().GetCustomAttribute<HideScriptFieldAttribute>() == null)
        {
            var scriptProp = serializedObject.FindProperty("m_Script");
            if (scriptProp != null)
            {
                var scriptField = new PropertyField(scriptProp);
                scriptField.SetEnabled(false);
                root.Add(scriptField);
                root.Add(new VisualElement
                {
                    style = { height = EditorGUIUtility.standardVerticalSpacing * 0.5f }
                });
            }
        }

        // 按 BoxGroup 名称分组所有可序列化字段
        var grouped = new Dictionary<string, List<MemberInfo>>();
        var groupOrder = new List<string>();

        var members = target.GetType().GetMembers(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        foreach (var member in members)
        {
            if (!(member is FieldInfo) && !(member is PropertyInfo)) continue;

            // 必须有 BoxGroup 特性
            BoxGroupAttribute groupAttr = null;
            foreach (var attr in member.GetCustomAttributes(true))
            {
                if (attr is BoxGroupAttribute bg) { groupAttr = bg; break; }
            }
            if (groupAttr == null) continue;

            // 必须可序列化（public 或带 [SerializeField]）
            bool isSerializable = false;
            if (member is FieldInfo f)
            {
                isSerializable = f.IsPublic;
                if (!isSerializable)
                {
                    foreach (var a in f.GetCustomAttributes(true))
                    {
                        if (a is SerializeField) { isSerializable = true; break; }
                    }
                }
            }
            else if (member is PropertyInfo)
            {
                foreach (var a in member.GetCustomAttributes(true))
                {
                    if (a is SerializeField) { isSerializable = true; break; }
                }
            }
            if (!isSerializable) continue;

            string groupName = string.IsNullOrEmpty(groupAttr.GroupPath) ? "默认" : groupAttr.GroupPath;
            if (!grouped.ContainsKey(groupName))
            {
                grouped[groupName] = new List<MemberInfo>();
                groupOrder.Add(groupName);
            }
            grouped[groupName].Add(member);
        }

        // 按 Order 排序组内的成员
        foreach (var kv in grouped)
        {
            kv.Value.Sort((a, b) => GetOrder(a).CompareTo(GetOrder(b)));
        }

        // 创建分组卡片
        foreach (var groupName in groupOrder)
        {
            var card = CreateGroupCard(groupName, grouped[groupName]);
            root.Add(card);
        }

        return root;
    }

    static int GetOrder(MemberInfo m)
    {
        foreach (var a in m.GetCustomAttributes(true))
        {
            if (a is OrderAttribute o) return o.Order;
        }
        return 0;
    }

    VisualElement CreateGroupCard(string title, List<MemberInfo> members)
    {
        var card = new VisualElement
        {
            style =
            {
                backgroundColor = new Color(0.22f, 0.22f, 0.22f, 0.4f),
                borderTopColor = new Color(0, 0, 0, 0.4f),
                borderBottomColor = new Color(0, 0, 0, 0.4f),
                borderLeftColor = new Color(0, 0, 0, 0.4f),
                borderRightColor = new Color(0, 0, 0, 0.4f),
                borderTopWidth = 1f,
                borderBottomWidth = 1f,
                borderLeftWidth = 1f,
                borderRightWidth = 1f,
                borderTopLeftRadius = 4f,
                borderTopRightRadius = 4f,
                borderBottomLeftRadius = 4f,
                borderBottomRightRadius = 4f,
                paddingTop = 6f,
                paddingBottom = 6f,
                paddingLeft = 6f,
                paddingRight = 6f,
                marginTop = 4f,
                marginBottom = 4f,
                flexDirection = FlexDirection.Column,
            }
        };

        var titleLabel = new Label(title)
        {
            style =
            {
                fontSize = 12,
                unityFontStyleAndWeight = FontStyle.Bold,
                marginBottom = 4f,
                color = new Color(0.85f, 0.85f, 0.85f, 1f),
                unityTextAlign = TextAnchor.MiddleLeft,
            }
        };
        card.Add(titleLabel);

        // "八、UI 模板覆写"分组：头部注入"从模板创建…"按钮（复制包内模板 + 自动指派覆写）
        if (title.Contains("UI 模板覆写"))
        {
            var hint = new Label("留空 = 使用包内默认模板（无需任何配置）。要自定义某个 UI，点击下方按钮从模板创建副本（画廊数据容器同理，也可直接拖拽指派）：")
            {
                style =
                {
                    whiteSpace = UnityEngine.UIElements.WhiteSpace.Normal,
                    marginBottom = 4f,
                    color = new Color(0.7f, 0.7f, 0.7f, 1f),
                }
            };
            card.Add(hint);

            var createButton = new Button(() =>
            {
                var config = target as VNProjectConfig;
                if (config != null) VNUIPrefabTemplateCreator.ShowTemplateMenu(config);
            })
            {
                text = "从模板创建自定义 UI…",
                style = { marginBottom = 6f },
            };
            card.Add(createButton);
        }

        foreach (var member in members)
        {
            var prop = serializedObject.FindProperty(member.Name);
            if (prop == null) continue;

            var field = new PropertyField(prop);
            field.style.marginTop = 1f;
            field.style.marginBottom = 1f;
            card.Add(field);
        }

        return card;
    }
}