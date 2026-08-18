using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Flag 作用域路由服务（Flag 系统扩展核心，见 Docs/VNFlagSystemDesign.md）。
/// 所有 Flag 读写经此统一入口，按注册表中该 Flag 的 Scope 决定实际存储位置：
///  - Global：写入 GlobalDataManager（global_data.json，跨存档持久，读档不回退）
///  - Save  ：写入内存快照区（随 SaveData 序列化，读档回退，新游戏复位为默认值）
///  - 兼容模式（无注册表资产）：全部按旧行为写入 GlobalData，存量剧本 100% 兼容。
/// </summary>
public class FlagService : BaseManager<FlagService>
{
    /// <summary>注册表默认 Resources 路径（Setup 后位于 Assets/Resources/VNovelizerRes/）</summary>
    public const string DefaultRegistryPath = "VNovelizerRes/VNFlagRegistry";

    private FlagRegistry registry;
    private bool initialized;
    private bool warnedNoRegistry;
    private readonly HashSet<string> warnedRelativeOnMissing = new HashSet<string>();

    // Save 作用域运行时存储（随存档快照序列化，不落 global_data.json）
    private readonly Dictionary<string, bool> saveBool = new Dictionary<string, bool>();
    private readonly Dictionary<string, int> saveInt = new Dictionary<string, int>();
    private readonly Dictionary<string, float> saveFloat = new Dictionary<string, float>();
    private readonly Dictionary<string, string> saveString = new Dictionary<string, string>();

    /// <summary>当前注册表（无则返回 null，即兼容模式）</summary>
    public FlagRegistry Registry
    {
        get { EnsureInit(); return registry; }
    }

    public bool HasRegistry
    {
        get { EnsureInit(); return registry != null; }
    }

    private void EnsureInit()
    {
        if (initialized) return;
        registry = Resources.Load<FlagRegistry>(DefaultRegistryPath);
        if (registry == null && !warnedNoRegistry)
        {
            warnedNoRegistry = true;
            Debug.Log("[FlagService] 未找到 Flag 注册表（Resources/" + DefaultRegistryPath + "），进入兼容模式：所有 Flag 按旧全局行为处理。可通过 VNovelizer → Flag 编辑器 创建注册表。");
        }
        initialized = true;
    }

    private FlagRegistry.FlagDefinition FindDef(string name)
    {
        EnsureInit();
        return registry != null ? registry.Find(name) : null;
    }

    public bool IsRegistered(string name)
    {
        return FindDef(name) != null;
    }

    // ==================== 类型查询（条件求值用） ====================

    /// <summary>
    /// 获取 Flag 的类型：注册表优先；未注册时按运行时存储推断（Int→Float→Bool→String）。
    /// </summary>
    public FlagType GetFlagType(string name)
    {
        var def = FindDef(name);
        if (def != null) return def.Type;

        var gd = GlobalDataManager.GetInstance().GetGlobalData();
        if (gd.IntFlags != null && gd.IntFlags.ContainsKey(name)) return FlagType.Int;
        if (gd.FloatFlags != null && gd.FloatFlags.ContainsKey(name)) return FlagType.Float;
        if (gd.Flags != null && gd.Flags.ContainsKey(name)) return FlagType.Bool;
        if (gd.StringFlags != null && gd.StringFlags.ContainsKey(name)) return FlagType.String;
        return FlagType.Bool;
    }

    /// <summary>任意存储中是否已存在该 Flag 的值</summary>
    public bool HasValue(string name)
    {
        var def = FindDef(name);
        if (def == null)
        {
            var gd = GlobalDataManager.GetInstance().GetGlobalData();
            return (gd.Flags != null && gd.Flags.ContainsKey(name))
                || (gd.IntFlags != null && gd.IntFlags.ContainsKey(name))
                || (gd.FloatFlags != null && gd.FloatFlags.ContainsKey(name))
                || (gd.StringFlags != null && gd.StringFlags.ContainsKey(name));
        }
        if (def.Scope == FlagScope.Save)
        {
            switch (def.Type)
            {
                case FlagType.Bool: return saveBool.ContainsKey(name);
                case FlagType.Int: return saveInt.ContainsKey(name);
                case FlagType.Float: return saveFloat.ContainsKey(name);
                default: return saveString.ContainsKey(name);
            }
        }
        var g = GlobalDataManager.GetInstance().GetGlobalData();
        switch (def.Type)
        {
            case FlagType.Bool: return g.Flags != null && g.Flags.ContainsKey(name);
            case FlagType.Int: return g.IntFlags != null && g.IntFlags.ContainsKey(name);
            case FlagType.Float: return g.FloatFlags != null && g.FloatFlags.ContainsKey(name);
            default: return g.StringFlags != null && g.StringFlags.ContainsKey(name);
        }
    }

