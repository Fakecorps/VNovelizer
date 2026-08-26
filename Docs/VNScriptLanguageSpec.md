# VNScript 语言规范

> VNScript 是 VNovelizer 视觉小说插件的剧本演出编排 DSL（Domain-Specific Language）。
> 它与 Excel/CSV 协同工作：Excel 负责"说什么"（数据层），VNScript 负责"怎么演"（演出层）。
> 两者通过行 ID 关联，互不生成，冲突域完全隔离。

> ### ⚠️ 文档状态（2026-08-25）：设计参考存档（.vnscript 方案已正式搁置）
>
> 痛点评审（详见 `VNRefactoringPlan.md` §4 决策声明）显示：真实痛点是**分支可读性、演出数据耦合、编辑体验**，而非语言表达力（时序编排已被实装的命令链语法解决，变量/条件块非刚需）。故改为"**命令链可视化生态**"路线——见 `VNCommandChainSpec.md` 第 11 章"行演出编辑器"。
>
> **本规范的新地位：行演出编辑器的数据模型规格书**，概念映射关系：
>
> | VNScript 概念 | 行演出编辑器等价物 |
> |--------------|-------------------|
> | ScriptBlock（§3.2） | 一"行"（演出单元 + Confirm 边界） |
> | VAR 块（§3.3） | 数据列（Text/BG/BGM/立绘等，隐式绑定） |
> | Seq/Par（§3.4-3.5） | Command 列的命令链（`&`/`->`/`[]`，ChainParser 解析） |
> | Confirm（§3.6） | 行边界（点击推进/存档锚点/历史记录单位） |
> | 第 5 章 AST 结构 | 图编辑器节点模型的直接设计参考 |
>
> **重启触发条件**：当用户真实需要变量（VAR 引用）或条件块（if/macro）且命令链体系无法表达时，重新评估本方案。

---

## 目录

