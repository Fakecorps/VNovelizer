# VNovelizer UI Canvas 架构重构计划（一界面一 Canvas · UIManager v2）

> 本文档规划 UI 层的架构清理：**删除 `VNGamePlayCanvas` 引导画布与六层 RectTransform 结构，UIManager 全量重写为"一面板一 Canvas + 注册表 + 泛型 API"**。
>
> 与剧场层重构（`VNTheaterRefactoring.md`）互补：剧场重构剥离了舞台渲染，本次重构清理 UI 宿主结构。两者完成后：`VNGameplayPanel` = 纯皮肤面板（自带 Canvas），剧场 = 世界空间，再无任何"中心画布"。
>
> **迁移策略：拆楼新起（与剧场层重构同一原则）**——不做旧 API 兼容层，所有调用点一次性迁移到新 API。

---

## 1. 背景与动机

### 1.1 旧结构与其问题

```
VNGamePlayCanvas (ScreenSpaceOverlay, CanvasScaler 1920x1080, DDoL)
├─ Bottom / Left / Middle / Right / Top / System   ← 六个空 RectTransform
│   └─ Panel（RectTransform 子节点，动态 SetParent）
```

| # | 问题 | 根源 |
|---|---|---|
| 1 | **Panel 锚点语义错乱**：新面板挂到六层空 RectTransform 下默认挤在左下角，每个面板都要手工修锚点 | Panel 被迫成为"别人 Canvas 里的子节点" |
| 2 | **rebuild 边界靠手工**：高频面板需手动加嵌套子 Canvas | 单 Canvas 承载全部面板 |
| 3 | **层级冗余**：审计证实 `Bottom/Left/Right` 三层**从未被任何调用使用**；`HistoryPanel` 曾同时挂在 Middle 与 Top 两层（历史混乱） | 层级树表达排序 |
| 4 | **生命周期绕**：切场景靠销毁中心画布连带销毁面板；DDoL 中心画布带来一堆补丁逻辑 | 中心画布 DDoL |
| 5 | **调用点噪音**：`ShowPanel<T>("T", path, layer, cb)` 四参数——名字重复泛型、路径散落各处、层级知识泄漏给调用方 | API 设计 |

### 1.2 新形态

```
（无中心画布）
每个 Panel prefab 根节点：
└─ Canvas(Overlay) + CanvasScaler(1920x1080, Shrink) + GraphicRaycaster + BasePanel 脚本
    └─ ...皮肤内容（根 RectTransform stretch 铺满，所见即所得）
```

## 2. 核心设计（已实现）

### 2.1 EUILayer：四档排序带

```csharp
public enum EUILayer
{
    Scene = 10,     // 全屏场景级（Gameplay / MainMenu / Gallery）
    Overlay = 20,   // 常规覆盖（Pause / SaveLoad / Settings / History / Choice）
    Popup = 30,     // 模态弹窗（Confirm）
    Loading = 40,   // 全局加载条（常驻）
}
// sortingOrder = (int)Layer + Order
```

六层中从未使用的 `Bottom/Left/Right` 直接删除；其余三档归并为四档新语义。

### 2.2 PanelSpec 注册表

面板元数据集中声明，调用方零路径/层级知识：

```csharp
public sealed class PanelSpec
{
    public string Name;                  // 注册键 = 类名
    public Func<string> PathResolver;    // 延迟解析（适配配置初始化时序）
    public EUILayer Layer;
    public int Order;
    public bool Persistent;              // 跨场景常驻（DDoL + Hide 不销毁）
}
```

- 引擎内置 10 个面板在 `Init()` 注册（路径绑定 `VNProjectConfig` 字段）；
- 用户自定义面板经 `Register(spec)` 接入（同名覆盖 = 可替换内置面板）。

### 2.3 泛型 API（取代 ShowPanel 四参数版）

```csharp
UIManager.GetInstance().Show<SaveLoadPanel>((p) => p.SetMode(Mode.Save));  // 显示（幂等）
UIManager.GetInstance().Hide<PausePanel>();                               // 隐藏（Persistent 只藏不销毁）
UIManager.GetInstance().Get<VNGameplayPanel>();                           // 获取（null 语义同旧）
UIManager.GetInstance().TryGet<T>(out T panel);                           // 显式空检查
UIManager.GetInstance().HideAll();                                        // 场景切换清理
UIManager.GetInstance().Register(spec);                                   // 用户面板注册
```

### 2.4 排序分配表（实现于注册表，与旧层级语义对照）

| 面板 | 旧层 | 新 Layer+Order = sortingOrder |
|---|---|---|
| VNGameplayPanel / MainMenuPanel | Middle | Scene+0 = 10 |
| GalleryPanel | Middle | Scene+1 = 11 |
| PausePanel | Top | Overlay+0 = 20 |
| HistoryPanel | Middle/Top（混乱） | Overlay+1 = 21（统一） |
| SaveLoadPanel | Top | Overlay+2 = 22 |
| SettingsPanel | Top | Overlay+3 = 23 |
| ChoicePanel | Top | Overlay+5 = 25 |
| ConfirmPanel | System | Popup+0 = 30 |
| LoadingProgressPanel | System | Loading+0 = 40（Persistent） |
| PlayVideo 临时 Canvas | System | 45（视频自建，播完自毁） |