    // ==================== Bool ====================

    public void SetBool(string name, bool value)
    {
        var def = FindDef(name);
        if (def != null && def.Type != FlagType.Bool)
        {
            Debug.LogError($"[FlagService] 类型不匹配：flag '{name}' 在注册表中声明为 {def.Type}，不能按 Bool 写入");
            return;
        }
        if (def != null && def.Scope == FlagScope.Save)
        {
            saveBool[name] = value;
            return;
        }
        // Global 与未注册（兼容模式）：走旧的 GlobalData 路径（含立即落盘）
        GlobalDataManager.GetInstance().SetBoolFlag(name, value);
    }

    public bool GetBool(string name)
    {
        var def = FindDef(name);
        if (def == null)
        {
            return GlobalDataManager.GetInstance().GetGlobalData().GetFlag(name);
        }
        if (def.Type != FlagType.Bool)
        {
            Debug.LogError($"[FlagService] 类型不匹配：flag '{name}' 在注册表中声明为 {def.Type}，不能按 Bool 读取");
            return false;
        }
        if (def.Scope == FlagScope.Save)
        {
            bool v;
            return saveBool.TryGetValue(name, out v) ? v : ParseBoolDefault(def);
        }
        var gd = GlobalDataManager.GetInstance().GetGlobalData();
        return gd.Flags.ContainsKey(name) ? gd.Flags[name] : ParseBoolDefault(def);
    }

    // ==================== Int ====================

    public void SetInt(string name, int value)
    {
        var def = FindDef(name);
        if (def != null && def.Type != FlagType.Int)
        {
            Debug.LogError($"[FlagService] 类型不匹配：flag '{name}' 在注册表中声明为 {def.Type}，不能按 Int 写入");
            return;
        }
        if (def != null && def.Scope == FlagScope.Save)
        {
            saveInt[name] = value;
            return;
        }
        GlobalDataManager.GetInstance().SetIntFlag(name, value);
    }

    public int GetInt(string name)
    {
        var def = FindDef(name);
        if (def == null)
        {
            return GlobalDataManager.GetInstance().GetGlobalData().GetIntFlag(name);
        }
        if (def.Type != FlagType.Int)
        {
            Debug.LogError($"[FlagService] 类型不匹配：flag '{name}' 在注册表中声明为 {def.Type}，不能按 Int 读取");
            return 0;
        }
        if (def.Scope == FlagScope.Save)
        {
            int v;
            return saveInt.TryGetValue(name, out v) ? v : ParseIntDefault(def);
        }
        var gd = GlobalDataManager.GetInstance().GetGlobalData();
        return gd.IntFlags.ContainsKey(name) ? gd.IntFlags[name] : ParseIntDefault(def);
    }

    /// <summary>
    /// int 相对运算（setintflag(name,+10) 等的底层实现）。
    /// 目标 Flag 从未设置过时以 0 为基准运算（首次警告）。
    /// 返回运算结果。
    /// </summary>
    public int ApplyIntOperation(string name, char op, int operand)
    {
        if (!HasValue(name) && warnedRelativeOnMissing.Add(name))
        {
            Debug.LogWarning($"[FlagService] 对从未设置过的 flag '{name}' 做相对运算，以 0 为基准。建议在 Flag 注册表中声明默认值。");
        }
        int cur = GetInt(name);
        int result;
        switch (op)
        {
            case '+': result = cur + operand; break;
            case '-': result = cur - operand; break;
            case '*': result = cur * operand; break;
            case '/':
                if (operand == 0)
                {
                    Debug.LogError($"[FlagService] flag '{name}' 相对除法除数为 0，已忽略");
                    return cur;
                }
                result = cur / operand;
                break;
            default:
                Debug.LogError($"[FlagService] 不支持的相对运算符 '{op}'");
                return cur;
        }
        SetInt(name, result);
        return result;
    }

    // ==================== Float ====================

    public void SetFloat(string name, float value)
    {
        var def = FindDef(name);
        if (def != null && def.Type != FlagType.Float)
        {
            Debug.LogError($"[FlagService] 类型不匹配：flag '{name}' 在注册表中声明为 {def.Type}，不能按 Float 写入");
            return;
        }
        if (def != null && def.Scope == FlagScope.Save)
        {
            saveFloat[name] = value;
            return;
        }
        GlobalDataManager.GetInstance().SetFloatFlag(name, value);
    }