1. [设计目标](#1-设计目标)
2. [词法规范](#2-词法规范)
3. [语法结构](#3-语法结构)
4. [数据类型与字面量](#4-数据类型与字面量)
5. [AST 数据结构](#5-ast-数据结构)
6. [时序语义](#6-时序语义)
7. [命令系统](#7-命令系统)
8. [Source of Truth 工作流](#8-source-of-truth-工作流)
9. [解析器架构](#9-解析器架构)
10. [错误处理](#10-错误处理)
11. [保留字与关键字](#11-保留字与关键字)
12. [完整语法示例](#12-完整语法示例)
13. [未来扩展](#13-未来扩展)

---

## 1. 设计目标

### 1.1 核心原则

| 原则 | 说明 |
|------|------|
| **块边界统一** | 所有块用 `{}` 包裹，栈式匹配 |
| **时序三关键字** | `Seq`（串行链）/ `Par`（并行链）/ `Confirm`（用户交互） |
| **单一根 Seq** | 每个 ScriptBlock 内有且仅有一个顶层 `Seq` |
| **语义无歧义** | Seq 永远串行，Par 永远并行，行为不依赖嵌套位置 |
| **Par 是 fork-join 屏障** | 进入 Par 时并行启动所有直接子项，退出时等待全部完成 |
| **数据与演出分离** | VAR 区域（数据）自动生成，Seq/Par（演出）手写 |
| **字符串明确** | 引号包裹为字面文本，无引号为标识符/变量引用 |

### 1.2 与 Excel 的分工

| 职责 | 载体 | 内容 |
|------|------|------|
| 数据层 | Excel → CSV → VAR | 说话人、对话文本、背景、BGM、谁出场什么表情 |
| 演出层 | VNScript（手写） | 命令编排、时序控制、动画、并行/串行 |
| 交互层 | VNScript（Confirm） | 用户点击后执行的跳转命令 |

---

## 2. 词法规范

### 2.1 字符集

VNScript 源文件使用 UTF-8 编码，支持中文字符。

### 2.2 Token 分类

| Token 类型 | 说明 | 示例 |
|------------|------|------|
| `LBrace` / `RBrace` | 花括号 | `{` `}` |
| `LParen` / `RParen` | 圆括号 | `(` `)` |
| `LBracket` / `RBracket` | 方括号 | `[` `]` |
| `Keyword` | 关键字 | `Script` `VAR` `Seq` `Par` `Confirm` |
| `Identifier` | 标识符 | `showChar` `Amy` `spk` |
| `String` | 字符串字面量 | `"好了~别生气了"` |
| `Number` | 数值字面量 | `100` `0.5` `-50` |
| `HashAttr` | 属性后缀 | `#stage(left)` `#typewriter` |
| `Comma` | 逗号 | `,` |
| `Semicolon` | 分号 | `;` |
| `Colon` | 冒号 | `:`（角色ID:表情用） |
| `Assign` | 赋值号 | `=` |
| `Arrow` | 箭头 | `->`（Confirm 分支用） |
| `LineComment` | 行注释 | `// 注释` |
| `BlockComment` | 块注释 | `/* 注释 */` |
| `Newline` | 换行符 | `\n` |
| `EOF` | 文件结束 | — |
| `Unknown` | 无法识别的字符 | — |

### 2.3 标识符规则

```
标识符 = 字母 | 下划线 , { 字母 | 数字 | 下划线 }
```

- 以字母或下划线开头
- 可包含字母、数字、下划线
- 区分大小写
- 支持中文字符（用于角色名、资源名）

**合法标识符**：`Amy` `showChar` `_private` `角色01` `BGM01`

**非法标识符**：`2fast`（数字开头）`show-char`（含连字符）

### 2.4 字符串规则

```
字符串 = '"' , { 任意字符 | 转义序列 } , '"'
转义序列 = '\"' | '\\' | '\n' | '\t' | '\r'
```

- 用双引号 `"` 包裹
- 内部 `\"` 转义双引号
- 支持 UTF-8 中文

**合法字符串**：`"好了~别生气了"` `"She said \"Hi\""` `"line1\nline2"`

### 2.5 数值规则

```
数值 = [ '-' ] , 数字 , { 数字 } , [ '.' , { 数字 } ]
```

- 可选负号前缀
- 支持整数与浮点数
- 不支持科学计数法

**合法数值**：`100` `0.5` `-50` `3.14`

### 2.6 注释

```
行注释 = '//' , { 任意字符 } , 换行
块注释 = '/*' , { 任意字符 } , '*/'
```

- `//` 后直到行尾为注释
- `/* */` 可跨多行
- 注释在词法分析阶段被丢弃，不影响 AST

---

## 3. 语法结构

### 3.1 整体结构

一个 `.vnscript` 文件由若干个 **ScriptBlock（脚本块）** 组成：

```
文件 = ScriptBlock , { ScriptBlock }
```

每个 ScriptBlock 对应 Excel 中的一行（通过 ID 关联）。

### 3.2 ScriptBlock（脚本块）

```
ScriptBlock = "Script" , 标识符 , "ID" , 行号 , "{" , [ VAR块 ] , Seq块 , [ Confirm块 ] , "}"
```

| 组成 | 必填 | 说明 |
|------|------|------|
| `Script 脚本名` | ✅ | 脚本名（标识符），对应 Excel 文件名 |
| `ID 行号` | ✅ | 行号（标识符或数值），对应 Excel 行 ID |
| `VAR { }` | ❌ | 数据层，自动生成 |
| `Seq { }` | ✅ | 唯一根 Seq，演出编排 |
| `Confirm { }` | ❌ | 用户交互层，点击后执行 |

### 3.3 VAR 块（数据层）

```
VAR块 = "VAR" , "{" , { 变量声明 } , "}"
变量声明 = 标识符 , "=" , 值 , ";"
值 = 字符串 | 标识符 | 数组
数组 = "[" , [ 角色项 , { "," , 角色项 } ] , "]"
角色项 = 标识符 , ":" , 标识符
```

**约定**：
- 由工具从 Excel 自动生成，块首标记 `// AUTO-GENERATED`
- **只存数据**：谁说话、说什么、谁出场什么表情
- **不存坐标/缩放/翻转**（这些归 Seq/Par 演出层）
- 字符串值用双引号包裹

### 3.4 Seq 块（串行链）

```
Seq块 = "Seq" , "{" , { 子项 } , "}"
子项 = 命令 | Seq块 | Par块
```

**语义**：Seq 内所有直接子项按顺序串行执行，上一项完成才执行下一项。

### 3.5 Par 块（并行链）

```
Par块 = "Par" , "{" , { 子项 } , "}"
子项 = 命令 | Seq块 | Par块
```

**语义**：
- Par 内所有直接子项同时并行启动
- 退出 Par 块时等待全部子项完成（fork-join 语义）

### 3.6 Confirm 块（用户交互层）

```
Confirm块 = "Confirm" , "{" , { 命令 } , "}"
         | "Confirm" , "{" , { 分支项 } , "}"
分支项 = "->" , [ 字符串 ] , 命令 | Seq块
```

**两种形态**：

**形态 A：单一确认**（最常见）
```
Confirm {
    nextline();
}
```

**形态 B：多选分支**（未来扩展）
```
Confirm {
    -> "去天台" jump(200);
    -> "回教室" jump(300);
}
```

**约定**：
- 必须包含跳转命令（`nextline()` / `jump(id)`），否则解析报错
- 内部语法与 Seq 一致（可嵌套 Par）

**与 CSV `@Confirm:` 语法糖的映射**：CSV Command 列已实装等价语法——`@Confirm:` 之后的内容即形态 A 的 Confirm 块，未声明时默认 `nextline()`：

| CSV 写法 | 等价 DSL |
|------|------|
| `shake(0.5)&@Confirm:jump(1010)` | `Seq { shake(0.5); }` + `Confirm { jump(1010); }` |
| `@Confirm:jumpif(Favor>=60,1010)&jumpif(Favor<60,1011)` | `Confirm { jumpif(...); jumpif(...); }` |
| （未写 @Confirm:） | `Confirm { nextline(); }` |

CSV 侧由 `ScriptParser.SplitConfirmSection` 切分，运行时由 `VNManager.ExecuteConfirmExit` 执行（点击/AutoPlay/命令驱动推进统一入口）；快进预演（`FastForwardToLine`）同步模拟出口段以保持状态一致。CSV 的 `choice(...)` 命令对应形态 B 的运行时前身。

### 3.7 命令

```
命令 = 命令名 , "(" , [ 参数 , { "," , 参数 } ] , ")" , { 属性后缀 } , ";"
命令名 = 标识符
参数 = 变量引用 | 字面量
属性后缀 = "#" , 属性名 , [ "(" , 参数 , { "," , 参数 } , ")" ]
属性名 = 标识符
```

**示例**：
```
showChar(Amy)#stage(left)#fade(1.0);
showDialogue(dlg)#typewriter;
nextline();
```

### 3.8 嵌套规则总表

| 父 → 子 | 允许？ | 说明 |
|---------|--------|------|
| ScriptBlock → Seq | ✅ 唯一根 | 每个 ScriptBlock 有且仅有一个顶层 Seq |
| ScriptBlock → Par | ❌ 禁止 | 必须先有根 Seq |
| ScriptBlock → VAR | ✅ 可选 | 数据层，自动生成 |
| ScriptBlock → Confirm | ✅ 可选 | 交互层 |
| Seq → Seq | ✅ | 逻辑分组，内部仍串行 |
| Seq → Par | ✅ | 串行流中插入并行段（核心能力） |
| Seq → 命令 | ✅ | 叶子节点 |
| Par → Seq | ✅ | 并行分支内部串行 |
| Par → Par | ✅ | 并行段内再并行 |
| Par → 命令 | ✅ | 叶子节点 |
| Confirm → 命令 | ✅ | 确认命令链 |
| Confirm → Seq | ✅ | 确认块内串行 |
| Confirm → Par | ✅ | 确认块内并行 |
| 嵌套深度 | 建议 ≤2 层 | 根 Seq + 1~2 层嵌套 |

---

## 4. 数据类型与字面量

### 4.1 基本类型

| 类型 | 字面量语法 | 说明 | 示例 |
|------|-----------|------|------|
| **字符串** | `"..."` | 双引号包裹，支持转义 | `"Amy"` `"你好"` |
| **数值** | `数字` | 整数或浮点，可负 | `100` `0.5` `-50` |
| **标识符** | `字母开头` | 变量引用或角色 ID | `Amy` `spk` `BGM01` |

### 4.2 复合类型

| 类型 | 语法 | 说明 | 示例 |
|------|------|------|------|
| **数组** | `[item, item]` | VAR 中 chars 字段专用 | `[Amy:smile, Jack:angry]` |
| **角色项** | `角色ID:表情` | 数组元素，角色与初始表情 | `Amy:smile` |

### 4.3 变量引用

VNScript 的变量来自 VAR 区域，引用时直接写变量名（无 `$` 前缀）：

```
VAR {
    spk = "Amy";
}

Seq {
    showSpeaker(spk);    // spk 引用 VAR 中的值，运行时解析为 "Amy"
}
```

| 写法 | 含义 |
|------|------|
| `标识符`（无引号，在 VAR 中已定义） | 变量引用，运行时替换为 VAR 中的值 |
| `标识符`（无引号，在 VAR 中未定义） | 角色 ID 字面量（如 `Amy`） |
| `"文本"`（引号包裹） | 字面字符串，原样使用 |
| `数字` | 数值字面量 |

---

## 5. AST 数据结构

### 5.1 节点继承关系

```
ASTNode (抽象基类)
├── ScriptBlock
├── VarDecl
├── SeqNode
├── ParNode
├── CommandNode
├── HashAttr
├── Expr (表达式基类)
│   ├── StringLiteral
│   ├── NumberLiteral
│   └── IdentifierExpr
├── ConfirmNode
└── ConfirmBranch
```

### 5.2 节点详细定义

#### ASTNode（抽象基类）

| 字段 | 类型 | 说明 |
|------|------|------|
| `line` | `int` | 源码行号（错误定位） |
| `column` | `int` | 源码列号（错误定位） |

#### ScriptBlock（脚本块）

| 字段 | 类型 | 说明 |
|------|------|------|
| `scriptName` | `string` | 脚本名（如 `Chapter1`） |
| `id` | `string` | 行号（如 `1001`），与 Excel 关联 |
| `vars` | `List<VarDecl>` | VAR 区域变量声明列表 |
| `rootSeq` | `SeqNode` | 唯一根 Seq（演出编排） |
| `confirm` | `ConfirmNode` | Confirm 块（可为 null） |

#### VarDecl（变量声明）

| 字段 | 类型 | 说明 |
|------|------|------|
| `name` | `string` | 变量名 |
| `value` | `Expr` | 值（StringLiteral / ArrayExpr / IdentifierExpr） |

#### SeqNode（串行块）

| 字段 | 类型 | 说明 |
|------|------|------|
| `children` | `List<ASTNode>` | 子项列表（Command / Seq / Par），按顺序串行执行 |

#### ParNode（并行块）

| 字段 | 类型 | 说明 |
|------|------|------|
| `children` | `List<ASTNode>` | 子项列表（Command / Seq / Par），全部并行启动 |

#### CommandNode（命令）

| 字段 | 类型 | 说明 |
|------|------|------|
| `name` | `string` | 命令名（如 `showChar`） |
| `args` | `List<Expr>` | 参数列表（变量引用或字面量） |
| `attrs` | `List<HashAttr>` | `#属性` 后缀列表 |

#### HashAttr（属性后缀）

| 字段 | 类型 | 说明 |
|------|------|------|
| `name` | `string` | 属性名（如 `stage`） |
| `args` | `List<Expr>` | 属性参数（如 `left`） |

#### Expr（表达式基类）

| 子类 | 字段 | 说明 |
|------|------|------|
| `StringLiteral` | `value: string` | 字符串字面量 |
| `NumberLiteral` | `value: float` | 数值字面量 |
| `IdentifierExpr` | `name: string` | 标识符（变量引用或角色 ID） |
| `ArrayExpr` | `items: List<CharItem>` | 数组（chars 专用） |
| `CharItem` | `charId: string`, `emotion: string` | 角色项 |

#### ConfirmNode（Confirm 块）

| 字段 | 类型 | 说明 |
|------|------|------|
| `branches` | `List<ConfirmBranch>` | 分支列表（单分支 = 普通确认） |

#### ConfirmBranch（确认分支）

| 字段 | 类型 | 说明 |
|------|------|------|
| `label` | `string` | 分支显示文本（可选，多选分支用） |
| `child` | `ASTNode` | 该分支的执行块（Command / Seq / Par） |

### 5.3 C# 类定义参考

```csharp
namespace VNovelizer.DSL.AST
{
    public abstract class ASTNode
    {
        public int line;
        public int column;
    }

    public class ScriptBlock : ASTNode
    {
        public string scriptName;
        public string id;
        public List<VarDecl> vars = new();
        public SeqNode rootSeq;
        public ConfirmNode confirm;
    }

    public class VarDecl : ASTNode
    {
        public string name;
        public Expr value;
    }

    public class SeqNode : ASTNode
    {
        public List<ASTNode> children = new();
    }

    public class ParNode : ASTNode
    {
        public List<ASTNode> children = new();
    }

    public class CommandNode : ASTNode
    {
        public string name;
        public List<Expr> args = new();
        public List<HashAttr> attrs = new();
    }

    public class HashAttr : ASTNode
    {
        public string name;
        public List<Expr> args = new();
    }

    public abstract class Expr : ASTNode { }

    public class StringLiteral : Expr
    {
        public string value;
    }

    public class NumberLiteral : Expr
    {
        public float value;
    }

    public class IdentifierExpr : Expr
    {
        public string name;
    }

    public class ArrayExpr : Expr
    {
        public List<CharItem> items = new();
    }

    public class CharItem
    {
        public string charId;
        public string emotion;
    }

    public class ConfirmNode : ASTNode
    {
        public List<ConfirmBranch> branches = new();
    }

    public class ConfirmBranch : ASTNode
    {
        public string label;     // 可为 null（单一确认）
        public ASTNode child;    // Command / Seq / Par
    }
}
```

---

## 6. 时序语义

### 6.1 执行模型总表

| 构造 | 进入时 | 内部执行 | 退出时 |
|------|--------|----------|--------|
| `Seq { ... }` | 按顺序开始执行第一个子项 | 子项**串行**（上一项完成才下一项） | 全部子项完成 |
| `Par { ... }` | **同时**启动所有直接子项 | 子项**并行** | 等待全部子项完成（join） |
| `命令;` | 立即执行 | — | 执行完成 |
| 顶层 `Seq`（根） | ScriptBlock 执行时启动 | — | 全部完成，等待 Confirm |
| `Confirm { ... }` | 等待用户点击 | — | 执行完命令后跳转 |

### 6.2 命令完成定义

命令分为两类，"完成"的含义不同：

| 类型 | 例子 | "完成"意味着 |
|------|------|------------|
| 瞬时命令 | `showSpeaker(Amy)` | 立即返回，下一条立即执行 |
| 动画命令 | `showChar(Amy)#fade(1.0)` | 等动画播完 |

这复用 VNovelizer 现有的 `VNCommand` 基类区分：

```csharp
abstract bool Execute(string args);           // 同步：瞬间完成
virtual IEnumerator ExecuteAsync(string args); // 异步：协程，等它跑完
```

### 6.3 Seq/Par 嵌套组合

#### 串行流中插入并行段

```
Seq {
    A;              // 串行
    Par {           // 并行屏障
        B1;
        B2;
    }               // 等 B1/B2 都完成
    C;              // 回到串行
}
```

执行流程：
```
A ──→ ┌─ Par ──┐
      │ B1  B2 │  （B1/B2 同时启动）
      └── join ┘ ──→ C
```

#### 并行中嵌套串行（分支内部串行）

```
Par {
    Seq { A1; A2; }    // 分支1：A1→A2 串行
    Seq { B1; B2; }    // 分支2：B1→B2 串行
}                       // 等两条分支都完成
```

执行流程：
```
┌─ Par ──────────────┐
│ A1→A2    B1→B2     │  （两条分支并行，分支内串行）
└────── join ────────┘
```

#### 深层组合（串行→并行→串行→并行）

```
Seq {
    A;                      // 串行
    Par {                   // 并行段
        Seq { B1; B2; }     //   分支1串行
        Seq { C1; C2; }     //   分支2串行
    }                       // join
    D;                      // 串行
    Par {                   // 并行段
        E1;
        E2;
    }                       // join
    F;                      // 串行
}
```

---

## 7. 命令系统

### 7.1 命令语法

```
命令名(参数1, 参数2, ...)#属性1(值1)#属性2(值2);
```

| 部分 | 说明 |
|------|------|
| 命令名 | 标识符，如 `showChar`、`showDialogue` |
| 参数 | 位置参数，逗号分隔，括号包裹 |
| 属性后缀 | `#` 前缀，顺序无关，可省略，可扩展 |
| 终止符 | 分号 `;` |

### 7.2 内置命令清单（复用 VNovelizer 现有命令）

DSL 底层复用 VNovelizer 现有的 39 个命令（`CommandManager` 注册），通过 `CommandManager.ExecuteCommand()` 执行。

#### 流程控制

| 命令 | 语法 | 说明 |
|------|------|------|
| `nextline` | `nextline();` | 推进到下一行（DSL 层语义，映射点击推进） |
| `jump` | `jump(id);` | 跳转到指定行 ID |
| `jumpif` | `jumpif(cond, id);` | 条件为真时跳转（`Flag_Favor >= 50`、`!Met_Amy` 等表达式） |
| `jumpifnot` | `jumpifnot(cond, id);` | 条件为假时跳转 |
| `loadscript` | `loadscript(name[, startId]);` | 加载新剧本，可指定起始行 |
| `loadscriptif` | `loadscriptif(cond, name[, startId]);` | 条件为真时加载剧本 |
| `loadscriptifnot` | `loadscriptifnot(cond, name[, startId]);` | 条件为假时加载剧本 |
| `loadscene` | `loadscene(sceneName);` | 加载 Unity 场景 |
| `choice` | `choice(text \| cmd);` | 分支选项（本地化：`choice(@loc:key \| jump(...))`） |
| `exit` | `exit();` | 返回主菜单场景 |

#### 视觉演出

| 命令 | 语法 | 说明 |
|------|------|------|
| `showbg` | `showbg(bg);` | 显示背景（DSL 层语义） |
| `bgfade` | `bgfade(imageName[, duration]);` | 背景淡入切换（duration 缺省 1.0） |
| `shake` | `shake(target[, duration, strength]);` | 震动效果。target：`screen` / `dialogue` / `L`/`ML`/`M`/`MR`/`R` 立绘槽 |
| `wait` | `wait(seconds);` | 等待指定秒数 |
| `playvideo` | `playvideo(filename[, nextCmd]);` | 播放视频，结束后可执行一条命令 |
| `playanim` | `playanim(name[, pos[, loop]]);` | 播放动画（pos 缺省 M；loop 循环需配 stopanim） |
| `stopanim` | `stopanim(name);` | 停止动画 |
| `playparticle` | `playparticle(name);` | 播放粒子特效 |
| `stopparticle` | `stopparticle(name);` | 停止粒子特效 |
| `fadeBlackIn` | `fadeBlackIn(duration);` | 黑幕淡入（画面显现） |
| `fadeBlackOut` | `fadeBlackOut(duration);` | 黑幕淡出（完成后自动推进下一行） |
| `hide` | `hide();` | 隐藏对话框（大图/CG 模式） |

#### 立绘操作

| 命令 | 语法 | 说明 |
|------|------|------|
| `showChar` | `showChar(charId)#stage(pos);` | 显示角色立绘 |
| `showSpeaker` | `showSpeaker(spk);` | 设置说话人 |
| `showDialogue` | `showDialogue(dlg)#typewriter;` | 显示对话文本 |
| `setExpression` | `setExpression(charId, group, emotion);` | 换表情（group 对应 CharacterProfile 二维立绘分组） |
| `charmove` | `charmove(charId, x, y[, duration]);` | 移动立绘（duration 缺省 0.5） |
| `charjump` | `charjump(charId[, duration, times, height]);` | 立绘跳跃（缺省 0.4 / 1 次 / 30px） |
| `charflip` | `charflip(charId[, dir]);` | 翻转立绘（dir：`1`/`-1`/`left`/`right`，缺省切换） |
| `charfadein` | `charfadein(charId[, duration]);` | 立绘淡入（duration 缺省 0.5） |
| `charfadeout` | `charfadeout(charId[, duration]);` | 立绘淡出（duration 缺省 0.5） |
| `setchartrans` | `setchartrans(charId, x, y, scale);` | 设置立绘变换（保留翻转朝向） |

> 注：当前 CSV 命令实现以槽位 posCode（`L/ML/M/MR/R`）定位立绘；上表以 `charId` 定位是本语言的升级设计（tag 即槽位），两者在转换层兼容。

#### 音频

| 命令 | 语法 | 说明 |
|------|------|------|
| `playBGM` | `playBGM(bgm);` | 播放背景音乐（DSL 层语义；CSV 中 BGM 由列继承驱动） |
| `playsfx` | `playsfx(name[, times]);` | 播放音效（times 缺省 1） |

#### 变量与标志

| 命令 | 语法 | 说明 |
|------|------|------|
| `setboolflag` | `setboolflag(key[, value]);` | 设置布尔标志（value 缺省 true） |
| `setintflag` | `setintflag(key, value);` | 设置整数标志，支持 `+10`/`-10`/`*2`/`/2` 相对运算 |
| `setstringflag` | `setstringflag(key, value);` | 设置字符串标志（值含逗号需引号包裹） |
| `unlockcg` | `unlockcg(name);` | 解锁 CG |
| `unlockmusic` | `unlockmusic(name);` | 解锁音乐 |
| `unlockscene` | `unlockscene(name);` | 解锁场景回想 |

#### 文本样式

| 命令 | 语法 | 说明 |
|------|------|------|
| `t_color` | `t_color(R, G, B);` | 设置文本颜色（0-255，不继承） |
| `t_size` | `t_size(fontSize);` | 设置文本字号（10-200，不继承） |
| `settextspeed` | `settextspeed(speed);` | 设置打字速度（秒/字） |
| `setautospeed` | `setautospeed(speed);` | 设置自动播放速度 |
| `showprompt` | `showprompt(text[, duration]);` | 显示提示文字（duration 缺省 2 秒） |

#### 配置

| 命令 | 语法 | 说明 |
|------|------|------|
| `config` | `config(key:value);` | 配置命令。key：`voice` / `textspeed` / `autospeed` |

### 7.3 属性后缀清单

| 属性 | 语法 | 说明 | 适用命令 |
|------|------|------|----------|
| `#stage` | `#stage(left)` / `#stage(midleft)` / `#stage(center)` / `#stage(midright)` / `#stage(right)` | 站位预设（五槽位） | `showChar` |
| `#fade` | `#fade(duration)` | 淡入淡出 | `showChar` / `showbg` |
| `#typewriter` | `#typewriter` | 打字机效果 | `showDialogue` |
| `#pos` | `#pos(x, y)` | 自定义坐标 | `showChar` / `charmove` |
| `#express` | `#express(group, emotion)` | 表情覆盖（group 对应二维立绘分组） | `showChar` |
| `#scale` | `#scale(value)` | 缩放 | `showChar` |
| `#flip` | `#flip(true)` / `#flip(false)` | 翻转 | `showChar` |

属性后缀是**可扩展**的——第三方命令可自定义属性，解析器自动透传。

---

## 8. Source of Truth 工作流

### 8.1 核心原则

> Excel 管"说什么"，VNScript 管"怎么演"，用 ID 关联，互不生成。

### 8.2 关联关系

```
Excel (CSV)                          VNScript 脚本 (.vnscript)
┌──────────────────────────┐        ┌──────────────────────────┐
│ ID=1001, Speaker=Amy,     │←─ID──→│ Script Chapter1 ID 1001 { │
│ Text="好了~别生气了",     │  关联 │   VAR {    ← 自动生成      │
│ BGM=BGM01, BG=操场        │       │     spk = "Amy";           │
│ CharLeft=Amy#uniform#Normal │       │     dlg = "好了~别生气了"; │
│ CharMid=Jack#casual#Angry   │       │   }                        │
└──────────────────────────┘        │   Seq {     ← 手写不覆盖    │
                                    │     showChar(Amy)#stage(left)│
                                    │   }                        │
                                    │   Confirm {                │
                                    │     nextline();            │
                                    │   }                        │
                                    │ }                          │
                                    └──────────────────────────┘
```

### 8.3 VAR 字段映射

| Excel/CSV 列 | VAR 变量 | 类型 |
|-------------|----------|------|
| Speaker | `spk` | 字符串 |
| Text | `dlg` | 字符串 |
| Background | `bg` | 字符串 |
| BGM | `bgm` | 字符串 |
| Voice | `voice` | 字符串 |
| CharLeft/Mid_Left/Mid/Mid_Right/Right | `chars` | 数组 `[角色ID#分组#表情, ...]` |
| HeadProfile | `head` | 字符串 |

### 8.4 工作流程

```
编剧：
  1. 在 Excel 填数据（台词、角色、背景、BGM）
  2. 点击"转换" → 工具读取 CSV，生成/更新 .vnscript 的 VAR 块
  3. 在 .vnscript 的 Seq/Par 块里写演出编排

运行时：
  1. 解析器读取 .vnscript，生成 AST
  2. 通过当前行 ID 找到对应 ScriptBlock
  3. 注入 VAR（从 CSV 读取的数据覆盖 VAR）
  4. 执行根 Seq（串行/并行调度命令）
  5. 等待 Confirm（用户点击）
  6. 执行 Confirm 块
```

### 8.5 一致性规则

| 场景 | 行为 |
|------|------|
| Excel 改台词 | 只更新 VAR 块，Seq/Par 不受影响 |
| VNScript 改演出 | Excel 不受影响 |
| VNScript 缺少某 ID 的块 | 该行用"默认演出"（背景+角色+对话直接显示，无动画） |
| Excel 缺少某 ID 的行 | VNScript 块报错 |

---

## 9. 解析器架构

### 9.1 处理流程

```
.vnscript 文本
    │
    ▼
Lexer（词法分析）→ Token 流
    │
    ▼
Parser（语法分析）→ AST（抽象语法树）
    │
    ▼
执行器 → 编译为 CommandManager 命令序列 → 执行
```

### 9.2 技术选型

| 层 | 技术 | 理由 |
|----|------|------|
| 词法+语法分析 | **Pidgin**（Parser Combinator，MIT） | 声明式 API，代码量少 50%，错误信息自带 |
| AST 执行 | 复用 VNovelizer `CommandManager` | 零成本，复用现有 31+ 命令 |
| 编辑体验 | UI Toolkit 正则高亮（MVP）/ Monaco Editor（标准档） | 见技术选型文档 |

### 9.3 执行器伪代码

```csharp
// 统一的节点执行器：Seq 串行，Par 并行
IEnumerator ExecuteNode(ASTNode node)
{
    switch (node)
    {
        case CommandNode cmd:
            if (cmd.IsAsync)
                yield return cmd.ExecuteAsync(args);   // 等协程结束
            else
                cmd.Execute(args);                      // 瞬间完成
            break;

        case SeqNode seq:
            // Seq：子项串行执行
            foreach (var child in seq.children)
                yield return ExecuteNode(child);        // 逐个等待
            break;

        case ParNode par:
            // Par：子项并行启动，全部完成才继续
            var routines = new List<IEnumerator>();
            foreach (var child in par.children)
                routines.Add(ExecuteNode(child));

            // 启动全部并行协程
            var handles = new List<Coroutine>();
            foreach (var r in routines)
                handles.Add(MonoManager.GetInstance().StartCoroutine(r));

            // 等待全部完成（join）
            bool allDone = false;
            while (!allDone)
            {
                yield return null;                      // 等一帧
                allDone = true;
                // 实际通过引用计数或标志位判断
            }
            break;
    }
}

// ScriptBlock 执行入口：启动唯一根 Seq
IEnumerator ExecuteScriptBlock(ScriptBlock block)
{
    // 1. 注入 VAR
    foreach (var v in block.vars)
        runtimeEnv.SetVar(v.name, csvData[v.name]);

    // 2. 执行根 Seq
    yield return ExecuteNode(block.rootSeq);

    // 3. 等待 Confirm（用户点击）
    yield return WaitForConfirm();
    yield return ExecuteNode(block.confirm);
}
```

---

## 10. 错误处理

### 10.1 原则

1. **收集所有错误**：不遇到第一个错误就停，继续解析，报告全部问题
2. **错误恢复**：跳到下一个 `;`、`}` 或下一个 `Script` 关键字继续解析
3. **精确定位**：每个错误带行号列号
4. **可读信息**：错误描述人类可读，给出修复建议

### 10.2 错误类型

| 类型 | 示例 | 检测时机 |
|------|------|----------|
| 词法错误 | 未闭合的字符串、非法字符 | Lexer 阶段 |
| 语法错误 | 缺少分号、括号不匹配、块未闭合 | Parser 阶段 |
| 语义错误 | 未知命令名、缺少必需参数 | 语义分析阶段 |
| 一致性错误 | Confirm 缺少跳转命令、VAR 缺少必需字段 | 语义分析阶段 |
| 资源错误 | 引用的角色/背景/BGM 不存在 | 运行时（可选预检） |

### 10.3 错误输出格式

```
[Chapter1.vnscript:15:12] 错误：命令 'showCha' 未知，是否想用 'showChar'？
[Chapter1.vnscript:15:25] 错误：缺少分号 ';'
[Chapter1.vnscript:23:5]  错误：Confirm 块缺少跳转命令（nextline/jump）
[Chapter1.vnscript:30:1]  错误：ScriptBlock 未闭合，缺少 '}'

解析完成：共 4 个错误，0 个警告。
```

---

## 11. 保留字与关键字

### 11.1 关键字列表

| 关键字 | 上下文 | 说明 |
|--------|--------|------|
| `Script` | ScriptBlock 头 | 声明脚本块 |
| `ID` | ScriptBlock 头 | 声明行号 |
| `VAR` | 数据层块 | 数据层区域 |
| `Seq` | 演出层块 | 串行链 |
| `Par` | 演出层块 | 并行链 |
| `Confirm` | 交互层块 | 用户交互 |
| `import` | 文件级（未来） | 导入其他脚本 |
| `macro` | 文件级（未来） | 宏定义 |
| `if` | Seq 内（未来） | 条件块 |

### 11.2 命名约定

| 约定 | 规则 | 示例 |
|------|------|------|
| 脚本名 | 大驼峰 | `Chapter1` `OpeningScene` |
| 行号 | 数字字符串 | `1001` `2005` |
| 变量名 | 小写 | `spk` `dlg` `bg` `bgm` `chars` |
| 命令名 | 小驼峰 | `showChar` `showDialogue` `setExpression` |
| 属性名 | 小写 | `stage` `fade` `typewriter` `pos` |
| 角色 ID | 大驼峰 | `Amy` `Jack` `Bob` |
| 表情名 | 小写 | `smile` `angry` `normal` |

---

## 12. 完整语法示例

### 12.1 简单示例

```
// Chapter1.vnscript

Script Chapter1 ID 1001 {
    // VAR: 自动从 Excel 生成，请勿手编
    VAR {
        spk   = "Amy";
        dlg   = "好了~别生气了";
        bg    = "操场";
        bgm   = "BGM01";
        chars = [Amy:smile, Jack:angry];
    }

    Seq {
        showSpeaker(spk);
        showbg(bg);
        playBGM(bgm);

        Par {
            showChar(Amy)#stage(left);
            showChar(Jack)#stage(right);
        }

        showDialogue(dlg)#typewriter;
    }

    Confirm {
        nextline();
    }
}
```

### 12.2 复杂示例（含嵌套）

```
Script Chapter1 ID 1003 {
    VAR {
        spk   = "Amy";
        dlg   = "大家听我说！";
        bg    = "教室";
        bgm   = "tension";
        chars = [Amy:serious, Bob:worried, Cat:surprised];
    }

    Seq {
        // 串行：布置场景
        showbg(bg);
        playBGM(bgm);

        // 并行：三个角色同时淡入
        Par {
            showChar(Amy)#stage(left);
            showChar(Bob)#stage(center);
            showChar(Cat)#stage(right);
        }

        // 串行：Amy 说话
        showSpeaker(spk);
        showDialogue(dlg)#typewriter;

        // 并行中嵌套串行：震屏+对话同时，Amy 移动+换表情
        Par {
            Seq {
                shake(screen, 0.3);
                wait(0.5);
            }
            Seq {
                charmove(Amy, x=100, 0.5);
                setExpression(Amy, angry);
            }
            showDialogue("！！");
        }

        // 串行：等待
        wait(1.0);
    }

    Confirm {
        nextline();
    }
}
```

### 12.3 多选分支示例（未来扩展）

```
Script Chapter1 ID 1005 {
    VAR {
        dlg = "你要去哪里？";
    }

    Seq {
        showDialogue(dlg);
    }

    Confirm {
        -> "去天台" jump(200);
        -> "回教室" jump(300);
        -> "去图书馆" jump(400);
    }
}
```

---

## 13. 未来扩展

### 13.1 计划中的扩展

| 扩展 | 语法 | 说明 |
|------|------|------|
| **导入** | `import "common.vnscript";` | 复用其他脚本的变量/宏 |
| **宏定义** | `macro Greeting(char) { showChar(char); ... }` | 复用命令序列 |
| **条件块** | `if (metBob) { ... }` | 条件执行 |
| **循环** | 暂不需要 | DSL 应保持简单 |
| **变量作用域** | ScriptBlock 级 | 块间隔离 |
| **多文件** | `import` + Script 名称作用域 | 拆分大剧本 |
| **命名空间** | `Amy.Chapter1` | 角色名/资源名消歧 |

### 13.2 不计划支持

| 特性 | 原因 |
|------|------|
| 用户自定义函数 | 增加复杂度，违背"DSL 应保持简单" |
| 异常处理 | DSL 不需要 try-catch |
| 指针/引用 | 不适用 |
| 类/继承 | 不适用 |

---

## 附录 A：EBNF 语法摘要

```
文件           = ScriptBlock , { ScriptBlock } ;

ScriptBlock    = "Script" , 标识符 , "ID" , 行号 , "{" , [ VAR块 ] , Seq块 , [ Confirm块 ] , "}" ;

VAR块          = "VAR" , "{" , { 变量声明 } , "}" ;
变量声明        = 标识符 , "=" , 值 , ";" ;
值             = 字符串 | 标识符 | 数组 ;
数组           = "[" , [ 角色项 , { "," , 角色项 } ] , "]" ;
角色项          = 标识符 , ":" , 标识符 ;

Seq块          = "Seq" , "{" , { 子项 } , "}" ;
Par块          = "Par" , "{" , { 子项 } , "}" ;
子项           = 命令 | Seq块 | Par块 ;

命令           = 命令名 , "(" , [ 参数 , { "," , 参数 } ] , ")" , { 属性后缀 } , ";" ;
命令名          = 标识符 ;
参数           = 字符串 | 标识符 | 数值 ;
属性后缀        = "#" , 属性名 , [ "(" , 参数 , { "," , 参数 } , ")" ] ;
属性名          = 标识符 ;

Confirm块      = "Confirm" , "{" , { 命令 | 分支项 } , "}" ;
分支项          = "->" , [ 字符串 ] , ( 命令 | Seq块 ) ;

字符串          = '"' , { 任意字符 | 转义序列 } , '"' ;
数值           = [ "-" ] , 数字 , { 数字 } , [ "." , { 数字 } ] ;
标识符          = ( 字母 | 下划线 ) , { 字母 | 数字 | 下划线 } ;
注释           = "//" , { 任意字符 } , 换行 | "/*" , { 任意字符 } , "*/" ;
```
