# VNScript 命令链语法规范（Command Chain Syntax）

> 命令链语法是 VNovelizer Command 列的内嵌时序编排语法。它通过三个符号（`&`、`->`、`[]`）在 Excel 单元格内表达命令的并行/串行/分组时序，无需第二文件、无需同步机制。
>
> 定位：**完整 DSL（.vnscript）的低成本前置替代方案**。用 5% 的实现成本获取 DSL 70% 的核心价值（时序编排），同时规避双文件同步的所有风险。

---

## 目录

1. [设计目标](#1-设计目标)
2. [语法规则](#2-语法规则)
3. [语法形式化（EBNF）](#3-语法形式化ebnf)
4. [执行模型](#4-执行模型)
5. [AST 数据结构](#5-ast-数据结构)
6. [向后兼容策略](#6-向后兼容策略)
7. [完整规范细则](#7-完整规范细则)
8. [使用示例](#8-使用示例)
9. [与完整 DSL 的对比](#9-与完整-dsl-的对比)
10. [实施计划](#10-实施计划)

---

## 1. 设计目标

### 1.1 核心原则

| 原则 | 说明 |
|------|------|
| **零新文件** | 时序编排内嵌在 Command 列，不引入第二文件 |
| **零同步成本** | 不存在 Excel ↔ VNScript 的 ID 关联，插入/删除/重排行零风险 |
| **三符号学习成本** | `&`（并行）、`->`（串行）、`[]`（分组），无关键字 |
| **表达力等价** | 嵌套 `[]` 可表达任意 fork-join 树，与 Seq/Par DSL 等价 |
| **旧剧本零破坏** | 检测式双轨切换，不含新符号的命令串走旧逻辑 |
| **复用现有命令** | 叶子节点就是现有 31+ 命令，`CommandManager` 原样复用 |

### 1.2 解决的痛点

| 旧痛点 | 命令链方案的解法 |
|--------|-----------------|
| `&` 拼接无法表达时序（`bgfade&charmove&wait` 语义模糊） | `&` 明确为并行，`->` 明确为串行 |
| 完整 DSL 需要 .vnscript 第二文件 | 无第二文件，内嵌 Command 列 |
| Excel 插入行破坏 VNScript ID 关联 | 不存在 ID 关联，零风险 |
| fade 批处理等隐式并行规则不可控 | 并行/串行完全显式 |

---

## 2. 语法规则

### 2.1 三个符号

```
command1 & command2                  // 并行：同时触发
command1 -> command2                 // 串行：1 完成后执行 2
[command1 & command2] -> command3    // 分组：并行组完成后执行 3
```

### 2.2 优先级定义

**`&` 优先级高于 `->`**（类比乘法高于加法）。

混合表达式的解析规则：

| 表达式 | 解析为 | 语义 |
|--------|--------|------|
| `a & b -> c` | `(a & b) -> c` | a、b 并行，**都完成后**执行 c |
| `a -> b & c` | `a -> (b & c)` | a 完成后，b 和 c 并行 |
| `[a -> b] & c` | 强制分组 | "a→b 串行链"与 c 并行 |
| `[a & b] -> [c -> d]` | 强制分组 | 并行组完成后串行链 |

**设计理由**：
- "先把这几个同时做，做完再下一个"是演出编排最常见的思维
- 裸写 `a & b -> c` 默认等于 `[a & b] -> c`，`[]` 只在需要**改变**优先级时才写

### 2.3 n 元平坦结构

`&` 和 `->` 都解析为 **n 元平坦结构**而非二元递归：

| 表达式 | 正确语义 | 错误语义（二元递归） |
|--------|----------|---------------------|
| `a & b & c` | 三个命令**同时**启动 | ~~(a&b) 完成后再与 c 并行~~（引入错误波次） |
| `a -> b -> c` | a→b→c 串行链 | （二元递归语义恰好相同，但实现统一为 n 元） |

---

## 3. 语法形式化（EBNF）

```
命令链     = 串行表达式                       // 整条链的根
串行表达式 = 并行组 , { "->" , 并行组 }       // n 元串行链
并行组     = 单元 , { "&" , 单元 }            // n 元并行组
单元       = 命令 | "[" , 命令链 , "]"        // 分组可递归嵌套
命令       = 命令名 "(" [ 参数 , { "," , 参数 } ] ")"
命令名      = 标识符                          // 沿用现有 31+ 命令
参数       = 字面量 | 引号字符串
```

**解析要点**：
- 词法阶段先把 Command 列字符串按**括号深度 + 引号状态**切分为符号流（`&`、`->`、`[`、`]`、`命令(...)`）
- 语法阶段按优先级组装：先 `&`（平坦收集），后 `->`（平坦收集）
- `[]` 内部递归解析为子命令链

---

## 4. 执行模型

### 4.1 树形结构

命令链解析结果是**一棵 fork-join 树**，而非线性序列。例如：

```
[A & B] -> C -> [D & [E -> F]]
```

解析树：

```
      串行链（根）
     /     |      \
  并行组   C     并行组
  /   \        /    \
 A     B      D    串行链
                   /    \
                  E      F
```

### 4.2 执行规则

| 节点类型 | 进入时 | 内部 | 退出时 |
|----------|--------|------|--------|
| 串行链 | 开始执行第一个子节点 | 子节点**逐个等待** | 全部子节点完成 |
| 并行组 | **同时启动**所有子节点 | 子节点**各自独立运行** | 等待全部子节点完成（join） |
| 命令（叶子） | 立即执行 | — | 执行完成 |

### 4.3 "完成"的定义

沿用 VNovelizer 现有 `VNCommand` 基类的二分：

| 命令类型 | "完成"意味着 |
|----------|------------|
| 瞬时命令（`Execute`） | 立即返回 |
| 动画命令（`ExecuteAsync`） | 协程跑完（如 1 秒淡入动画播放完毕） |

### 4.4 执行器伪代码

```csharp
// 递归执行命令链树
IEnumerator ExecuteChain(ChainNode node)
{
    switch (node)
    {
        case SeqNode seq:                          // 串行：逐个等待
            foreach (var child in seq.Children)
                yield return ExecuteChain(child);
            break;

        case ParNode par:                          // 并行：全部启动，全部 join
            var running = par.Children
                .Select(c => MonoManager.GetInstance()
                    .StartCoroutine(ExecuteChain(c)))
                .ToList();
            foreach (var r in running)
                yield return r;                    // 依次等待全部协程
            break;

        case CommandNode cmd:                      // 叶子：同步或异步
            if (cmd.IsAsync)
                yield return cmd.ExecuteAsync(cmd.Args);
            else
                cmd.Execute(cmd.Args);
            break;
    }
}
```

### 4.5 Simulate（预演）与 Interrupt（中断）

| 模式 | 行为 |
|------|------|
| **Simulate**（FastForwardToLine 预演） | 按"深度优先串行序"展开树，逐个 Simulate（预演不关心时序，只关心最终状态：背景/BGM/立绘/标志） |
| **Interrupt**（跳过/快进） | 对树中所有运行中的协程广播 Interrupt，与现有 skip 机制对齐 |

---

## 5. AST 数据结构

### 5.1 节点定义

```csharp
namespace VNovelizer.DSL.Chain
{
    public abstract class ChainNode
    {
        public int position;    // 源字符串中的偏移（错误定位）
    }

    // 串行链：n 元，逐个执行
    public class SeqNode : ChainNode
    {
        public List<ChainNode> Children = new();
    }

    // 并行组：n 元，同时启动，全部 join
    public class ParNode : ChainNode
    {
        public List<ChainNode> Children = new();
    }

    // 命令叶子：复用现有 VNCommand
    public class CommandNode : ChainNode
    {
        public string Name;             // 命令名
        public string RawArgs;          // 原始参数串
        public bool IsAsync;            // 是否异步（动画命令）
    }

    // 解析结果
    public class ParseResult
    {
        public ChainNode Root;                  // 树根（可为 null）
        public List<ChainError> Errors = new(); // 全部错误
        public bool UsesChainSyntax;            // 是否含 -> 或 [
    }
}
```

### 5.2 树深度与规模

| 约束 | 值 | 说明 |
|------|-----|------|
| `[]` 嵌套深度 | ≤2 层 | 更深解析警告"建议拆分" |
| 单链命令数 | 无硬限制 | 建议 ≤20（可读性） |

---

## 6. 向后兼容策略

### 6.1 语义差异（唯一的炸弹）

| | 旧逻辑（`ExecuteCommandsAsync`） | 新语义 |
|---|---|---|
| `cmd1 & cmd2 & cmd3` | **顺序执行**，仅 CharFadeIn/Out 批处理并行 | **全部同时启动** |

如果直接切换，旧剧本的时序会变。例如旧剧本：

```
charfadein(L) & charfadein(M) & wait(0.5) & showprompt(...)
```

- 旧逻辑：fade 都完成后才开始 wait，prompt 在 fade + 0.5s 后出现
- 新逻辑：wait 与 fade 同时启动，prompt 提前约 1 秒出现

### 6.2 检测式双轨切换（推荐）

```
解析 Command 列字符串
    │
    ├─ 不含 "->" 且不含 "[" ──→ 旧逻辑（ExecuteCommandsAsync，逐条顺序 + fade 批处理）
    │                            旧剧本 100% 行为不变
    │
    └─ 含 "->" 或 "["     ──→ 新链式解析器
                                 & = 严格并行
                                 -> = 严格串行
                                 [] = 分组
```

### 6.3 配套规则

| 规则 | 说明 |
|------|------|
| 语义声明 | 命令串一旦使用 `->` 或 `[]`，**整串**按新语义解析（`&` 即严格并行） |
| 迁移旧剧本 | 把希望并行的改写 `a & b`，希望顺序的改写 `a -> b`，一次性消除旧语义模糊性 |
| 校验器提示 | 检测到纯 `&` 串时提醒："当前为兼容模式（顺序执行），如需并行请显式使用链式语法" |
| 强制开关（可选） | `VNProjectConfig.ForceChainSyntax`，开启后全部走新解析器（用于新项目） |

---

## 7. 完整规范细则

### 7.1 错误处理

| 情况 | 处理 |
|------|------|
| 空组 `[]` | 解析报错："空的命令分组" |
| 悬空 `->`（如 `a ->`） | 解析报错，带位置信息 |
| 悬空 `&`（如 `& b` 或 `a &`） | 解析报错，带位置信息 |
| 括号不匹配（`[a & b`） | 解析报错："缺少闭合 ']'" |
| 未知命令名 | 语义警告（沿用现有命令注册表校验） |
| **运行时命令失败** | 记录错误，**该单元视为完成**，链继续（演出容错优先，不因一条命令卡死整行） |

错误输出格式：

```
[Chapter1.csv 行 1001, Command 列] 错误：位置 23 处缺少闭合 ']'
[Chapter1.csv 行 1001, Command 列] 错误：'->' 后缺少命令（悬空操作符）
命令链解析完成：共 2 个错误。
```

### 7.2 参数中的特殊字符（转义规则）

命令参数可能与语法符号冲突（如 `showprompt(进度->下一步)`）。

**规则：参数中含 `&`、`->`、`[`、`]`、`,` 时，必须用引号包裹。**

```
// 合法：引号包裹的参数
showprompt("进度->下一步");
t_color(255, 0, 0);

// 非法：裸参数含语法符号
showprompt(进度->下一步);    // 解析器会把 -> 当作串行操作符
```

**词法实现**：切分符号时维护**括号深度 + 引号状态**，引号内的语法符号不参与切分。

### 7.3 单元素组

`[a]` 合法，等价于 `a`（允许，便于多行排版对齐）。

### 7.4 多行排版

Excel 单元格内支持换行（Alt+Enter），CSV 转义已处理引号内换行。命令链可跨行排版提高可读性：

```
[ charfadein(L) &
  charfadein(M) &
  charfadein(R)
] -> wait(0.5) -> showprompt("全员就位")
```

解析器忽略命令串中的换行符与多余空白。

### 7.5 嵌套深度限制

| 深度 | 处理 |
|------|------|
| 1 层 `[a & b] -> c` | 正常，最常用 |
| 2 层 `[[a & b] -> c] & [d -> e]` | 合法，可读性已下降 |
| 3 层及以上 | 解析**警告**："命令链嵌套过深，建议拆分到多行或使用完整 DSL" |

---

## 8. 使用示例

### 8.1 基础并行

```
// 三个角色同时淡入
charfadein(L, 1) & charfadein(M, 1) & charfadein(R, 1)
```

### 8.2 基础串行

```
// 先震屏，再等待，最后显示提示
shake(screen, 0.3) -> wait(0.5) -> showprompt("注意！")
```

### 8.3 并行组后串行（最常用模式）

```
// 角色全部就位后，背景切换，再显示对话
[charfadein(L, 1) & charfadein(M, 1)] -> bgfade(School, 1.5) -> showDialogueTypewriter
```

### 8.4 串行链并行推进（两线并进）

```
// Amy 的移动+换表情 与 震屏+对话 同时进行
[charmove(Amy, 100, 0, 0.5) -> setexpression(Amy, happy)]
&
[shake(screen, 0.3) -> wait(0.2)]
```

### 8.5 深层组合

```
// 等价于：fork(A, fork(B->C)) -> join -> D
[[a & b] -> c] & d
```

### 8.6 实战完整示例

```
// Excel Command 列内容：
[
  bgfade(Beach, 1.5) &
  charfadein(L, Amy_Normal, 1) &
  charfadein(M, Jack_Angry, 1)
] -> wait(0.5) -> playBGM(BGM01) -> shake(screen, 0.3, 5)
```

语义：
1. 背景淡入、Amy 淡入、Jack 淡入**同时开始**
2. 三者**全部完成**后，等待 0.5 秒
3. 播放 BGM01
4. 震屏

---

## 9. 与完整 DSL 的对比

| 维度 | 命令链（本方案） | 完整 DSL（.vnscript） |
|------|-----------------|----------------------|
| 载体 | Command 列内嵌 | 独立第二文件 |
| 同步成本 | **零**（无第二文件） | 高（ID 关联，插入行难题） |
| 学习成本 | 三个符号 | 一门语言（VAR/Seq/Par/Confirm） |
| 表达力 | fork-join 树（无变量） | fork-join 树 + VAR + Confirm 定制 + 未来 if/macro |
| 可读性 | ≤2 层嵌套尚可，深了差 | 多行缩进，任意复杂度都清晰 |
| 实现成本 | **1-2 周** | 2-3 个月（含工具链） |
| 适用人群 | 编剧 + 演出策划 | 演出策划（程序员向） |

### 定位结论

命令链是完整 DSL 的**低成本前置替代**：

1. **先落地命令链**（1-2 周）——立即解决"单元格内 `&` 拼接表达不了时序"的痛点
2. **观察实际使用**——如果 95% 的演出场景它都能覆盖，完整 DSL 降级为"远期可选"，重构计划大幅瘦身
3. **仅当出现明确的复杂度天花板**（多层嵌套成为常态、需要变量/条件）再启动完整 DSL

---

## 10. 实施计划

### 10.1 任务拆解

| 任务 | 工作量 | 说明 |
|------|--------|------|
| 词法切分器（含引号/括号深度处理） | 2-3 天 | 切分 `&` `->` `[]` 命令 token |
| 递归下降解析器（树构建） | 2-3 天 | 按优先级组装 SeqNode/ParNode |
| 树执行器（协程调度） | 2-3 天 | 递归 ExecuteChain |
| 检测式双轨切换 | 1 天 | Command 列入口分流 |
| 错误收集与定位 | 1-2 天 | 位置信息 + 友好报错 |
| Simulate/Interrupt 适配 | 1-2 天 | 深度优先展开 + 广播中断 |
| 校验器集成 | 1 天 | 剧本管理器转换时校验 |
| 文档与示例 | 1 天 | README + 示例剧本 |
| **合计** | **约 2 周** | |

### 10.2 实施顺序建议

```
第 1 周：词法 + 语法 + AST + 单元测试（纯 C#，无 Unity 依赖）
第 2 周：执行器 + 双轨切换 + Simulate 适配 + 校验器 + 文档
```

### 10.3 验收标准

| 标准 | 说明 |
|------|------|
| 旧剧本回归 | 不含 `->`/`[` 的命令串行为与旧版完全一致 |
| 并行语义 | `[a & b] -> c` 中 c 严格在 a、b 都完成后执行 |
| 串行语义 | `a -> b` 中 b 严格在 a 完成后执行 |
| 错误容错 | 单命令失败不阻断整链 |
| 预演一致 | FastForward 后状态与顺序执行最终状态一致 |
| 跳过可用 | skip 时整树可中断，无协程泄漏 |

### 10.4 实现状态

命令链语法已实现，代码位于 `Runtime/Scripts/VNovelizer/Core/Commands/Chain/`：

| 文件 | 职责 | 对应规范章节 |
|------|------|-------------|
| `ChainNodes.cs` | AST 节点（SeqNode/ParNode/CommandNode/ChainError/ChainParseResult） | 第 5 章 |
| `ChainLexer.cs` | 词法切分器（引号感知 + 括号深度，切分 `&` `->` `[]` 与命令单元） | 第 3 章 |
| `ChainParser.cs` | 递归下降解析器（n 元平坦结构、优先级 `&` > `->`、错误收集与恢复、深度警告） | 第 3、7 章 |
| `ChainExecutor.cs` | 树执行器（Seq 串行等待 / Par fork-join 协程调度 / CollectCommands 深度优先展开） | 第 4 章 |

对现有代码的修改：

| 文件 | 修改 | 说明 |
|------|------|------|
| `Commands/VNCommand.cs` | `ExecuteCommandsAsync` | 双轨切换：含 `->`/`[` 走链式执行器，否则旧逻辑（旧剧本零破坏） |
| `Commands/VNCommand.cs` | `SimulateCommands` | 链式串按深度优先展开逐个 Simulate（预演不关心时序） |
| `Commands/VNCommand.cs` | `ExecuteCommands` | 同步模式检测到链式语法时按展开顺序执行并警告时序被忽略 |
| `Managers/VNManager.cs` | `ExtractNonChoiceCommands` | 改用链式词法器切分，链式语法下 choice 也能被正确剔除 |

双轨切换逻辑（`ExecuteCommandsAsync`）：

```
解析 Command 列字符串（ChainParser.Parse）
    │
    ├─ UsesChainSyntax == false（无 -> 无 [）
    │       └─→ 旧逻辑（顺序执行 + CharFade 批处理并行），行为 100% 不变
    │
    └─ UsesChainSyntax == true
            ├─ Root != null → 报告解析错误（若有）→ ChainExecutor.Execute（严格并行/串行语义）
            └─ Root == null（致命）→ 报告全部错误 → 回退旧逻辑容错
```

---

## 附录：三符号速查卡

```
┌─────────────────────────────────────────────────┐
│  &    并行：两侧同时启动                          │
│  ->   串行：左侧完成后执行右侧                     │
│  []   分组：内部视为一个整体（完成 = 内部全部完成）  │
│                                                 │
│  优先级：& 高于 ->                               │
│  a & b -> c  等价于  [a & b] -> c                │
│                                                 │
│  常用模式：                                      │
│  [并行组] -> 串行步骤                             │
│  [A & B & C] -> D -> E                           │
└─────────────────────────────────────────────────┘
```

---

## 附录 B：全命令遍历审查报告（链式语法兼容性）

对全部已注册 VNCommand 逐一审查在 `&`（并行）/ `->`（串行）/ `[]`（分组）下的行为，结论如下。

### B.1 已修复的问题

| # | 命令 | 问题 | 修复 |
|---|------|------|------|
| 1 | `charmove` | **实例字段并发覆盖**（P0）：`_targetRect`/`_moveTween` 为实例字段而命令是单例，`[charmove(L,..) & charmove(M,..)]` 时第二个调用覆盖字段，第一个 tween 的回调把位置写到第二个角色上（**两个角色一起动**），且 Interrupt 只能中断最后一个 | 重写为 token 列表模式（仿 charfadein），闭包捕获局部变量，Interrupt 中断全部并行移动 |
| 2 | `shake` | **无 Interrupt + 协程句柄未跟踪**：点击跳过后震动协程继续跑（UI 持续抖动到 duration 结束）；链式放大暴露面 | 登记活动协程句柄+原始位置（启动前捕获），新增 `Interrupt()` 停止全部并归位；自然结束时自动清理 |
| 3 | `bgfade` | **重入状态错乱**：`_front/_back/_fadeTween/isRunning` 单组实例字段，并行或快速连续 bgfade 时两协程互相覆盖状态 | 入口重入保护：新淡入开始前强制完成上一个（瞬间切换）并警告 |
| 4 | `playvideo` | **无 Interrupt**：点击跳过后协程死了但 VideoModel 继续全屏播放，用户被卡在视频前 | `VideoModel` 新增公开 `Stop()`（不触发完成回调）；`VNAPI` 新增 `StopVideo()` 跟踪活动实例；命令新增 `Interrupt()` |
| 5 | `charjump` | **实例字段并发覆盖**（P1）：`currentTarget/startPos/runningCoroutine` 单组字段，`[charjump(L) & charjump(M)]` 时归位基准被篡改（L 被拉到 M 的起始位置），Interrupt 只能中断最后一个 | 重写为 token 列表模式，闭包捕获局部变量，Interrupt 归位全部并行实例 |
| 6 | `playanim` | **实例字段并发覆盖**（P1）：`currentAnimObj` 等单组字段，`[playanim(a,L) & playanim(b,R)]` 时第一个动画自然结束不回收（**池泄漏**），Interrupt 只回收最后一个 | 重构为 `ActiveAnim` 活动列表 + entry 对象比对；`ExecuteAsync` 统一为主入口（链式/同步双路径合一），Interrupt 回收全部活动动画 |
| 7 | `stopanim` | **对象池路径错位**（P0，确定性既有 bug）：回收路径用 `ParticalEffectPath + "/Animation"`，与 playanim 加载路径 `AnimationPath` 不一致——对象被推进错误的池，下次 playanim 取不到（重复实例化），错误池对象永久闲置 | 回收路径统一为 `AnimationPath + "/" + name` |
| 8 | 系统级 | **选项选择与残留流程协程双重演出**（P0）：`[choice(...) & wait(3)]`（choice 不在链尾）+ 用户在 wait 结束前点选项 → `ExecuteChoiceCommand` 启动新协程，但旧 `_flowCoroutine` 未停，其结束后检测行号已变**再次触发 PlayCurrentLine**——双重演出/行索引二次推进 | 选择前先 `StopCoroutine(_flowCoroutine)` + `InterruptAll()` 终止残余演出，新协程纳入 `_flowCoroutine` 统一跟踪 |

### B.2 解析器校验的语义陷阱（警告不阻断）

| # | 场景 | 风险 | 处理 |
|---|------|------|------|
| 5 | `a -> jump(x) -> b` | jump 立即改行索引，b 在"行已切换"上下文执行，演出污染 | `ChainParser.ValidateFlowCommands`：流程命令（`jump/choice/loadscript/loadscene`）非链尾时警告 |
| 6 | `[a & loadscene(x)]` | 场景切换后旧场景 UI 销毁，残留分支命令操作已销毁对象（MissingReference） | 同上校验；且 `loadscene` 本身要求链尾 |
| 7 | `playvideo(v.mp4, loadscript(c2)) -> b` | playvideo 第二参数与 `->` 构成双重流程语义 | 校验警告：链式下建议 `playvideo(v.mp4) -> loadscript(c2)` |

### B.3 确认安全的命令（无需修改）

| 命令 | 并行安全性说明 |
|------|---------------|
| `charfadein` / `charfadeout` | token 列表模式（旧批处理时代为并行设计），多实例并行安全 |
| `wait` | 无状态；分支/主链被 StopCoroutine 直接杀死，链式下跳过反而更干脆 |
| `t_color` / `t_size` / `showprompt` / `setboolflag` / `setintflag` / `setstringflag` / `unlockcg` / `unlockmusic` / `unlockscene` / `config` | 同步瞬时命令，`StartCoroutine` 首帧同步执行完毕，并行下按书写顺序生效（结果可预测）；showprompt 自管理淡出 |
| `playparticle` / `stopparticle` | 幂等（同名特效防叠加），各自独立对象，并行安全 |
| `playsfx` | 逐次顺序播放；并行时共用单一 SFX 声源会互相顶替（既有设计，见 B.4） |

### B.4 已知限制（既有行为，非链式引入，文档说明）

| # | 现象 | 说明 |
|---|------|------|
| 1 | 并行分支内的同步 flag 写入 | `[setintflag(x,1) & setintflag(x,2)]` 按书写顺序同步执行，后者生效——结果确定但依赖实现细节，建议避免对同一 flag 并行写入 |
| 2 | `playsfx` 并行顶替 | 共用单一 SFX AudioSource（`Play` 而非 `PlayOneShot`），并行播放时后播顶掉先播；如需叠放音效需 MusicManager 支持多声源/PlayOneShot |
| 3 | `jump` 无 Simulate | FastForwardToLine 按物理行序线性预演，忽略跳转（既有行为）；链式不改变此语义 |
| 4 | 矛盾链的预演差异 | `[charfadeout(L) & charfadein(L)]`（同槽矛盾写法）：Simulate 按深度优先序取后者为最终态，实际执行为动画竞态——属作者笔误，校验器不拦截 |
| 5 | 同步入口 `ExecuteCommands` 的链式串 | 时序被忽略，按展开顺序执行（已实现 Debug.LogWarning 提示） |
| 6 | fire-and-forget 同步入口 | `playsfx`/`charjump` 的同步入口 `Execute` 自行 `StartCoroutine` 不登记引用计数（旧路径既有）；链式路径经 `ExecuteSingleCommandAsync` 有正确计数，不受影响 |
| 7 | `stopparticle` 的 5 秒延迟回收 | 延迟回收协程无人跟踪（期望行为：特效飘完自回收）；对象销毁有判空保护 |
| 8 | 同名动画/特效并行 | `playanim(a,L)` 与 `playanim(a,R)` 并行时按名字 `Find` 只找到第一个；建议避免同名并行播放 |

### B.5 模拟复杂场景验证结论

| 场景 | 模拟内容 | 结论 |
|------|----------|------|
| A. 并行动画混用 | `[charmove(L,100,0,1) & charjump(M) & playanim(fx,L)]` | 三个命令全部 token/entry 列表化，并行安全；点击跳过全部归位/回收 |
| B. 快速选择竞态 | `[choice(...) & wait(3)]` + 0.2 秒内点选项 | 修复后：选择即终止残余（停旧协程 + InterruptAll），单一流程协程不变量保持 |
| C. 循环动画生命周期 | `playanim(rain,loop)` → 数行后 `stopanim(rain)` | 池路径统一后正确回收；loop 注册/注销对称 |
| D. timeScale=10 快进 | wait/shake/charjump 在 timeScale 下执行 | WaitForSeconds/Time.deltaTime/tween 均受 timeScale 影响，快进一致 |
| E. 跳过时引用计数 | 分支协程被 StopCoroutine 杀死，计数递减代码未执行 | InterruptAll 清空字典兜底，无泄漏；下次点击 IsRunning 正确为 false |
| F. playvideo 链式 | `playvideo(a.mp4) -> jump(x)` | 视频自然播完触发 jump（VideoPlayer.loopPointReached）；中途点击 → StopVideo 不触发 jump，由下次点击正常前进 |

### B.6 链式语法使用守则（汇总）

1. **流程命令必须放链尾**：`jump` / `choice` / `loadscript` / `loadscene` 只能作为整条链最后一个命令
2. **playvideo 的"结束后命令"参数在链式中改用 `->`**：`playvideo(a.mp4) -> jump(x)`
3. **同一并行组内避免操作同一状态**：不要对同一背景、同一 flag 写两个并行命令
4. **`[]` 嵌套 ≤2 层**：更深嵌套会收到警告
5. **参数含 `&` `->` `[` `]` `,` 时必须引号包裹**
