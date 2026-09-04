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
11. [行演出编辑器（2026-08-25 架构决策）](#11-行演出编辑器row-performance-editor2026-08-25-架构决策)

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
| 多余 `)`（如 `showbg(a))`） | 括号深度 clamp 到 0（2026-08-26 修复），不再吞掉后续命令 |
| 参数以 `\` 结尾 | 词法安全前进（2026-08-26 修复），不再抛越界异常 |
| 嵌套深度 >2 层 | **警告**（`Warnings`），不阻断 |
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
| 3 层及以上 | 解析**警告**（进 `ChainParseResult.Warnings`，非 `Errors`）："命令链嵌套过深，建议拆分到多行"——不阻断执行，也不阻断图编辑器保存 |

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
[charmove(Amy, 100, 0, 0.5) -> setexpression(Amy, uniform, happy)]
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
  charfadein(L, 1) &
  charfadein(ML, 1)
] -> wait(0.5) -> playBGM(BGM01) -> shake(screen, 0.3, 5)
```

语义：
1. 背景淡入、左槽立绘淡入、中左槽立绘淡入**同时开始**（立绘内容由该行 CSV 槽位列决定，格式 `角色ID#分组#表情`）
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

## 11. 行演出编辑器（Row Performance Editor）——2026-08-25 架构决策

> 本章为命令链语法的上层编辑形态，是"命令链可视化生态"路线的核心章节。数据模型规格参考 `VNScriptLanguageSpec.md`（该文档已转为设计参考存档——其 ScriptBlock/VAR/Seq/Par/Confirm 概念在本章架构中复活）；路线图见 `VNRefactoringPlan.md` §12。
>
> **UI 实施规格（GraphView 节点体系 / 图结构校验 / 序列化 / 10 大功能区块）见独立文档 `VNRowPerfEditorSpec.md`**——本章聚焦架构决策，实施细节以该文档为基线。

### 11.1 设计动机

命令链解决了"单元格内表达时序"的问题，但 Excel 单元格永远无法提供语法高亮、行内错误提示与自动补全。行演出编辑器把 Command 列的编辑主战场移入 Unity（GraphView），同时保持 CSV 为唯一 Source of Truth：

- **图与命令链 AST 天然同构**：`SeqNode`=顺序连线、`ParNode`=分叉-汇合、`CommandNode`=方块节点——已实装的 `ChainParser` 直接复用（读取方向零新增）
- **零新语法学习成本**：拖节点/连线/结构化参数面板（角色 ID 下拉、时长滑块、槽位枚举）替代手写 `&`/`->`/`[]`
- **引擎运行时零改动**：图保存时序列化回命令链文本写入 Command 列，执行路径不变
- **行的重新定义**：行不再是"固定列表格行"，而是**演出单元 + Confirm 边界（一拍）**——解锁行内多段对话（多个 showDialogue 节点）、纯演出行（删除对话节点）、任意时序编排

### 11.2 三层行形态（核心机制）

| 形态 | 判定（Command 列内容） | 执行路径 | 说明 |
|------|----------------------|---------|------|
| **普通行** | 空 | 默认模板（数据列驱动，现有隐式路径） | 大多数对话行，零成本 |
| **增强行** | 仅普通命令（无系统命令） | 默认模板 + 命令追加（**现有语义原样保留**） | 与现状完全向后兼容 |
| **定制行** | 含系统命令（完整链） | 全链执行（ChainExecutor） | 触碰默认节点后"按需提升"的产物 |

**判定规则天然向后兼容**：所有存量剧本的 Command 列均不含系统命令 → 自动落入普通/增强形态，**零迁移**。

#### 11.2.1 已落地实现（2026-08-26）

**判定入口**：`VNManager.IsCustomPerformanceRow(line)` → `CommandManager.ContainsSystemCommand()`。
带缓存（`Dictionary<StoryLine, bool>`）——判定要跑 `ChainParser`，而 jump 回跳 / AutoPlay 连播会反复经过同一行；`StoryLine` 在剧本生命周期内是稳定引用，故可直接作键，`SetScriptData` 时清空（防跨剧本累积泄漏）。

**四个分流点**：

| 路径 | 普通行 / 增强行 | 定制行 |
|------|---------------|--------|
| `PlayCurrentLine`（正常播放） | `UpdateVisualState` + `UpdateCharacterSlots` + `UpdateAudioState` + `UpdateDialogue`，随后执行 Command 列 | `PrepareCustomRowBaseline()`（清空五槽立绘），随后**全权**交由命令链 |
| `PlayCurrentLineImmediately`（skip） | 同上 + `DisplayAllText`，Command 列走 `SimulateCommands` | 清空立绘后 Command 列走 **`ExecuteCommandsInstant`** |
| `FastForwardToLine` 主循环（预演） | `SimulateLineVisualAudioState` 施加隐式状态 | 同方法内清空立绘字典，交由命令链 `Simulate` |
| `FastForwardToLine` choice 分支 | 同上（**复用同一方法**） | 同上 |

**三个实现要点**（均为核实代码后的结论，非想当然）：

1. **定制行必须预清空五个立绘槽位**。普通行的 `UpdateCharacterSlots` 对五槽逐一调 `UpdateCharacter`，空值即触发 `HideCharacter`——它正是「立绘列不继承，空 = 隐藏」规则的执行者。定制行跳过该步后，若作者只写了 `showChar(M)`，其余四槽的上一行立绘会残留，既违反既定规则也破坏「提升不改变演出」（默认模板本会生成全部五个 `showChar`，其中空值者即隐藏）。

2. **skip 路径的定制行不能只 Simulate**。`showDialogue.Simulate` 是空实现（对话是纯呈现，无状态副作用），若 skip 时只走 `SimulateCommands`，画面会没有文本/背景/立绘。故新增 `CommandManager.ExecuteCommandsInstant()`——与 `ExecuteCommands` 的唯一区别是**不打"同步模式忽略时序"警告**，因为在 skip 语境下"忽略时序取终态"正是预期语义而非误用（否则每次 skip 定制行都会刷警告）。

3. **`SetLineContextForSimulate` 的背景/BGM 必须自行推导继承，不能直读 `currentBG`/`currentBGM`**。定制行跳过了隐式状态更新，此时 `currentBG` 仍是上一行值；若上下文直取它，`showbg()` 的隐式绑定就会读到上一行背景——形成"命令依赖状态、状态又依赖命令"的循环依赖。现按与 `ResolveLine` 相同的规则推导（本行非空取本行，为空则继承）。

**不需要做的事**：无需重置立绘槽位基准位置。查证 `TheaterManager.OnShowCharacter`（TheaterManager.cs:119-162）确认，每次显示角色都从 `SlotBasePositions[posCode] + profile.offset` 重新布局，故 `charmove` 的偏移不会跨行叠加。

**预演侧与播放侧的对偶抽取**：`FastForwardToLine` 的主循环与 choice 分支原本各写一份状态更新代码，现统一抽为 `SimulateLineVisualAudioState()`——分流规则若只改一处会静默漂移，这是必须消除的重复。

### 11.3 可覆盖模板与按需提升

#### 11.3.1 默认模板结构（2026-08-26 修订：双分支 Par）

**默认模板**是普通行在图编辑器中的可视化形态（运行时从数据列自动生成，不占用 Command 列）：

```
Par {
    showDialogue(typewriter)                                    // 分支 A：对话独立一路
  &
    [ showbg() & showChar(L) & showChar(ML) & showChar(M) & showChar(MR) & showChar(R)
      & playBGM() & playVoice()                                 // 分支 B：瞬时系统命令（同帧）
      -> 用户命令链 ]                                            //         随后启动用户编排
}
```

> **⚠ 模板中没有 `showSpeaker()`** —— 这是 2026-08-26 等价性回归测试**实测**修正的结果。
> 引擎的 `UpdateDialogue` 是**一步**广播 `UpdateDialogue` + `UpdateHeadProfile` 两个事件
> （说话人、正文、头像同属一次更新）。而 `showSpeaker()` 也广播 `UpdateHeadProfile`，
> 模板若同时含两者，该事件会发生**两次**——实测「引擎 8 条事件 vs 模板 9 条」。
> 因此说话人显示由 `showDialogue` 一并承担，`showSpeaker` 的定位是
> "定制行中只想刷说话人而不重播正文"（如一行内换发言人）的独立场景。
>
> 这正是决策 s6 建立回归测试的价值：这个缺陷靠阅读代码极难发现——
> 两个命令各自看都正确，只有把事件序列摆在一起才暴露重复。

#### 11.3.2 硬契约：提升不改变演出

**「用户触碰模板节点导致行提升为定制行时，演出必须逐帧不变」——这是硬契约，不是软期望。**

等价性论证（对照 `VNManager.PlayCurrentLine`，`VNManager.cs:904-914`）：

| 引擎隐式路径 | 模板链对应部分 | 等价性 |
|-------------|---------------|--------|
| `UpdateVisualState`（背景） | 分支 B `showbg()` | 同帧同步 ✓ |
| `UpdateCharacterSlots`（五槽立绘） | 分支 B `showChar(L..R)` | 同帧同步 ✓ |
| `UpdateAudioState`（BGM + Voice） | 分支 B `playBGM()` / `playVoice()` | 同帧同步 ✓ |
| `UpdateDialogue`（说话人 + 文本 + 头像，启动打字机后立即返回） | 分支 A `showDialogue(typewriter)` | 一个命令覆盖引擎这一整步（含 `UpdateHeadProfile` 广播）✓ |
| 随后 `StartCoroutine(ExecuteActionsAndContinue)` 启动 Command 列 | 分支 B `-> 用户命令链` | 系统命令同帧完成后启动 ✓ |

**为何 `showDialogue` 必须独立成分支 A**：`showDialogue(typewriter)` 的完成语义是"等打字机跑完"（见 §11.4.1）。若把它放进分支 B 的串行链中，`playBGM/playVoice` 与用户命令会被推迟到文本全部打完之后——与现状（打字与 Command 列并行）不符，硬契约当场破裂。把它隔离到自己的并行分支，阻塞只阻塞自己。

> **设计洞察**：这正是命令链的表达力所在——"不阻塞"无需专门造命令（如 `waitfortext()`），并行是链本身的能力。

**验收手段**（决策 s6）：新增「模板等价性」回归测试工具——对同一行分别跑隐式路径与模板链，逐帧对比 `EventCenter` 事件序列（`UpdateDialogue` / `ChangeBackground` / `PlayBGM` / `UpdateHeadProfile` 等的触发时序）。防止后续改引擎或改模板时静默漂移。

#### 11.3.3 图编辑器中的呈现（决策 s8）

- 模板默认渲染为**单个半透明「默认演出」胶囊**（内含引用了哪些数据列的徽章），双击展开为完整 8 分支影子链。
- 理由：展开态横向约 2000px，每次打开行编辑器都要先滚过一屏模板才能看到自己的命令。
- 定制行（模板已实体化）默认展开。

#### 11.3.4 按需提升协议

- 图编辑器中，普通/增强行的默认模板以**半透明节点**展示（可见但未持久化到 Command 列）
- 用户**触碰任何默认节点**（删除 / 修改参数 / 重排 / 加特效）→ 该行提升为定制行：完整命令链（含系统命令）写入 Command 列
- 提升是**单向的**（可手动"重置回模板"，但会丢弃定制内容 → 提升时弹确认框）
- 未触碰的行**永不膨胀**（Command 列保持为空 / 仅普通命令）

### 11.4 隐式绑定（系统命令参数协议）

系统命令的参数为空 = 引用本行数据列（VAR 思想的图形化落地）：

| 系统命令 | 空参引用 | 继承语义 | 可否断开引用改内联 |
|---------|---------|---------|------------------|
| `showDialogue([mode])` | 本行 Text 列 | 本地化键 `text.{lineID}` 无损 | **否**（见 §11.4.1） |
| `showSpeaker()` | 本行 Speaker 列 | 本地化键 `speaker.{lineID}` 无损 | **否** |
| `showbg([name])` | 本行 Background 列 | 列空 → 节点跳过（保留"空格=继承"语义） | 可 |
| `playBGM([name])` | 本行 BGM 列 | 列空 → 节点跳过（保留继承语义） | 可 |
| `playVoice([name])` | 本行 Voice 列 | 列空 → 节点跳过 | 可 |
| `showChar(pos)` | 本行对应立绘列 | 列空 → 节点跳过（= 该槽隐藏） | 可 |

图上以"**引用：Text 列**"徽章显示绑定关系，点击徽章跳转表格视图对应单元格。**内容（数据列）与编排（图）职责彻底分离**：改台词改数据列，图自动跟随。

#### 11.4.0 已落地实现（2026-08-26）

六个命令位于 `Runtime/Core/Commands/SystemCommands/`，在 `RegisterDefaultCommands` 中**显式注册**（不依赖反射扫描的偶然性——图编辑器与模板依赖它们必然存在）。

**核心原则：系统命令必须复用引擎既有实现，不得另写一套。** 否则「提升不改变演出」硬契约会因双实现漂移而破裂。为此 `VNManager` 新增 `SysXxx` 系列包装方法（既有私有逻辑的"显式参数"版本，内部走同一条代码路径）：

| 系统命令 | 复用的引擎方法 | 关键语义 |
|---------|--------------|---------|
| `showDialogue([mode])` | `SysShowDialogue` → `UpdateDialogue` | 本地化解析 / 双事件广播 / `isTextDisplaying` / 历史记录**逐字一致** |
| `showSpeaker()` | `SysShowSpeaker` → `UpdateHeadProfile` 事件 + `VNGameplayPanel.UpdateSpeakerDisplay` | 仅刷说话人与头像，不重播正文 |
| `showbg([name])` | `SysShowBackground` → `UpdateVisualState` | 分支语义一致：普通名 / `black` / `hide` |
| `showChar(pos[,ref])` | `SysShowCharacter` → `UpdateCharacter` | 空值 = 隐藏该槽（立绘列不继承） |
| `playBGM([name])` | `SysPlayBGM` → `UpdateAudioState` | `stop`/`pause`/`resume` + **同名幂等跳过** |
| `playVoice([name])` | `SysPlayVoice` → `UpdateAudioState` 语音分支 | 含 URL 形式拒绝 |

**"无值即跳过"与"无值即隐藏"的分野**——这不是实现随意性，而是数据列继承规则的直接映射：

| 命令 | 数据列为空时 | 依据 |
|------|------------|------|
| `showbg` / `playBGM` / `playVoice` | **跳过**（不动当前状态） | Background / BGM 是框架**唯二的继承列**，"空格 = 继续" |
| `showChar` | **隐藏该槽** | 立绘列**不继承**，"空格 = 隐藏"是框架既定规则 |

**三层行形态判定入口**：`CommandManager.IsSystemCommand(name)` / `ContainsSystemCommand(commandString)`。后者复用 `ChainParser` 解析 + 深度优先展开，因此 `&` / `->` / `[]` 任意嵌套都能正确识别。

**`showDialogue` 阻塞的中断安全性**（已核实 `VNManager.cs:1013-1039`）：

```
点击 → isCmdRunning=true（阻塞的 showDialogue 在引用计数中）
     → StopCoroutine(_flowCoroutine)   掐断整链，等待循环随之停止
     → InterruptAll() → showDialogue.Interrupt() → DisplayAllText 事件
                        → 打字机立即全显 → TypingFinished 事件
                        → OnTypingFinished() → isTextDisplaying = false
```

`isTextDisplaying` 由**事件**复位而非由等待循环复位，因此协程被掐断不会造成状态残留。`ExecuteAsync` 中的 60 秒帧数上限仅在面板缺失（`TypingFinished` 永不到达）时兜底，防协程永久挂起。

#### 11.4.1 `showDialogue` 的显示方式参数（2026-08-26 新增，决策 dlg）

```
showDialogue()             // 空参 = typewriter（默认，与现状一致）
showDialogue(direct)       // 瞬间全显
showDialogue(typewriter)   // 逐字打字
```

| mode | 显示行为 | 链中完成语义 |
|------|---------|-------------|
| `direct` | 文本瞬间全部显示 | 立即返回（不阻塞） |
| `typewriter`（默认） | 逐字打字机 | **等打字机跑完**（阻塞本分支） |

**文本永不内联**：`showDialogue` 只接显示方式参数，文本永远引用本行 Text 列。因此：
- `text.{lineID}` 本地化键**永不可能失效**（§11.6 的"断开引用"风险对它天然不存在）
- 改台词只能改数据列——内容与编排职责彻底分离
- 同理 `showSpeaker()` 不接文本参数

**`Interrupt()` 必须实现为"立即全显文本"**。原因见点击处理链路（`VNManager.cs:991-1017`）——两个分支互斥且**命令中断优先**：

```
玩家点击 → ① 命令/流程协程在跑？ → StopCoroutine + InterruptAll → return   ← 阻塞的 showDialogue 命中此处
        → ② isTextDisplaying？    → DisplayAllText → return                ← 永远走不到
        → ③ 推进下一行
```

若 `showDialogue.Interrupt()` 不触发 `DisplayAllText`，点击后文本会永久停在半截。这与 `VNCommand.Interrupt()` 的既有语义（快进到最终态）一致。

#### 11.4.2 行上下文的获取（决策 s5）

系统命令需要读"当前行的解析后值"，但 `VNCommand.Simulate(string args)` / `Execute(string args)` 只有 args。

**方案**：`VNManager` 暴露 `CurrentLineContext`（存 `ResolveLine()` 结果——**继承已应用后的终值**，而非 `StoryLine` 原始字段），在 Simulate 与 Execute 两条路径入口统一赋值，系统命令经 `VNAPI.GetCurrentLineContext()` 读取。

- **`VNCommand` 签名不变** → 用户自定义命令零破坏
- **前置修复（2026-08-26 已完成）**：`FastForwardToLine` 原在循环末尾（`SimulateCommands` **之后**）才赋值 `lastLine`，而 choice 分支是先赋值后模拟——两条路径顺序相反。现有命令都不读 `lastLine` 故未暴露，但隐式绑定一旦落地会**静默读到上一行的 Text/Background**。已统一为"模拟前赋值"。
- Simulate 路径同样需要 `ResolveLine`（否则背景/BGM 继承未应用）

**已落地实现（2026-08-26）**：

| 组成 | 位置 | 说明 |
|------|------|------|
| `VNLineContext` | `Runtime/Core/Commands/Meta/VNLineContext.cs` | 公开只读类型。字段 = `ResolvedLine`（Speaker/Text/HeadProfile/Background/BGM/Voice）**并集** 五个立绘槽位 + `LineID`/`LineIndex`/`IsSimulating`。提供 `GetCharBySlot(posCode)`（接受 `L`/`Left` 等两种写法）与 `GetColumn(columnName)`（按 `[VNParam(BoundColumn)]` 声明泛化读取） |
| `VNManager.CurrentLineContext` | `VNManager.cs`（属性 private set） | 与 `lastLine` 同一语义组，`ResetState()` 与卸载路径一并清空 |
| `SetLineContextForPlay` | `VNManager.cs` | Execute 路径：复用已算好的 `ResolvedLine`，零重复计算 |
| `SetLineContextForSimulate` | `VNManager.cs` | Simulate 路径：**不复用 `ResolveLine`**（该方法有写 `isVoiceEnabled` 的副作用，且快进循环已按自己的顺序处理过继承）。调用点位于 `currentBG` 更新之后，此时 `currentBG`/`currentBGM` 已等于本行解析后取值，直接取用即与 Execute 语义一致；`Voice` 置空（预演不播音频） |
| `VNAPI.GetCurrentLineContext()` / `GetCurrentLineColumn(col)` | `Core/Utils/API.cs` | 命令侧统一入口，不直连 `VNManager` |

**四个赋值点**（全部早于命令执行/模拟）：`PlayCurrentLine` · `PlayCurrentLineImmediately` · `FastForwardToLine` 主循环 · `FastForwardToLine` 的 choice 分支。

> **为何不直接把 `ResolvedLine` 改成公开**：① 它是 `VNManager` 的 private struct，公开会把内部实现细节固化进 API 表面；② 它**不含五个立绘槽位**（立绘走 `UpdateCharacterSlots` 另一条路径），而 `showChar(pos)` 需要立绘引用；③ 它没有行标识（`LineID` 是本地化键 `text.{lineID}` 的来源）与 `IsSimulating` 标记。


### 11.5 编辑入口与数据流

```
Excel（数据列批量编辑：说话人/文本/背景/BGM/立绘）
    │ AutoExcelConverter 列分工转换：
    │   ① 数据列从 xlsx 读取（ExcelDataReader，现有逻辑）
    │   ② Command 列保留 CSV 现值（跳过覆盖，防丢编排数据）
    │   ③ 镜像写回：xlsx 的 Command 列与 CSV 不一致时，用 ClosedXML
    │      仅写回 Command 列到 xlsx（保留用户格式），随后刷新时间戳防循环
    ▼
CSV（唯一 Source of Truth：数据列 + Command 列）
    ▲▼ 图↔文本双向转换：
    │    读：ChainParser.Parse → AST → AstToGraph + AutoLayout
    │    写：Validator → GraphToAst（SP 分解）→ ChainSerializer → ChainParser 自校验
GraphView 行演出编辑器（拖资源/连线/参数面板/实时校验/触碰提升/Undo/链复制粘贴）
    │ 运行时
    ▼
ScriptParser → 三路径执行（默认模板 / 增强 / 定制）→ ChainExecutor
```

- **列分工**：数据列归 Excel（批量录入 + 低门槛），Command 列归 Unity 图编辑器（高亮/补全/校验）
- **镜像写回（ClosedXML，MIT，2026-08-25 选型）**：转换时顺带把 CSV 的 Command 列写回 xlsx，Excel 侧浏览剧本时看到的编排永远最新。四个关键设计：
  - **对比后跳过**：写回前对比 xlsx 与 CSV 的 Command 列，一致则不发生物理写入（常态零扰动）
  - **文件锁自动处理（2026-09-04 新增）**：图编辑器保存 CSV 时 xlsx 可能正被 Excel/WPS 打开（文件锁占用，ClosedXML 保存抛 IOException）。`TrySaveWorkbook` 失败时经 Windows Restart Manager（`ExcelProcessHelper`）精确定位锁定该文件的电子表格进程 → 关闭 → 重试写回 → 成功后用系统默认应用重新打开恢复用户视图，全程自动。风险提示：被关闭的表格程序未保存的修改会丢失（文档恢复机制可找回）
  - **Command 列永远以 CSV 为准**：两侧都改且不一致时采用 CSV 值并警告（实现为单元格级三方合并，基准存 `.csv.cmdmap.json`）——Excel 模板应将 Command 列灰底标注"由 Unity 图编辑器维护"
  - **写回失败自动补写**：关进程后仍写不进去（非表格进程锁定等）仅告警，CSV 侧不受影响；sidecar 基准不更新，下次转换时三方合并自然产生差异并再次尝试写回
- **防死循环**：写回会更新 xlsx 修改时间 → 立即刷新 `AutoExcelConverter._lastWriteTicks`，避免下次轮询误判再次转换
- **`.xls` 限制**：ClosedXML 仅支持 `.xlsx`；检测到 `.xls` 时跳过镜像写回并警告建议转存（.xls 为 2003 旧格式，另存即迁移）
- **备选记录**：NPOI（Apache 2.0）支持 .xls 原地写，但 API 繁琐、格式保留弱，仅当出现 .xls 刚需再评估
- **资源拖拽**：从角色编辑器 / 资源管理器拖资源到画布直接生成命令节点（角色 → showChar、背景 → showbg、BGM → playBGM）
- **实时校验**：图结构校验器 11 规则（`VNRowPerfEditorSpec.md` §5）+ 11.2 形态判定 + 11.4 引用完整性检查
- **保存竞态防护**：写 CSV 用「临时文件 + `File.Replace`」原子写，并在保存期间挂起 `AutoExcelConverter` 的 2 秒轮询，避免转换器读到半写文件
- **两个 sidecar 职责严格分离**：
  - `.csv.cmdmap.json` —— 三方合并**基准**，正确性直接决定 Excel↔CSV 不丢数据，**必须**进版本控制
  - `.csv.graphpos.json` —— 节点位置/折叠状态**纯缓存**，丢失仅导致重新 AutoLayout，可 `.gitignore`（避免团队协作位置冲突）

### 11.6 待实现组件清单（2026-08-26 修订）

| 组件 | 说明 | 工作量 |
|------|------|--------|
| 命令节点化契约 | `[VNCommandMeta]`/`[VNParam]` 特性定义（Runtime）+ `CommandMetaReader` 反射读取（Editor）；替代原「Editor-only 手写 Schema 注册表」方案 | 3-5 天 |
| 行上下文 | `VNManager.CurrentLineContext`（ResolveLine 结果，Simulate/Execute 统一赋值）+ `VNAPI` 暴露 | 1-2 天 |
| 系统命令族 | `showbg/showChar/showSpeaker/showDialogue/playBGM/playVoice`，含隐式绑定解析、`Simulate`、`showDialogue` 的 `direct/typewriter` 与 `Interrupt` | 1-2 周 |
| 引擎三路径执行 | 默认模板 / 增强追加 / 定制全链的分流与回归测试 | 1-2 周 |
| 模板等价性回归测试 | EventCenter 事件时序逐帧对比（隐式路径 vs 模板链），守护 §11.3.2 硬契约 | 2-3 天 |
| **图 → AST（`GraphToAst`）** | **SP 分解（Fork→递归分支→共同 Join→续主链）；原规格遗漏的一环** | 3-4 天 |
| AST → 文本序列化器 | `ChainSerializer`，含括号归一化规则（Par 的 Seq 子项强制 `[]`、单子项包装透传） | 2-3 天 |
| 图结构校验器 | 11 规则 + 分级阻断（复用 `ChainParser.IsFlowCommand`） | 3-5 天 |
| GraphView 编辑器 | 五类节点 / 模板折叠胶囊 / 触碰提升 / 资源拖拽 / 元数据驱动参数面板 / 实时校验 | 3-4 周 |
| 持久化与可用性 | `GraphPosStore`（位置 sidecar）+ `GraphUndoStack`（文本快照栈）+ 链复制粘贴 + 原子写 | 3-5 天 |
| ~~AutoExcelConverter 改造~~ | ✅ **Phase 0 已完成**（列分工三方合并 + ClosedXML 镜像写回） | — |

### 11.7 遗留风险

| 风险 | 缓解措施 |
|------|---------|
| 系统命令与引擎隐式路径双实现漂移（showDialogue 命令 vs UpdateDialogue 引擎代码，同一逻辑两处维护） | 隐式路径内部逐步改为复用系统命令实现；**模板等价性回归测试**（§11.3.2）作为自动护栏 |
| 引擎三路径重构的回归负担（存档/快进/AutoPlay 全路径） | Phase 0 前置 Simulate 债务偿还（已完成）+ 三层形态专项回归测试 |
| 按需提升的单向性（误操作重置丢失定制内容） | 提升时确认弹窗；重置前二次确认；`GraphUndoStack` 提供撤销 |
| 特性标注是渐进工程（39 个命令），标注期内图体验不均质 | 「通用节点」是永久兼容层而非临时态，功能完整（可连线/拖拽/序列化）；任何命令都能上图 |
| 模板双分支结构的认知成本 | 默认折叠为单胶囊隐藏内部结构；展开时泳道标签明示「对话独立分支，不阻塞其他演出」 |

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
| 5 | `a -> jump(x) -> b` | jump 立即改行索引，b 在"行已切换"上下文执行，演出污染 | `ChainParser.ValidateFlowCommands`：流程命令非链尾时警告。**流程命令集合（2026-08-26 补全为 8 个）**：`jump / jumpif / jumpifnot / loadscript / loadscriptif / loadscriptifnot / choice / loadscene`——原仅 4 个，遗漏条件跳转族（它们同样改写行索引/剧本数据源）。现另提供公开方法 `ChainParser.IsFlowCommand(name)` 供图编辑器校验复用同一份定义 |
| 6 | `[a & loadscene(x)]` | 场景切换后旧场景 UI 销毁，残留分支命令操作已销毁对象（MissingReference） | 同上校验；且 `loadscene` 本身要求链尾 |
| 7 | `playvideo(v.mp4, loadscript(c2)) -> b` | playvideo 第二参数与 `->` 构成双重流程语义 | 校验警告：链式下建议 `playvideo(v.mp4) -> loadscript(c2)`。检测改为遍历 `FlowCommands` 集合（原硬编码 3 个，同样漏条件跳转族） |

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

1. **流程命令必须放链尾**：`jump` / `jumpif` / `jumpifnot` / `loadscript` / `loadscriptif` / `loadscriptifnot` / `choice` / `loadscene` 只能作为整条命令链最后一个命令
2. **playvideo 的"结束后命令"参数在链式中改用 `->`**：`playvideo(a.mp4) -> jump(x)`
3. **同一并行组内避免操作同一状态**：不要对同一背景、同一 flag 写两个并行命令
4. **`[]` 嵌套 ≤2 层**：更深嵌套会收到**警告**（不阻断执行、不阻断图编辑器保存）
5. **参数含 `&` `->` `[` `]` `,` `@Confirm:` 时必须引号包裹**

### B.7 词法/解析器修复记录（2026-08-26）

| # | 位置 | 问题 | 影响 | 修复 |
|---|------|------|------|------|
| 1 | `ChainLexer` 引号转义 | `if (cc == '\\') { pos += 2; }` 无边界检查 | 参数以 `\` 结尾时 `pos > len`，`Substring` 抛 `ArgumentOutOfRangeException`（解析崩溃） | 串尾时只前进 1 |
| 2 | `ChainLexer` 括号深度 | `parenDepth--` 无下限 | 多余 `)` 使深度变负 → `parenDepth == 0` 顶层判定永久失效 → 剩余整串被吞成一个 Token（`showbg(a)) -> wait(1)` 中 `-> wait(1)` 被静默吃掉） | clamp 到 0，语法错误交解析器报告 |
| 3 | `ChainParser` 深度警告 | 嵌套超限写入 `Errors` | `ChainParseResult.Success == false` + `Debug.LogError`；且图编辑器若以 `Success` 为保存闸门，3 层嵌套将**无法保存**——与 §7.5「警告不阻断」规范矛盾 | 改写入 `Warnings`（warnings 通道贯穿 4 个递归方法） |
| 4 | `ChainParser.FlowCommands` | 仅 4 个，漏条件跳转族 | `jumpif` 等非链尾时不告警 | 补全为 8 个 + 新增公开 `IsFlowCommand()` |
| 5 | `ScriptParser.SplitConfirmSection` | `@Confirm:` 用裸 `IndexOf`，不感知引号 | 参数含 `@Confirm:` 字面量时把一条完整命令劈成两半 | 新增引号感知的 `IndexOfConfirmToken()` |
| 6 | `VNManager.FastForwardToLine` | `lastLine` 在 `SimulateCommands` **之后**赋值（choice 分支相反） | 隐式绑定系统命令会静默读到**上一行**的 Text/Background | 统一提到模拟前赋值，删除 3 处冗余后置赋值 |
