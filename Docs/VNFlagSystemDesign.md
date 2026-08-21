# VN Flag 系统扩展设计

> 本文档定义 VNovelizer Flag（游戏标志/变量）系统的完整扩展方案：作用域分离、变量注册表、条件分支命令族、编辑器工具链。
> 该方案对应《VNRefactoringPlan》中"核心差距 #1 条件分支与逻辑系统"与"重要增强 #10 好感度系统"的落地设计。
> 参考 Ren'Py（变量 + 条件跳转）以及成熟 VN 引擎的 Custom State 条件表达式的实践。

---

## 目录

1. [现状分析与问题清单](#1-现状分析与问题清单)
2. [设计目标](#2-设计目标)
3. [作用域模型（Scope）](#3-作用域模型scope)
4. [FlagRegistry 变量注册表](#4-flagregistry-变量注册表)
5. [条件表达式规范](#5-条件表达式规范)
6. [命令族设计](#6-命令族设计)
7. [运行时架构](#7-运行时架构)
8. [编辑器工具链](#8-编辑器工具链)
9. [兼容性策略与前置修复](#9-兼容性策略与前置修复)
10. [完整示例：好感度分支](#10-完整示例好感度分支)
11. [实施路线图](#11-实施路线图)

---

## 1. 现状分析与问题清单

### 1.1 现有架构（五层）

| 层 | 文件 | 现状 |
|------|------|------|
| 数据层 | `Runtime/.../Core/Data/GlobalData.cs` | 三个字典：`Flags`(bool) / `IntFlags`(int) / `StringFlags`(string) |
| 持久化层 | `Runtime/.../Core/Managers/GlobalDataManager.cs` | Set 即写入 `persistentDataPath/global_data.json`（LitJson） |
| 命令层 | `Commands/SetBoolFlagCommand.cs` 等 3 个 | `setboolflag` / `setintflag` / `setstringflag` |
| 存档层 | `Data/SaveData.cs` + `VNManager.cs:1302/1747` | 三字典随存档快照保存，读档时**整体覆盖**全局值 |
| API 层 | `Utils/API.cs:219-281` | `VNAPI.Set/GetXxxFlag` 静态门面 |

### 1.2 问题清单

| # | 问题 | 严重性 | 说明 |
|---|------|:---:|------|
| P1 | **只写不读** | 严重 | `GetXxxFlag` 除 API 门面外无任何调用者；`jump` 无条件跳转，`choice` 无法按 flag 显隐——引擎内部无任何剧本层消费 flag |
| P2 | **`SetBoolFlagCommand` 未重写 `Simulate()`** | 严重 | 基类默认 `Simulate` 为空（`VNCommand.cs:41`）。`FastForwardToLine`（读档/跳行/开局）经过的行中 `setboolflag` 不生效，而 int/string 版本生效，行为不一致 |
| P3 | **`JumpCommand`/`LoadScriptCommand` 未重写 `Simulate()`** | 严重 | 快进路径忽略跳转与剧本切换：① 分支状态错误；② 线性扫描会把**被 jump 跳过的行**也 Simulate 一遍，flag 状态与实际游玩路径不符 |
| P4 | **无作用域概念** | 重要 | flag 同时存在于全局 `global_data.json` 与每个存档槽，读旧档会把好感度一起回退——不符合好感度系统的预期 |
| P5 | **无变量注册表** | 重要 | 拼写错误（`Amy_Favro`）只有运行到该行才报错；无默认值；无类型元数据供静态校验 |
| P6 | **无算术能力** | 增强 | `setintflag` 只能绝对赋值，好感度增减需要"读取-计算-写回"，Excel 无法表达 |

---

## 2. 设计目标

| 原则 | 说明 |
|------|------|
| **注册表先行** | 所有 flag 先在 FlagRegistry 中声明（名称/类型/作用域/默认值），剧本中引用 |
| **类型不出现在命令名中** | 条件命令统一为 `jumpif(cond, targetId)`，类型由注册表推断，避免 `jumpifint/jumpifbool/jumpifstring` 命令爆炸 |
| **Simulate 与 Execute 语义一致** | 条件判断/跳转在快进路径必须同样生效，读档后分支状态可精确重建 |
| **存量剧本零破坏** | 现有 `setboolflag/setintflag/setstringflag/jump/choice` 语法 100% 兼容 |
| **错误前置** | Excel→CSV 转换时即校验 flag 引用与跳转目标，而非等到运行时 |

---

## 3. 作用域模型（Scope）

### 3.1 三种作用域

| Scope | 存储位置 | 生命周期 | 典型用途 |
|-------|---------|---------|---------|
| `Global` | `global_data.json` | 跨存档、跨周目持久 | **好感度**、累计解锁、全流程标记、玩家自定义名 |
| `Save` | `SaveData` 快照 | 随存档保存/读档回退 | 章节进度、剧情分支标记、章节内变量 |
| `Temp` | 仅内存 | 本次游戏运行 | 临时计算、行内状态（v2 可选，首期不实现） |

### 3.2 路由规则

```
写入/读取 flag 时，按注册表中该 flag 的 Scope 路由：
  Global → GlobalDataManager（现有 global_data.json 路径，不变）
  Save   → SaveData 快照（随存档序列化，读档恢复）
```

- **未注册的 flag**：默认视为 `Save` 作用域（与现状最接近），运行时输出警告（可配置为报错，见 §9）。
- **好感度必须声明为 `Global`**，否则读旧档会被回退。
- 读档时仅用存档中的 `Save` 快照覆盖对应作用域，`Global` 值不受影响。

---

## 4. FlagRegistry 变量注册表

### 4.1 数据结构

```csharp
// Runtime/Scripts/VNovelizer/Core/Data/FlagRegistry.cs
public enum FlagType  { Bool, Int, Float, String }
public enum FlagScope { Global, Save }

[CreateAssetMenu(menuName = "VNovelizer/FlagRegistry")]
public class FlagRegistry : ScriptableObject
{
    [System.Serializable]
    public class FlagDefinition
    {
        public string Name;          // Amy_Favor（唯一键，建议 PascalCase_下划线风格）
        public FlagType Type;        // Int
        public FlagScope Scope;      // Global
        public string DefaultValue;  // "0"（统一存字符串，按 Type 解析）
        public string Group;         // 好感度（编辑器分组显示用）
        public string Comment;       // 备注（用途说明）
    }

    public List<FlagDefinition> Definitions;
}
```

### 4.2 注册表职责

| 职责 | 说明 |
|------|------|
| **类型推断** | `jumpif` 的条件表达式按注册表中 flag 的 Type 选择比较方式 |
| **默认值初始化** | 新游戏（`StartGame`）时按注册表初始化所有 flag；快进前也先复位到默认值 |
| **静态校验依据** | Excel→CSV 转换时校验剧本中引用的 flag 是否已注册、类型是否匹配 |
| **编辑器数据源** | Flag 编辑器窗口的增删改查列表 |

### 4.3 校验规则

| 规则 | 级别 |
|------|------|
| flag 名重复 | 报错（阻止保存） |
| 同名不同类型 | 报错 |
| 名称含空格或 `,()&|!` 等保留字符 | 报错 |
| 剧本引用未注册 flag | 默认警告，`VNProjectConfig.StrictFlagValidation = true` 时报错 |
| `jumpif` 条件中 operator 与类型不匹配（如 bool 用 `>=`） | 报错（转换时）/ Error（运行时） |
| `jumpif` 目标行 ID 不存在 | 报错（转换时）/ Error（运行时，沿用 `JumpCommand` 报错行为） |

### 4.4 注册表资产位置

由 `VNProjectConfig` 引用（新增 `FlagRegistryAsset` 字段），Setup Wizard 在 `Assets/Resources/VNovelizerRes/` 下创建默认实例 `VNFlagRegistry.asset`。运行时通过 `Resources.Load` 加载，允许不存在（进入兼容模式，全部按未注册处理）。

---

## 5. 条件表达式规范

### 5.1 语法

```
条件     := 布尔项 | 比较式
布尔项   := 标识符 | '!' 标识符
比较式   := 标识符 op 值
op       := '>=' | '<=' | '==' | '!=' | '>' | '<'
值       := 数字 | true | false | 字符串字面量
字符串字面量 := 双引号或单引号包裹（含逗号时必须包裹）
```

### 5.2 示例

| 条件 | 说明 |
|------|------|
| `Amy_Favor >= 50` | int/float 比较 |
| `Met_Amy` | bool 直接写名 = 判 true |
| `!Met_Amy` | bool 取反 |
| `PlayerName == "Alice"` | string 相等（仅支持 `==` / `!=`） |
| `Amy_Favor != 0` | int 不等 |

### 5.3 类型与 operator 约束

| FlagType | 允许的 operator |
|----------|----------------|
| Bool | 无 operator（直接判定）；不允许 `==`（如确需比较布尔字面量，用两个 flag 表达） |
| Int / Float | `> < >= <= == !=` |
| String | `== !=`（值含逗号必须用引号包裹） |

### 5.4 设计约束（与 Excel/命令系统的边界）

| 约束 | 原因 |
|------|------|
| **不支持 `&&` / `\|\|` 组合条件** | `&` 是命令分隔符。需要组合就写多条 `jumpif` 链（见 §10 示例） |
| **条件不得以 `=` `+` `-` `@` 开头** | Excel 会将其识别为公式导致单元格损坏 |
| **值中的字符串用引号包裹** | 与 `setstringflag` 现有引号约定一致 |
| **flag 名大小写敏感** | 与 `GlobalData` 字典现状一致 |
| **操作符匹配顺序：先双字符后单字符** | `>=` 先于 `>`，避免截断误解析 |

---

## 6. 命令族设计

### 6.1 总表

| 命令 | 语法 | 状态 | 说明 |
|------|------|:---:|------|
| `setboolflag` | `setboolflag(name)` / `setboolflag(name, false)` | 已有→增强 | 补 `Simulate` 重写（修复 P2） |
| `setintflag` | `setintflag(name, 50)` | 已有→增强 | **新增相对运算**：`setintflag(name, +10)` 支持 `+ - * /` 前缀 |
| `setstringflag` | `setstringflag(name, "Alice")` | 已有 | 不变 |
| `addintflag` | `addintflag(name, 10)` | 新增（可选） | 语义化加法，与 `setintflag(name,+10)` 等价，二选一使用 |
| `toggleflag` | `toggleflag(name)` | 新增（可选） | bool 翻转 |
| `jump` | `jump(targetId)` | 已有→修复 | 补 `Simulate` 重写（修复 P3） |
| `jumpif` | `jumpif(cond, targetId)` | **新增（核心）** | 条件真 → 跳转；假 → 继续 |
| `jumpifnot` | `jumpifnot(cond, targetId)` | 新增 | 条件假 → 跳转（比 Excel 里写 `!` 更醒目） |
| `loadscriptif` | `loadscriptif(cond, scriptName[, startID])` | **新增（核心）** | 条件真 → 加载剧本（等价 `loadscript(scriptName, startID)`）；假 → 继续当前剧本下一行 |
| `loadscriptifnot` | `loadscriptifnot(cond, scriptName[, startID])` | 新增（可选） | 与 `jumpifnot` 对称的条件加载 |
| `choice`（扩展） | `choice(text \| cmd \| if:cond)` | 已有→扩展 | 第三个 `\|` 分支可选，条件不满足则**隐藏该选项** |
| `switchint` | `switchint(name, 50:A, 80:B, _:C)` | v2 | 数值分档跳转，多结局好感度场景 |
| `randomint` | `randomint(dest, min, max)` | v2 | 随机数写入 int flag |

### 6.2 `setintflag` 相对运算语义

```
setintflag(Amy_Favor, +20)    → 当前值 + 20
setintflag(Amy_Favor, -10)    → 当前值 - 10
setintflag(Amy_Favor, *2)     → 当前值 * 2
setintflag(Amy_Favor, /2)     → 当前值 / 2（整数除法）
setintflag(Amy_Favor, 50)     → 绝对赋值（现状，不变）
```

- 目标 flag 未注册或不存在时：以 0 为基准做相对运算，并输出警告。
- Excel 注意：`+20` 出现在**参数内**（命令名开头）不会触发 Excel 公式识别，安全。

### 6.3 `jumpif` / `jumpifnot` 语义

```
jumpif(Amy_Favor >= 50, AmyGoodRoute)
  条件真 → 等价 jump(AmyGoodRoute)（跳转立即生效；旧 & 语法为顺序执行，同行后续命令仍会执行——与 jump 现状行为完全一致）
  条件假 → 本命令无操作，继续执行同行后续命令（如再跟一条 jump 兜底）
```

- `Execute`（正常播放）与 `Simulate`（快进）**行为完全一致**（见 §7.3）。
- 多条件链式写法（伪 AND）：

```
jumpif(Amy_Favor >= 50, _CHK1)     # 不满足直接兜底
jump(Met_Boss, _CHK1 ...)          # 链式写法示例见 §10
```

实际推荐的链式模式：用连续 `jumpif` + 中间标签逐级过滤（见 §10 完整示例）。

### 6.4 `choice` 条件选项

```
choice(送她花 | setintflag(Amy_Favor,+20) & jump(FL_A) | if:Amy_Favor >= 0)
choice(送钻石 | setintflag(Amy_Favor,+100) & jump(FL_B) | if:Amy_Favor >= 50)
choice(离开 | jump(FL_C))
```

- 第三个 `|` 段以 `if:` 开头时视为显示条件；条件不满足的选项**不显示**（而非置灰）。
- 若过滤后所有选项均隐藏：运行时报错（剧本设计缺陷，转换时检测到"全部带条件"的 choice 组合给出警告）。
- 与本地化 `@loc:` 前缀共存：`@loc:` 作用于第一段文本，互不干扰。

### 6.5 `loadscriptif` / `loadscriptifnot` 条件加载剧本

语法与 `jumpif` 的条件规则完全一致（复用 §5 条件表达式与 `ConditionParser`），只是跳转目标从"本剧本行 ID"变为"剧本 + 可选起始行"：

```
loadscriptif(Amy_Favor >= 80, Chapter2A)          # 条件真 → 加载 Chapter2A，从头播放
loadscriptif(Amy_Favor >= 50, Chapter2B, Scene_030) # 条件真 → 加载 Chapter2B 并快进到 Scene_030
loadscriptifnot(Met_Boss, Chapter2C)               # 条件假 → 加载 Chapter2C
```

**语义规则：**

| 规则 | 说明 |
|------|------|
| 条件真 | 等价于执行 `loadscript(scriptName, startID)`：解析新剧本 → `SetScriptData`（当前行索引重置）→ 有 startID 则快进到目标行。**该行后续命令不再执行**（与 `jumpif` 一致） |
| 条件假 | 本命令无操作，继续执行同行后续命令——典型用法是连续多条 `loadscriptif` + 末尾一条无条件 `loadscript` 兜底 |
| startID 缺省 | 从新剧本第 0 行开始（沿用 `LoadScriptCommand` 现有行为，含 startID 不存在时警告并从头开始的容错） |
| 参数分割 | 采用**引号感知的逗号分割**（与 `ConditionParser` 的字符串规则一致）：`loadscriptif(PlayerName == "Alice, B", Chapter2A)` 中含逗号的字符串值必须用引号包裹，分割时不能切断 |
| 跨剧本跳转环防护 | 快进时若剧本 A → B → A 形成环，按 §7.3 防死循环规则报错终止 |

**与 `jumpif` 的适用场景区分：**

| 命令 | 跳转目标 | 适用场景 |
|------|---------|---------|
| `jumpif` | 本剧本内的行 ID | 单剧本内的分支合流 |
| `loadscriptif` | 其他剧本（+ 起始行） | **章节级分支**：按好感度/累计 flag 进入不同章节文件，避免单剧本无限膨胀 |

---

## 7. 运行时架构

### 7.1 新增组件

```
Runtime/Scripts/VNovelizer/Core/
├── Data/
│   ├── FlagRegistry.cs            # 注册表 SO（§4）
│   └── SaveData.cs                # 扩展：Save 作用域存储（可沿用现有三字典）
├── Managers/
│   └── FlagService.cs             # ★ 新增：flag 读写统一入口（作用域路由）
├── Commands/
│   ├── ConditionParser.cs         # ★ 新增：条件表达式解析（纯静态函数）
│   ├── VNScriptCommands/
│   │   ├── JumpIfCommand.cs       # ★ 新增
│   │   ├── JumpIfNotCommand.cs    # ★ 新增
│   │   ├── LoadScriptIfCommand.cs     # ★ 新增
│   │   └── LoadScriptIfNotCommand.cs  # ★ 新增
│   └── ...
```

### 7.2 `FlagService`（作用域路由中枢）

```csharp
// 纯 C# 类，BaseManager<FlagService> 单例
public class FlagService : BaseManager<FlagService>
{
    private FlagRegistry registry;                 // 允许为 null（兼容模式）

    public void Init(FlagRegistry registry);       // StartGame / 读档前调用
    public void ResetToDefaults(bool includeSaveScope); // 新游戏/快进前复位
    public bool   GetBool(string name);
    public int    GetInt(string name);
    public float  GetFloat(string name);
    public string GetString(string name);
    public void Set<T>(string name, T value);      // 按 Scope 路由写入
    // 未注册 → 按 Save 作用域处理 + 警告
}
```

现有 `GlobalDataManager.SetBoolFlag` 等方法保留为兼容入口，内部委托 `FlagService`；`VNAPI` 门面签名不变。

### 7.3 `Simulate` 跳转机制（`PendingJumpIndex` + `PendingScriptSwitch`）

**核心机制**：`VNManager.FastForwardToLine` 循环中，每行 `SimulateCommands` 完成后检查跳转请求。请求分两种：

- **`PendingJumpIndex`**（本剧本跳转）：`jump` / `jumpif` / `jumpifnot` 设置，值为目标行索引。
- **`PendingScriptSwitch`**（跨剧本切换）：`loadscript` / `loadscriptif` / `loadscriptifnot` 设置，值为 `(scriptName, startID)`。

```
FastForwardToLine(targetIndex):
  for i in 0..targetIndex:
      PendingJumpIndex = null                    # 每行复位
      PendingScriptSwitch = null
      SimulateCommands(line[i].Command)          # 各跳转命令的 Simulate 判定后写入上述字段
      if PendingScriptSwitch != null:            # 跨剧本优先：切换数据源后重定向循环
          scriptData = ScriptParser.Parse(scriptName)
          SetScriptData(...)                     # 行索引重置
          i = ResolveStartIndex(startID) - 1     # 解析 startID（缺省/不存在则回到容错语义）
          continue
      if PendingJumpIndex != null:
          i = PendingJumpIndex                   # 快进指针跳转
          continue
      ...（现有状态累积逻辑）
```

- `JumpCommand.Simulate` / `JumpIfCommand.Simulate` / `JumpIfNotCommand.Simulate` 统一通过设置 `PendingJumpIndex` 生效；`LoadScriptCommand.Simulate` / `LoadScriptIfCommand.Simulate` / `LoadScriptIfNotCommand.Simulate` 通过设置 `PendingScriptSwitch` 生效。
- **注意**：`LoadScriptCommand` 现状同样未重写 `Simulate`（与 P3 同源问题），快进路径遇到 `loadscript` 会静默丢失剧本切换——纳入 Fix-2 一并修复。
- `Execute` 路径（正常播放）行为不变：`jump`/`jumpif` 直接调用 `FastForwardToLine(target, ignoreChoice: true)` + `CurrentLineIndex = target`（沿用 `JumpCommand.Execute` 现有写法）；`loadscript`/`loadscriptif` 沿用 `LoadScriptCommand.Execute` 现有写法（解析 → 注入 → 快进）。
- 防死循环：快进时记录已访问的 `(剧本名, 行ID)` 集合，同一位置被二次进入时报告错误并终止快进——**含跨剧本环**（A → B → A）。

### 7.4 `ConditionParser`（纯静态、可测试）

```csharp
public static class ConditionParser
{
    // 输入原始条件串，输出结构化条件；解析失败抛出带位置信息的异常
    public static Condition Parse(string condition);
    // 求值：从 FlagService 读取实际值比较
    public static bool Evaluate(Condition cond, FlagService flags);

    // Editor 静态校验复用：Parse 成功 + flag 已注册 + 类型匹配 + 目标行存在
}
```

- 解析顺序：先双字符 operator（`>= <= == !=`），后单字符（`> <`）。
- 不依赖 Unity 生命周期，Editor 窗口与转换校验可直接复用。

---

## 8. 编辑器工具链

### 8.1 Flag 编辑器窗口（`VNovelizer → Flag 编辑器`）

参照 `CharacterEditor` 的 UIElements 模式：

| 功能 | 说明 |
|------|------|
| 列表管理 | 按 Group 分组显示所有 FlagDefinition，支持增删改、按名称/类型筛选 |
| 校验面板 | 显示重复名/非法字符/类型冲突；一键定位 |
| **Play Mode 实时调试** | Play 模式下直接读写 `FlagService` 单例当前值，**临时修改立即生效**（满足运行时调参需求）；显示 Scope 归属 |
| 引用统计 | 扫描所有 CSV，统计每个 flag 被哪些剧本/多少行引用；反向列出"剧本引用了但未注册"的 flag |

### 8.2 Excel→CSV 转换静态校验

钩子：`ScriptManager` 的"转换"按钮流程（`ExcelToCSVConverter`）内：

| 校验 | 时机 | 级别 |
|------|------|------|
| `jumpif/jumpifnot/loadscriptif/loadscriptifnot` 条件可解析 | 转换时 | 错误 |
| 条件中 flag 已注册且类型匹配 | 转换时 | 警告（严格模式→错误） |
| `jumpif/jump` 目标行 ID 存在于本剧本 | 转换时 | 错误 |
| `loadscriptif/loadscript` 目标剧本 CSV 存在且可解析 | 转换时 | 错误 |
| `loadscriptif/loadscript` 指定 startID 时存在于目标剧本 | 转换时 | 警告 |
| `setintflag` 相对运算目标是 int 类型 | 转换时 | 警告 |
| choice 条件段语法正确 | 转换时 | 错误 |

校验结果汇总显示在 ScriptManager 状态栏，点击可跳转到对应 Excel 行。

### 8.3 与资源管理器集成

`VNovelizer → 资源管理器` 增加 FlagRegistry 资产的引用检查（资产缺失/未配置时提示）。

---

## 9. 兼容性策略与前置修复

### 9.1 前置修复（扩展开工前必须完成）

| 修复 | 内容 |
|------|------|
| **Fix-1** | `SetBoolFlagCommand` 补 `Simulate` 重写（调用 `Execute`）——修复 P2 |
| **Fix-2** | `JumpCommand` 与 `LoadScriptCommand` 补 `Simulate` 重写 + `PendingJumpIndex`/`PendingScriptSwitch` 机制接入 `FastForwardToLine`——修复 P3 |
| **Fix-3** | `FastForwardToLine` 快进前按注册表复位 flag 到默认值（未注册的不动），保证状态重建确定性 |

### 9.2 兼容规则

| 场景 | 行为 |
|------|------|
| 无 FlagRegistry 资产 | 兼容模式：全部 flag 按 `Save` 作用域处理（即现状行为），仅打印一次性提示 |
| 老剧本无新命令 | 100% 兼容，无任何行为变化 |
| 未注册 flag 的 `set/jumpif` | 默认警告继续运行；`VNProjectConfig.StrictFlagValidation` 开启后报错阻止 |
| 存档向后兼容 | `SaveData` 现有三字典结构不变；新增字段（如 Float 值可编码进 StringFlags 或新增字典）需在 `LoadGlobalData` 式的 null 兼容处理中初始化 |
| `VNAPI` 门面 | 签名不变，内部改走 `FlagService` |

---

## 10. 完整示例：好感度分支

**注册表定义：**

| Name | Type | Scope | Default | Group |
|------|------|-------|---------|-------|
| `Amy_Favor` | Int | Global | 0 | 好感度 |
| `Met_Amy` | Bool | Save | false | 进度 |
| `PlayerName` | String | Global | "" | 玩家 |

**Excel 剧本（Command 列）：**

```
# 行 A1：初遇
ID: 010 | Command: setboolflag(Met_Amy) & setintflag(Amy_Favor,+10)

# 行 A2：送礼物选项（好感度门槛选项）
ID: 020 | Command: choice(送她花 | setintflag(Amy_Favor,+20) & jump(FL_A))
ID: 021 | Command: choice(送钻石 | setintflag(Amy_Favor,+100) & jump(FL_B) | if:Amy_Favor >= 30)
ID: 022 | Command: choice(只是路过 | jump(FL_C))

# 行 A3：分支判定（链式多条件 = 伪 AND）
ID: 030 | Command: jumpifnot(Met_Amy, NormalRoute)
ID: 031 | Command: jumpif(Amy_Favor >= 80, AmyBestRoute)
ID: 032 | Command: jumpif(Amy_Favor >= 50, AmyGoodRoute)
ID: 033 | Command: jump(NormalRoute)      # 兜底：条件均不满足时落到这里

# 结局区
ID: AmyBestRoute | ...
ID: AmyGoodRoute | ...
ID: NormalRoute  | ...
```

**行为要点：**

1. 读档回退：`Met_Amy`（Save）随档回退；`Amy_Favor`（Global）跨档累计。
2. 快进重建：读档 `FastForwardToLine` 时，`jumpif` 的 `Simulate` 同样判定并跳转快进指针，分支状态精确还原。
3. `021` 行选项：好感度不足 30 时"送钻石"选项直接不显示。

**跨剧本章节分支（`loadscriptif`）：**

剧本 `Chapter1` 末尾（Command 列）：

```
ID: 900 | Command: loadscriptif(Amy_Favor >= 80, Chapter2A, Scene_000)
ID: 901 | Command: loadscriptif(Amy_Favor >= 50, Chapter2B, Scene_000)
ID: 902 | Command: loadscript(Chapter2C)                      # 兜底：好感度不足 50
```

- 好感度 ≥ 80 → 进入 `Chapter2A`（最佳线章节）；50~79 → `Chapter2B`；否则 `Chapter2C`。
- 读档快进时，`loadscriptif` 的 `Simulate` 通过 `PendingScriptSwitch` 在快进循环中真实切换剧本数据源，跨剧本分支状态可精确重建（见 §7.3）。

---

## 11. 实施路线图

| 阶段 | 内容 | 交付物 | 依赖 | 状态 |
|------|------|--------|------|:---:|
| **P1 地基** | FlagRegistry SO + FlagService（作用域路由）+ Fix-1/2/3 | `FlagRegistry.cs`、`FlagService.cs`、修改 3 个既有文件 | 无 | ✅ 已完成 |
| **P2 核心命令** | ConditionParser + `jumpif`/`jumpifnot` + `loadscriptif`/`loadscriptifnot` + `setintflag` 相对运算 + PendingJumpIndex/PendingScriptSwitch 完整接入 | `ConditionParser.cs`、4 个新命令、Excel 可实测好感度分支与跨剧本章节分支 | P1 | ✅ 已完成 |
| **P3 编辑器** | Flag 编辑器窗口（含 Play Mode 调试）+ Excel 转换静态校验 + 引用统计 | `Editor/FlagEditor/` 目录、`ExcelToCSVConverter` 校验钩子 | P1 | 🔶 窗口已完成，转换校验/引用统计待做 |
| **P4 增强** | choice 条件选项、`toggleflag`/`addintflag`、`switchint`、`randomint`、Temp 作用域 | 按需逐个合入 | P2 | ⬜ 未开始 |

> **实现备注（2026-08）**：P1/P2 已落地。新增文件：`Runtime/.../Data/FlagRegistry.cs`、`Runtime/.../Managers/FlagService.cs`、`Runtime/.../Commands/ConditionParser.cs`、4 个条件命令；修改：`GlobalData`/`GlobalDataManager`/`SaveData`（新增 FloatFlags）、`VNManager`（Pending 机制 + 存档走 FlagService + 新游戏复位）、3 个 set 命令与 `jump`/`loadscript`（Simulate 补全）。Flag 编辑器窗口位于 `Editor/FlagEditor/FlagEditorWindow.cs`（VNovelizer → Flag 编辑器）。

**验收标准（P2 结束时）：**

- [ ] Excel 中可用 `setintflag` 累计好感度并触发 `jumpif` 三段式分支
- [ ] `loadscriptif` 按好感度进入不同章节剧本，读档后跨剧本分支位置精确还原
- [ ] 存档 → 读档后，`Save` 作用域 flag 与分支位置精确还原
- [ ] 读旧档不回退 `Global` 作用域的好感度
- [ ] 存量剧本（无新命令）行为与扩展前完全一致
