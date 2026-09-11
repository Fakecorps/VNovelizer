# VNovelizer × Live2D 角色集成设计方案（v3：官方 OW 组件直封）

> 状态：设计稿（待确认后实施）｜日期：2026-09-11
> 关联文档：`VNTheaterRefactoring.md`（剧场层扩展点）、`VNCommandChainSpec.md`（命令与图编辑器规范）
> v3 变更：放弃 v2 的"自建 AnimatorController 状态机"路线，改为 **直接封装 Live2D 官方 Cubism SDK 5 的 OW（Original Workflow）组件体系**——播放内核 100% 官方组件，封装层只做"剧本语义 → 官方 API"映射与"缺失引用注入"。

---

## 1. 调研依据（源码级事实，全部来自 Dev1 工程实测）

| 依据 | 结论 |
|---|---|
| Dev1 工程 `Assets/Live2D/Cubism/cubism-info.yml` | SDK 版本 **Cubism 5-r.4.2**（OW 工作流），SDK 源码可直接读 |
| `CubismMotionController.OnEnable`（源码 L285-289） | **Animator 上挂有 runtimeAnimatorController 时直接罢工**（警告后 return）→ v2 的"生成/覆写 AnimatorController"路线被官方明确否决 |
| `CubismMotionPriority`（源码） | 官方内置优先级阶梯：`PriorityNone=0 / PriorityIdle=1 / PriorityNormal=2 / PriorityForce=3`——**官方就是按"待机 < 常规 < 强制打断"设计的** |
| `CubismMotionController.PlayAnimation`（源码 L119-138） | `PlayAnimation(clip, layerIndex, priority, isLoop, speed)`；同层新动作被拒当 `当前优先级 >= 新优先级`（Force 例外） |
| `CubismMotionLayer.CreateFadePlayingMotion`（源码 L196-244） | clip 与 fade 数据的匹配**不靠名字**，靠 clip 内烘焙的 `InstanceId` AnimationEvent 匹配 `CubismFadeMotionList.MotionInstanceIds` → 用户配置的 clip 必须是 SDK 导入生成的 `.anim` |
| `CubismFadeMotionData`（源码） | `FadeInTime/FadeOutTime` 公开字段（含 model3 默认值 `ModelFadeInTime/OutTime`）→ fade 时长可运行时覆写 |
| `CubismExpressionController`（源码） | 表情 = 设 `CurrentExpressionIndex`（-1=无表情）；切换即新表情淡入、旧表情淡出；与动作管线**完全独立**（可重叠） |
| 官方示例 `Samples/OriginalWorkflow/Motion/CubismMotionPreview.cs` | 官方用法范式：按优先级规则播动作 + `AnimationBegin/EndHandler` 回调 + 循环动作用协程计长 |
| 实测 `Mao/Mao.prefab`（SDK 自带测试模型，开发联调使用） | ① `Animator.m_Controller = {fileID: 0}`（官方 OW 生成即无 controller）；② `ExpressionsList` / `CubismFadeMotionList` **官方导入已自动赋值**（旧 miku 模型曾为 null 属用户模型导入异常，封装层注入作为兜底保留） |
| 实测 Mao 资产 | 表情 8 个：`exp_01`~`exp_08`（`expressions/*.exp3.asset`）；动作 8 个：`mtn_01`~`mtn_04`、`sample_01`、`special_01`~`special_03`（`motions/*.anim`，motion3 元数据全部 `Loop:true`）；含 `Mao.expressionList.asset` / `Mao.fadeMotionList.asset` |

---

## 2. 官方推荐的 VN 结合逻辑（从 SDK 源码提炼）

### 2.1 播放内核 = 官方 OW 组件，一个都不自建

```
CubismUpdateController（每帧驱动顺序）
  ├─ CubismMotionController（PlayableGraph + 多层 CubismMotionLayer）
  │     ├─ Layer 0：Idle 循环动作（PriorityIdle）      ← 永远垫底
  │     ├─ Layer 1：常规动作（PriorityNormal）
  │     └─ Layer 2：强制打断动作（PriorityForce）
  ├─ CubismFadeController（fade 权重混合，动作切换平滑）
  ├─ CubismExpressionController（CurrentExpressionIndex 表情通道，独立于动作）
  └─ CubismEyeBlinkController / CubismMouthController（自动眨眼/口型，prefab 自带）
```

### 2.2 三条 L2D 特殊语义的官方解法

| 诉求 | 官方解法（无需自建任何状态机） |
|---|---|
| 播完动作回 Idle | Idle 循环在 Layer 0 垫底；常规动作在 Layer 1 播放，播完 SDK 自动 fade out → **底层 Idle 露出来，即"自动回 Idle"**（官方多层混合语义） |
| Motion 与 Exp 可重叠 | 动作走 Motion 管线、表情走 Expression 管线，两条独立更新通道天然并存互不打断 |
| 动作打断 | 一次性动作统一以 `PriorityForce` 在动作层**同层替换**（官方原生语义：`PlayAnimation(clip, layer, Force)` 断开旧动作状态、交叉淡化接新动作）；Idle 永远垫底不可被误杀 |

### 2.3 封装层存在的意义（不重复造轮子，只补官方缺口）

官方组件已经解决播放问题，但离 VN 剧本还差三层，这正是封装层的职责：

