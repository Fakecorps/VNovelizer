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
| `Editor/VNResourceProvider/VNAddressablesRegistrar.cs` | Addressables 资产注册器 + 菜单 + **拖放分配 API**（AssignToCategory/Unassign/Rename） |
| `Editor/VNResourceProvider/VNProjectPaths.cs` | 工作区/旧版目录路径解析 |
| `Editor/VNResourceProvider/VNEditorResourceResolver.cs` | 编辑器按键定位资产 |
| `Editor/VNResourceProvider/VNWorkspaceAssetPostprocessor.cs` | 工作区自动登记 |
| `Editor/VNResourceProvider/VNResourceEditorProbe.cs` | 编辑器可用性探针（经委托注入运行时提供者——运行时程序集不能引用 Unity.Addressables.Editor） |
| `Editor/VNResourceProvider/VNInputDialogue.cs` | 轻量模态输入框（逻辑名重命名用） |

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

## 6. 初始化向导新流程（统一零复制）

```
一键初始化（新/存量项目同一流程，全程零复制）
  ├─ StreamingAssets/VNovelizerRes/Videos     （空目录；视频始终走 StreamingAssets）
  ├─ VNProjectConfig（Assets/Resources，唯一引导资产；顺带填 Excel/CSV 默认文件夹）
  ├─ EnsureWorkspaceFolders（Assets/VNovelizer 空目录骨架，不写文件）
  ├─ SyncAll（初始化 Addressables + 注册，不复制文件）：
  │     ├─ 存量项目（旧目录存在）→ 注册旧目录中的用户副本（副本优先于包内原件，
  │     │   与 Resources 兜底所见一致；不复制/不移动/不修改任何文件）
  │     └─ 新项目 → 注册包内默认资源（文件本体留在包里）
  ├─ 画廊数据容器（目录按存量/新项目双模式解析；已存在则跳过并确保注册）
  ├─ 场景：本地副本（Assets/Scenes，存量用户可能已自定义）存在则注册副本；
  │        否则直接注册包内场景到 Build Settings（不复制）
  └─ PrimeTween / TMP / InputSystem（不变）
```

变化要点：

1. **全程零复制**：不复制任何资源到 Assets（除了用户必须拥有的 `VNProjectConfig.asset` 与画廊数据容器这两个用户数据资产）；
2. **场景不再复制**——Build Settings 直接引用包内场景路径（`Packages/com.fakecorps.vnovelizer/Runtime/Scenes/*.unity`），按名加载（`SceneManager.LoadScene("VNGamePlay")`）行为不变；
3. **存量项目重跑向导**：旧目录被整体注册进 Addressables（副本优先），运行时链首命中——获得与新项目一致的地址化加载，且不改动用户任何文件；
4. 包升级带来的新默认资源：执行菜单"同步全部资源注册"即可纳入（新项目）；存量项目如需引入新默认资源，从包内 `Runtime/PackageDefault` 手动复制所需文件（明确的手动操作，不再隐式同步）。

---

## 7. 用户工作流

### 7.0 拖放分配工作流（Addressables 托管模式，推荐）

**目标：彻底消灭"地址索引地狱"——用户不需要知道资源在磁盘的哪里，也不再需要任何文件夹约定。**

```
用户打开资源管理器 → 选中分页（背景/BGM/音效/语音）
  → 把图片/音频拖进 Unity（放 Assets 内任意位置）
  → 把资产从 Project 窗口拖到资源管理器分页
  → 完成：分页里显示逻辑名，Excel 直接写这个名字
```

机制（三层解耦）：

| 层 | 说明 |
|----|------|
| 物理位置 | 拖进 Unity 落在哪都行（`Assets/MyArt/随便什么文件夹`） |
| 逻辑地址 | 拖放分配时生成 `{类别前缀}/{逻辑名}`（默认文件名，可右键重命名） |
| 类别 Label | 分页决定（背景/BGM/SFX/Voice），供批量加载 |

配套操作（右键菜单）：

- **重命名（逻辑名）**：改 Addressables 地址尾段——**不动文件名**；用户之后在 Project 里重命名文件也不影响剧本（地址与文件名彻底解耦）；
- **移除分配（保留文件）**：从 VNovelizer 组移除条目，文件原地保留（删除按钮变三选一：移除分配 / 删除文件 / 取消）；
- 拖入时自动做类型校验（背景收图片、音频页收 AudioClip），并自动把图片导入设置修正为 Sprite（省掉"背景不显示"的头号坑）；同类别重名有冲突提示。

