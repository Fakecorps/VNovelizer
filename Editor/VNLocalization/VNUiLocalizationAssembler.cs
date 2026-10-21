using System.Collections.Generic;
using System.IO;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Localization;
using UnityEngine.Localization.Components;
using UnityEngine.Localization.Tables;
using VNovelizer.Core.Localization;

/// <summary>
/// 【P3 UI 独立集合·开发用】把 UI 预制体的静态 TMP 一次性装配上
/// <see cref="LocalizeStringEvent"/>（表引用 "VNUI" + 对应 ui.* key）。
///
/// <para>装配范围：</para>
/// <list type="bullet">
/// <item>包内模板预制体（随包分发，开箱即用）；</item>
/// <item>项目内同名本地副本（VNProjectConfig 的 UI 覆写指向的 Assets 副本）——
///     副本与包内模板结构可能分叉（用户自行增删节点），装配同一份白名单，
///     白名单外的文本一律跳过（动态文本天然不受影响）。</item>
/// </list>
///
/// <para>仅在开发本包时运行（VNovelizer → Localization → 装配 UI 本地化组件）。
/// 重跑幂等：已挂组件的物体自动跳过（不覆盖已有配置）。</para>
///
/// <para>装配同时把新 key 追加到包内 UI 翻译表 UI_Locale_en.csv（Text_Origin 取预制体文本，
/// 译文列留空待译），保证 key 清单与预制体装配结果同步。</para>
///
/// <para>白名单语义：只有出现在 TextToKey 里的文本才会挂组件；
/// 动态文本（运行时被代码覆盖的 TMP，如页码/音量值/对话文本）不在名单内，天然跳过。</para>
/// </summary>
public static class VNUiLocalizationAssembler
{
    private sealed class PanelConfig
    {
        public string PrefabPath;
        public Dictionary<string, string> TextToKey = new Dictionary<string, string>();
    }

    // 【2026-10-21 收进开发工具】普通用户无需此功能：包内模板出厂已预装 LocalizeStringEvent
    //（开箱即用），旧版缺绑定由运行时 VNLocalizeStringAutoBinder 自动自愈。
    // 包开发者（改包内预制体/同步 UI 翻译表 key）：Scripting Define Symbols 加 VN_DEV_TOOLS
    // 后菜单出现，与其他开发工具一致。
#if VN_DEV_TOOLS
    [MenuItem("VNovelizer/装配 UI 本地化组件（开发用）", false, 230)]
#endif
    public static void Assemble()
    {
        if (!EditorUtility.DisplayDialog("装配 UI 本地化组件",
                "将为包内 UI 预制体及项目内同名本地副本的静态 TMP 挂载 LocalizeStringEvent（表引用 VNUI）。\n" +
                "已挂载的物体会跳过（不覆盖已有配置）。\n仅开发本包时需要运行。",
                "继续", "取消"))
            return;

        var report = new StringBuilder();
        int added = 0, rebound = 0, skipped = 0, unmatched = 0;
        var newEntries = new List<(string key, string origin)>();

        foreach (var panel in BuildConfigs())
        {
            // 1) 包内模板
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(panel.PrefabPath);
            if (prefab != null)
            {
                report.AppendLine(panel.PrefabPath);
                ProcessPrefab(prefab, panel, report, ref added, ref rebound, ref skipped, ref unmatched, newEntries);
            }
            else
            {
                report.AppendLine($"⚠ 未找到预制体：{panel.PrefabPath}");
            }

            // 2) 项目内同名本地副本（UI 覆写可能指向它们；结构可能与包内分叉）
            string panelName = Path.GetFileNameWithoutExtension(panel.PrefabPath);
            foreach (var guid in AssetDatabase.FindAssets(panelName + " t:Prefab"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path) || path == panel.PrefabPath || path.StartsWith("Packages/"))
                    continue;

                var copy = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (copy == null)
                    continue;

                report.AppendLine($"  （本地副本）{path}");
                ProcessPrefab(copy, panel, report, ref added, ref rebound, ref skipped, ref unmatched, newEntries);
            }
        }

        // 同步包内 UI 翻译表：新 key 追加（Text_Origin = 预制体文本，en 留空待译）
        int csvAdded = SyncUiTranslationCsv(newEntries, report);

        AssetDatabase.SaveAssets();