1. **引用注入（单一事实源 + 兜底）**：官方正常导入会自动接好 `ExpressionsList`/`CubismFadeMotionList`（Mao 实测已赋值）；但用户模型导入异常时可能为 null（旧 miku 实测如此）——封装层在实例化时**总是以 L2DCharacterProfile 配置覆盖注入**，保证单一事实源在 VNovelizer 配置；
2. **ID 映射**：剧本写的是人读 ID（`Smile`/`wave`），官方 API 要的是索引/资产引用——封装层维护"ID → 资产"映射表；
3. **生命周期治理**：初始化序列（LayerCount 须在 Graph 建立前设好）、就绪队列、中断（只停动作层不杀 Idle）、等待完成、实例缓存。

---

## 3. 架构边界（v3 最终决策：L2D 代码随插件发布 + 编译门控）

**核心原则**：VNovelizer 核心程序集（Runtime/Editor）**保持零 Live2D 依赖**；L2D 能力是插件的**派生功能**（用户须先手动导入 Live2D SDK 与模型），因此 L2D 代码**随插件一起发布**，位于插件内的独立目录，通过 **asmdef `defineConstraints` + `VN_L2D` 脚本定义符号**做编译门控：

```
d:\VNovelizer\
├── Runtime/Scripts/VNovelizer/L2D/      ← L2D 运行时（defineConstraints: VN_L2D）
│   └── VNovelizer.L2D.Runtime.asmdef    （引用 VNovelizer.Runtime + Live2D.Cubism）
├── Editor/L2D/                          ← L2D 编辑器（defineConstraints: VN_L2D）
│   └── VNovelizer.L2D.Editor.asmdef     （引用 VNovelizer.Editor + L2D.Runtime + Live2D.Cubism）
└── Editor/L2DSupportActivator.cs        ← 激活器（VNovelizer.Editor 程序集，恒编译）
```

- **激活器**：检测 `Assets/Live2D/Cubism`（SDK 导入位置）是否存在 → 自动向所有 BuildTargetGroup 的 Scripting Define Symbols 添加/移除 `VN_L2D` → 触发重编译。SDK 在 → L2D 代码参与编译；SDK 不在 → 门控程序集整体不编译，**全插件零报错、零行为变化**；
- 注：Live2D SDK 是 Assets 导入式（非 UPM 包），`versionDefines` 无法探测 → 必须用激活器管理符号；
- L2D 能力通过核心 3 处**泛型**扩展点接入（核心仍不引用 L2D 程序集）：
  1. `CharacterProfile` 加 `public virtual bool IsSpriteBased => true;`（`L2DCharacterProfile` 覆写为 false）；
  2. `ActorAppearance` 加 `public CharacterProfile profile;`（外观携带配置引用，核心自有类型）；
  3. `TheaterManager` 加静态演员工厂钩子 + `IActor.Accepts(ActorAppearance)` + `IActor.Dispose()`（MeshActor ↔ L2DActor 无缝切换）。

命令侧零改动：`CommandManager` 反射自动注册，`l2dmotion`/`l2dexp` 带 `[VNCommandMeta]`/`[VNParam]` 即自动进命令面板与图编辑器。

---

## 4. 数据模型

### 4.1 `L2DCharacterProfile : CharacterProfile`

```csharp
[CreateAssetMenu(fileName = "L2DCharacterProfile", menuName = "VNovelizer/L2D角色配置")]
public class L2DCharacterProfile : CharacterProfile
{
    public override bool IsSpriteBased => false;

    [Header("Live2D 模型（SDK 导入产物）")]
    public GameObject ModelPrefab;                        // SDK 自动生成的模型 Prefab
    public CubismExpressionList ExpressionList;           // 表情总表（.expressionList 资产）
    public CubismFadeMotionList FadeMotionList;           // 动作 fade 总表（.fadeMotionList 资产）

    [Header("表情配置（ID → 表情资产）")]
    public List<L2DExpressionEntry> Expressions;

    [Header("动作配置（ID → 动作 clip）")]
    public List<L2DMotionEntry> Motions;
}
```

> 说明：v2 的 `AnimatorController` 字段**删除**——官方 OW 路径 Animator 必须无 controller（§1）。"Animation Controller" 的角色由 `ExpressionList` + `FadeMotionList` + clip 引用三个官方资产取代。
> 继承自动获得：`CharacterID`、`SpeakerBox`、`HeadFrame`、`scale`、`offset`、静态头像分组、旧资产迁移。

### 4.2 `L2DExpressionEntry`（表情条目）

```csharp
[Serializable] public class L2DExpressionEntry
{
    public string ID;                      // 剧本里写的表情名（如 "Smile"）
    public CubismExpressionData Data;      // SDK 导入的 .exp3 资产（比心.exp3.asset）
    public float FadeOverride = -1f;       // 覆写淡入秒数；-1 = 用 exp3 自带
}
```

- 运行时：`index = Array.IndexOf(ExpressionList.CubismExpressionObjects, Data)` → `CurrentExpressionIndex = index`；
- **校验**：`Data` 必须存在于 `ExpressionList` 内（Inspector 红字拦截）。

### 4.3 `L2DMotionEntry`（动作条目）

