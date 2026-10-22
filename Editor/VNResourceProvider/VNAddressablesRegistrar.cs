using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

/// <summary>
/// Addressables 资产注册器（Phase 1 核心，见 Docs/VNResourceProviderRefactoring.md）。
///
/// 职责：把包内默认资源（Runtime/PackageDefault/VNovelizerRes，只注册 GUID、不复制文件）
/// 与用户工作区资源（Assets/VNovelizer）注册进 "VNovelizer" 组：
/// - 地址 = 资源键（"VNovelizerRes/..."，与运行时 VNResourceService 查询键一致）；
/// - Label = 类别（VNResourceKeys.CategoryToLabel，供运行时 LoadAll 检索）；
/// - 组内条目的地址/Label 由注册器托管（组内条目勿手动编辑：手动改的地址会被同步时覆盖）；
/// - 已被用户归入其他组的条目不动（尊重手动组织）。
///
/// 注意：包内资产的资产路径是虚拟路径（Packages/{包名}/...），不是文件系统路径，
/// 枚举文件须用包的真实路径（PackageInfo.resolvedPath），注册时再换算为资产路径。
///
/// 触发时机：初始化向导、菜单命令、Excel→CSV 转换后、
/// 工作区资产导入/移动时（VNWorkspaceAssetPostprocessor 自动登记）。
/// </summary>
public static class VNAddressablesRegistrar
{
    /// <summary>不参与运行时注册的扩展名（编辑器工作流文件/文档）</summary>
    private static readonly HashSet<string> ExcludedExtensions = new HashSet<string>
    {
        ".xlsx", ".md", ".txt", ".meta",
    };

    /// <summary>Addressables 设置资产是否存在（纯加载检查，不触发自动创建）</summary>
    public static bool HasAddressablesData()
    {
        // 注：kDefaultSettingsPath 为 internal，不可直接引用；
        // Settings 属性只做加载（仅 GetSettings(true) 会创建资产文件）
        return AddressableAssetSettingsDefaultObject.Settings != null;
    }