        report.Insert(0, $"装配完成：新增 {added}，补事件绑定 {rebound}，跳过 {skipped}，动态文本未收录 {unmatched}，" +
                         $"翻译表新增 {csvAdded} 行。\n");
        Debug.Log($"[VNUiLocalizationAssembler]\n{report}");
        EditorUtility.DisplayDialog("装配 UI 本地化组件",
            $"新增 {added}，补事件绑定 {rebound}，跳过 {skipped}，动态文本未收录 {unmatched}。\n" +
            $"UI 翻译表新增 {csvAdded} 行（en 列待填）。\n详情见 Console。", "确定");
    }

    /// <summary>单个预制体的装配：遍历全部 TMP，白名单命中且未挂组件时挂 LocalizeStringEvent。</summary>
    private static void ProcessPrefab(GameObject prefab, PanelConfig panel, StringBuilder report,
        ref int added, ref int rebound, ref int skipped, ref int unmatched, List<(string key, string origin)> newEntries)
    {
        bool dirty = false;

        foreach (var tmp in prefab.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            string text = (tmp.text ?? "").Trim();
            if (string.IsNullOrEmpty(text))
                continue;

            if (!panel.TextToKey.TryGetValue(text, out var key))
            {
                // 动态文本（页码/音量值/对话文本等）不在白名单 → 不装配
                unmatched++;
                continue;
            }

            var existing = tmp.GetComponent<LocalizeStringEvent>();
            if (existing != null)
            {
                // 【Fix-BrokenRefReattach】失效引用检测：KeyId 型指向已不存在的 id / 引用为空。
                // 典型来源：用户在 Inspector 手动挂的组件（保存为数字 KeyId），聚合器全量重建后
                // id 重排导致失效（Inspector 警告 "Missing an entry with the Key Id: xxx"）。
                // 装配器重挂为 key 字符串引用（KeyId=0），跨聚合稳定。
                if (IsBrokenStringReference(existing))
                {
                    Object.DestroyImmediate(existing, true);
                    report.AppendLine($"  ⟳ 失效引用已重挂：{text} → {key}");
                }
                else if (IsMissingUpdateStringBinding(existing))
                {
                    // 【Fix-L10N-Bind】存量组件缺事件绑定（旧版装配器产物：OnUpdateString 空列表，
                    // 译文拉到了也写不进 TMP → 切语言文字不变）→ 补持久化绑定，无需重挂组件。
                    BindUpdateStringToTmp(existing, tmp);
                    rebound++;
                    dirty = true;
                    report.AppendLine($"  ⟳ 补事件绑定：{text} → OnUpdateString → set_text");
                    continue;
                }
                else
                {
                    skipped++;
                    continue;
                }
            }

            var evt = tmp.gameObject.AddComponent<LocalizeStringEvent>();
            evt.StringReference = new LocalizedString(VNLocalizationService.UICollectionName, key);
            // 【Fix-L10N-Bind】程序化 AddComponent 必须显式绑定 OnUpdateString → set_text：
            // 官方 1.5.2 的 Inspector 没有任何自动绑定，缺失时组件空转
            //（表加载成功、StringChanged 正常触发，但没有任何东西写 TMP）。
            BindUpdateStringToTmp(evt, tmp);
            added++;
            report.AppendLine($"  ✓ {text} → {key}");
            dirty = true;
            newEntries.Add((key, text));
        }

        if (dirty)
        {
            EditorUtility.SetDirty(prefab);
            AssetDatabase.SaveAssetIfDirty(prefab);
        }
    }

    /// <summary>
    /// 【Fix-L10N-Bind】把 OnUpdateString 持久化绑定到同物体 TMP 的 set_text（与 Inspector 手动
    /// 绑定 text 属性完全等价、随预制体序列化）。EditorAndRuntime 保证编辑器预览与 Play/打包后均触发。
    /// 实现说明：本 Unity 版本的 UnityEventTools 没有 (UnityEvent&lt;string&gt;, UnityAction&lt;string&gt;)
    /// 两参重载（仅有的三参版第 3 参是"固定调用参数值"，不适用"传事件参数"语义），
    /// 故用 SerializedObject 直接构造 PersistentCall：m_MethodName=set_text、
    /// m_Mode=EventDefined（事件参数 = 本地化文本传入）、m_CallState=EditorAndRuntime。
    /// </summary>
    private static void BindUpdateStringToTmp(LocalizeStringEvent evt, TextMeshProUGUI tmp)
    {
        var so = new SerializedObject(evt);
        var calls = so.FindProperty("m_UpdateString.m_PersistentCalls.m_Calls");

        // 防重：已存在指向同 TMP 的绑定则不重复添加
        for (int i = 0; i < calls.arraySize; i++)
        {
            if (calls.GetArrayElementAtIndex(i).FindPropertyRelative("m_Target").objectReferenceValue == tmp)
                return;
        }

        int idx = calls.arraySize;
        calls.InsertArrayElementAtIndex(idx);
        var call = calls.GetArrayElementAtIndex(idx);
        call.FindPropertyRelative("m_Target").objectReferenceValue = tmp;
        call.FindPropertyRelative("m_TargetAssemblyTypeName").stringValue = string.Empty;
        call.FindPropertyRelative("m_MethodName").stringValue = "set_text";
        call.FindPropertyRelative("m_Mode").intValue =
            (int)UnityEngine.Events.PersistentListenerMode.EventDefined;
        var args = call.FindPropertyRelative("m_Arguments");
        args.FindPropertyRelative("m_ObjectArgument").objectReferenceValue = null;
        args.FindPropertyRelative("m_ObjectArgumentAssemblyTypeName").stringValue = string.Empty;
        args.FindPropertyRelative("m_IntArgument").intValue = 0;
        args.FindPropertyRelative("m_FloatArgument").floatValue = 0f;
        args.FindPropertyRelative("m_StringArgument").stringValue = string.Empty;
        args.FindPropertyRelative("m_BoolArgument").boolValue = false;
        call.FindPropertyRelative("m_CallState").intValue =
            (int)UnityEngine.Events.UnityEventCallState.EditorAndRuntime;
        so.ApplyModifiedProperties();
    }

    /// <summary>【Fix-L10N-Bind】OnUpdateString 无任何持久化监听（旧版装配器产物 / 手挂遗漏）。</summary>
    private static bool IsMissingUpdateStringBinding(LocalizeStringEvent evt)
    {
        return evt.OnUpdateString == null || evt.OnUpdateString.GetPersistentEventCount() == 0;
    }

    /// <summary>
    /// 【Fix-BrokenRefReattach】判断 LocalizeStringEvent 的 StringReference 是否已失效：
    /// ① 引用为空（TableReference/EntryReference 均空）；② KeyId 型引用但当前 SharedData 里查不到该 id
    /// （聚合器全量重建后 id 重排的典型表现）。key 字符串型引用（KeyId=0）不会失效，返回 false。
    /// </summary>
    private static bool IsBrokenStringReference(LocalizeStringEvent evt)
    {
        try
        {
            var sr = evt.StringReference;
            if (sr == null || sr.IsEmpty)
                return true;

            if (sr.TableReference.ReferenceType == TableReference.Type.Empty)
                return true;

            if (sr.TableEntryReference.ReferenceType == TableEntryReference.Type.Id)
            {
                var collection = UnityEditor.Localization.LocalizationEditorSettings
                    .GetStringTableCollection(sr.TableReference.TableCollectionName);
                var sharedData = collection?.SharedData;
                if (sharedData == null)
                    return true; // 集合都没了，必然失效

                return sharedData.GetEntry(sr.TableEntryReference.KeyId) == null;
            }

            return false; // key 字符串型：运行时按名查，不随聚合失效
        }
        catch
        {
            return false; // 检测异常时不误删用户组件
        }
    }

    /// <summary>
    /// 把新 key 追加到包内 UI 翻译表 UI_Locale_en.csv（幂等：已存在的 key 跳过）。
    /// 返回追加行数。
    /// </summary>
    private static int SyncUiTranslationCsv(List<(string key, string origin)> entries, StringBuilder report)
    {
        if (entries.Count == 0)
            return 0;

        const string relPath = "Packages/com.fakecorps.vnovelizer/Runtime/PackageDefault/VNovelizerRes/UI_Localization/UI_Locale_en.csv";
        string full = Path.GetFullPath(relPath);
        if (!File.Exists(full))
        {
            report.AppendLine($"⚠ UI 翻译表不存在：{full}");
            return 0;
        }

        var lines = new List<string>(File.ReadAllLines(full, Encoding.UTF8));
        var existing = new HashSet<string>();
        for (int i = 1; i < lines.Count; i++)
        {
            var cols = ScriptParser.SplitCSV(lines[i]);
            if (cols.Length >= 1 && !string.IsNullOrEmpty(cols[0]))
                existing.Add(cols[0].Trim());
        }

        int added = 0;
        foreach (var e in entries)
        {
            if (existing.Contains(e.key))
                continue;
            lines.Add(string.Join(",",
                VNTranslationTemplateGenerator.EscapeCsvField(e.key),
                VNTranslationTemplateGenerator.EscapeCsvField(e.origin ?? ""),
                "")); // en 译文列留空待译
            existing.Add(e.key);
            added++;
        }

        if (added > 0)
        {
            File.WriteAllLines(full, lines.ToArray(), new UTF8Encoding(true));
            report.AppendLine($"UI 翻译表已追加 {added} 行（{relPath}）");
        }

        return added;
    }

    private static PanelConfig[] BuildConfigs()
    {
        const string root = "Packages/com.fakecorps.vnovelizer/Runtime/PackageDefault/VNovelizerRes/VNPrefabs/UI";

        // ==================== 主菜单 ====================
        var mainMenu = new PanelConfig { PrefabPath = $"{root}/MainMenu/MainMenuPanel.prefab" };
        mainMenu.TextToKey["VNovelizer"] = "ui.mainmenu.title";
        mainMenu.TextToKey["开始新游戏"] = "ui.mainmenu.start";
        mainMenu.TextToKey["加载游戏"] = "ui.mainmenu.load";
        mainMenu.TextToKey["画廊"] = "ui.mainmenu.gallery";
        mainMenu.TextToKey["设置"] = "ui.mainmenu.settings";
        mainMenu.TextToKey["退出游戏"] = "ui.mainmenu.exit";

        // ==================== 设置面板（静态标签；数值文本/下拉占位为动态，不入名单） ====================
        var settings = new PanelConfig { PrefabPath = $"{root}/Settings/SettingsPanel.prefab" };
        settings.TextToKey["Settings"] = "ui.settings.title";
        settings.TextToKey["音量"] = "ui.settings.volumn.title";
        settings.TextToKey["主音量"] = "ui.settings.volumn.mastervolumnlabel";
        settings.TextToKey["背景音乐"] = "ui.settings.volumn.bgmvolumnlabel";
        settings.TextToKey["效果音"] = "ui.settings.volumn.sfxvolumnlabel";
        settings.TextToKey["配音音量"] = "ui.settings.volumn.voicevolumnlabel";
        settings.TextToKey["文本"] = "ui.settings.text.title";
        settings.TextToKey["文本播放速度"] = "ui.settings.text.textspeedlabel";
        settings.TextToKey["自动播放间隔"] = "ui.settings.text.autointervallabel";
        settings.TextToKey["显示"] = "ui.settings.display.title";
        settings.TextToKey["DisplayMode"] = "ui.settings.display.displaymodelabel";
        settings.TextToKey["Resolution"] = "ui.settings.display.resolutionlabel";
        settings.TextToKey["语言"] = "ui.settings.language"; // 本地副本新增的 LocalizationGroup

        // ==================== 暂停面板 ====================
        var pause = new PanelConfig { PrefabPath = $"{root}/Pause/PausePanel.prefab" };
        pause.TextToKey["保存"] = "ui.pause.save";
        pause.TextToKey["加载"] = "ui.pause.load";
        pause.TextToKey["设置"] = "ui.pause.settings";
        pause.TextToKey["返回标题画面"] = "ui.pause.totitle";

        // ==================== 确认弹窗（Message 文本为动态，不入名单） ====================
        var confirm = new PanelConfig { PrefabPath = $"{root}/Confirm/ConfirmPanel.prefab" };
        confirm.TextToKey["Yes"] = "ui.confirm.yes";
        confirm.TextToKey["No"] = "ui.confirm.no";

        // ==================== 画廊（页签按钮；页码/音量/曲名等为动态，不入名单） ====================
        // 注：CGPage/ScenePage 的翻页/关闭按钮已改为图标（无 TMP），不参与装配；
        //     中文显示由翻译表 Text_Origin 列控制（预制体英文文本仅作装配锚点）。
        var gallery = new PanelConfig { PrefabPath = $"{root}/Gallery/GalleryPanel.prefab" };
        gallery.TextToKey["CG"] = "ui.gallery.tab.cg";
        gallery.TextToKey["Scene"] = "ui.gallery.tab.scene";
        gallery.TextToKey["Music"] = "ui.gallery.tab.music";

        return new[] { mainMenu, settings, pause, confirm, gallery };
    }
}