```csharp
[Serializable] public class L2DMotionEntry
{
    public string ID;                      // 剧本里写的动作名（如 "wave"）
    public AnimationClip Clip;             // SDK 生成的 motions/*.anim（必须含 InstanceId 事件）
    public L2DMotionKind Kind;             // Idle / Motion（两层设计，见 §2.2 动作打断决策）
    public bool Loop;                      // 循环（Idle 必须 Loop）
    public float FadeInOverride = -1f;     // 覆写淡入秒数；-1 = 用 model3 默认
    public float FadeOutOverride = -1f;    // 覆写淡出秒数；-1 = 用 model3 默认
}
public enum L2DMotionKind { Idle, Motion }
```

### 4.4 配置校验规则（Inspector 即时提示 + 保存前拦截）

| # | 规则 | 级别 |
|---|---|---|
| 1 | 所有 ID 非空、动作/表情各域内唯一（大小写不敏感） | 错误 |
| 2 | **恰好 1 个 `Idle` 条目（不允许缺省）**、必须 `Loop=true`（新建 L2D 角色时自动生成一个空 Idle 条目占位，等用户拖入 clip） | 错误 |
| 3 | `Clip` 必须含 `InstanceId` AnimationEvent（SDK 导入的 .anim 才有；Editor 可读 `clip.events` 校验） | 错误 |
| 4 | 表情 `Data` 必须在 `ExpressionList` 内 | 错误 |
| 5 | `ExpressionList` / `FadeMotionList` 未拖入但配置了对应条目 | 错误 |
| 6 | 同 Clip 被多个 ID 引用 | 错误 |

---

## 5. 运行时封装（L2DActor : IActor）

### 5.1 创建与初始化序列（关键：顺序决定成败）

```csharp
GameObject inst = Object.Instantiate(profile.ModelPrefab, parent);
var animator = inst.GetComponent<Animator>();
animator.runtimeAnimatorController = null;            // ① 官方 OW 硬约束：Animator 不得挂 controller

var fade = inst.GetComponent<CubismFadeController>();
if (fade == null) fade = inst.AddComponent<CubismFadeController>();   // ② 缺啥补啥（Mao 等示例模型未必齐备）

if (inst.GetComponent<CubismMotionController>() != null)
    Object.DestroyImmediate(inst.GetComponent<CubismMotionController>()); // ③ 一律移除重建（见下方 SDK 陷阱）

bool wasActive = inst.activeSelf;
inst.SetActive(false);                                // ④ 未激活状态下加组件：AddComponent 不触发 OnEnable
var mc = inst.AddComponent<CubismMotionController>();
fade.CubismFadeMotionList = profile.FadeMotionList;   // ⑤ OnEnable 会读取该列表——必须激活前注入
mc.LayerCount = 2;                                    // ⑥ 两层：0=Idle 垫底 / 1=一次性动作（同层 Force 替换）
inst.SetActive(wasActive);                            // ⑦ 首次 OnEnable：数组按 LayerCount=2 创建，Graph 建立成功

expr.ExpressionsList = profile.ExpressionList;        // ⑧ 表情总表注入（配置为单一事实源）
fade.Refresh();                                       // ⑨ 重读 fade states（层数组已按正确层数建好）
```

> **【SDK 陷阱（2026-09-11 实测 IndexOutOfRange）】`CubismMotionController` 在首次 OnEnable 时按 LayerCount 创建内部 `_motionLayers` 数组并永久缓存，不随 LayerCount 变化重建**——若先以 LayerCount=1 激活过，再改 2 并重新 enable，OnEnable / StopAnimation 会按 2 层访问 size=1 的数组直接越界（官方源码未考虑该场景）。正确做法 = "移除旧组件 + 未激活 AddComponent + 预置 LayerCount + 激活"。
> 另注意 SDK 的 StopAnimation 边界检查用的是 `LayerCount` 字段而非数组真实长度，越界防护形同虚设——L2DActor 所有 SDK 调用均已套 try/catch 兜底。

### 5.2 封装 API（命令层唯一控制面，与 v2 完全一致）

```csharp
public void PlayMotion(string motionID);              // → PlayAnimation(clip, layer, priority, isLoop)
public void StopMotion();                             // → 停动作层（保留 Idle 层）
public void SetExpression(string expID, float? fadeOverride = null);
public void ClearExpression();                        // → CurrentExpressionIndex = -1（官方淡出）
public IEnumerator WaitMotionFinished();              // AnimationEndHandler + IsPlayingAnimation 轮询
```

### 5.3 配置 → 官方 API 映射表（v3.1：单层 + 自动重播 Idle 修正）

| 配置 Kind | layerIndex | priority | isLoop | 备注 |
|---|---|---|---|---|
| `Idle` | 0 | PriorityIdle(1) | true | 永久循环；非循环动作结束自动重播兜底 |
| `Motion` | 0 | **PriorityForce(3)** | entry.Loop | 同层 Force = 官方原生"替换当前动作 + 交叉淡化"；播完自动重播 Idle |

**架构关键修正（2026-09-11 实测）**：原设计 LayerCount=2（Idle 垫底 + Motion 替换），**实测失败**——`AnimationLayerMixerPlayable` 两层 weight=1 时为**覆盖**语义：动作层 fade 完输出"该层身份"继续遮蔽 Idle 层，导致模型停在末帧。修正为**单层**（LayerCount=1）——Idle 与 Motion 共享同一层，SDK 的 `CubismFadeController` 自动做 crossfade（旧动作按 FadeOutTime 渐出 + 新动作按 FadeInTime 渐入，叠加）；非循环 Motion 的 `AnimationEndHandler` 触发自动重播 Idle 兜底。