    /// <summary>包内默认资源的资产路径根（Packages 虚拟路径）；定位失败返回 null</summary>
    public static string GetPackageDefaultAssetRoot()
    {
        var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(VNAddressablesRegistrar).Assembly);
        if (packageInfo == null || string.IsNullOrEmpty(packageInfo.name)) return null;
        string assetRoot = $"Packages/{packageInfo.name}/Runtime/PackageDefault/{VNResourceKeys.RootPrefix}";
        return AssetDatabase.IsValidFolder(assetRoot) ? assetRoot : null;
    }

    /// <summary>包内默认资源的文件系统路径根（枚举文件用）；定位失败返回 null</summary>
    public static string GetPackageDefaultFsRoot()
    {
        var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(VNAddressablesRegistrar).Assembly);
        if (packageInfo == null || string.IsNullOrEmpty(packageInfo.resolvedPath)) return null;
        string fsRoot = Path.Combine(packageInfo.resolvedPath, "Runtime/PackageDefault/VNovelizerRes").Replace('\\', '/');
        return Directory.Exists(fsRoot) ? fsRoot : null;
    }

    // 注：原"资源管理(Addressables)/同步全部资源注册"菜单已移除——初始化向导
    // 内部已调用 SyncAll 完成注册；如需手动重新同步，使用资源管理器窗口或
    // 菜单"VNovelizer → 资源管理器"中的同步入口。

    /// <summary>
    /// 同步全部（用户内容 + 默认资源）。不存在 Addressables 设置时自动创建。返回处理的条目数。
    ///
    /// 默认资源注册源（二选一，避免同地址重复条目）：
    /// - 存量项目（存在旧版 Assets/Resources/VNovelizerRes）→ 注册用户副本
    ///   （副本可能被用户改过，且与 Resources 兜底所见一致）；
    /// - 新项目 → 注册包内 Runtime/PackageDefault/VNovelizerRes（不复制文件）。
    /// 工作区（Assets/VNovelizer）始终纳入扫描。
    /// </summary>
    public static int SyncAll()
    {
        if (Application.isPlaying) return 0; // Play 模式中绝不执行（SaveAssets/Reset 会干扰运行时）
        var settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
        if (settings == null)
        {
            Debug.LogError("[VNAddressablesRegistrar] 无法初始化 Addressables 设置（Assets/AddressableAssetsData）");
            return 0;
        }

        var group = EnsureGroup(settings);
        if (group == null) return 0;

        int count = 0;
        if (Directory.Exists(VNProjectPaths.LegacyRoot))
        {
            // 存量项目：注册旧目录副本（不注册包内原件，避免同地址重复）
            count += RegisterFolderTree(group, VNProjectPaths.LegacyRoot, VNProjectPaths.LegacyRoot);
        }
        else
        {
            // 新项目：注册包内默认资源（文件本体留在包里）
            count += RegisterFolderTree(group, GetPackageDefaultFsRoot(), GetPackageDefaultAssetRoot());
        }
        count += RegisterWorkspaceTree(group);

        AssetDatabase.SaveAssets();

        // 重建运行时提供者链，使编辑器可用性检查（组存在性）重新评估
        VNResourceService.Reset();

        Debug.Log($"[VNAddressablesRegistrar] 同步完成：{count} 个资产已注册进 {VNResourceKeys.GroupName} 组");
        return count;
    }

    /// <summary>
    /// 仅同步用户工作区（轻量：转换 CSV / 编辑器窗口新增资产后调用）。
    /// 未初始化 Addressables 的项目为空操作。
    /// </summary>
    public static void SyncWorkspace()
    {
        if (Application.isPlaying) return; // Play 模式中绝不执行（SaveAssets/Reset 会干扰运行时）
        if (!HasAddressablesData()) return; // 未初始化的项目（如旧版兼容模式）不做任何事
        var settings = AddressableAssetSettingsDefaultObject.GetSettings(false);
        if (settings == null) return;
        var group = EnsureGroup(settings);
        if (group == null) return;
        int count = RegisterWorkspaceTree(group);

        AssetDatabase.SaveAssets();

        // 重建运行时提供者链，使编辑器可用性检查（组存在性）重新评估
        VNResourceService.Reset();

        // 无论是否有新增，都输出同步结果（便于确认同步执行过）
        Debug.Log($"[VNAddressablesRegistrar] 工作区同步完成：{count} 个新增/更新条目" +
                  (count == 0 ? "（全部已是最新，无变更）" : ""));
    }

    /// <summary>
    /// 注册单个资产（编辑器窗口创建新资产时调用）。
    /// resourceKey = 该资产运行时被查询的资源键（如 "VNovelizerRes/GalleryContent/CG/CGDataContainer"）。
    /// 未初始化 Addressables 的项目为空操作。
    /// </summary>
    public static void RegisterAssetAtPath(string assetPath, string resourceKey)
    {
        if (Application.isPlaying) return; // Play 模式中绝不执行
        if (!HasAddressablesData()) return;
        if (string.IsNullOrEmpty(assetPath) || string.IsNullOrEmpty(resourceKey)) return;
        if (!File.Exists(assetPath)) return;

        var settings = AddressableAssetSettingsDefaultObject.GetSettings(false);
        if (settings == null) return;
        var group = EnsureGroup(settings);
        if (group == null) return;

        if (RegisterAsset(settings, group, assetPath, resourceKey))
        {
            AssetDatabase.SaveAssets();
            VNResourceService.Reset();
        }
    }

    /// <summary>
    /// 自动登记（资产导入/移动钩子用，见 VNWorkspaceAssetPostprocessor）：
    /// 只登记"从未进过任何组"的资产——已在 VNovelizer 组（地址已由注册器/拖放分配托管）
    /// 或被用户手动归入其他组的资产一律不动；地址已被其他资产占用时警告并跳过
    /// （防固定键如 VNFlagRegistry 被重复资产静默劫持）。
    /// 返回 true 表示本次完成了新登记。
    /// </summary>
    public static bool TryAutoRegister(string assetPath, string resourceKey)
    {
        if (Application.isPlaying) return false; // Play 模式中绝不执行
        if (!HasAddressablesData()) return false;
        if (string.IsNullOrEmpty(assetPath) || string.IsNullOrEmpty(resourceKey)) return false;

        var settings = AddressableAssetSettingsDefaultObject.GetSettings(false);
        if (settings == null) return false;

        string guid = AssetDatabase.AssetPathToGUID(assetPath);
        if (string.IsNullOrEmpty(guid)) return false;

        // 已在任何组中（含本组）：地址已被托管，保持现状（移动/改名不影响寻址）
        if (settings.FindAssetEntry(guid) != null) return false;

        // 地址冲突：同地址已被其他资产占用（如存在多份 FlagRegistry）
        foreach (var g in settings.groups)
        {
            if (g == null) continue;
            foreach (var e in g.entries)
            {
                if (e != null && e.address == resourceKey)
                {
                    Debug.LogWarning($"[VNAddressablesRegistrar] 自动登记跳过：地址 \"{resourceKey}\" 已被 {e.AssetPath} 占用，" +
                                     $"{assetPath} 未登记。如需更换占用者，请先移除旧条目。");
                    return false;
                }
            }
        }

        var group = EnsureGroup(settings);
        if (group == null) return false;

        if (RegisterAsset(settings, group, assetPath, resourceKey))
        {
            AssetDatabase.SaveAssets();
            VNResourceService.Reset();
            Debug.Log($"[VNAddressablesRegistrar] 自动登记: {assetPath} → \"{resourceKey}\"");
            return true;
        }
        return false;
    }

    /// <summary>确保 VNovelizer 组存在（含打包 Schema；BundleMode = Pack Separately，释放即卸载，内存最优）</summary>
    private static AddressableAssetGroup EnsureGroup(AddressableAssetSettings settings)
    {
        var group = settings.FindGroup(VNResourceKeys.GroupName);
        if (group != null) return group;

        group = settings.CreateGroup(VNResourceKeys.GroupName, false, false, false, null);
        if (group == null)
        {
            Debug.LogError($"[VNAddressablesRegistrar] 创建组失败: {VNResourceKeys.GroupName}");
            return null;
        }

        group.AddSchema<BundledAssetGroupSchema>();
        group.AddSchema<ContentUpdateGroupSchema>();

        // Pack Separately：每个资产独立成包、释放即卸载（内存行为最接近旧 Resources）。
        // 资产很多导致构建变慢时，可在 Addressables Groups 窗口改回 Pack Together / Pack Together By Label。
        var bundleSchema = group.GetSchema<BundledAssetGroupSchema>();
        if (bundleSchema != null)
            bundleSchema.BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackSeparately;

        Debug.Log($"[VNAddressablesRegistrar] 已创建 Addressables 组: {VNResourceKeys.GroupName}（Pack Separately）");
        return group;
    }

    /// <summary>
    /// 注册用户工作区资产（**类别锚定地址**，核心设计，见 Docs/VNResourceProviderRefactoring.md）：
    ///
    /// 地址 = 匹配的类别前缀（Config）+ 剩余相对路径，而**非物理路径推导**——
    /// 保证"运行时查询地址（Config 前缀 + 资源名）与注册地址"必然一致，
    /// 工作区文件夹名的单复数/大小写差异不再影响寻址（宽松匹配）。
    ///
    /// 例：Assets/VNovelizer/VNScript/NewChapter.csv（用户文件夹为单数）
    ///   → 注册地址 = VNovelizerRes/VNScripts/NewChapter（按 Config 的复数类别锚定）
    ///   → 运行时 ScriptParser 查询 VNovelizerRes/VNScripts/NewChapter 直接命中。
    /// 未匹配到任何类别的文件按物理路径推导注册（兜底保持可寻址）。
    /// </summary>
    private static int RegisterWorkspaceTree(AddressableAssetGroup group)
    {
        if (group == null) return 0;
        if (!AssetDatabase.IsValidFolder(VNProjectPaths.WorkspaceRoot)) return 0;

        var settings = group.Settings;
        var categories = GetKnownCategories();
        string[] files = Directory.GetFiles(VNProjectPaths.WorkspaceRoot, "*.*", SearchOption.AllDirectories);
        int count = 0;

        foreach (string file in files)
        {
            string ext = Path.GetExtension(file).ToLowerInvariant();
            if (ExcludedExtensions.Contains(ext)) continue;

            string assetPath = file.Replace('\\', '/');
            string relative = assetPath.Substring(VNProjectPaths.WorkspaceRoot.Length).TrimStart('/');
            string resourceKey = BuildCategoryAnchoredKey(relative, categories, out bool anchored);

            // anchored = false：物理上不在任何类别文件夹内（如 Assets/VNovelizer/Sounds/x.mp3）。
            // 该类资产的地址可能是用户拖放分配的类别逻辑名（如 Voice/Chapter01/x）——
            // 同步时保留手动分配的地址，只用物理推导兜底注册"从未分配过"的资产。
            if (RegisterAsset(settings, group, assetPath, resourceKey, preserveManualAddress: !anchored)) count++;
        }
        return count;
    }

    /// <summary>已知类别全集（Config 前缀优先，缺省回退默认值）</summary>
    private static List<string> GetKnownCategories()
    {
        VNProjectConfig.TryGetInstance(out var config);
        var list = new List<string>();
        void Add(string path) { if (!string.IsNullOrEmpty(path) && !list.Contains(path)) list.Add(path); }

        Add(config != null ? config.VNScriptResPath : "VNovelizerRes/VNScripts");
        Add(config != null ? config.BackgroundResPath : "VNovelizerRes/Backgrounds");
        Add(config != null ? config.CharacterResPath : "VNovelizerRes/Characters");
        Add(config != null ? config.ParticalEffectPath : "VNovelizerRes/VFX/Partical");
        Add(config != null ? config.AnimationPath : "VNovelizerRes/VFX/Animation");
        Add(config != null ? config.FilterResPath : "VNovelizerRes/VFX/Filters");
        Add(config != null ? config.BgmResPath : "VNovelizerRes/Audio/Music/BGM");
        Add(config != null ? config.SFXResPath : "VNovelizerRes/Audio/SFX");
        Add(config != null ? config.VoiceResPath : "VNovelizerRes/Audio/Voice");
        Add("VNovelizerRes/ExcelVNScripts");
        Add("VNovelizerRes/GalleryContent/CG");
        Add("VNovelizerRes/GalleryContent/Music");
        Add("VNovelizerRes/GalleryContent/Scene");
        return list;
    }

    /// <summary>路径段归一化：小写 + 去尾部复数 s（宽松匹配用）</summary>
    private static string NormalizeSegment(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        s = s.ToLowerInvariant();
        if (s.Length > 1 && s.EndsWith("s")) s = s.Substring(0, s.Length - 1);
        return s;
    }

    /// <summary>资源键是否命中已知类别（作为整体或以 类别+"/" 为前缀；大小写不敏感）</summary>
    private static bool IsKnownCategory(string category)
    {
        if (string.IsNullOrEmpty(category)) return false;
        foreach (var cat in GetKnownCategories())
        {
            if (string.Equals(cat, category, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>
    /// 资源键 → 所属类别。【子文件夹支持】按已知类别最长前缀匹配，
    /// 而非旧的"去掉最后一段"（旧规则会把 Voice/Chapter01/Voice01 派生成
    /// …_Voice_Chapter01 子标签，导致 GetCategoryEntries 按 Voice 类标签精确过滤时查不到）。
    /// 未命中任何已知类别时回退物理推导（去最后一段），保持非类别资产的原有行为。
    /// </summary>
    private static string ResolveCategoryForKey(string resourceKey)
    {
        if (string.IsNullOrEmpty(resourceKey)) return resourceKey;
        string best = null;
        foreach (var cat in GetKnownCategories())
        {
            if (string.IsNullOrEmpty(cat)) continue;
            if (string.Equals(resourceKey, cat, StringComparison.OrdinalIgnoreCase) ||
                resourceKey.StartsWith(cat + "/", StringComparison.OrdinalIgnoreCase))
            {
                if (best == null || cat.Length > best.Length) best = cat;
            }
        }
        return best ?? VNResourceKeys.KeyToCategory(resourceKey);
    }

    /// <summary>
    /// 逻辑名校验（允许子文件夹路径，如 "Chapter01/Voice01"）。返回 null = 通过。
    /// 供 RenameAssignment / AssignToCategory / 文件夹操作共用。
    /// </summary>
    public static string ValidateLogicalName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "名称不能为空";
        name = name.Replace('\\', '/');
        if (name.Contains("//")) return "路径不能包含空层级（//）";
        if (name.Contains("..")) return "路径不能包含 ..";
        foreach (var seg in name.Split('/'))
        {
            if (string.IsNullOrEmpty(seg)) return "路径不能包含空层级";
            foreach (char c in Path.GetInvalidFileNameChars())
                if (seg.IndexOf(c) >= 0) return $"层级名称包含非法字符「{c}」: {seg}";
        }
        return null;
    }

    /// <summary>
    /// 工作区相对路径 → 类别锚定资源键。逐段宽松匹配（单复数/大小写不敏感）已知类别，
    /// 命中则地址 = 类别原样路径 + 剩余目录段 + 文件名（去扩展名）；未命中按物理路径推导兜底。
    /// anchored = 是否物理上位于某个已知类别文件夹内（子文件夹路径由此自然进入地址）。
    /// </summary>
    private static string BuildCategoryAnchoredKey(string relativePath, List<string> categories, out bool anchored)
    {
        string[] segments = relativePath.Split('/');
        string fileName = segments[segments.Length - 1];
        string ext = Path.GetExtension(fileName);
        string fileNameNoExt = fileName.Substring(0, fileName.Length - ext.Length);

        string[] dirSegments = new string[segments.Length - 1];
        System.Array.Copy(segments, dirSegments, dirSegments.Length);

        // 从最长目录前缀开始尝试匹配（嵌套类别如 Audio/Music/BGM 优先于短前缀）
        for (int take = dirSegments.Length; take >= 1; take--)
        {
            var rest = new string[dirSegments.Length - take];
            System.Array.Copy(dirSegments, take, rest, 0, rest.Length);

            foreach (var cat in categories)
            {
                // 类别去掉根前缀后的目录段
                string catRelative = cat.StartsWith(VNResourceKeys.RootPrefix + "/", StringComparison.Ordinal)
                    ? cat.Substring(VNResourceKeys.RootPrefix.Length + 1)
                    : cat;
                string[] catSegs = catRelative.Split('/');
                if (catSegs.Length != take) continue;

                bool match = true;
                for (int i = 0; i < take; i++)
                {
                    if (NormalizeSegment(catSegs[i]) != NormalizeSegment(dirSegments[i])) { match = false; break; }
                }
                if (!match) continue;

                // 命中：地址 = 类别原样路径 + 剩余目录段（保留用户命名，子文件夹路径由此进入地址）+ 文件名
                anchored = true;
                var parts = new List<string> { cat };
                parts.AddRange(rest);
                parts.Add(fileNameNoExt);
                return string.Join("/", parts);
            }
        }

        // 未命中任何类别：物理路径推导（RootPrefix + 相对路径）——保持可寻址兜底
        anchored = false;
        return $"{VNResourceKeys.RootPrefix}/{relativePath.Substring(0, relativePath.Length - ext.Length)}";
    }


    private static int RegisterFolderTree(AddressableAssetGroup group, string fsRoot, string assetRoot)
    {
        if (group == null || string.IsNullOrEmpty(fsRoot) || !Directory.Exists(fsRoot)) return 0;

        var settings = group.Settings;
        string[] files = Directory.GetFiles(fsRoot, "*.*", SearchOption.AllDirectories);
        int count = 0;

        foreach (string file in files)
        {
            string ext = Path.GetExtension(file).ToLowerInvariant();
            if (ExcludedExtensions.Contains(ext)) continue;

            string unified = file.Replace('\\', '/');
            string relative = unified.Substring(fsRoot.Length).TrimStart('/');
            string assetPath = assetRoot + "/" + relative;
            string resourceKey = $"{VNResourceKeys.RootPrefix}/{relative.Substring(0, relative.Length - ext.Length)}";

            if (RegisterAsset(settings, group, assetPath, resourceKey)) count++;
        }
        return count;
    }

    /// <summary>
    /// 注册单个资产（设置地址与类别 Label）。已被用户归入其他组的条目跳过。
    /// preserveManualAddress：物理位置不在类别文件夹内的资产，若已有手动分配的类别地址
    /// （拖放分配 / 重命名产生），同步时不覆盖该地址（物理移动语义与手动分配语义解耦）。
    /// </summary>
    private static bool RegisterAsset(AddressableAssetSettings settings, AddressableAssetGroup group,
        string assetPath, string resourceKey, bool preserveManualAddress = false)
    {
        string guid = AssetDatabase.AssetPathToGUID(assetPath);
        if (string.IsNullOrEmpty(guid))
        {
            // 【修复时序问题】AssetDatabase.Refresh() 是异步的，新文件可能尚未完成导入（GUID 未就绪）。
            // 主动调用 ImportAsset 强制同步导入，再重试一次获取 GUID，消除"转换→注册"之间的竞争窗口。
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            guid = AssetDatabase.AssetPathToGUID(assetPath);
        }
        if (string.IsNullOrEmpty(guid))
        {
            Debug.LogWarning($"[VNAddressablesRegistrar] 资产导入后 GUID 仍不存在，跳过: {assetPath}");
            return false;
        }

        // 尊重用户手动组织：已在其他组中的条目不动
        var existing = settings.FindAssetEntry(guid);
        if (existing != null && existing.parentGroup != null && existing.parentGroup != group)
            return false;

        var entry = settings.CreateOrMoveEntry(guid, group);
        if (entry == null) return false;

        // 手动分配地址保护：当前地址是已知类别的逻辑名（非物理推导兜底键）时保留不动
        bool keepManual = preserveManualAddress
            && entry.address != null
            && !string.Equals(entry.address, resourceKey, StringComparison.Ordinal)
            && IsKnownCategory(ResolveCategoryForKey(entry.address));

        string effectiveKey = resourceKey;
        if (keepManual)
        {
            effectiveKey = entry.address;
        }
        else
        {
            entry.SetAddress(resourceKey);
        }

        // 【子文件夹支持】Label 按已知类别最长前缀派生：Voice/Chapter01/Voice01 归属 Voice 类，
        // 而非派生出 …_Voice_Chapter01 子标签（旧规则会让类别枚举查不到子目录条目）
        string category = ResolveCategoryForKey(effectiveKey);
        string label = VNResourceKeys.CategoryToLabel(category);
        if (!string.IsNullOrEmpty(label))
            entry.SetLabel(label, true);
        // 清掉该资产残留的旧类别标签（地址曾随配置/目录变迁漂移过时，旧 Label 会永久残留；
        // 也顺带清理旧派生规则产生的 …_Chapter01 形态子标签）
        CleanupStaleLabels(entry, category);

        return true;
    }

    /// <summary>
    /// 重打全组条目的类别 Label（只改 Label，不动地址）。
    /// 子目录 Label 派生规则升级后，存量条目带着旧派生标签（…_Chapter01）在类别枚举中查不到——
    /// 编辑器会话内首次需要时调用一次即可自愈（幂等）。
    /// </summary>
    public static int RelabelAllCategories()
    {
        if (Application.isPlaying) return 0;
        if (!IsManagedMode) return 0;
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        var group = settings.FindGroup(VNResourceKeys.GroupName);
        if (group == null) return 0;

        int changed = 0;
        foreach (var entry in group.entries)
        {
            if (entry == null || string.IsNullOrEmpty(entry.address)) continue;
            string category = ResolveCategoryForKey(entry.address);
            string label = VNResourceKeys.CategoryToLabel(category);
            if (string.IsNullOrEmpty(label)) continue;

            var before = new HashSet<string>(entry.labels);
            if (!entry.labels.Contains(label))
            {
                entry.SetLabel(label, true);
                changed++;
            }
            CleanupStaleLabels(entry, category);
            if (!before.SetEquals(entry.labels)) changed++;
        }

        if (changed > 0)
        {
            AssetDatabase.SaveAssets();
            VNResourceService.Reset();
            Debug.Log($"[VNAddressablesRegistrar] 类别标签自愈完成：{changed} 处已按新派生规则修正");
        }
        return changed;
    }

    // ==================== 拖放分配（资源管理器工作流，见 Docs/VNResourceProviderRefactoring.md） ====================

    /// <summary>
    /// 是否处于 Addressables 托管模式（设置资产与 VNovelizer 组均存在）。
    /// 托管模式下资源管理器的数据源是组内条目，而非文件夹扫描。
    /// </summary>
    public static bool IsManagedMode
    {
        get
        {
            if (!HasAddressablesData()) return false;
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            return settings != null && settings.FindGroup(VNResourceKeys.GroupName) != null;
        }
    }

    /// <summary>资源管理器分页 → 类别资源键（Excel 索引名所挂靠的逻辑前缀）。视频为 StreamingAssets 特例，返回 null。</summary>
    public static string GetCategoryKey(ResType type)
    {
        var config = VNProjectConfig.Instance;
        if (config == null) return null;
        switch (type)
        {
            case ResType.Background: return config.BackgroundResPath;
            case ResType.BGM: return config.BgmResPath;
            case ResType.SFX: return config.SFXResPath;
            case ResType.Voice: return config.VoiceResPath;
            default: return null; // Video：StreamingAssets 文件，不经 Addressables
        }
    }

    /// <summary>枚举类别下全部已分配条目（组内按类别 Label 过滤）</summary>
    public static IEnumerable<AddressableAssetEntry> GetCategoryEntries(string category)
    {
        if (string.IsNullOrEmpty(category) || !IsManagedMode) yield break;
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        var group = settings.FindGroup(VNResourceKeys.GroupName);
        string label = VNResourceKeys.CategoryToLabel(category);
        foreach (var entry in group.entries)
        {
            if (entry != null && entry.labels.Contains(label))
                yield return entry;
        }
    }

    /// <summary>
    /// 拖放分配：把项目内任意位置的资产纳入类别（地址 = 类别/逻辑名，物理位置从此无关）。
    /// 含类型校验、图片导入设置自动修正、同名冲突检测。
    /// 返回 null 表示成功，否则为用户可读的错误信息。
    /// </summary>
    public static string AssignToCategory(ResType type, string assetPath, string logicalName = null)
    {
        string category = GetCategoryKey(type);
        if (category == null)
            return "视频资源不走 Addressables，请直接拖入窗口（将复制到 StreamingAssets）";

        // 类型校验（按类别）
        string typeError = ValidateAssetType(type, assetPath);
        if (typeError != null) return typeError;

        // 图片导入设置自动修正：LoadAsync<Sprite> 需要 Sprite 类型，替用户省掉头号坑
        if (type == ResType.Background) EnsureSpriteImport(assetPath);

        // 逻辑名（默认文件名去扩展名）。允许子文件夹路径（"Chapter01/Voice01"），反斜杠统一为 '/'
        if (string.IsNullOrEmpty(logicalName))
            logicalName = Path.GetFileNameWithoutExtension(assetPath);
        else
        {
            logicalName = logicalName.Trim().Trim('\\').Replace('\\', '/').Trim('/');
            string invalidError = ValidateLogicalName(logicalName);
            if (invalidError != null) return invalidError;
        }

        var settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
        var group = EnsureGroup(settings);
        if (group == null) return "无法创建 Addressables 组";

        // 同名冲突：同类别下已有相同逻辑名的条目
        string address = $"{category}/{logicalName}";
        foreach (var entry in GetCategoryEntries(category))
        {
            if (entry.address == address && !string.Equals(entry.AssetPath, assetPath, StringComparison.OrdinalIgnoreCase))
                return $"逻辑名 \"{logicalName}\" 已被同类别其他资源占用，请先重命名";
        }

        string guid = AssetDatabase.AssetPathToGUID(assetPath);
        if (string.IsNullOrEmpty(guid)) return "资产尚未导入（GUID 不存在）";

        // 显式分配是用户意图：已在其他组也移入本组
        var target = settings.CreateOrMoveEntry(guid, group);
        if (target == null) return "注册条目失败";

        target.SetAddress(address);
        target.SetLabel(VNResourceKeys.CategoryToLabel(category), true);
        // 清掉可能残留的旧类别标签（资产从别的类别重新分配时）
        CleanupStaleLabels(target, category);

        AssetDatabase.SaveAssets();
        VNResourceService.Reset();
        return null;
    }

    /// <summary>移除分配：从 VNovelizer 组移除条目（文件原地保留，只是不再被剧本索引）</summary>
    public static bool UnassignAsset(string assetPath)
    {
        if (!IsManagedMode) return false;
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        string guid = AssetDatabase.AssetPathToGUID(assetPath);
        var entry = settings.FindAssetEntry(guid);
        if (entry == null || entry.parentGroup == null || entry.parentGroup.Name != VNResourceKeys.GroupName)
            return false;

        settings.RemoveAssetEntry(guid);
        AssetDatabase.SaveAssets();
        VNResourceService.Reset();
        return true;
    }

    /// <summary>重命名逻辑名（改地址尾段，不动文件名；支持子文件夹路径如 "Chapter01/Voice01"）。返回 null 表示成功，否则为错误信息。</summary>
    public static string RenameAssignment(string assetPath, string newLogicalName)
    {
        if (string.IsNullOrEmpty(newLogicalName)) return "名称不能为空";
        // 【子文件夹支持】允许斜杠路径；反斜杠统一为 '/'，去首尾分隔符
        newLogicalName = newLogicalName.Trim().Trim('\\').Replace('\\', '/').Trim('/');
        string invalidError = ValidateLogicalName(newLogicalName);
        if (invalidError != null) return invalidError;

        if (!IsManagedMode) return "当前不是 Addressables 托管模式";
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        string guid = AssetDatabase.AssetPathToGUID(assetPath);
        var entry = settings.FindAssetEntry(guid);
        if (entry == null || entry.address == null || !entry.address.Contains("/"))
            return "该资源未被分配逻辑名";

        // 【子文件夹支持】类别按已知类别最长前缀解析：Voice/Chapter01/Voice01 的类别是 Voice，
        // 不能再用"去最后一段"（会把 Chapter01 当类别，导致改名结果错挂到 Chapter01/ 之下）
        string category = ResolveCategoryForKey(entry.address);
        if (string.IsNullOrEmpty(category) || !entry.address.StartsWith(category + "/", StringComparison.Ordinal))
            category = entry.address.Substring(0, entry.address.LastIndexOf('/'));
        string newAddress = $"{category}/{newLogicalName}";

        // 同类别重名检测
        foreach (var other in GetCategoryEntries(category))
        {
            if (other.address == newAddress && !string.Equals(other.AssetPath, assetPath, StringComparison.OrdinalIgnoreCase))
                return $"逻辑名 \"{newLogicalName}\" 已被同类别其他资源占用";
        }

        entry.SetAddress(newAddress);
        // 类别未变时 Label 不变；防御性重打一次（幂等）
        CleanupStaleLabels(entry, category);
        AssetDatabase.SaveAssets();
        VNResourceService.Reset();
        return null;
    }

    /// <summary>类别类型校验：返回 null = 通过</summary>
    private static string ValidateAssetType(ResType type, string assetPath)
    {
        var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
        if (asset == null) return "无法加载资产（可能是文件夹或尚未导入）";

        switch (type)
        {
            case ResType.Background:
                if (!(asset is Texture2D) && !(asset is Sprite))
                    return $"背景需要图片资产（png/jpg 等），当前类型: {asset.GetType().Name}";
                break;
            case ResType.BGM:
            case ResType.SFX:
            case ResType.Voice:
                if (!(asset is AudioClip))
                    return $"音频类别需要 AudioClip，当前类型: {asset.GetType().Name}";
                break;
        }
        return null;
    }

    /// <summary>图片导入设置自动修正为 Sprite（仅背景类别；幂等）</summary>
    private static void EnsureSpriteImport(string assetPath)
    {
        var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer == null || importer.textureType == TextureImporterType.Sprite) return;
        importer.textureType = TextureImporterType.Sprite;
        importer.SaveAndReimport();
    }

    /// <summary>清理资产残留的旧类别标签（跨类别重新分配时）</summary>
    private static void CleanupStaleLabels(AddressableAssetEntry entry, string currentCategory)
    {
        string currentLabel = VNResourceKeys.CategoryToLabel(currentCategory);
        // 所有 VNovelizerRes_* 形态的标签中，不属于当前类别的移除
        var stale = new List<string>();
        foreach (var label in entry.labels)
        {
            if (label.StartsWith(VNResourceKeys.RootPrefix + "_", StringComparison.Ordinal) && label != currentLabel)
                stale.Add(label);
        }
        foreach (var label in stale)
            entry.SetLabel(label, false);
    }
}