    public float GetFloat(string name)
    {
        var def = FindDef(name);
        if (def == null)
        {
            return GlobalDataManager.GetInstance().GetGlobalData().GetFloatFlag(name);
        }
        if (def.Type != FlagType.Float)
        {
            Debug.LogError($"[FlagService] 类型不匹配：flag '{name}' 在注册表中声明为 {def.Type}，不能按 Float 读取");
            return 0f;
        }
        if (def.Scope == FlagScope.Save)
        {
            float v;
            return saveFloat.TryGetValue(name, out v) ? v : ParseFloatDefault(def);
        }
        var gd = GlobalDataManager.GetInstance().GetGlobalData();
        return gd.FloatFlags.ContainsKey(name) ? gd.FloatFlags[name] : ParseFloatDefault(def);
    }

    // ==================== String ====================

    public void SetString(string name, string value)
    {
        var def = FindDef(name);
        if (def != null && def.Type != FlagType.String)
        {
            Debug.LogError($"[FlagService] 类型不匹配：flag '{name}' 在注册表中声明为 {def.Type}，不能按 String 写入");
            return;
        }
        if (def != null && def.Scope == FlagScope.Save)
        {
            saveString[name] = value ?? "";
            return;
        }
        GlobalDataManager.GetInstance().SetStringFlag(name, value);
    }

    public string GetString(string name)
    {
        var def = FindDef(name);
        if (def == null)
        {
            return GlobalDataManager.GetInstance().GetGlobalData().GetStringFlag(name);
        }
        if (def.Type != FlagType.String)
        {
            Debug.LogError($"[FlagService] 类型不匹配：flag '{name}' 在注册表中声明为 {def.Type}，不能按 String 读取");
            return "";
        }
        if (def.Scope == FlagScope.Save)
        {
            string v;
            return saveString.TryGetValue(name, out v) ? v : (def.DefaultValue ?? "");
        }
        var gd = GlobalDataManager.GetInstance().GetGlobalData();
        return gd.StringFlags.ContainsKey(name) ? gd.StringFlags[name] : (def.DefaultValue ?? "");
    }

    // ==================== 存档快照 ====================

    /// <summary>
    /// 导出随存档序列化的 Flag 快照（Save 作用域 + 兼容模式下 GlobalData 的全部 Flag）。
    /// Global 作用域 Flag 不进存档（跨存档累计）。
    /// </summary>
    public void ExportForSave(SaveData saveData)
    {
        EnsureInit();
        if (saveData == null) return;

        var gd = GlobalDataManager.GetInstance().GetGlobalData();
        saveData.Flags = new Dictionary<string, bool>();
        saveData.IntFlags = new Dictionary<string, int>();
        saveData.FloatFlags = new Dictionary<string, float>();
        saveData.StringFlags = new Dictionary<string, string>();

        if (registry == null)
        {
            // 兼容模式：全量快照 GlobalData（与旧版行为一致）
            CopyInto(gd.Flags, saveData.Flags);
            CopyInto(gd.IntFlags, saveData.IntFlags);
            CopyInto(gd.FloatFlags, saveData.FloatFlags);
            CopyInto(gd.StringFlags, saveData.StringFlags);
            return;
        }

        // Save 作用域：写入注册的 Flag（未设置过的写入默认值，保证快照完整）
        foreach (var def in registry.Definitions)
        {
            if (def == null || def.Scope != FlagScope.Save) continue;
            switch (def.Type)
            {
                case FlagType.Bool: saveData.Flags[def.Name] = GetBool(def.Name); break;
                case FlagType.Int: saveData.IntFlags[def.Name] = GetInt(def.Name); break;
                case FlagType.Float: saveData.FloatFlags[def.Name] = GetFloat(def.Name); break;
                case FlagType.String: saveData.StringFlags[def.Name] = GetString(def.Name); break;
            }
        }

        // 兼容残留：剧本中使用了未注册 Flag（存于 GlobalData）时仍按旧行为纳入快照
        AddUnregistered(gd.Flags, saveData.Flags);
        AddUnregistered(gd.IntFlags, saveData.IntFlags);
        AddUnregistered(gd.FloatFlags, saveData.FloatFlags);
        AddUnregistered(gd.StringFlags, saveData.StringFlags);
    }