**动作打断的最优解（已决策）**：单层 + 同层 `PriorityForce` 替换；多非循环动作连续触发时只重播最后一个（`_activeNonIdleInstanceId` 仅匹配最新）。Loop=true 的 Motion 按用户意图永远循环，EndHandler 不触发（SDK 行为），Idle 不会被打断。

### 5.4 IActor 兼容（存量演出命令免费可用）

| IActor 成员 | L2DActor 实现 |
|---|---|
| `SetPosition/SetScale` | 模型根 Transform（1 剧本像素 = 0.01 世界单位，同 MeshActor 契约） |
| `SetFlip` | 根 `scale.x = -1`（需材质 Cull=Off，Inspector 提示） |
| `SetDepth` | 世界 Z = -0.1 × zOrder |
| `SetAlpha` | `CubismRenderController.Opacity`（全 Drawable 透明度） |
| `SetVisible` | 根节点 SetActive |
| `FadeAsync/MoveAsync` | 根 Transform / Opacity 协程插值（同 MeshActor） |
| `Interrupt`（行跳过） | `StopMotion()` 停动作层（layer 1），**保留 Idle**；表情保持（下一行立绘列决定去留） |

`charfadein`/`charfadeout`/`charmove`/`charflip`/`shake`/`charjump`/`setchartrans` 零改动全部可用。

### 5.5 其他运行时要点

- **FadeTime 覆写**：`CubismFadeMotionData.FadeInTime/FadeOutTime` 是公开字段，但资产被共享——覆写前先 `Instantiate` 克隆一份再改，避免污染共享资产；MVP 可先忽略覆写（用 model3 默认 fade）；
- **就绪队列**：模型首帧初始化完成前收到的 `SetExpression`/`PlayMotion` 先入队，就绪后补发；
- **实例缓存**：按 CharacterID 缓存实例化模型，同角色反复登台零重复加载；
- **行边界语义（与 v2 相同）**：表情层每行以立绘列情绪为准重置（VN 每行重发 ShowCharacter）；动作层不强制打断（非循环自然回 Idle，循环动作用 `l2dmotion(pos, idle)` 收）。

---

## 6. 命令设计（v3 决策版：四参数严格、两层动作模型）

### 6.1 `l2dmotion(pos, motionID[, mode[, fade]])`

| 参数 | 说明 |
|---|---|
| pos | 槽位 L/ML/M/MR/R 或自由角色 posID（复用 `NormalizePosCode`） |
| motionID | 动作 ID（配置表内定义）；特殊值 `idle` = 强制回待机（`StopMotion()` 语义） |
| mode | **严格枚举**，两值：`wait` = 阻塞命令链直到动作结束（`ExecuteAsync` 等待 `WaitMotionFinished()`）；`async` = 立即返回不等待（显式非阻塞，与省略同义）。图编辑器经 `[VNParam]` Enum 元数据提供**下拉选取** |
| fade | 覆盖淡入淡出秒数（可选，仅第 4 位有效） |

**四参数严格性（已决策）**：参数按位置解释，无宽容解析——第 3 位只认 `wait`/`now`（其余报"参数无法识别"），fade 只认第 4 位数字。理由：图编辑器按 `[VNParam]` 位置生成表单，位置语义必须与运行期解析严格一致，宽容解析会造成图编辑器节点与实际执行的分裂。

语义：

- 缺省 mode（非阻塞，边说边动）；`wait` + `Loop` 动作永远等不到 → 校验器警告；
- 动作打断：同层 `PriorityForce` 替换（§5.3），后动打断前动、播完自动回 Idle；
- 槽位不是 L2D 演员 / 模型未就绪 → 警告并跳过，不打断命令链。

### 6.2 `l2dexp(pos, expID[, fade])`

- `expID` = 表情 ID；`none` = 清除（`CurrentExpressionIndex = -1`，官方自带淡出）；
- **保持型**：官方 `CurrentExpressionIndex` 语义天然就是"应用并保持"，与立绘列/存档语义一致。

### 6.3 `l2dreset(pos)`（已决策：缓到 P4）

`StopMotion()` + `ClearExpression()`。MVP 不含此命令，需要时用 `l2dmotion(pos, idle)&l2dexp(pos, none)` 替代。

### 6.4 立绘列填写规则（v3 扩展）/ 快进读档跳过语义

立绘列引用规则（统一解析入口 `VNManager.UpdateCharacter`，详见 §14）：静态 = `角色ID#分组#表情`；L2D = `角色ID#动作#表情`（中段按角色类型解释）。快进不播动作（瞬态）、表情由目标行立绘列经 `ApplyState` 重建；跳过 = 停动作保 Idle。

### 6.5 命令元数据（自动进图编辑器）

`[VNCommandMeta(VNCommandCategory.Performance, ...)]` + `[VNParam]`（pos=SlotCode、motionID/expID=String、**mode=Enum `wait|async`（下拉选取）**、fade=Float）→ 反射自动注册进命令面板与图编辑器，节点化/连线/校验全免费。P4 可选增强：motionID/expID 动态下拉（按目标角色读配置表）。

### 6.6 剧本示例

