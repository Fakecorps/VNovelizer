using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class VNLocalizationWindow : EditorWindow
{
    private string scriptName = "";
    private bool fillDefaultLocaleFromCsv = true;
    private Vector2 collectionScroll;

    [MenuItem("VNovelizer/Localization/剧情本地化管理器")]
    public static void ShowWindow()
    {
        var wnd = GetWindow<VNLocalizationWindow>("剧情本地化管理器");
        wnd.minSize = new Vector2(600, 320);
    }

    private void OnGUI()
    {
        GUILayout.Label("VNovelizer 剧情本地化管理器", EditorStyles.boldLabel);
        scriptName = EditorGUILayout.TextField("剧本名（不含扩展名）", scriptName);
        fillDefaultLocaleFromCsv = EditorGUILayout.Toggle("默认语言从 CSV 填充（value 为空时）", fillDefaultLocaleFromCsv);

        EditorGUILayout.Space(10);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("校验 ID", GUILayout.Height(35)))
            {
                ValidateIds();
            }
            if (GUILayout.Button("准备当前剧本 Collection", GUILayout.Height(35)))
            {
                PrepareScriptCollection();
            }
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("从 CSV 同步 Key", GUILayout.Height(35)))
            {
                SyncFromCsv();
            }
            if (GUILayout.Button("定位当前剧本 Collection", GUILayout.Height(35)))
            {
                LocateScriptCollection();
            }
        }

        EditorGUILayout.Space(10);

        var help = VNProjectConfig.Instance != null
            ? $"按剧本分表：{VNProjectConfig.Instance.ScriptTablePrefix}{(string.IsNullOrEmpty(scriptName) ? "<scriptName>" : scriptName)}"
            : "请先在 VNProjectConfig 配置 ScriptTablePrefix。";
        EditorGUILayout.HelpBox(help, MessageType.Info);

        DrawCollectionList();
    }

    private void DrawCollectionList()
    {
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("当前剧本 Collection 列表", EditorStyles.boldLabel);

        var collections = VNLocalizationSyncUtility.GetAllScriptCollectionNames();
        if (collections == null || collections.Count == 0)
        {
            EditorGUILayout.HelpBox("暂无匹配 ScriptTablePrefix 的 Collection。", MessageType.None);
            return;
        }

        collectionScroll = EditorGUILayout.BeginScrollView(collectionScroll, GUILayout.Height(220));
        foreach (var collectionName in collections)
        {
            string rowScriptName = VNLocalizationSyncUtility.ExtractScriptNameFromCollection(collectionName);
            using (new EditorGUILayout.HorizontalScope("box"))
            {
                EditorGUILayout.LabelField(collectionName, GUILayout.MinWidth(280));

                if (GUILayout.Button("使用", GUILayout.Width(50)))
                {
                    scriptName = rowScriptName;
                }

                if (GUILayout.Button("定位", GUILayout.Width(50)))
                {
                    LocateByCollectionName(collectionName);
                }

                if (GUILayout.Button("同步", GUILayout.Width(50)))
                {
                    scriptName = rowScriptName;
                    SyncFromCsv();
                }

                if (GUILayout.Button("删除", GUILayout.Width(50)))
                {
                    if (EditorUtility.DisplayDialog("删除 Collection", $"确定删除 {collectionName} 吗？\n此操作不可撤销。", "删除", "取消"))
                    {
                        if (!VNLocalizationSyncUtility.DeleteCollectionByName(collectionName, out var err))
                        {
                            EditorUtility.DisplayDialog("删除失败", err ?? "删除失败。", "确定");
                        }
                    }
                }
            }
        }
        EditorGUILayout.EndScrollView();
    }

    private void ValidateIds()
    {
        if (string.IsNullOrEmpty(scriptName))
        {
            EditorUtility.DisplayDialog("提示", "请输入剧本名。", "确定");
            return;
        }

        if (VNLocalizationSyncUtility.TryValidateScriptCsvIds(scriptName, out var error))
        {
            EditorUtility.DisplayDialog("校验通过", "CSV ID 校验通过。", "确定");
        }
        else
        {
            EditorUtility.DisplayDialog("校验失败", error ?? "校验失败。", "确定");
        }
    }

    private void PrepareScriptCollection()
    {
        if (string.IsNullOrEmpty(scriptName))
        {
            EditorUtility.DisplayDialog("提示", "请输入剧本名。", "确定");
            return;
        }

        if (VNLocalizationSyncUtility.EnsureScriptCollection(scriptName, out _, out var error))
        {
            EditorUtility.DisplayDialog("完成", "当前剧本 Collection 已准备就绪。", "确定");
        }
        else
        {
            EditorUtility.DisplayDialog("失败", error ?? "准备当前剧本 Collection 失败。", "确定");
        }
    }

    private void SyncFromCsv()
    {
        if (string.IsNullOrEmpty(scriptName))
        {
            EditorUtility.DisplayDialog("提示", "请输入剧本名。", "确定");
            return;
        }

        if (VNLocalizationSyncUtility.TrySyncKeysFromCsv(scriptName, fillDefaultLocaleFromCsv, out var error))
        {
            EditorUtility.DisplayDialog("完成", "Key 同步完成。", "确定");
        }
        else
        {
            EditorUtility.DisplayDialog("失败", error ?? "同步失败。", "确定");
        }
    }

    private void LocateScriptCollection()
    {
        if (string.IsNullOrEmpty(scriptName))
        {
            EditorUtility.DisplayDialog("提示", "请输入剧本名。", "确定");
            return;
        }

        if (VNLocalizationSyncUtility.EnsureScriptCollection(scriptName, out var obj, out var error) && obj != null)
        {
            Selection.activeObject = obj;
            EditorGUIUtility.PingObject(obj);
        }
        else
        {
            EditorUtility.DisplayDialog("失败", error ?? "定位失败。", "确定");
        }
    }

    private void LocateByCollectionName(string collectionName)
    {
        var all = UnityEditor.Localization.LocalizationEditorSettings.GetStringTableCollections();
        var obj = all?.FirstOrDefault(c => c != null && c.TableCollectionName == collectionName);
        if (obj != null)
        {
            Selection.activeObject = obj;
            EditorGUIUtility.PingObject(obj);
        }
    }
}