拖入**外部文件**（OS 文件管理器）仍是导入语义：复制到工作区文件夹后自动注册——两种来源在同一个拖放区，按来源自动分流（Project 资产 = Link 光标 = 分配；外部文件 = Copy 光标 = 导入）。

### 7.0.1 SO 资产创建（角色 / 画廊容器 / Flag 注册表）

编辑器创建 SO 时不再硬编码落点——弹出**保存位置对话框**（`SaveFilePanelInProject`，限定项目内），用户自选位置与文件名：

- **角色**：文件名 = 角色 ID（剧本 Speaker / CharLeft 列引用的名字）；创建后即按 `Characters/{角色ID}` 注册地址。角色编辑器列表同时聚合"类别 Label 注册条目 ∪ 默认文件夹扫描"——**保存在任意位置的角色都会出现在列表里**；
- **画廊容器 / Flag 注册表**：运行时索引键固定（如 `VNovelizerRes/GalleryContent/CG/CGDataContainer`），注册按固定键写地址——文件保存位置与文件名完全自由；
- 默认落点仍是对应工作区类别目录（对话框起点），只是"默认"而不再"强制"；
- 一键初始化向导创建的容器仍走默认位置（一键流程不打断用户）。

### 7.0.2 UI 模板覆写（Panel / 子项 / 基础设施预制体）

对标 Naninovel UI 定制机制，取代旧"复制 VNPrefabs 到 Assets/Resources 供用户编辑"流程：

```
用户指派了自定义模板？ ──是──► Instantiate(直接引用)   ← 零字符串、零加载、零寻址
        │否
        ▼
按引擎固定键经服务链加载包内默认模板（Addressables 注册，不复制文件）
```

- **覆写配置**：`VNProjectConfig`"八、UI 模板覆写"分组，23 个可空引用字段
  （10 主面板 + 7 子项 + 3 基础设施：EventSystem/SoundObj/VideoObj + 3 画廊数据容器 SO）；
  画廊数据容器走 `VNUIPrefabs.LoadAsset<T>`（SO 直接引用/服务链加载，键 = VNUIPrefabKeys 常量）；
- **统一解析入口**：`VNUIPrefabs.Load/LoadAsync(prefabKey, fallbackPath)`——覆写命中直接返回，
  否则按 fallback 路径经服务链加载；返回 prefab 本体（调用方 Instantiate）；
- **键约定**：`VNUIPrefabKeys` 固定常量（= 包内默认资源路径），覆写查询不随路径字段改动漂移；
- **从模板创建**：Config Inspector 对应分组的"从模板创建自定义 UI…"按钮 → 选择模板 →
  自选保存位置（SaveFilePanelInProject）→ 复制包内模板 → **自动填入覆写字段**；
- **接入点**：UIManager `Show<T>`（经 `PanelSpec.PrefabKey`）、EventSystem（原硬编码，顺带字段化）、
  PoolManager（SoundObj/VideoObj/HistoryItem 经池加载自动获得覆写能力）、
  6 处子项直接加载（PromptItem/ChoiceItem/SaveSlot/三 Slot）；
- **画廊三个页签**（CGPage/MusicPage/ScenePage prefab）随 GalleryPanel 模板序列化引用走，无独立字段；
- **TransitionManagerRoot** 为场景内置（无运行时加载），不在此机制管辖；
- **用户自定义面板进构建**：VNProjectConfig 在 Resources → 引用依赖自动进包（无需懂 Addressables）；
- 存量项目：覆写为空 → fallback 加载旧 Resources 副本（`SyncAll` 已注册同地址），行为不变；
  想迁移就把改过的预制体拖进覆写字段，然后删副本。

### 7.0.3 VNProjectConfig 瘦身（UI 路径字段清零）

UI 模板机制就位后，原"三、UI 预制件路径"整组字段失去存在意义，已删除（14 个字段）：
10 个 UI 面板路径、`UI_EventSystemPath`、`CG_DataPath`/`Music_DataPath`/`Scene_DataPath`、
`SoundObjPath`（死字段，无任何引用）/`VideoObjPath`。它们的职责由 `VNUIPrefabKeys` 固定常量接管
（键即包内默认地址，引擎私有）。

Config 现结构（用户可见路径字段 = 0）：

| 分组 | 内容 |
|------|------|
| 一、编辑器工具路径 | Excel/CSV 工作流（直接引用 + 开关） |
| 二、资源默认地址（引擎内部，勿改） | 9 个媒体/VFX 类别前缀，**只读**（引擎私有寻址常量，对标 Naninovel 内部地址前缀） |
| 三、UI 默认资源 | 2 个 Sprite 引用 |
| 四~七 | 启动/本地化/加密/剧场（与路径无关） |
| 八、UI 模板覆写 | 23 个引用字段 + "从模板创建…"按钮 |

