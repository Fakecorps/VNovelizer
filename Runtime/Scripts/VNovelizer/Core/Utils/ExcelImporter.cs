using UnityEngine;
using UnityEditor.AssetImporters;
using System.IO;

// 专门处理 .xls
[ScriptedImporter(1, "xls")]
public class XlsImporter : ScriptedImporter
{
    public override void OnImportAsset(AssetImportContext ctx)
    {
        // 创建一个占位符，骗过 Unity
        TextAsset subAsset = new TextAsset("Excel file handled by custom importer.");
        ctx.AddObjectToAsset("main", subAsset);
        ctx.SetMainObject(subAsset);
    }
}

// 专门处理 .xlsx
[ScriptedImporter(1, "xlsx")]
public class XlsxImporter : ScriptedImporter
{
    public override void OnImportAsset(AssetImportContext ctx)
    {
        // 逻辑是一样的
        TextAsset subAsset = new TextAsset("Excel file handled by custom importer.");
        ctx.AddObjectToAsset("main", subAsset);
        ctx.SetMainObject(subAsset);
    }
}