```
| CharMid     | Text              | Command                                  |
| Mao         | 你好。            |                                          |  ← 默认 Idle + 无表情
| Mao_微笑    | 很高兴见到你！    | l2dmotion(M,wave,wait)                   |  ← 播动作（等播完自动回 Idle）+ 常驻微笑
| Mao_惊讶    | 真的吗？！        | l2dexp(M,surprise)&l2dmotion(M,shock)    |  ← 表情与动作同时叠加

> 示例中 `微笑/surprise/wave/shock` 均为配置表里用户自定义的 ID（如 微笑→exp_01、wave→mtn_01）。
```

---

## 7. 角色编辑器扩展设计（核心 + 包装包）

### 7.1 目标与硬约束

- 目标：角色编辑器「新建」时询问创建**普通立绘角色**还是 **L2D 角色**；编辑器本身能编辑 L2D 角色的全部字段。
- **硬约束**：核心 `VNovelizer.Editor` 程序集**不能引用** `L2DCharacterProfile`（该类型在包装包，引用了 `Live2D.Cubism`）→ 必须走**编辑器插件式扩展点**：核心只定义"角色类型扩展"抽象 + 注册表，L2D 的全部具体 UI / 资产创建在包装包的 Editor 程序集。
- 原则：未装包装包时注册表为空 → 角色编辑器行为与现在**完全一致（零回归）**。

### 7.2 核心侧扩展点（新文件 `Editor/CharacterEditor/CharacterTypeExtension.cs`）

```csharp
/// 角色类型扩展：外部包装包（如 Live2D）把新类型角色接入核心角色编辑器
public interface ICharacterTypeExtension
{
    string TypeName { get; }                    // "Live2D 角色"（新建对话框的卡片标题）
    string TypeDescription { get; }             // 新建对话框的卡片说明
    bool IsTypeOf(CharacterProfile profile);    // 该资产是否属于本类型
    CharacterProfile CreateProfileAsset(string path, string characterId); // 创建资产 + 初始化默认字段
    VisualElement CreateDetailSection(CharacterProfile profile, System.Action onRebuild); // 详情面板专属区块（可 null）
    bool HideSpriteTabs(CharacterProfile profile);   // 是否隐藏"立绘"Tab（L2D = true；头像 Tab 保留给静态头图）
    string GetPreviewPlaceholder(CharacterProfile profile); // MVP 预览占位文案（P4 换实例化预览）
}

public static class CharacterTypeExtensionRegistry
{
    public static void Register(ICharacterTypeExtension ext);   // 包装包在 [InitializeOnLoadMethod] 调用
    public static ICharacterTypeExtension Find(CharacterProfile profile);
    public static IReadOnlyList<ICharacterTypeExtension> All;   // 新建对话框遍历用
}
```

### 7.3 新建流程改造（询问类型）

1. 点击「+ 新建角色」→ 弹出**类型选择对话框**（UIToolkit 卡片弹窗，复用 GalleryTheme）：第一张固定为「普通立绘角色」，其后每张对应一个已注册扩展（当前即「Live2D 角色」，说明：模型 Prefab + 表情/动作 ID 配置）；
2. 选定类型 → 沿用现有 `SaveFilePanelInProject` 流程（文件名 = 角色 ID）→ 普通走现有 `CreateInstance<CharacterProfile>()`；L2D 走 `extension.CreateProfileAsset(path, id)`；
3. **地址注册统一由核心 presenter 负责**（`RegisterCharacterAddress` 移到扩展调用之后，各类型共用）；
4. 创建后 `LoadAll(true)` + 选中（现有逻辑复用——`AssetDatabase.FindAssets("t:CharacterProfile")` 天然命中子类资产，列表零改动）；
5. 列表卡片加**类型徽标**：`Registry.Find(profile)` 命中扩展时显示小标签（如 "L2D"）。

### 7.4 详情面板改造

| 区域 | 普通角色 | L2D 角色 |
|---|---|---|
| Header（ID / 删除） | 共用 | 共用 |
| 基础配置卡片（SpeakerBox / HeadFrame / scale / offset） | 共用 | **共用**（需求①：L2D 同样要配置这些） |
| 预览区 | Sprite 预览 | 扩展 `GetPreviewPlaceholder`（MVP 占位；P4 换隐藏场景实例化预览） |
| Tab 区 | [立绘 \| 头像] | [**L2D 配置（扩展区块）** \| 头像]（`HideSpriteTabs=true`） |

L2D 专属区块（包装包实现，`CreateDetailSection` 渲染）：
- 三个 ObjectField：模型 Prefab / ExpressionList 表情总表 / FadeMotionList 动作总表；
- 表情列表：每行 [ID 文本框] + [.exp3 资产 ObjectField] + [×]，可增删行，支持拖 .exp3 直接入行；
- 动作列表：每行 [ID] + [.anim ObjectField] + [Kind 下拉 Idle/Motion] + [Loop 勾选] + [fade 覆写]，支持拖 .anim 入行；
- 实时校验（§4.4）：ID 重复/为空、Data 不在总表内、Clip 缺 InstanceId 事件、Idle 多条等红字提示。

### 7.5 插件内 L2D 编辑器目录结构（P3 实施）

```
Editor/L2D/
├── VNovelizer.L2D.Editor.asmdef     // defineConstraints: VN_L2D；引用 VNovelizer.Editor + VNovelizer.L2D.Runtime + Live2D.Cubism
├── L2DCharacterTypeExtension.cs     // 实现 ICharacterTypeExtension + [InitializeOnLoadMethod] 注册
└── L2DDetailSectionView.cs          // L2D 专属区块 UI + 校验
```