配套改造：
- **画廊数据容器**（方案 A）：`VNUIPrefabs.LoadAsset<T>` 泛型入口（SO 覆写字段直接引用 /
  服务链 fallback，键 = VNUIPrefabKeys 常量）；三个 Gallery Page 的容器加载走此通道；
- **UI 预制体管理器**升级为"当前生效模板查看器"：三态显示（覆写生效 / 用户副本 / 包内默认），
  编辑包内默认模板时弹警示（影响所有项目 + 升级丢失，引导走"从模板创建"）；
- **UIManager**：内置面板 `PanelSpec` 不再设 `PathResolver`（`PrefabKey` 即完整 fallback 地址）；
  自定义面板的 `PathResolver` 保留兼容；
- 顺手修复：`MusicManager` SoundObj 池键不匹配（取 `VNovelizerRes/...`、还 `Music/SoundObj`，
  池对象永远无法回收）——统一为 `VNUIPrefabKeys.SoundObj`。

### 7.1 新项目（Addressables 模式）

1. 安装 VNovelizer（Addressables 由 UPM 硬依赖自动安装）→ 首次自动弹出向导 → 一键初始化；
2. 自己的内容**两种姿势任选**：拖放分配（推荐，见 7.0）或放进 `Assets/VNovelizer/**` 对应子目录（放入即被自动登记）；
3. Excel 剧本工作流不变（转换 CSV 后自动登记；逻辑名 = 文件名）；
4. **构建游戏前**：`Window → Asset Management → Addressables → Groups → Build → New Build → Default Build Script`，然后正常 Build（向导完成弹窗与同步菜单均有提醒）。

### 7.2 存量项目（旧目录兼容）

- `Assets/Resources/VNovelizerRes` 已存在 → 重跑向导：旧目录整体注册进 Addressables（**不复制/不移动/不修改任何文件**），运行时链首按地址命中其中的用户副本；Resources 兜底继续可用（双保险）；
- 想迁移到工作区模式：逐步把内容移入 `Assets/VNovelizer`（或拖放分配到资源管理器）→ 执行"同步全部资源注册"；移完后删除旧目录即可（内容已在别处注册）；
- 需要引入包升级后的新默认资源：从包内 `Runtime/PackageDefault/VNovelizerRes` 手动复制所需文件到旧目录（明确的用户操作，向导不再隐式同步）。

---

## 8. 已知限制与注意事项

| 项 | 说明 |
|----|------|
| **构建前须先构建 Addressables** | 否则运行时 Addressables 初始化失败 → 自动熔断回退 Resources → 新项目 Resources 为空 → 资源缺失报错（各调用方既有错误日志会指出具体键） |
| **编辑器内 Play Mode Script 必须为 "Use Asset Database (fastest)"** | 提供者的编辑器探针只在 Fast 模式下启用 Addressables（操作同步完成、无阻塞）。切换到 "Use Existing Build / Virtual" 等模式时编辑器内自动回退 Resources——这是刻意设计：同步桥 `WaitForCompletion` 在这些模式下（尤其未构建内容时）会永久自旋导致编辑器主线程冻结（卡死）。想在编辑器内测试已构建的 Addressables 内容请构建后仍保持 Fast 模式（Fast 与构建后运行行为一致：地址/Label 相同） |
| **同步 API 在 WebGL** | `WaitForCompletion` 不适用于 WebGL，热路径应使用异步 API（当前 BGM/背景/语音已是异步；`ScriptParser`/`CharacterResManager`/`TheaterManager.ResolveAppearance` 仍是同步调用点） |
| **未注册键的 InvalidKeyException 日志** | 已注册 Addressables 的项目中查询未注册键时，Addressables 会打印一次异常日志后由链回退（编辑器内可用性检查已尽量规避整类噪声） |
| **资源无卸载** | Phase 0/1 不改变内存语义（与旧版一致：加载后常驻）；`VNResourceService.Release`/`AddressablesProvider.Release` 已预留接口，Phase 3 接线 |
| **VNovelizer 组内条目勿手动编辑** | 地址/Label 由注册器托管；组设置（BundleMode 等）可自由调整 |
| **`VNResourceService.Reset()` 在运行中调用** | 仅设计给编辑器注册器使用（Registrar 全部入口已加 Play 模式守卫）；运行中调用会重建链（Addressables 句柄缓存丢失，句柄泄漏） |
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
