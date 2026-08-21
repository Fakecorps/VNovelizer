using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Flag 数据类型
/// </summary>
public enum FlagType
{
    Bool,
    Int,
    Float,
    String
}

/// <summary>
/// Flag 作用域：
/// Global = 跨存档持久（global_data.json），读档不回退（适合好感度等累计值）；
/// Save   = 随存档快照保存，读档回退（适合章节进度、分支标记）。
/// </summary>
public enum FlagScope
{
    Global,
    Save
}

/// <summary>
/// Flag 注册表（变量声明清单）。
/// 所有剧本中引用的 Flag 建议先在此声明（名称/类型/作用域/默认值），
/// 供运行时类型推断、新游戏复位与编辑器静态校验使用。
/// 资产可保存在项目内任意位置：运行时按固定资源键 FlagService.DefaultRegistryPath
/// 经资源服务链加载（Addressables 地址由编辑器自动登记；旧版项目回退 Resources 路径）；
/// 不存在时 FlagService 进入兼容模式（所有 Flag 按旧全局行为处理）。
/// </summary>
[CreateAssetMenu(menuName = "VNovelizer/FlagRegistry", fileName = "VNFlagRegistry")]
public class FlagRegistry : ScriptableObject
{
    [Serializable]
    public class FlagDefinition
    {
        [Tooltip("Flag 唯一名称（建议 PascalCase，禁止空格与 ,()&|! 等保留字符）")]
        public string Name = "NewFlag";

        [Tooltip("数据类型")]
        public FlagType Type = FlagType.Bool;

        [Tooltip("Global=跨存档持久（好感度等累计值）；Save=随存档回退（分支标记等）")]
        public FlagScope Scope = FlagScope.Save;

        [Tooltip("默认值（统一按字符串存储，按 Type 解析；新游戏时复位）")]
        public string DefaultValue = "";

        [Tooltip("分组（仅编辑器显示用）")]
        public string Group = "";

        [Tooltip("备注说明")]
        public string Comment = "";
    }

    public List<FlagDefinition> Definitions = new List<FlagDefinition>();

    /// <summary>
    /// 按名称查找定义（flag 数量级小，线性扫描即可；运行时注册表不变，无需缓存）
    /// </summary>
    public FlagDefinition Find(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        for (int i = 0; i < Definitions.Count; i++)
        {
            if (Definitions[i].Name == name) return Definitions[i];
        }
        return null;
    }

    /// <summary>
    /// 校验 Flag 名称是否合法（供编辑器与命令运行时共用）
    /// </summary>
    public static bool IsValidName(string name, out string reason)
    {
        reason = null;
        if (string.IsNullOrEmpty(name) || string.IsNullOrWhiteSpace(name))
        {
            reason = "名称为空";
            return false;
        }
        // 保留字符：空格、命令分隔符与条件语法符号
        char[] forbidden = { ' ', ',', '(', ')', '&', '|', '!', '"', '\'', '=', '<', '>', '\t' };
        foreach (char c in name)
        {
            if (Array.IndexOf(forbidden, c) >= 0)
            {
                reason = $"名称包含非法字符 '{c}'（禁止空格与 ,()&|!=<> 引号）";
                return false;
            }
        }
        return true;
    }

    private void OnValidate()
    {
        // 编辑器侧轻量清洗：去首尾空格
        if (Definitions == null) return;
        foreach (var def in Definitions)
        {
            if (def != null && def.Name != null) def.Name = def.Name.Trim();
        }
    }
}