类型选择对话框由核心实现（遍历注册表生成卡片），L2D 目录无需参与。

### 7.6 边界与兼容

| 场景 | 行为 |
|---|---|
| 未装 L2D 包装包 | 注册表空 → 新建只有普通角色，编辑器 100% 现有行为 |
| 卸载 L2D 包但已有 L2D 资产 | 资产 missing script → `LoadAssetAtPath<CharacterProfile>` 返回 null → 列表不显示（现有容错）；可选加"检测到缺失类型的角色资产"提示 |
| 未来第三种角色类型（Spine 等） | 注册表天然支持多扩展并存，核心零改动 |
| Duplicate / Delete / Rename | 全类型通用（只操作基类字段与 AssetDatabase），无需扩展钩子 |

---

## 8. 用户使用流程

```
【配置阶段（一次性）】
1. 导入 Live2D 模型 → SDK 生成：模型 Prefab、.exp3 表情资产、.anim 动作、.fade、
   .expressionList 表情总表、.fadeMotionList 动作 fade 总表
2. 角色编辑器 →「+ 新建角色」→ 类型对话框选「Live2D 角色」→ 选保存位置（文件名=角色ID）
3. 填基础字段（ID / 姓名框 / 头像框 / 缩放 / 偏移）—— 与普通角色完全一致
4. 拖入三个官方资产：模型 Prefab + ExpressionList 表情总表 + FadeMotionList 动作总表
5. 配置表情列表：每行 = [表情ID] + [.exp3 资产]（如 微笑 → exp_01.exp3.asset）
6. 配置动作列表：每行 = [动作ID] + [.anim] + [Idle/Motion] + [Loop]（如 待机 → mtn_01 选 Idle+Loop）
7. 保存即完成（无"生成 Controller"步骤）

【写剧本阶段】
- 立绘列写 <角色ID>_<表情ID>（情绪串 = 表情 ID）
- Command 列写 l2dmotion / l2dexp（图编辑器里直接拖节点）

【运行阶段】
- 试玩即所见；快进 / 读档 / 跳过行为见 §6.4
```

> 开发联调模型 = SDK 自带 `Samples/Models/Mao`（8 表情 8 动作、两个列表已自动赋值，可直接开测）。其 8 个动作的 motion3 元数据均带 `Loop:true`，任选一个（如 `mtn_01`）配置为 Kind=Idle 即可测"播完回 Idle"。

---

## 9. v2 → v3 变更对照

| 维度 | v2（自建状态机） | v3（官方直封） |
|---|---|---|
| 播放内核 | 自生成 AnimatorController + Trigger 参数 | SDK OW 组件：CubismMotionController 多层 + 优先级 |
| "播完回 Idle" | Exit 过渡回 Idle 状态 | 官方多层混合：动作层 fade out 露出垫底 Idle 层 |
| 打断语义 | AnyState 过渡 | 官方同层 `PriorityForce` 替换（§5.3 已决策） |
| Motion×Exp 重叠 | Animator 双 Layer | 官方双管线（Motion 管线 + Expression 管线）天然独立 |
| 动画 fade | 生成器写过渡时长 | SDK `CubismFadeMotionData.FadeIn/OutTime`（可克隆覆写） |
| 表情 API | 自定义 StateMachineBehaviour | 官方 `CurrentExpressionIndex` |
| 编辑器 | **需要写 AnimatorController 生成器**（工作量大） | 角色编辑器插件式扩展点（§7）+ L2D 详情区块 |
| 风险 | 与官方组件互斥（SDK 源码 L285 拒绝 controller） | 完全在官方语义内（无自建状态机、无偏离扩展） |

---

## 10. 健壮性与边界

| 风险 / 边界 | 处理 |
|---|---|
| 未装 Live2D SDK 的项目引用伴生包 | asmdef `versionDefines` 探测 `com.live2d.cubismsdk`，代码包在 `#if VN_L2D`（同核心对 Localization 先例） |
| prefab 上 Animator 意外挂了 controller | L2DActor 初始化时 `animator.runtimeAnimatorController = null` 强制清空（官方要求无 controller） |
| `ExpressionList`/`FadeMotionList` 官方导入异常为 null（如旧 miku 实测） | 封装层注入序列 §5.1 兜底；配置未拖入则校验拦截 |
| **模型 prefab 缺 `CubismMotionController` / `CubismFadeController`**（SDK 5 R4.2 部分示例模型如 Mao 导入时不会自动生成 MotionController） | `L2DActor.ConfigureOfficialComponents` 缺啥补啥（AddComponent 自动补齐）—避免 l2dmotion 静默失败 |
| 动作 clip 缺 `InstanceId` 事件 | Editor 校验拦截（§4.4 #3），运行时报 SDK 原生错误 |
| 动作连续触发 | 同层 `PriorityForce` 替换（§5.3 已决策）：后动打断前动、官方交叉淡化，无冲突场景 |
| 模型加载 / 初始化失败 | 槽位留空 + 错误日志，不崩行不崩游戏 |
| 表情 / 动作 ID 查无 | 警告 + 跳过，其余命令照常 |
| 首帧注入前的 SDK 报错日志 | 无害单条（`CubismFadeMotionList doesn't set`），注释说明可忽略 |
| `SetFlip` 镜像 | 需材质 Cull=Off（Inspector 提示） |
| 渲染管线 | Cubism 自带 shader 兼容 Built-in；URP 需 SDK URP 变体（Inspector 提示） |

