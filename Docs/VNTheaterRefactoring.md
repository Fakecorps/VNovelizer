# VNovelizer 剧场层重构计划（Theater Refactoring）

> 本文档规划 VNovelizer 的下一次底层架构演进：**将舞台内容（背景、角色立绘、场景特效）从 UGUI 层剥离，建立独立的演员抽象（IActor）与剧场层（Theater）——含场景相机系统，实现"编剧语言与渲染实现解耦、UI 与剧场分离"**。
>
> **迁移策略：单轨直切（淘汰式重构）。** 不提供新旧双轨开关、不做灰度过渡，以一个破坏性版本完成切换。背景与立绘从 `VNGameplayPanel` 中彻底移除，面板瘦身为纯对话 UI 皮肤层。
>
> 设计思想参考业界成熟商业视觉小说引擎与实时渲染的通用分层实践，结合 VNovelizer 现有架构（EventCenter 单向数据流、Simulate/Execute 双轨、命令系统）落地。
>
> 关联文档：《VNRefactoringPlan.md》§2.3（角色显示系统重构）、§3.2（摄像机命令族）、§12（实施路线图）。**建议与该文档的"角色动态槽位系统"合并为一次手术。**

---

## 目录

1. [背景与动机](#1-背景与动机)
2. [设计原则](#2-设计原则)
3. [目标架构](#3-目标架构)
4. [核心设计](#4-核心设计)
5. [与现有系统的衔接](#5-与现有系统的衔接)
6. [破坏性变更清单](#6-破坏性变更清单)
7. [实施路线（单轨直切）](#7-实施路线单轨直切)
8. [风险](#8-风险)
9. [工作量估算](#9-工作量估算)
10. [非目标（明确不做的事）](#10-非目标明确不做的事)
11. [关键设计决策记录](#11-关键设计决策记录)

---

## 1. 背景与动机

### 1.1 现状

当前所有舞台内容都寄居在 UGUI 体系内：

| 舞台内容 | 当前载体 | 代码位置 |
|---|---|---|
| 背景 | `bgImage_F / bgImage_B` 两个 `Image` | `VNGameplayPanel.cs:23-24` |
| 角色立绘 | 5 个槽位 `Image` | `VNGameplayPanel.cs:25-29` |
| 场景特效 | UGUI `effectLayer`（UIParticle） | `VNGameplayPanel.cs:33` |
| 相机操作 | 无（ScreenSpaceCamera 仅作 UI 投影仪） | — |

### 1.2 问题清单（Canvas 管线假设与舞台需求的冲突）

UGUI Canvas 的三个结构性假设，对"舞台演出"全部不成立：

| Canvas 的假设 | 舞台的实际需求 | 冲突后果 |
|---|---|---|
| 元素是平面贴片，排序 = 层级树 | 需要 Z 深度、按距离的景深与遮挡 | 无法实现景深对焦（如"对焦到某角色"）、推拉视差、真 3D 倾斜 |
| 合批优先、材质共享 | 需要逐演员独立材质/着色器（转场、溶解、模糊） | 自定义转场要写特殊 UI Shader 且破坏合批，`bgfade` 做到头只有交叉淡化 |
| 无相机概念 | 演出需要推拉摇移、透视切换、后处理 | 相机操作只能"模拟"（容器变换），后处理（模糊/暗角/调色）在 UI 层无解 |

派生问题：

1. **Live2D / Spine 缺口**：动态立绘是带骨骼的世界空间对象，与 UGUI Image 管线天然不兼容（对应《VNRefactoringPlan.md》Phase 4 最大缺口）。
2. **文字 rebuild 税**：对话文本每行必变，导致整个根 Canvas（含 2 张全屏 BG + 5 张全屏立绘）每行重批一次。
3. **命令层硬耦合 UGUI 类型**：`charmove` 直接摸 `RectTransform`、`bgfade` 直接摸双 `Image`，渲染实现被焊死（详见 §5.2）。
4. **产品边界模糊**：用户在 `VNGameplayPanel` prefab 里同时面对"可定制的 UI 皮肤"与"不该动的舞台结构"，定制面过大。

### 1.3 目标

```
VNGameplayPanel（瘦身）            剧场层 Theater（新建）
├─ 对话框 / 说话人 / 头像           TheaterManager → IActor 池（BG / 角色 / 特效）
├─ 系统按钮 / 快捷栏               ├─ MeshActor（世界空间 quad + 场景相机）
└─ 用户可自由编辑的 UI 皮肤         └─ 未来：Live2DActor / SpineActor
                                          ↑
                              IActor 统一抽象，剧本命令只对接口说话
```

- **编剧语言与渲染实现解耦**：CSV 格式、命令语法、坐标语义全部保持不变，存量剧本零迁移。
- **UI 与剧场分离**：UI 层保留 UGUI prefab 定制工作流；剧场层由状态驱动、配置管理，`VNGameplayPanel` 中**不再出现背景与立绘**。
- **相机能力落地**：推拉摇移、透视切换、景深、后处理成为引擎一等公民。
- **单轨直切**：一个破坏性版本完成切换，不保留 UGUI 演出轨道。

> 术语约定：**剧场层（Theater）** 指引擎内独立的演出空间与渲染管理层；行文中的"舞台/舞台内容"作为普通中文词汇使用，指标配在该层内的演出内容（背景、立绘、特效）。

---

## 2. 设计原则

| # | 原则 | 含义 |
|---|---|---|
| P1 | **演员抽象** | 舞台上的一切（背景、角色、特效）都是"演员"（Actor）：拥有 ID、外观、可见性、变换，可随时间异步变化。渲染方式是演员的实现细节。接口保留的目的**不是兼容，而是扩展**（Live2D/Spine/特效演员可插拔）与命令层解耦 |
| P2 | **状态与渲染分离** | 存档/预演（Simulate）快照的是**剧场状态**（谁在台上、什么外观、相机在哪），不是渲染对象。渲染对象是状态的"表达式" |
| P3 | **编剧语言稳定** | 剧本面向语义（像素坐标、命名位置、过渡名），永不面向渲染 API。存量 CSV 一字不改 |
| P4 | **单轨直切** | 不做新旧双轨开关、不做灰度。开发期新旧代码在同一分支短暂共存（重构的必然过程），产品以一个破坏性版本切换，旧演出轨道**淘汰**而非冻结 |
| P5 | **UI 层零革命** | 对话框、按钮、面板体系保持 UGUI + prefab 定制工作流不变；剧场重构不触碰用户 UI 定制权 |
| P6 | **依赖顺序交付** | 直切不等于大爆炸合并：阶段间保持依赖顺序（先建剧场 → 切命令 → 删面板舞台 → 迁特效 → 升存档），每个阶段结束开发验证通过再进下一阶段 |

---

## 3. 目标架构

### 3.1 运行时分层

```
┌──────────────────────────────────────────────────────┐
│ 剧本层（不变）                                          │
│ CSV → ScriptParser → StoryLine → VNManager            │
└──────────────┬───────────────────────────────────────┘
               │ EventCenter 事件契约（不变）
┌──────────────▼─────────────────┐  ┌─────────────────────────────────┐
│ 剧场层 Theater（新）             │  │ UI 层（瘦身后的 VNGameplayPanel） │
│ TheaterManager                 │  │ 对话框 / 说话人 / 头像 / 按钮      │
│ ├─ Dictionary<id, ActorState>  │  │ 纯 UGUI，用户可编辑 prefab       │
│ ├─ CameraState                 │  │ 不含任何 BG/立绘/特效            │
│ └─ MeshActor（quad + 材质）      │  │ 不受相机/转场影响                │
│     + SceneCameraManager        │  └─────────────────────────────────┘
│       （专用场景相机）             │
└────────────────────────────────┘
```

### 3.2 双相机栈

```
UI 相机（保持现有 ScreenSpaceCamera Canvas 不变）
   └─ 渲染 UGUI Canvas（对话框、按钮、面板）
场景相机（新建，orthographic 起步）
   └─ 渲染剧场空间（世界坐标）
      ├─ BG quad            z = 0
      ├─ 角色演员 quad       z = -0.5（按 zOrder 映射）
      ├─ 世界空间粒子/视频
      └─ 可挂载后处理组件（Bloom / DoF / 自定义滤镜，预制体预置 + 剧本开关）
```

- 相机与演员都由 `SceneCameraManager` 统一管理，支持自定义相机预制体。
- UI 层相机/Canvas 结构不变——`VNGamePlayCanvas` 继续承担 UI 排序契约（六层结构保留）。
- 存档截图（`ScreenCapture`）抓取最终帧缓冲，双相机合成结果自动包含。

---

## 4. 核心设计

### 4.1 IActor 接口

```csharp
namespace VNovelizer.Core.Theater
{
    /// <summary>演员抽象：剧场层渲染实现的唯一契约</summary>
    public interface IActor
    {
        string ActorId { get; }              // "MainBackground" / "Amy" / 槽位 ID
        ActorKind Kind { get; }              // Background / Character / Effect
        bool IsValid { get; }                // 渲染对象是否存活

        // ---- 外观 ----
        void SetAppearance(ActorAppearance appearance);    // sprite / texture / 未来 Live2D 参数

        // ---- 变换（剧本像素语义，实现层负责换算到自身坐标系）----
        void SetPosition(Vector2 posPx);     // 1920x1080 参考像素
        void SetScale(float scale);
        void SetFlip(bool flipped);          // scaleX = -1
        void SetDepth(int zOrder);           // 前后层级

        // ---- 可见性 ----
        void SetAlpha(float alpha);
        void SetVisible(bool visible);

        // ---- 转场（外观切换的演出化包装）----
        void Transition(ActorAppearance next, string transitionName,
                        float duration, float[] parameters);

        // ---- 异步动画（命令系统驱动，协程风格与 VNCommand 一致）----
        IEnumerator FadeAsync(float targetAlpha, float duration);
        IEnumerator MoveAsync(Vector2 targetPx, float duration);
        void Interrupt();                    // 跳过/中断时瞬间到终态
    }
}
```

**设计要点**：

- 接口只约定**演出语义**（外观/变换/可见性/转场），不出现任何渲染类型（`Image`/`RectTransform`/`MeshRenderer` 均不可见）。
- 坐标参数统一用**剧本像素语义**（1920×1080 参考分辨率），由实现层换算——这是 P3 原则的落点。
- `Transition` 把"换图"从数据操作升级为演出操作，为转场着色器系统留位（见 §4.5）。

### 4.2 剧场状态（可序列化）

```csharp
[Serializable]
public class ActorState
{
    public string actorId;
    public string appearance;        // "Amy#uniform#Smile" / BG 资源名
    public Vector2 position;         // 剧本像素语义
    public float scale = 1f;
    public float scaleX = 1f;        // 翻转（沿用现有存档字段语义）
    public int zOrder;
    public float alpha = 1f;
    public bool visible;
}

[Serializable]
public class CameraState
{
    public Vector3 offset;           // 相机偏移
    public float zoom = 1f;          // 1 = 默认
    public Vector3 rotation;
    public bool orthographic = true;
    public List<string> activeFxComponents = new();   // 相机上启用的后处理组件名
}
```

- `TheaterManager` 持有 `Dictionary<string, ActorState>` + `CameraState`，**是剧场的唯一事实源**。
- `SaveData` 新增 `theater` 字段（含全部演员状态 + 相机状态）+ `saveVersion=2`；旧存档不兼容（见 §6）。
- `Simulate()` 更新 `ActorState`（纯数据，不触碰渲染），`Execute/ExecuteAsync()` 通过 `IActor` 表达状态——**Simulate/Execute 双轨语义天然覆盖新架构**，同时顺手解决技术债"Simulate 仅 3 个命令实现"。

### 4.3 MeshActor（唯一演出实现）

- 世界空间 quad（`MeshFilter` + `MeshRenderer` + Unlit Transparent 材质），由 `SceneCameraManager` 的场景相机拍摄。
- 尺寸映射：**1 剧本像素 = 0.01 世界单位**（1920×1080 → 19.2×10.8），场景相机正交 Size = 5.4 时画面铺满且与旧 UGUI 观感一致。
- 深度：`zOrder` 映射到世界 Z（每级 -0.1），透明排序由渲染队列 + Z 双重保障。
- 帧动画（playanim）与视频（playvideo）在剧场空间以世界空间 quad 承载；粒子特效迁移为世界空间 `ParticleSystem`。
- 槽位语义：`L/ML/M/MR/R` 的默认 X 坐标由 `CharacterProfile.stagePoses`（§2.3）提供，仍以剧本像素表达。

### 4.4 相机系统

#### 命令族（剧本语法，配合《VNRefactoringPlan.md》§3.2）

| 命令 | 语法 | 语义 |
|---|---|---|
| `camerazoom` | `camerazoom(scale, duration)` | 推拉（正交尺寸 / FOV） |
| `camerapan` | `camerapan(x, y, duration)` | 平移（相机世界位移） |
| `cameraroll` | `cameraroll(angle, duration)` | 绕视轴旋转 |
| `camerashake` | `camerashake(duration, strength)` | 震屏（**只震场景相机，UI 纹丝不动**） |
| `camerarest` | `camerarest(duration)` | 归位（相机状态回默认） |
| `camerafx` | `camerafx(Bloom,false)` | 开关相机上的后处理组件 |

- 命令统一走 `SceneCameraManager`，直接操纵场景相机属性。
- 相机状态入存档（§4.2），跳过/中断语义与其他命令一致（`Interrupt()` 归位或到终态）。

#### 后处理挂载模式

- 引擎提供**场景相机预制体**（`SceneCamera.prefab`），后处理组件以禁用状态预挂在相机对象上。
- `camerafx(组件名, on/off)` 按**组件类型名**开关，状态随存档持久化。
- 特效分级：**相机级**（camerafx，作用于整个剧场画面）、**演员级**（blur 等实现 `IBlurable` 类接口，作用于单个演员）、**UI 级**（现有 UGUI 特效不动）。

### 4.5 转场系统（阶段 6）

- 语法升级：`bgfade(资源名.过渡名, 时长)`，如 `bgfade(Beach.Dissolve,1.5)`；不带 `.过渡名` 时保持交叉淡化语义。
- 实现机制：转场是**演员材质的着色器变体**（`multi_compile_local` + `THEATER_TRANSITION_XXX` 关键字），在片元函数中实现（Crossfade / Dissolve / Pixelate / Ripple / Blinds / Wave…）。
- 溶解遮罩：灰度纹理，黑像素先过渡、白像素最后，用户可自定义遮罩图。
- 转场只作用于剧场层——UI 层不在场景相机里，天然不受影响。

### 4.6 特效迁移对照（单轨：全部迁移）

| 现有特效 | 旧实现（淘汰） | 新实现 |
|---|---|---|
| `playparticle` / `stopparticle` | ~~UIParticle + effectLayer~~ | 世界空间 `ParticleSystem` |
| `playanim` | ~~帧动画 Image 序列~~ | 世界空间 quad 序列 |
| `playvideo` | ~~RawImage~~ | 世界空间 quad + VideoPlayer |
| 全屏滤镜/模糊/暗角 | 无 | 相机后处理组件 + `camerafx` |

---

## 5. 与现有系统的衔接

### 5.1 不变的部分（重构安全区）

| 系统 | 不变内容 |
|---|---|
| 剧本管线 | CSV 12/14 列格式、继承规则、ScriptParser、IDMap |
| 命令语法 | 全部存量命令的对外签名；`charmove(M,100,200,1)` 语义原样 |
| 事件契约 | `VNGameEvents` 全部事件名与载荷结构 |
| UI 体系 | UIManager 六层、`VNGamePlayCanvas`、对话框/按钮/面板 prefab、本地化、历史记录 |
| 编辑器 | 剧本管理器、资源管理器主体、角色编辑器主体 |

### 5.2 命令层重写

五个硬耦合命令改为只对 `IActor` 说话：

| 命令 | 现状（耦合点） | 改写后 |
|---|---|---|
| `charmove` | `VNAPI.GetCharRect()` → `RectTransform.anchoredPosition`（`CharMoveCommand.cs:68-80`） | `actor.SetPosition / MoveAsync` |
| `charfadein/out` | 直接操作 `Image` | `actor.SetAlpha / FadeAsync` |
| `charflip` / `setchartrans` | `VNManager.SetCharacterScaleX` | `actor.SetFlip` |
| `bgfade` | `VNAPI.GetBG_F()/GetBG_B()` 双 Image | `actor.Transition` |
| `charjump` | RectTransform 位移动画 | `actor.MoveAsync` 组合 |

`VNAPI` 中舞台相关方法（`GetBG_F/GetBG_B/GetCharRect/GetCharImage/GetEffectLayer`）由 Theater API 取代（见 §6）。

### 5.3 VNGameplayPanel 瘦身（直接删除）

- **删除**：`bgImage_F/B`、5 个立绘槽、`effectLayer` 字段；`OnShowCharacter`/背景变更等舞台事件 handler；`GetCharRect/GetCharImage/GetBG_F/GetBG_B` 等舞台访问器；`SaveDefaultCharTransform` 等舞台状态辅助逻辑。
- **保留**：对话框、说话人、头像、继续图标、系统按钮、点击推进——**这些是用户 prefab 定制的核心资产，100% 保留编辑权**。
- prefab 删除舞台子树，随版本重发；面板从"游戏画面全托管"变为"对话 UI 皮肤层"。

### 5.4 与角色系统重构（§2.3）合并

《VNRefactoringPlan.md》§2.3 的 `CharSlotState` + 动态槽位设计与本计划的 `ActorState` 是同一件事的两面。**合并为一次重构**：

- `CharSlotState` 字段并入 `ActorState`（新增 zOrder/alpha）。
- 五槽位 → 动态槽位的演进在剧场层内部完成，命令层无感。
- 两边动的是同一批文件（VNGameplayPanel、CharCommands、VNManager 字典），分开做等于二次手术。

### 5.5 配置项

`VNProjectConfig` 新增：

```csharp
[Header("剧场")]
public GameObject customSceneCameraPrefab;   // 自定义场景相机（预挂后处理），留空用默认
```

### 5.6 必须同步修复的现有问题

- **多相机查找**：`UIManager.SetupCanvasCamera()` 的 `Camera.main → cameras[0]` 兜底（`UIManager.cs:494-505`）在引入场景相机后会绑错 UI 相机。阶段 1 必须改为显式指定/标签查找（UI 相机打 `MainCamera` 标签或专用字段）。
- **`shake(screen)` 语义**：改震场景相机而非整个面板（对话框不再跟着抖）。

---

## 6. 破坏性变更清单（单轨直切的代价，一次性接受）

| # | 变更 | 影响面 | 处置 |
|---|---|---|---|
| B1 | `VNGameplayPanel` prefab 删除舞台子树 | 已定制该 prefab 的用户项目 | 随版本发布迁移说明：以新版 prefab 为基，重套用户 UI 皮肤 |
| B2 | `SaveData` 结构变更（`saveVersion=2`，新增 `theater`/`camera` 字段，角色字典改为 `ActorState` 列表） | 旧存档 | 旧档直接淘汰，或提供一次性迁移器（阶段 5 决策） |
| B3 | `VNAPI` 舞台方法移除（`GetBG_F/GetBG_B/GetCharRect/GetCharImage/GetEffectLayer`） | 用户自定义命令/外部脚本 | 以 `TheaterManager.GetActor()` 系 API 取代；变更日志列明对照表 |
| B4 | 剧场布局从 prefab 可编辑改为 `CharacterProfile.stagePoses` 配置驱动 | 依赖直接拖摆立绘槽位置的用户 | 布局入口迁移至角色编辑器，文档说明 |
| B5 | UIParticle 特效预制体不再适用 | 使用 `playparticle` 的项目 | 内置特效预制体随版本更新为世界空间版 |

**明确接受的取舍**：引擎处于快速迭代期，一次断裂升级的通知成本 << 兼容层的长期维护成本。变更集中在同一个大版本（2.0）交付，Changelog 一次性说明。

---

## 7. 实施路线（单轨直切）

> 开发期新旧代码在同一分支短暂共存（重构的必然过程，不是产品双轨）；每个阶段结束做开发验证（全命令冒烟 + 存档往返 + 基线比对），通过后进入下一阶段。**不设双轨开关、不做灰度发布。**

### 阶段 1：剧场地基（1.5-2 周）

| 任务 | 说明 |
|---|---|
| `IActor` + `ActorState` + `CameraState` + `TheaterManager` | 状态层建立，剧场唯一事实源 |
| `MeshActor` | 世界空间 quad + Unlit 材质，像素→世界单位换算（1px=0.01u），zOrder→Z |
| `SceneCameraManager` | 专用场景相机（正交 Size 5.4）、双相机栈、自定义相机预制体挂载 |
| `SetupCanvasCamera` 修复 | UI 相机显式指定（标签/配置），消除 `cameras[0]` 兜底 |
| 槽位默认布局 | `stagePoses`（L/ML/M/MR/R 默认 X）进 CharacterProfile |

**验证**：场景相机下 MeshActor 渲染的 BG/立绘与旧 UGUI 版截图对齐；UI 层不受影响。

### 阶段 2：命令层切换（1-1.5 周，与 §2.3 合并）

| 任务 | 说明 |
|---|---|
| 事件链改接 | `ShowCharacter`/背景事件由 TheaterManager 消费；面板舞台 handler 停用 |
| 五命令重写（§5.2） | 命令层不再出现 UGUI 类型 |
| Simulate 全命令补全 | 状态层就位后，全部命令实现 `Simulate()`（偿还技术债） |
| 槽位状态合并 | `currentCharacters` 等字典统一进 `ActorState`，key 统一 slotId（偿还 key 不一致技术债） |

**验证**：剧本驱动的演出（显示/移动/淡入淡出/换背景/换表情）全走新管线。

### 阶段 3：面板瘦身 + prefab 直切（1 周）

| 任务 | 说明 |
|---|---|
| `VNGameplayPanel` 删舞台代码 | §5.3 清单 |
| prefab 重发 | 删舞台子树；随版本发布迁移说明（B1） |
| VNAPI 断裂升级 | 移除舞台方法，提供 Theater API 对照表（B3） |
| UI预制体管理器 / 资源管理器同步 | 校验规则适配 |

**验证**：`VNGameplayPanel` 中搜索不到 bg/char 相关字段；全功能冒烟。

### 阶段 4：特效与媒体迁移（1 周）

| 任务 | 说明 |
|---|---|
| `playparticle/stopparticle` | 世界空间 `ParticleSystem` + 内置特效预制体更新（B5） |
| `playanim` | 世界空间 quad 帧序列 |
| `playvideo` | 世界空间 quad + VideoPlayer |

**验证**：特效/动画/视频在新管线播放正常。

### 阶段 5：存档 v2 + 相机命令族（1 周）

| 任务 | 说明 |
|---|---|
| `saveVersion=2` | `theater`/`camera` 字段；旧档淘汰或一次性迁移器（B2 决策） |
| 相机命令族 | `camerazoom/pan/roll/shake/rest`（Execute/ExecuteAsync/Simulate/Interrupt 四件套齐全） |
| `shake(screen)` 切换目标 | 震场景相机（§5.6） |

**验证**：存档往返（含相机状态恢复）；快进中断归位。

### 阶段 6：演出能力兑现（长期，按需求驱动）

| 任务 | 说明 |
|---|---|
| 转场系统 | `资源名.过渡名` 语法、着色器变体、溶解遮罩（§4.5） |
| 演员级特效 | blur 等演员级滤镜接口 |
| URP 后处理命令 | `screenblur/screentint/screenvignette`（兑现《VNRefactoringPlan.md》§3.2） |
| Live2D / Spine Actor | 动态立绘作为 `IActor` 实现接入，与静态立绘同语法 |
| 透视模式演出 | 透视相机 + 分层 Z 的视差推拉、真倾斜 |

> 阶段 1-5 合计约 5-6 周，完成后剧场层与相机能力一步到位，`VNGameplayPanel` 成为纯 UI 皮肤层。

---

## 8. 风险

| 风险 | 等级 | 缓解 |
|---|---|---|
| 破坏性变更集中交付，用户升级成本高 | **高（产品级）** | 全部变更集中在 2.0 一个版本；迁移说明 + VNAPI 对照表 + prefab 迁移指南随版本发布 |
| 开发期引擎不可发布的窗口较长（~5-6 周） | 中 | 阶段间依赖顺序推进，每阶段结束开发验证；主分支冻结非必要功能 |
| 透明排序（quad 之间 / 与粒子的次序） | 中 | 渲染队列 + Z 双保险；建立排序专项测试场景 |
| Shader 开发成本（转场变体） | 中 | 阶段 6 才启动；先交付 3-4 个高需求变体（Crossfade/Dissolve/Pixelate/Blinds） |
| 多相机引入的 UI 绑定错误 | 中 | 阶段 1 强制修复 `SetupCanvasCamera`；UI 相机打标签 |
| 存档断裂引发用户数据丢失 | 中 | 一次性迁移器作为可选项提供（阶段 5 决策） |

---

## 9. 工作量估算

| 阶段 | 工作量 | 产出 |
|---|---|---|
| 1. 剧场地基 | 1.5-2 周 | TheaterManager + MeshActor + SceneCameraManager + 多相机修复 |
| 2. 命令层切换 | 1-1.5 周（含 §2.3 合并） | 事件链改接 + 五命令重写 + Simulate 补全 |
| 3. 面板瘦身 | 1 周 | VNGameplayPanel 纯 UI 化 + prefab 重发 + VNAPI 升级 |
| 4. 特效迁移 | 1 周 | 粒子/帧动画/视频世界空间化 |
| 5. 存档 v2 + 相机命令 | 1 周 | saveVersion=2 + 相机命令族 |
| 6. 演出能力 | 长期（按需求切片） | 转场 / 后处理 / Live2D / 透视演出 |

---

## 10. 非目标（明确不做的事）

| 非目标 | 理由 |
|---|---|
| 不提供 UGUI 演出轨道/双轨开关 | P4 单轨直切，旧轨道淘汰 |
| 不引入 3D 场景/导航 | VNovelizer 是视觉小说引擎，剧场是 2D 演出空间 |
| 不替换 UI 层渲染方案（不迁移 UI Toolkit 等） | UGUI 对 UI 层是正确工具；本计划只动剧场层 |
| 不修改 CSV 列结构与命令语法 | P3 原则，存量剧本零迁移是硬约束 |
| 不在本计划内做 DSL / 节点编辑器 | 与《VNRefactoringPlan.md》§4/§5 各自推进，接口（命令系统）稳定即可 |

---

## 11. 关键设计决策记录

| # | 决策 | 选择 | 理由 |
|---|---|---|---|
| D1 | 演员抽象单元 | 演员接口（IActor），非图层/节点 | 一个接口同时容纳静态立绘、动态立绘（Live2D）、背景、特效；实现可插拔 |
| D2 | 剧本坐标语义 | 保持像素语义（1920×1080 参考） | 存量剧本零迁移；实现层换算（1px=0.01 世界单位） |
| D3 | 渲染迁移策略 | **单轨直切（淘汰式）**，非双轨 | 引擎处于快速迭代期，一次断裂升级成本 << 兼容层长期维护成本；变更集中在 2.0 版本（§6） |
| D4 | 剧场事实源 | `TheaterManager` 的可序列化状态字典（`ActorState`）+ `CameraState` | 状态/渲染分离；Simulate 与存档天然统一 |
| D5 | 相机操作 | 命令 → SceneCameraManager → 直接操纵场景相机 | 无需模式分派；真相机即真演出 |
| D6 | 后处理挂载 | 相机预制体预挂组件 + 剧本按组件名开关 + 状态入存档 | 零代码扩展；演出状态完整可恢复 |
| D7 | 转场语法 | `资源名.过渡名`（点号后缀），缺省 = 交叉淡化 | 向后兼容；着色器变体按名寻址 |
| D8 | 舞台布局定制 | CharacterProfile.stagePoses 配置驱动 | 舞台从 prefab 可编辑区移出后的新定制入口（B4） |
| D9 | 特效分级 | 相机级 / 演员级 / UI 级三层 | 各归其位，避免一个特效系统服务所有层 |
| D10 | 与 §2.3 关系 | 合并实施（阶段 2） | 同批文件、同批概念（槽位状态），避免二次手术 |
| D11 | 命名体系 | `Theater`（剧场层）+ `IActor`（演员） | 完整剧场隐喻；`Stage` 与 Unity Scene 语境易混、`Scene` 被引擎占用；`TheaterManager.GetActor()` 读感自然 |
| D12 | 破坏性变更处置 | 集中在 2.0 一次性交付（B1-B5） | 直切策略的必然代价；用迁移文档而非代码兼容层消化 |

---

*文档版本：v2.0（2026-08-19）——迁移策略定稿：单轨直切，删除双轨/UguiActor/CameraRoot 过渡方案；VNGameplayPanel 瘦身为纯 UI 皮肤层*