    /// <summary>
    /// 从存档恢复 Flag 状态：Save 作用域 → 内存快照区；兼容模式 → 整体覆盖 GlobalData（旧行为）。
    /// Global 作用域不受存档影响。
    /// </summary>
    public void ImportFromSave(SaveData saveData)
    {
        EnsureInit();
        saveBool.Clear();
        saveInt.Clear();
        saveFloat.Clear();
        saveString.Clear();

        var gd = GlobalDataManager.GetInstance().GetGlobalData();
        if (registry == null)
        {
            // 兼容模式：现状行为——整体覆盖 GlobalData（不立即落盘，与旧版一致）
            if (saveData.Flags != null) gd.Flags = new Dictionary<string, bool>(saveData.Flags);
            if (saveData.IntFlags != null) gd.IntFlags = new Dictionary<string, int>(saveData.IntFlags);
            if (saveData.StringFlags != null) gd.StringFlags = new Dictionary<string, string>(saveData.StringFlags);
            if (saveData.FloatFlags != null) gd.FloatFlags = new Dictionary<string, float>(saveData.FloatFlags);
            return;
        }

        // Save 作用域：从存档恢复，缺失项回落到注册表默认值
        foreach (var def in registry.Definitions)
        {
            if (def == null || def.Scope != FlagScope.Save) continue;
            switch (def.Type)
            {
                case FlagType.Bool:
                    saveBool[def.Name] = (saveData.Flags != null && saveData.Flags.ContainsKey(def.Name))
                        ? saveData.Flags[def.Name] : ParseBoolDefault(def);
                    break;
                case FlagType.Int:
                    saveInt[def.Name] = (saveData.IntFlags != null && saveData.IntFlags.ContainsKey(def.Name))
                        ? saveData.IntFlags[def.Name] : ParseIntDefault(def);
                    break;
                case FlagType.Float:
                    saveFloat[def.Name] = (saveData.FloatFlags != null && saveData.FloatFlags.ContainsKey(def.Name))
                        ? saveData.FloatFlags[def.Name] : ParseFloatDefault(def);
                    break;
                case FlagType.String:
                    saveString[def.Name] = (saveData.StringFlags != null && saveData.StringFlags.ContainsKey(def.Name))
                        ? saveData.StringFlags[def.Name] : (def.DefaultValue ?? "");
                    break;
            }
        }

        // 兼容残留：未注册 Flag 按名字覆盖回 GlobalData（不落盘）
        if (saveData.Flags != null)
            foreach (var kv in saveData.Flags)
                if (registry.Find(kv.Key) == null) gd.Flags[kv.Key] = kv.Value;
        if (saveData.IntFlags != null)
            foreach (var kv in saveData.IntFlags)
                if (registry.Find(kv.Key) == null) gd.IntFlags[kv.Key] = kv.Value;
        if (saveData.FloatFlags != null)
            foreach (var kv in saveData.FloatFlags)
                if (registry.Find(kv.Key) == null) gd.FloatFlags[kv.Key] = kv.Value;
        if (saveData.StringFlags != null)
            foreach (var kv in saveData.StringFlags)
                if (registry.Find(kv.Key) == null) gd.StringFlags[kv.Key] = kv.Value;
    }

    /// <summary>
    /// 新游戏：将 Save 作用域复位为注册表默认值（Global 不动；兼容模式不动，保持旧行为）。
    /// </summary>
    public void ResetSaveScope()
    {
        EnsureInit();
        if (registry == null) return;

        saveBool.Clear();
        saveInt.Clear();
        saveFloat.Clear();
        saveString.Clear();

        foreach (var def in registry.Definitions)
        {
            if (def == null || def.Scope != FlagScope.Save) continue;
            switch (def.Type)
            {
                case FlagType.Bool: saveBool[def.Name] = ParseBoolDefault(def); break;
                case FlagType.Int: saveInt[def.Name] = ParseIntDefault(def); break;
                case FlagType.Float: saveFloat[def.Name] = ParseFloatDefault(def); break;
                case FlagType.String: saveString[def.Name] = def.DefaultValue ?? ""; break;
            }
        }
    }

    // ==================== 内部工具 ====================

    private static bool ParseBoolDefault(FlagRegistry.FlagDefinition def)
    {
        bool b;
        return bool.TryParse(def.DefaultValue, out b) && b;
    }

    private static int ParseIntDefault(FlagRegistry.FlagDefinition def)
    {
        int i;
        return int.TryParse(def.DefaultValue, out i) ? i : 0;
    }

    private static float ParseFloatDefault(FlagRegistry.FlagDefinition def)
    {
        float f;
        return float.TryParse(def.DefaultValue, out f) ? f : 0f;
    }

    private void AddUnregistered<K, V>(Dictionary<K, V> source, Dictionary<K, V> target)
    {
        if (source == null || registry == null) return;
        foreach (var kv in source)
        {
            string keyName = kv.Key as string;
            if (keyName != null && registry.Find(keyName) == null) target[kv.Key] = kv.Value;
        }
    }

    private static void CopyInto<K, V>(Dictionary<K, V> source, Dictionary<K, V> target)
    {
        if (source == null) return;
        foreach (var kv in source) target[kv.Key] = kv.Value;
    }
}