---

## 11. 分阶段实施计划

| 阶段 | 内容 | 风险 |
|---|---|---|
| **P1 核心扩展点** | `IsSpriteBased` + `ActorAppearance.profile` + 演员工厂钩子 + `IActor.Accepts`（3 处通用改动，存量立绘回归） | 低 |
| **P2 L2D 运行时（插件内）** | `Runtime/Scripts/VNovelizer/L2D/`：Profile/Entries + L2DActor（注入序列 §5.1 + 5 方法 API + 缓存）+ L2DBridge + l2dmotion/l2dexp 两条命令 + 激活器 `L2DSupportActivator`（VN_L2D 符号管理） | 低（官方 API 已验证） |
| **P3 L2D 编辑器（插件内）** | 核心：`CharacterTypeExtension` 扩展点 + 类型选择对话框 + 详情面板分支 + 列表徽标；`Editor/L2D/`：`L2DCharacterTypeExtension` + L2D 详情区块（3 资产拖拽 + 列表编辑 + §4.4 校验） | 低 |
| **P4 打磨** | 专用预览（隐藏场景实例化 + 专用相机 + RenderTexture）、motionID/expID 动态下拉、FadeTime 克隆覆写、l2dreset 命令、多 Idle 轮换 | 中（预览为真难点） |

> P2 依赖最小：官方 API 全部已验证存在，可直接在 Dev1 用 Mao 模型联调（8 动作均带 Loop 元数据，选 mtn_01 配为 Idle 即可测"播完回 Idle"）。

---

## 12. 决策记录（2026-09-11 全部拍板）

| # | 决策点 | 结论 |
|---|---|---|
| 1 | `l2dmotion` mode 非阻塞词 | **`async`**（`wait` 阻塞 / `async` 立即返回），`[VNParam]` Enum `wait\|async` 元数据 → 图编辑器下拉选取 |
| 2 | 包装形态 | **放在插件内**（派生功能，用户须手动导入 SDK+模型）：`Runtime/Scripts/VNovelizer/L2D/` + `Editor/L2D/`，asmdef `defineConstraints: VN_L2D` + 激活器管理符号（§3） |
| 3 | Idle 归属 | Kind=Idle 条目**恰好 1 个、不允许缺省**、必须 Loop；新建 L2D 角色自动生成空 Idle 条目占位 |
| 4 | 动作打断 | 同层 `PriorityForce` 替换、Kind 两层 `Idle/Motion`（§5.3） |
| 5 | `l2dmotion` 参数 | 四参数严格位置语义：`pos, motionID, mode, fade`（§6.1） |
| 6 | `l2dreset` | 缓到 P4（§6.3） |
| 7 | 表情语义 | 保持型（官方 `CurrentExpressionIndex` 天然如此） |
| 8 | 头像 | 静态贴图（MVP）；模型 RenderTexture 缓到 P4 |
| 9 | 核心改动 | 运行时 3 处通用扩展点（§3）+ 编辑器扩展点 `ICharacterTypeExtension`（§7.2）——已确认，**进入实施** |

> 实施顺序：P1 核心运行时扩展点 → P2 插件内 L2D 运行时 + 激活器 → P3 编辑器扩展（核心 + L2D 目录）→ P4 打磨。

---

## 13. 实施记录（2026-09-11，P1-P3 已完成）

### 已落地文件

**P1 核心运行时扩展点（VNovelizer.Runtime，无 Live2D 依赖）**
| 文件 | 改动 |
|---|---|
| `Core/Data/CharacterProfile.cs` | + `virtual bool IsSpriteBased => true` |
| `Core/Theater/ActorTypes.cs` | `ActorAppearance` + `profile` 字段 + `ActorAppearance(id, profile)` 构造 |
| `Core/Theater/IActor.cs` | + `Accepts(ActorAppearance)` + `Dispose()` |
| `Core/Theater/ActorFactory.cs`（新） | `IActorFactory` + `DefaultActorFactory` |
| `Core/Theater/TheaterManager.cs` | 静态 `ActorFactory` 属性；`EnsureActor` 走工厂；+ `RecreateActor`（外观不匹配换演员）；`OnShowCharacter`/`ResolveAppearance` 支持 `!IsSpriteBased` 动态外观；`SetAppearance`/`ApplyState` 换装逻辑；`RemoveActor`/`ClearTheater` 改用接口 `Dispose()` |
| `Core/Theater/MeshActor.cs` | 实现 `Accepts`（只接 sprite/texture） |

**P2 插件内 L2D 运行时（`Runtime/Scripts/VNovelizer/L2D/`，asmdef `defineConstraints: VN_L2D`）**
| 文件 | 内容 |
|---|---|
| `VNovelizer.L2D.Runtime.asmdef` | 引用 VNovelizer.Runtime + Live2D.Cubism，门控 VN_L2D |
| `L2DCharacterProfile.cs` | Profile + `L2DExpressionEntry`/`L2DMotionEntry`/`L2DMotionKind{Idle,Motion}` + 查询 API |
| `IL2DControllable.cs` | 命令层唯一控制面（5 方法） |
| `L2DActor.cs` | 官方组件注入序列 §5.1 + 两层 PriorityForce 动作模型 + `CurrentExpressionIndex` 表情 + IActor 全实现 |
| `L2DBridge.cs` | `[RuntimeInitializeOnLoadMethod]` 安装 `L2DActorFactory` |
| `Commands/L2DMotionCommand.cs` | `l2dmotion(pos, motionID[, mode[, fade]])`，mode=wait/async 下拉元数据，严格四参解析 |
| `Commands/L2DExpCommand.cs` | `l2dexp(pos, expID[, fade])`，none 清除，保持型 |

