# VN 资源管理重构：Provider 抽象层 + Addressables

> 本文档描述 VNovelizer 资源加载体系从"纯 Unity Resources + 初始化复制"向"可插拔提供者链（Addressables 优先 / Resources 兜底）"的迁移方案与实施记录。
> 参考：Naninovel 资源提供者（Resource Providers）与内存管理（Memory Management）架构。
> 阶段状态：**Phase 0（收口）与 Phase 1（Addressables 后端）已实施**；Phase 2/3 为规划。

---

## 目录

1. [背景与动机](#1-背景与动机)
2. [目标架构](#2-目标架构)
3. [统一键约定](#3-统一键约定)
4. [Phase 0：调用收口（已实施）](#4-phase-0调用收口已实施)
5. [Phase 1：Addressables 后端（已实施）](#5-phase-1addressables-后端已实施)
6. [初始化向导新流程](#6-初始化向导新流程)
7. [用户工作流](#7-用户工作流)
8. [已知限制与注意事项](#8-已知限制与注意事项)
9. [Phase 2 / Phase 3 规划](#9-phase-2--phase-3-规划)

---

## 1. 背景与动机

### 1.1 旧方案的问题

| 问题 | 说明 |
|------|------|
| **污染用户 Assets** | 初始化向导把包内 `Runtime/PackageDefault/VNovelizerRes` 下 9 个文件夹复制到 `Assets/Resources/VNovelizerRes`，用户项目里出现大量本不需要关心的插件内部资产；包升级后旧副本与新包不一致 |
| **Resources 本身的缺陷** | Unity 官方不推荐：全量打进包体（无法按需分发）、无引用计数（加载后常驻）、索引构建拖慢启动 |
| **无卸载语义** | 全项目没有任何 Unload 调用，内存管理完全依赖场景切换 |
| **无热更/远程分发能力** | Resources 资源全部内置，无法做 DLC、MOD、远程更新 |
| **散落的加载调用** | `Resources.Load` 直接散落在 12+ 个文件中，绕过 `ResourcesManager`，无法统一替换后端 |

### 1.2 参考方案（Naninovel）

Naninovel 采用**可插拔资源提供者**架构：

- 多 Provider（Addressable / Project(Resources) / Local / 自定义 `IResourceProvider`）；
- 每个资源类别可单独配置 Provider 有序回退链（如 Addressable → Project 兜底）；
- 引用计数 + Hold/Release 生命周期管理，Resource Policy 三策略（保守/乐观/懒惰）；
- 编辑器端自动把资产注册进 Addressables 组（组内条目自动重建，勿手动编辑）。

本方案取其骨架、按 VNovelizer 体量裁剪：**一条全局链（Addressables → Resources）+ 编辑器注册器 + 后续阶段的引用计数**。

---

## 2. 目标架构

```
CSV/Excel 剧本（不变，仍写资源名 "Beach" / "BGM01"）
        │
        ▼
VNResourceService（静态门面，全项目唯一加载入口）
        │
        ├── IVNResourceProvider 提供者链（按序回退）
        │     ├── AddressablesProvider   ← 链首（VN_ADDRESSABLES 时启用）
        │     │     地址 = 资源键；LoadAll 按类别 Label；句柄缓存 + Release
        │     └── ResourcesProvider      ← 永远兜底（行为与旧版完全一致）
        │
        └── 旧入口 ResourcesManager（保留对外 API，内部已委托 VNResourceService）
              GameObject 自动实例化契约不变、进度跟踪契约不变

编辑器端
        ├── VNAddressablesRegistrar     ← 注册器：包内默认资源 + 用户工作区 → "VNovelizer" 组
        ├── VNProjectPaths              ← 用户内容目录解析（工作区 / 旧版目录双模式）
        ├── VNEditorResourceResolver    ← 编辑器窗口按资源键定位资产（与运行时同键空间）
        └── VNWorkspaceAssetPostprocessor ← 工作区资产导入/移动时自动登记（延迟执行）
```

### 文件清单

| 文件 | 职责 |
|------|------|
| `Runtime/.../Core/Resources/VNResourceKeys.cs` | 键约定共享常量：组名、根前缀、类别↔Label 映射 |
| `Runtime/.../Core/Resources/VNLoadOperation.cs` | 异步加载操作句柄（惰性进度、一次性完成事件） |
| `Runtime/.../Core/Resources/IVNResourceProvider.cs` | 提供者接口契约 |
| `Runtime/.../Core/Resources/ResourcesProvider.cs` | Resources 兜底后端（零行为变化） |
| `Runtime/.../Core/Resources/AddressablesProvider.cs` | Addressables 后端（`#if VN_ADDRESSABLES`） |
| `Runtime/.../Core/Resources/VNResourceService.cs` | 门面 + 回退链调度 |
| `Editor/VNResourceProvider/VNAddressablesRegistrar.cs` | Addressables 资产注册器 + 菜单 |
| `Editor/VNResourceProvider/VNProjectPaths.cs` | 工作区/旧版目录路径解析 |
| `Editor/VNResourceProvider/VNEditorResourceResolver.cs` | 编辑器按键定位资产 |
| `Editor/VNResourceProvider/VNWorkspaceAssetPostprocessor.cs` | 工作区自动登记 |
| `Editor/VNResourceProvider/VNResourceEditorProbe.cs` | 编辑器可用性探针（经委托注入运行时提供者——运行时程序集不能引用 Unity.Addressables.Editor） |

依赖声明：`package.json` 硬依赖 `com.unity.addressables: 1.21.19`（UPM 自动安装，用户无需操作）；两个 asmdef 均已引用 `Unity.Addressables` / `Unity.Addressables.Editor` 并声明 `VN_ADDRESSABLES` versionDefine（与 `VN_LOCALIZATION` 模式一致）。

---

## 3. 统一键约定

**资源键 = 旧 Resources 相对路径 = Addressables 地址**，例：`VNovelizerRes/Backgrounds/Beach`（无扩展名）。

- `ResourcesProvider` 按 `Assets/Resources/{键}` 查找 → 存量项目零迁移；
- `AddressablesProvider` 按同名地址查找 → 注册器写入；
- 批量加载（`LoadAll`）按**类别 Label** 检索：`VNResourceKeys.CategoryToLabel("VNovelizerRes/Characters")` → `"VNovelizerRes_Characters"`；
- `VNProjectConfig` 的全部路径前缀字段**语义不变**——它们现在同时是 Resources 路径与 Addressables 地址前缀。

```
新项目（Addressables 模式）           存量项目（兼容模式）
Assets/VNovelizer/Backgrounds/x.png   Assets/Resources/VNovelizerRes/Backgrounds/x.png
        │ 注册器赋地址                        │
        ▼                                    ▼
地址 "VNovelizerRes/Backgrounds/x"    键 "VNovelizerRes/Backgrounds/x"
        │                                    │
   AddressablesProvider 命中            ResourcesProvider 兜底命中
```

两条腿可共存（混合状态也能工作）。

---

## 4. Phase 0：调用收口（已实施）

原则：**行为零变化**——`ResourcesProvider` 与直接调用 `Resources` 完全等价，只是从此所有加载经过统一链。

### 4.1 收口点

| 调用点 | 改造 |
|--------|------|
| `ResourcesManager` | 同步 `Load` / 两个 `LoadAsync` 重载内部改为委托 `VNResourceService`；GameObject 实例化与进度跟踪契约保持 |
| `ScriptParser.Parse` | CSV 加载 → `VNResourceService.Load<TextAsset>` |
| `CharacterResManager.LoadAllCharacterProfiles` | `Resources.LoadAll` → `VNResourceService.LoadAll`（IList） |
| `FlagService.EnsureInit` | FlagRegistry → `VNResourceService.Load` |
| `TheaterManager.LoadBackgroundSprite` | 背景异步加载（协程内轮询 `VNLoadOperation`） |
| `TheaterManager.ResolveAppearance` | 读档重建同步加载 |
| `ProjectConfig.Instance` | **保留** `Resources.Load`——引导配置是全项目唯一 Resources 资产（鸡生蛋问题，Phase 2 既定决策） |

无需改动的间接受益者（经 `ResourcesManager` 自动走链）：`UIManager`（面板预制体）、`PoolManager`（SoundObj/VFX 预制体）、`MusicManager`（BGM/SFX）、`VoiceManager`（语音）、画廊三页（数据容器）、`VNAPI.PlayVideo`（VideoObj）。

### 4.2 VNLoadOperation 设计要点

- `Progress` **惰性求值**（`Func<float>` 进度源直连底层 `ResourceRequest.progress` / `handle.PercentComplete`），无需逐帧推送协程；
- `Completed` 事件订阅时已完成则立即回调（一次性语义）；
- 服务层跨提供者回退时按链长加权进度（`ResourcesManager` 的进度条行为平滑保持）。

---

## 5. Phase 1：Addressables 后端（已实施）

### 5.1 AddressablesProvider

- **同步桥**：`Load<T>` 内部 `WaitForCompletion()`。编辑器资源库模式即时返回；WebGL 平台请只用异步 API（文档注明）；
- **句柄缓存**：`"key|类型"` 缓存句柄，重复加载复用（Addressables 自带引用计数）；`LoadAll` 的 Label 句柄以 `label:` 前缀区分；
- **失败语义**：未命中/初始化失败一律静默返回 null → 链上回退，由调用方既有的错误日志负责用户可见的报错；
- **可用性判定**（避免对未初始化项目逐次探查刷 InvalidKeyException 日志）：
  - 编辑器：Addressables 设置资产 + `"VNovelizer"` 组同时存在才启用（注册器注册后调 `VNResourceService.Reset()` 重建链重新评估）；
  - 构建包：首次 `InitializeAsync` 失败即熔断（整进程直接走 Resources）。

### 5.2 注册器（VNAddressablesRegistrar）

- 单组 `"VNovelizer"`（参照 Naninovel 默认单组），**Pack Separately**（每资产独立成包、释放即卸载，内存行为最接近旧 Resources；资产多构建慢可改 Pack Together）；
- 地址 = 资源键、Label = 类别；**已被用户归入其他组的条目不动**（尊重手动组织）；本组内条目地址由注册器托管，勿手动编辑（同 Naninovel）；
- 注册范围：包内 `Runtime/PackageDefault/VNovelizerRes` 全部资产（**只注册 GUID，不复制文件**——Addressables 直接引用包内资产是官方支持能力）+ 用户工作区 `Assets/VNovelizer`；
- 排除 `.xlsx/.md/.txt`（编辑器工作流文件，不参与运行时）；
- 入口：
  - 菜单 `VNovelizer → 资源管理(Addressables) → 同步全部资源注册`；
  - `SyncAll()`（向导）、`SyncWorkspace()`（轻量，Excel→CSV 转换后自动调用）、`RegisterAssetAtPath(path, key)`（编辑器窗口新建单资产）；
- **包内资产的资产路径是虚拟路径**（`Packages/{包名}/...`），枚举文件用 `PackageInfo.resolvedPath` 真实路径，注册时换算。

### 5.3 工作区自动登记（VNWorkspaceAssetPostprocessor）

- `Assets/VNovelizer/**` 下的导入/移动事件 → 延迟（`delayCall`，避免导入回调内改设置的 重入问题）执行 `SyncWorkspace()`；
- 仅在 Addressables 已初始化（设置资产存在）时生效——用户单纯拖放文件不会意外创建 `Assets/AddressableAssetsData`；
- 删除资产由 Addressables 自身挂钩清理。

### 5.4 编辑器窗口适配

| 窗口/服务 | 改造 |
|-----------|------|
| 画廊编辑器 | 数据容器加载经 `VNEditorResourceResolver.LoadByKey`（Addressables 地址 → 旧版 Resources 探测 → 包内默认）；"立即创建"容器写入 `VNProjectPaths` 解析的目录并注册 |
| 角色编辑器 | 角色目录 `CHARACTER_PATH` 改为动态解析 `VNProjectPaths.CharactersFolder` |
| Flag 编辑器 | 注册表创建目录经 `VNProjectPaths` 解析并注册（键 = `FlagService.DefaultRegistryPath`） |
| 剧本管理器 | 新建剧本模板优先取**包内** `Editor/Templates/ScriptTemplate.xlsx`（不再要求复制到 Assets） |
| 资源管理器 | `GetPathFromConfig` 经 `VNProjectPaths.ResourceKeyToFolder`（工作区/旧版目录自动切换） |
| Excel→CSV 转换 | 转换完成 + Refresh 后自动 `SyncWorkspace()`（手动转换与 AutoConvert 双入口均已挂钩） |

`VNProjectPaths` 解析规则：旧目录 `Assets/Resources/VNovelizerRes` 存在 → 沿用（存量兼容）；否则用工作区 `Assets/VNovelizer`。资源键 → 目录映射保持旧模式行为不变。

---

## 6. 初始化向导新流程

```
一键初始化
  ├─ StreamingAssets/VNovelizerRes/Videos     （视频始终走 StreamingAssets）
  ├─ VNProjectConfig（Assets/Resources，唯一引导资产；新模式顺带填 Excel/CSV 默认文件夹）
  ├─ 双模式判定：Assets/Resources/VNovelizerRes 是否已存在？
  │     ├─ 存量项目 → 兼容模式：旧版复制流程（9 文件夹 + 画廊容器，已存在跳过）
  │     └─ 新项目  → Addressables 模式：
  │           ├─ EnsureWorkspaceFolders（Assets/VNovelizer 空目录骨架，不写文件）
  │           ├─ SyncAll（初始化 Addressables + 注册包内默认资源，不复制文件）
  │           └─ 画廊数据容器（工作区 + 注册地址）
  ├─ 场景：本地副本（Assets/Scenes）优先；无副本直接注册包内场景到 Build Settings（不复制）
  └─ PrimeTween / TMP / InputSystem（不变）
```

变化要点：

1. **新项目不再复制任何资源到 Assets**（除了用户必须拥有的 `VNProjectConfig.asset`）；
2. **场景不再复制**——Build Settings 直接引用包内场景路径（`Packages/com.fakecorps.vnovelizer/Runtime/Scenes/*.unity`），按名加载（`SceneManager.LoadScene("VNGamePlay")`）行为不变；
3. 存量项目重跑向导 → 自动走兼容模式，行为与旧版一致（平滑兼容）。

---

## 7. 用户工作流

### 7.1 新项目（Addressables 模式）

1. 安装 VNovelizer（Addressables 由 UPM 硬依赖自动安装）→ 首次自动弹出向导 → 一键初始化；
2. 角色立绘/背景/CSV/音频等**自己的内容**放进 `Assets/VNovelizer/**` 对应子目录（放入即被自动登记进 Addressables）；
3. Excel 剧本工作流不变（转换 CSV 后自动登记）；
4. **构建游戏前**：`Window → Asset Management → Addressables → Groups → Build → New Build → Default Build Script`，然后正常 Build（向导完成弹窗与同步菜单均有提醒）。

### 7.2 存量项目（兼容模式）

- `Assets/Resources/VNovelizerRes` 已存在 → 一切照旧（Resources 兜底加载，无需任何迁移操作）；
- 增量更新默认资源：重跑向导（兼容模式会复制缺失的新默认文件）；
- 想迁移到 Addressables：删除/移走 `Assets/Resources/VNovelizerRes` → 重跑向导 → 执行菜单"同步全部资源注册"（内容本身仍在原处时 Resources 兜底继续工作，可分步迁移）。

---

## 8. 已知限制与注意事项

| 项 | 说明 |
|----|------|
| **构建前须先构建 Addressables** | 否则运行时 Addressables 初始化失败 → 自动熔断回退 Resources → 新项目 Resources 为空 → 资源缺失报错（各调用方既有错误日志会指出具体键） |
| **同步 API 在 WebGL** | `WaitForCompletion` 不适用于 WebGL，热路径应使用异步 API（当前 BGM/背景/语音已是异步；`ScriptParser`/`CharacterResManager`/`TheaterManager.ResolveAppearance` 仍是同步调用点） |
| **未注册键的 InvalidKeyException 日志** | 已注册 Addressables 的项目中查询未注册键时，Addressables 会打印一次异常日志后由链回退（编辑器内可用性检查已尽量规避整类噪声） |
| **资源无卸载** | Phase 0/1 不改变内存语义（与旧版一致：加载后常驻）；`VNResourceService.Release`/`AddressablesProvider.Release` 已预留接口，Phase 3 接线 |
| **VNovelizer 组内条目勿手动编辑** | 地址/Label 由注册器托管；组设置（BundleMode 等）可自由调整 |
| **`VNResourceService.Reset()` 在运行中调用** | 仅设计给编辑器注册器使用；运行中调用会重建链（Addressables 句柄缓存丢失，句柄泄漏） |
| **`Localization` 的表资产** | Unity Localization 有自己的 Addressables 集成，不在本链管辖范围 |

---

## 9. Phase 2 / Phase 3 规划

### Phase 2：用户资产进一步去 Resources 化（未实施）

- `CharacterProfile` / 剧本 CSV / `FlagRegistry` / 数据容器的类别 Label 已就绪，逐步把同步初始化点（`CharacterResManager.Init`、`ScriptParser`）改造为**异步预载**流程（配合 `LoadingProgressManager`）；
- 评估引导配置 `VNProjectConfig` 从场景引用（Bootstrap MonoBehaviour 序列化引用）加载的可行性，彻底清空 `Assets/Resources`；
- 存量项目 → Addressables 的一键迁移工具（复制/移动 + 注册 + 清理旧目录）。

### Phase 3：生命周期管理（未实施，对标 Naninovel）

- 引用计数：`VNResourceService` 层跟踪"持有者"（`Hold(asset, holder)` / `Release(asset, holder)`），归零触发 `Addressables.Release` / `Resources.UnloadUnusedAssets`；
- 策略开关（Resource Policy）：保守（剧本级预载/卸载）/ 乐观（常驻直到显式释放）/ 懒惰（即时加载、不可见即卸载）；
- `loadscript` 命令时按剧本内容预载（扫描行内引用的 BG/BGM/立绘）；
- UI 预制体走 `InstantiateAsync` + 池化释放。

---

## 附录：验证清单（升级 Unity 后手动执行）

1. 打开 Dev 项目（`D:\Unity\Unity项目\Vnovelizer_Dev`）→ Package Manager 自动安装 Addressables → 无编译错误；
2. 新建空工程走向导 → 确认：Assets 内仅出现 `VNovelizer/`（空目录）、`Resources/VNProjectConfig.asset`、`AddressableAssetsData/`；Build Settings 场景为 Packages 路径；
3. Play：VNDebugScene 加载剧本 → 背景/BGM/立绘/语音经 Addressables 命中（Console 应有 `[VNResourceService] 资源提供者链就绪: Addressables → Resources`）；
4. 存量工程（含 `Assets/Resources/VNovelizerRes`）→ 向导显示兼容模式 → Play 一切照旧（链描述应为 `Addressables(不可用) → Resources`）；
5. 剧本管理器"转换"CSV → Addressables Groups 窗口确认新 CSV 出现在 VNovelizer 组且地址正确；
6. 构建前执行 New Build → 打包 → 游戏内资源正常加载。