叠放审计：Pause(20) 压 Gameplay(10)；SaveLoad(22) 压 Pause(20)；Confirm(30) 压 SaveLoad(22)；Loading(40) 压一切；视频(45) 压全部面板、低于常驻加载条。

### 2.5 Canvas 契约与容错

`Show<T>` 实例化后自动执行 `EnsureCanvasContract`：
- 缺 Canvas → 补挂 Overlay + 警告；非 Overlay → 强制纠正
- 缺 CanvasScaler → 补挂 1920×1080/ScaleWithScreenSize/Shrink
- 缺 GraphicRaycaster → 补挂
- 根 RectTransform → 强制 stretch 铺满
- sortingOrder → 按注册表写入

**过渡期效果**：旧 prefab（无 Canvas）也能显示（自动补挂），但布局需按新契约自查。

### 2.6 生命周期

- 面板 = 场景根对象；`OnSceneLoaded` → `HideAll()`（Persistent 面板仅 SetActive(false)）；
- `LoadingProgressPanel` 为唯一 Persistent 面板（首次 Show 时 DDoL）；
- EventSystem 保障逻辑保留（场景已有则复用）。

### 2.7 已删除的旧机制

六层 Transform 与查找、`VNGamePlayCanvas` 动态加载/DDoL、`FindUsableSceneCanvas`、`SetupCanvasCamera`、`GetLayerFather`、`E_UI_Layer` 旧枚举、`UIManager.canvas` 公有属性、`DelayedInitGameplayUI` 补丁路径。

## 3. 调用点迁移（已完成，共 13 文件）

| 文件 | 迁移内容 |
|---|---|
| VNManager.cs | 8 处 ShowPanel、21 处 GetPanel（含 Gallery/MainMenu） |
| VNGameplayPanel.cs | 7 处 ShowPanel、4 处 GetPanel |
| MainMenuPanel.cs | 5 处 ShowPanel + `canvas` null 检查改 `IsInitialized` |
| PausePanel.cs / SaveLoadPanel.cs / SettingsPanel.cs | 3+3+2 处 ShowPanel（局部 path 变量一并清理） |
| ChoiceCommand / ShakeCommand / HideCommand / TColor / TSize | GetPanel/ShowPanel |
| LoadingProgressTest.cs | Show/Get |
| API.cs | GetPanel + `PlayVideo` 改自建全屏 Canvas（sortingOrder 45，播完自毁） |
| ScenePage.cs | 2 处 GetPanel |

`HidePanel(string)` 按名版保留（约 15 处存量调用语义不变，避免无谓触碰）。

## 4. Prefab 重构契约（用户执行部分）

每个 Panel prefab 根节点（我方代码已按此契约自动容错，但建议逐个落实）：

| 组件 | 配置 |
|---|---|
| `Canvas` | Render Mode = Screen Space - Overlay |
| `CanvasScaler` | Scale With Screen Size，Reference 1920×1080，Match Mode = Shrink |
| `GraphicRaycaster` | 交互面板必挂 |
| 根 RectTransform | Stretch 全铺（anchorMin=0,0 / anchorMax=1,1 / offset=0） |

**建议顺序**：VNGameplayPanel → Pause → SaveLoad → Settings → History → Confirm → Choice → MainMenu → Gallery → Loading。

每改完一个面板即验证：锚点所见即所得、开关正常、排序正确。

## 5. 删除清单（待执行）

| 对象 | 操作 |
|---|---|
| `Runtime/PackageDefault/.../VNGamePlayCanvas.prefab`（+ .meta） | 删除 |
| Setup Wizard（一键初始化） | 去掉 VNGamePlayCanvas 导入步骤，改为校验各 Panel prefab 的 Canvas 契约 |
| UI预制体管理器 | 校验规则更新为 Canvas/Scaler/Raycaster 存在性 |
| Dev 测试项目 | 删除旧 Canvas 引用，逐个重做 Panel prefab |

## 6. 回归清单

1. 主菜单 → 新游戏 → Gameplay（Loading 条全程可见）
2. Gameplay → Pause → SaveLoad → Confirm 逐层叠放（顺序 = §2.4 表）
3. History/Settings 开关与状态恢复（从 Pause 打开/关闭回 Pause）
4. Choice 面板（剧本 choice 命令）
5. Gallery（主菜单入口 + 场景回放页）
6. 场景往返：主菜单 ↔ 游戏 ↔ 主菜单（无面板泄漏、无重复面板）
7. playvideo 命令（独立 Canvas 播放、播完自毁）
8. 存档/读档 + 快进中断（Loading 常驻面板跨场景复用）

---

*文档版本：v2.0（2026-08-20）——UIManager v2 已实现，调用点迁移完成；待办：Panel prefab 契约落实（用户）+ VNGamePlayCanvas 删除与编辑器适配*