**P2 激活器（VNovelizer.Editor，恒编译）**
| 文件 | 内容 |
|---|---|
| `Editor/L2DSupportActivator.cs` | 检测 `Assets/Live2D/Cubism` / `Live2D.Cubism` asmdef → 管理全部 BuildTargetGroup 的 VN_L2D 符号（projectChanged 防抖） |

**P3 编辑器扩展**
| 文件 | 内容 |
|---|---|
| `Editor/CharacterEditor/CharacterTypeExtension.cs`（新） | `ICharacterTypeExtension` + `CharacterTypeExtensionRegistry` |
| `Editor/CharacterEditor/CharacterEditorPresenter.cs` | `CreateNewCharacter(ICharacterTypeExtension)` 类型分发（扩展创建实例、核心统一落盘+地址注册） |
| `Editor/CharacterEditor/CharacterListPanelView.cs` | 新建按钮 → 类型选择对话框（普通角色 + 注册扩展卡片）；卡片类型徽标 + 无立绘占位显示类型名 |
| `Editor/CharacterEditor/CharacterDetailPanelView.cs` | 详情面板扩展分支：预览占位、立绘 Tab 替换为扩展专属区块、头像 Tab 保留 |
| `Editor/L2D/VNovelizer.L2D.Editor.asmdef` | 引用 VNovelizer.Editor + L2D.Runtime + Live2D.Cubism，门控 VN_L2D |
| `Editor/L2D/L2DCharacterTypeExtension.cs` | 扩展实现：TypeName/IsTypeOf/CreateProfileAsset（自带空 Idle 条目）/详情区块 |
| `Editor/L2D/L2DDetailSectionView.cs` | 3 资产拖拽 + 表情/动作列表 + Kind 下拉/Loop 勾选 + §4.4 实时校验 + 自动填 ID |

### 待验证（Dev1 工程，Mao 模型）
1. 打开 Dev1 → 激活器自动加 VN_L2D → 编译通过（L2D 两程序集参与编译）；
2. 角色编辑器 → 新建 → 选「Live2D 角色」→ 拖 Mao prefab / expressionList / fadeMotionList → 配置 ID（如 微笑→exp_01、待机→mtn_01 选 Idle+Loop、挥手→mtn_02 选 Motion）；
3. 剧本立绘列写 `Mao_微笑`、Command 写 `l2dmotion(M,挥手,wait)` → 试玩验证登台/表情/动作/回 Idle/快进/读档。

### 未实施（P4 清单）
l2dreset 命令、FadeTime 覆写（克隆资产）、motionID/expID 动态下拉、模型实例化预览、多 Idle 轮换、按 CharacterID 实例缓存。

---

## 14. 立绘列填写规则（2026-09-11 实施：解析入口 VNManager.UpdateCharacter）

### 14.1 统一规则（中段语义按角色类型解释）

| 写法 | 静态立绘 | L2D 角色 |
|---|---|---|
| `角色ID#中段#表情` | 中段 = 分组名（换装） | 中段 = **动作 ID**（登台播放，**默认非阻塞**，播完自动回 Idle） |
| `角色ID#表情` | 分组缺省 = Default | 只切表情、不播动作 |
| `角色ID`（裸 ID） | ❌ 报错防呆（无表情定义） | ✅ 登台不改表情、不播动作 |
| 旧格式 `角色ID_表情` | ❌ 报错 + 迁移指引 | ❌ 报错 + 迁移指引 |

- 特殊动作值：`idle` = 停动作回待机；`Default`（或空）= 不播动作（兼容分组写法习惯）；
- 三段式 L2D 中段动作 ID 查无 → 警告（列出可用 ID）但不阻止登台，表情照常应用。

### 14.2 关键实现：动作瞬态与存档解耦

- 立绘列动作**不进存档、不随读档重播**：`ActorAppearance.showMotionId` 字段只由
  `TheaterManager.OnShowCharacter` 现场路径填充；读档/快进重建走 `ResolveAppearance`
  不填充该字段 → `L2DActor.SetAppearance` 只在 showMotionId 非空时播动作；
- `appearance.id` 对 L2D 归一为 `角色ID#Default#表情`（中段动作 ID 不进 id），
  存档 appearance 字符串稳定且仅承载表情语义。

### 14.3 示例

```
| CharMid         | 效果 |
| L2DMao#挥手#微笑 | 登台 + 播"挥手"动作（非阻塞，播完回 Idle）+ 常驻微笑（推荐写法）|
| L2DMao#微笑      | 登台 + 常驻微笑（无动作）|
| L2DMao           | 登台、保持当前表情（纯台词行）|
| L2DMao#idle#     | 停动作回待机（收循环动作）|
| L2DMao#Default#惊讶 | 等价于 L2DMao#惊讶（兼容分组写法）|
| Amy#uniform#Smile | 静态完整引用（不变）|
| Amy#Smile        | 静态 Default 分组（新支持）|
```
