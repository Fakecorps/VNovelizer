# VNovelizer 行演出编辑器 · 实施规格

> **定位**：Phase 1 行演出编辑器（GraphView）的实施基线文档，覆盖 UI 布局、节点体系、图结构校验、序列化与持久化的完整技术实现点与规范。
>
> **前置依赖**：
> - 架构决策：`VNCommandChainSpec.md` 第 11 章「行演出编辑器」（三层行形态 / 可覆盖模板 / 隐式绑定）
> - 命令链语法与真实参数签名：`VNCommandChainSpec.md` 全文（`&`/`->`/`[]`、Fork/Join 语义、流程命令链尾规则）
> - DSL 数据模型参考：`VNScriptLanguageSpec.md`（设计参考存档，ScriptBlock/VAR/Confirm 映射）
> - 路线图：`VNRefactoringPlan.md` §12（Phase 1 任务清单）
> - UI 原型：`Docs/mockups/RowPerfEditorMockup.html`（V8）
>
> **修订记录**：
> - **2026-08-26（V2 评审修订）**：补入缺失的 `GraphToAst` 环节（决策 d5）；节点化契约从「Editor-only 手写 Schema」改为「Runtime C# 特性 + 反射读取」（**决策 s4 废止重定义**）；默认模板改为双分支 Par 并确立「提升不改变演出」硬契约（决策 tpl）；`showDialogue` 增加 `direct/typewriter` 显示方式参数（决策 dlg）；隐式绑定行上下文改由 `VNManager.CurrentLineContext` 提供（决策 s5）；位置持久化拆出独立 sidecar（决策 s2b）；补入 Undo、链复制粘贴、保存竞态处理。

---

## 0. 总体架构与文件结构

```
Editor/RowPerformanceEditor/
├── RowPerfEditorWindow.cs      # 窗口宿主（三栏 + 工具栏 + 状态栏 + 行导航）
├── RowGraphView.cs             # GraphView 子类（画布 + 双泳道 + 拖拽 + 自动布局）
├── Nodes/
│   ├── CommandNodeView.cs      # 命令节点（结构化表单态 / 通用节点态 / 模板影子态）
│   ├── TerminalNodeView.cs     # 终端胶囊（行开始/等待确认/出口开始/跳转终点）
│   ├── ForkJoinNodeView.cs     # FORK/JOIN 并行胶囊
│   └── TemplateCapsuleNode.cs  # 默认演出折叠胶囊（可双击展开为完整影子链）
├── ChainGraphValidator.cs      # 图结构校验器（10 规则 + 分级阻断）
├── GraphToAst.cs               # 图 → AST（SP 分解，ChainParser 的图侧对偶）
├── ChainSerializer.cs          # AST → 命令链文本（含括号归一化）
├── ChainAutoLayout.cs          # 自动布局（执行深度分层 + Fork 分支均分）
├── RowPromotion.cs             # 三层行形态判定 + 模板影子生成 + 按需提升
├── CommandMetaReader.cs        # 反射读取 [VNCommandMeta]/[VNParam]（Editor 侧缓存）
├── InspectorBuilder.cs         # Inspector 动态表单（元数据驱动 + 隐式绑定切换）
├── GraphUndoStack.cs           # 撤销栈（命令链文本 + 节点位置快照，整图重建）
├── GraphPosStore.cs            # 节点位置 sidecar 读写（.csv.graphpos.json）
└── ResourceDragPanel.cs        # 命令面板 + 资源拖拽源

Runtime/Scripts/VNovelizer/Core/Commands/Meta/
├── VNCommandMetaAttribute.cs   # [VNCommandMeta(分类, 描述)]
├── VNParamAttribute.cs         # [VNParam(名, 类型, 取值域, 默认值, 可隐式绑定)]
└── VNParamType.cs              # 参数类型枚举（含 CharacterId/BackgroundName/BgmName 等动态取值域标记）
```

**技术栈**：`UnityEditor.Experimental.GraphView`（画布/节点/端口/边/拖拽）+ UIElements（面板/表单）+ 现有 `ChainParser`（解析复用）。

**核心数据流（双向闭环）**：

```
CSV Command 列 ──ChainParser.Parse──► AST ──AstToGraph + AutoLayout──► GraphView 图
                                                                            │ 用户编辑
GraphView 图 ──Validator──► GraphToAst ──► AST ──ChainSerializer──► 文本 ──┘
                                                     │
                                        ChainParser 反解析 → AST 等价性自校验
                                                     │ 通过且图确有变更
                                                     ▼
                                              写回 CSV Command 列
```

**规范**：任一环失败即阻断保存，错误定位回具体图节点（标红 + 状态栏汇总）。

### 0.1 前置组件落地状态（2026-08-26）

编辑器 UI（⑨-⑭）之前的全部前置件已实现，且**均不依赖 GraphView**——这是有意的分层：
转换与校验逻辑放在 Runtime 的 `Core/Commands/Chain/`，可脱离 UI 独立测试。

| 组件 | 位置 | 职责 |
|------|------|------|
| `ChainGraph` / `ChainGraphNode` / `ChainGraphEdge` | Runtime `Chain/` | **UI 无关**的图数据模型 + 邻接查询。UI 层只持有它的引用 |
| `GraphToAst` | Runtime `Chain/` | 图 → AST 的 SP 分解（决策 d5） |
| `AstToGraph` | Runtime `Chain/` | AST → 图（加载方向），同时生成节点身份 ID `{序号}:{命令名}` |
| `ChainSerializer` | Runtime `Chain/` | AST → 文本 + 三条括号规则 + 幂等自校验 + 结构等价比较 |
| `ChainGraphValidator` | Runtime `Chain/` | 11 条规则 + `Fatal`/`Warning` 分级 |
| `DefaultPerformanceTemplate` | Runtime `Chain/` | 默认模板的**单一信息源**（提升/影子渲染/等价测试三方共用） |
| `PerformanceEventRecorder` | Runtime `Diagnostics/` | EventCenter 事件时序录制 + 比对 |
| `ChainRoundTripTestWindow` | Editor | 文本→AST→图→AST→文本 往返一致性验证（10 个用例） |
| `TemplateEquivalenceTestWindow` | Editor | 模板等价性回归测试（硬契约验收） |
| `CommandMetaInspectorWindow` | Editor | 元数据检查器 + 标注进度看板 |

**两个验证工具是这批前置件的关键**：SP 分解与括号规则都属于"看起来对但极易出错"的逻辑（例如 `&` 优先级高于 `->`，使 `a->b & c->d` 被解析成完全不同的结构），这类正确性必须可执行验证，不能靠推理。

---

## 1. 窗口与总体架构

**职责**：承载编辑器，管理三栏布局、行导航、保存编排。

**技术实现点**：
- `RowPerfEditorWindow : EditorWindow`，由 `ScriptManager` 的 `previewTable` Command 单元格双击打开。
- 布局（UIElements）：工具栏（42px）· 左命令面板（225px）· 中画布（flex）· 右 Inspector（280px）· 状态栏（26px）。
- `RowGraphView` 作为 UIElements 子元素嵌入中央（非独立 GraphViewWindow，便于三栏共存）。
- 行导航：`◀ ▶` / ID 跳转 → 重新加载该行 AST → 重渲染画布 + Inspector。切换行前若有未保存变更 → 提示保存/丢弃。

**规范**：
- 数据源只挂 CSV（`ScriptParser` 解析），不直接读 xlsx。
- CSV 不存在时提示「先转换剧本」，不崩溃。

---

## 2. 画布与双泳道

**职责**：双链平级展示 + 缩放平移 + 节点自由拖拽。

**技术实现点**：
- `RowGraphView : GraphView`：`ContentDragger` 平移、`SetupZoom` 滚轮缩放、`RectangleSelector` 框选、`GridBackground` 点阵。
- 双泳道：进入段主链（左，紫系）+ 出口段 Confirm 链（右，绿系），**同一起始高度**（平级，非主从）；泳道标题用静态 `Label` 叠加。
- 拖拽：GraphView 原生 `Draggable`；拖动结束监听 `graphViewChanged` 保存位置。
- `ChainAutoLayout`：执行深度分层定 Y，Fork 分支横向均分定 X，Join 归位。

**规范**：
- 默认缩放 100%；MiniMap 默认关闭（行级节点少，可选开启）。
- 双链之间留 ≥100px 空白通道，供跨链点击虚线走线。

---

## 3. 节点体系（五类）

| 节点类 | 对应 AST | 端口结构 | 视觉 |
|-------|---------|---------|------|
| `CommandNodeView` | `CommandNode` | 上 in / 下 out（`capacity.Single`） | 方块卡片：头=命令名+分类色带+角标，体=参数 chip |
| `TerminalNodeView` | 链边界 | 见下方端口说明 | 药丸胶囊 |
| `ForkJoinNodeView` | `ParNode` | Fork：1 in + N out；Join：N in + 1 out（`capacity.Multiple`） | 紫/绿圆角胶囊「FORK ∥ / JOIN ⏫」 |
| `TemplateCapsuleNode` | 无（模板折叠态） | 上 in / 下 out | 半透明胶囊「默认演出」+ 引用列徽章，双击展开 |
| 模板影子（`CommandNodeView` 的影子态） | 无（运行时生成） | 同 CommandNodeView | 半透明虚线 + 🔒，仅视图层 |

**端口说明（`TerminalNodeView` 四种形态）**：

| 终端 | in | out | 说明 |
|------|----|-----|------|
| ▷ 行开始 | 无 | 有 | 进入链起点 |
| ⏸ 等待确认 | 有（主链汇入） | **有**（点击虚线 → 出口开始） | **双端口**（原规格记为「单侧端口」有误） |
| ⏵ 出口开始 | 有（点击虚线） | 有 | 出口链起点 |
| ⏭ 跳转终点 | 有 | 无 | 出口链终点 |

**技术实现点**：均继承 GraphView `Node`；样式用 USS；端口 `Port.Create<Edge>(Orientation.Vertical, Direction.Input/Output, capacity, type)`。

**规范（语义角标体系）**：
- 📎 引用数据列（隐式绑定）｜↩ 不继承（charmove 等）｜⚙ 无元数据（通用节点态）｜⚠ 未注册命令｜链尾（流程命令）。
- 流程命令节点仅可连到链尾（判定调用 `ChainParser.IsFlowCommand`，见 §5）。

---

## 4. 端口与连线

| 边类型 | 颜色 | 语义 | 实现 |
|-------|------|------|------|
| 串行边 | 灰 `#888` | `->` 顺序执行 | `Edge`，端口默认色 |
| 并行分支边 | 紫 `#9C8FCE` | Fork 分叉 / Join 汇聚 | `Edge` + 紫色端口（边色继承端口色） |
| 点击虚线 | 绿 `#7FAC5B` + dash | 等待确认 → 出口开始 | 自定义虚线 `Edge` |

**规范**：
- 端口方向统一 `Orientation.Vertical`（执行流从上到下）。
- Fork 出端口横排、Join 入端口横排（`capacity.Multiple`）。

---

## 5. 图结构校验器

**职责**：保证自由连线画出的图 ≡ 合法命令链树（SP 图，Series-Parallel）。

**校验规则（每次 `graphViewChanged` 触发，毫秒级）**：

| # | 规则 | 级别 |
|---|------|------|
| 1 | 唯一开始（无入边）→ 唯一终点（无出边）；全节点从开始可达、终于终点 | 致命 |
| 2 | 命令节点入/出端口各恰 1 条边 | 致命 |
| 3 | Join 至少 2 入边 | 致命 |
| 4 | 每个 Fork 全部分支必须汇入**同一个** Join（首遇节点判定） | 致命 |
| 5 | 无环（DFS） | 致命 |
| 6 | 出口段含 `choice`（`ScriptParser` 运行时报错，编辑期前置拦截） | 致命 |
| 7 | 流程命令仅可连在链尾（判定调 `ChainParser.IsFlowCommand`） | 警告 |
| 8 | Fork 仅 1 出边（提示「可简化为直连」） | 警告 |
| 9 | 嵌套深度 > `ChainParser.MaxRecommendedDepth`（2 层） | 警告 |
| 10 | 未注册命令（`CommandManager` 无此命令名——拼写错或未实现） | 警告 |
| 11 | 进入段含 `choice` 且出口段非空（出口段不会执行，与 `ScriptParser` 警告对齐） | 警告 |

**分级阻断（已确认决策 s1）**：
- 致命错误 → **阻断保存** + 节点标红。
- 警告 → **不阻断** + 高亮提示 + 状态栏计数。

**技术实现点**：校验器读图邻接表 → 输出错误列表 → 问题节点 `AddToClassList("has-error")` / `"has-warning"` + 状态栏汇总。

**✅ 已修复的运行时校验漏洞（2026-08-26）**：`ChainParser.FlowCommands` 原仅 `{jump, choice, loadscript, loadscene}`，已补入 `jumpif/jumpifnot/loadscriptif/loadscriptifnot`（它们同样改写行索引/剧本数据源），并新增公开方法 `ChainParser.IsFlowCommand(name)` 供本校验器复用，避免 Editor 侧另抄一份集合造成定义漂移。

---

## 6. 左命令面板 + 资源拖拽

**技术实现点**：
- 命令列表：UIElements `ListView`，数据源为 `CommandMetaReader`（反射扫描全部已注册命令，含第三方），按 `[VNCommandMeta]` 声明的分类分组 + 搜索。
- 无元数据的命令**照常列出**（拖入后为通用节点态），不隐藏——保证任何命令都能上图（见 §10）。
- 泳道 Tab（进入段 / 出口段）：决定新拖入节点落到哪条链；出口段禁用 `choice`。
- 拖拽建节点：`DragAndDrop` + 画布 `nodeCreationRequest` / dragEnter-dragPerform，在落点创建 `CommandNodeView`。
- 资源拖拽：角色/背景/BGM 从 `CharacterResManager` + 资源注册表读列表 → 生成 `showChar/showbg/playBGM`。

**规范**：命令签名提示（`sig`）由 `[VNParam]` 元数据实时拼装，不硬编码、不手写清单。

---

## 7. Inspector 属性检查器

**技术实现点**：
- 联动：`graphView.selectionChanged` → 重建表单。
- 表单：按 `[VNParam]` 元数据动态生成（角色/槽位下拉、时长滑块、文本输入框）；无元数据 → 单行原始参数文本框（通用节点态）。
- 隐式绑定：空参=引用数据列 → 📎「引用：XX 列」+ 只读回显；「✏️ 断开引用」切换内联值。
- 校验区 + 序列化预览（实时生成命令链文本）。

**规范**：
- 参数拆分复用 `ConditionParser.SplitTopLevel`（`CommandNode.Args` 是原始串，按元数据参数数拆分，写回时重拼）。
- 断开隐式绑定改内联值 → **强制确认弹窗**（已确认决策 s3），警告本地化键失效风险。
- **`showDialogue` / `showSpeaker` 无「断开引用」入口**：文本与说话人永远引用数据列（决策 dlg），因此 `text.{lineID}` / `speaker.{lineID}` 本地化键**永不可能失效**。「断开引用」仅适用于 `showbg` / `playBGM` / `playVoice` / `showChar`。

---

## 8. 三层行形态与按需提升

| 形态 | 判定（Command 列） | 画布表现 |
|------|-------------------|---------|
| 普通行 | 空 | 默认演出胶囊（折叠）+ 空用户区 |
| 增强行 | 仅普通命令（无系统命令） | 默认演出胶囊（折叠）+ 实体用户命令 |
| 定制行 | 含系统命令 | 全实体（完整链，模板段默认展开） |

**技术实现点**：
- 判定复用 `SplitConfirmSection` 语义（系统命令 = `showbg/showChar/showSpeaker/showDialogue/playBGM/playVoice`）。
- `RowPromotion` 从数据列生成模板影子（仅视图层，AST 不占位）。
- **模板默认折叠为单个 `TemplateCapsuleNode`**（决策 s8）：胶囊内显示引用了哪些数据列的徽章；双击展开为完整影子链（8 分支 Par）。折叠状态存 `.csv.graphpos.json`。
- 触碰提升：影子节点删/改/重排 → 确认弹窗 → 影子实体化 + 系统命令写入 AST → 定制行。

**规范**：提升单向；「重置回模板」二次确认，重置丢弃定制内容。

**⚠ 硬契约：提升不改变演出**（决策 tpl）。模板链必须与引擎隐式路径**逐帧等价**——模板结构、等价性论证与回归测试见 `VNCommandChainSpec.md` §11.3。

---

## 9. 序列化与持久化

### 9.1 图 → AST（`GraphToAst`，决策 d5）

**职责**：把用户自由连线画出的 SP 图还原为 fork-join 树。`ChainGraphValidator` 只判定图**是否**合法 SP 图，不产出 AST；本组件负责结构分解。

**算法**（递归 SP 分解）：
```
ParseFrom(node, stopAt):
    seq = []
    cur = node
    while cur != stopAt:
        if cur is Fork:
            join = FindCommonJoin(cur)           # 各分支首个共同汇聚点
            par  = [ParseFrom(branch, join) for branch in cur.outEdges]
            seq.Add(ParNode(par))
            cur = join.next
        else:
            seq.Add(CommandNode(cur))
            cur = cur.next
    return SeqNode(seq)
```

**规范**：进入链与出口链**分别**分解为两棵独立树（不共享节点）。

### 9.2 AST → 文本（`ChainSerializer`）

**归一化规则（必须遵守，否则语义错乱）**：

| 规则 | 说明 |
|------|------|
| **跳过单子项包装** | `ChainParser` 的 AST 恒为 `Seq(Par(...), Par(...))`——单命令 `wait(1)` 也被包成 `Seq(Par(Command))`。序列化时单子项的 `SeqNode`/`ParNode` 必须**透传**其子节点，否则输出 `[wait(1)]` 且往复几次括号层层累积直撞深度警告。 |
| **Par 的 Seq 子项强制加 `[]`** | 因 `&` 优先级高于 `->`，`Par{Seq{a,b}, Seq{c,d}}` 若裸写 `a->b & c->d` 会被反解析为 `a -> (b∥c) -> d`（**语义完全不同**）。正确输出 `[a->b] & [c->d]`。 |
| **顶层与单元素 Par 省略 `[]`** | 顶层 `Seq` 不加括号；`Par` 只有 1 个子项时等价于该子项，不加括号（避免白吃一层嵌套深度）。 |
| **参数引号包裹** | 参数含 `&` `->` `[` `]` `,` `"` 时用引号包裹（规范见 `VNCommandChainSpec.md` §7.2）。 |
| **`@Confirm:` 禁止出现于参数** | 序列化时若参数含 `@Confirm:` 字面量 → 强制引号包裹（`ScriptParser.IndexOfConfirmToken` 已引号感知，2026-08-26 修复）。 |

**写回格式**：`进入链` + `@Confirm:` + `出口链` 拼接写入 Command 列同一单元格（复用 Phase 0 `ExcelToCsvConverter` 侧车 + 镜像写回链路）。

### 9.3 保存前自校验（决策 s7）

```
序列化文本 → ChainParser.Parse → AST' → 与 GraphToAst 的 AST 比对
    │
    ├─ AST 结构等价（忽略括号/空白差异）→ 通过
    └─ 不等价 → 阻断保存 + 报「序列化器内部错误」（不应发生，属实现 bug）
```

**规范**：
- **未编辑不写回**：图无变更时不碰 CSV，存量剧本手写的多行排版/自定义括号完整保留。仅当用户真的改了图，该行才被规范化重写。
- **原子写**：写 CSV 用「临时文件 + `File.Replace`」，并在保存期间挂起 `AutoExcelConverter` 轮询（2 秒周期），避免转换器读到半写文件。

### 9.4 位置持久化（决策 s2a / s2b）

**独立 sidecar `{csv}.graphpos.json`**（**不与 `.csv.cmdmap.json` 合并**——后者是三方合并基准，其正确性直接决定 Excel↔CSV 不丢数据；位置是高频写入的纯缓存，两者生命周期语义完全不同）：

```json
{
  "rows": [
    { "id": "1003",
      "templateCollapsed": true,
      "nodes": [ { "key": "3:shake", "x": 300, "y": 480 } ] }
  ]
}
```

**节点身份 = 深度优先展开序号 + 命令名签名**（`"{序号}:{命令名}"`）。命令链文本中**不存在节点身份**（`CommandNode.Position` 是源串偏移，插入节点即全部位移），故只能由结构推导：

| 情形 | 行为 |
|------|------|
| 结构未变 | 序号与命令名全部对上 → 位置完美恢复 |
| 结构变更 | 对得上签名的恢复位置；对不上的单独 `AutoLayout` 摆放 |
| sidecar 缺失/损坏 | 整行 `AutoLayout`，无任何副作用 |

**规范**：位置数据**永不阻塞保存**，丢失只是重排。该文件可加入 `.gitignore`（团队协作时避免位置冲突）；`.csv.cmdmap.json` 则**必须**进版本控制。

### 9.5 撤销栈（决策 s9）

- `GraphUndoStack`：每次图变更后压入「当前行命令链文本 + 节点位置 + 折叠状态」快照；`Ctrl+Z`/`Ctrl+Y` 反序/正序重建整张图。
- 不接 Unity 原生 `Undo.RecordObject`（GraphView + Undo 易出幽灵节点/状态不同步）。
- 已知代价：粒度为整图重建，撤销后选中状态丢失。
- 切换行时清空栈（撤销不跨行）。

### 9.6 链复制/粘贴（决策 s10）

- 「复制本行链」→ 命令链文本（含 `@Confirm:` 段）进系统剪贴板（`EditorGUIUtility.systemCopyBuffer`）。
- 「粘贴到当前行」→ `ChainParser.Parse` → 校验 → 重建图（进栈可撤销）。
- 内部即文本传递，因此可跨剧本、跨 Unity 实例，甚至可粘到聊天里给别人。
- 命名编排预设库排 Phase 2。

---

## 10. 命令节点化契约（决策 s4 废止重定义）

> **原设计（已废止）**：Editor-only 手写 `CommandSchemas.cs` 注册表，MVP 覆盖高频 15 个命令。
> **废止原因**：① 手写签名与命令实现是两份信息源，必然漂移；② **第三方自定义命令永远进不了图编辑器**——`CommandManager.Init` 用反射注册用户命令是本插件的核心扩展点，而用户改不了插件的 Editor 注册表。
> **新契约**：元数据以 C# 特性声明在命令类上（Runtime，与实现同位），Editor 侧反射读取。**所有命令终将节点化**。

### 10.1 特性定义（Runtime）

**已落地文件**（`Runtime/Scripts/VNovelizer/Core/Commands/Meta/`）：

| 文件 | 内容 |
|------|------|
| `VNParamType.cs` | `VNParamType` 枚举（24 项，含动态取值域标记）+ `VNCommandCategory` 枚举（6 项） |
| `VNCommandMetaAttribute.cs` | `[VNCommandMeta(Category, Description)]`，可选 `ArgSeparator` / `VariadicArgs` / `Planned` |
| `VNParamAttribute.cs` | `[VNParam(Index, Name, Type)]`，可选 `Options` / `Min` / `Max` / `Default` / `Optional` / `ImplicitBinding` / `BoundColumn` / `InlineForbidden` |
| `CommandMetaReader.cs` | 反射读取器（带缓存）+ `VNCommandInfo` / `VNParamInfo` 投影类 |

**标注位置约定**：`[VNCommandMeta]` 标在类上；`[VNParam]` 标在 `CommandName` 属性上（该属性是每个命令必然存在的成员，语义上代表"这个命令"，便于集中阅读）。读取器两处都扫。

```csharp
[VNCommandMeta(VNCommandCategory.Performance, "屏幕 / 角色 / 对话框震动")]
public class ShakeCommand : VNCommand
{
    [VNParam(0, "target", VNParamType.Enum, Options = "screen|dialogue|L|ML|M|MR|R",
        Description = "震动目标：screen=相机（UI 不动）/ dialogue=对话框 / 槽位码=角色")]
    [VNParam(1, "duration", VNParamType.Float, Min = 0f, Max = 10f, Default = "0.5", Optional = true)]
    [VNParam(2, "intensity", VNParamType.Float, Min = 0f, Max = 100f, Default = "10", Optional = true)]
    public override string CommandName => "shake";
}
```

**配套的 `CommandManager` 改动**（`VNCommand.cs`）：

| 新增成员 | 用途 |
|---------|------|
| `RegisteredCommandCount` | 已注册命令数（0 = 未 Init） |
| `IsCommandRegistered(name)` | 图校验规则 10「未注册命令」判定 |
| `EnumerateRegisteredCommands()` | 只读快照枚举，元数据读取器与命令面板的数据源（**含反射注册的第三方命令**） |
| `EnsureInitialized()` | 幂等初始化，供 Editor 工具在非播放模式下使用 |

**同时修复的健壮性隐患**：`RegisterCustomCommandsViaReflection` 中 `assembly.GetTypes()` 原在 `try` 之外——某程序集类型加载失败抛出的 `ReflectionTypeLoadException` 会**中断整个注册循环**，其后所有程序集的命令全部注册不上。Editor 域程序集数量远多于运行时，风险更高，且这直接破坏「第三方命令可进图编辑器」这一核心扩展点。现按程序集粒度隔离（`ReflectionTypeLoadException` 时取 `e.Types` 中已成功加载的部分），并将逐条注册日志汇总为一条（避免 Editor 工具触发初始化时刷屏）。

### 10.2 声明什么 / 不声明什么

| 信息 | 来源 | 理由 |
|------|------|------|
| 参数名 / 类型 / 取值域 / 默认值 / 可选性 | `[VNParam]` **声明** | 反射问不出来 |
| 分类 / 描述 / 图标 | `[VNCommandMeta]` **声明** | 反射问不出来 |
| 是否支持隐式绑定 | `[VNParam(ImplicitBinding = ...)]` **声明** | 反射问不出来 |
| 是否异步（`IsAsync`） | **反射推导**（是否 override `ExecuteAsync`） | 绝不重复声明 |
| 是否流程命令 | **反射/查询** `ChainParser.IsFlowCommand` | 单一信息源 |
| 是否实现 `Simulate` | **反射推导**（是否 override `Simulate`） | 用于校验器提示「该命令无预演，读档可能状态不一致」 |

### 10.3 动态取值域

用**参数类型**表达而非枚举值列表，Editor 侧按类型拉候选——纯静态特性也能有动态下拉，无需第二套接口：

| `VNParamType` | Editor 候选来源 |
|---------------|----------------|
| `CharacterId` | `CharacterResManager` 全部 `CharacterProfile` |
| `Emotion` | 选定角色的 `ElementSprites` 分组/表情 |
| `BackgroundName` | 资源注册表背景列表 |
| `BgmName` / `SfxName` / `VoiceName` | 资源注册表音频列表 |
| `SlotCode` | `L / ML / M / MR / R` |
| `LineId` | 当前剧本全部行 ID |
| `FlagName` | Flag 编辑器已登记的标志 |
| `Float` / `Int` / `Bool` / `String` / `Enum` | 由 `Min`/`Max`/`Options` 约束 |

### 10.4 通用节点（永久兼容层，决策 s4-q1）

无 `[VNCommandMeta]` 的命令**不是不支持**，而是降级为通用节点形态——这是**最低层契约**而非过渡方案：

| 形态 | 条件 | 表现 |
|------|------|------|
| 结构化表单 | 已注册 + 有元数据 | 参数 chip + Inspector 下拉/滑块 |
| **通用节点** | 已注册 + 无元数据 | 单行原始参数文本框，⚙ 角标；**可连线、可拖拽、可序列化** |
| 未注册警告 | 命令名不在 `CommandManager` | 通用节点 + ⚠ 角标 + 校验警告（不阻断保存） |

**规范**：
- 任何人写的任何命令都能上图——这是插件开放性的底线。
- 存量 39 个命令逐步标注，不设"必须全标"的硬门槛（否则违反零迁移）。
- 标注优先级：系统命令族 6 个 > 高频演出命令（shake/wait/charmove/charfade 族/charjump/fadeBlack 族）> 流程命令族 > 其余。

### 10.5 标注进度与验证工具

**`Editor/RowPerformanceEditor/CommandMetaInspectorWindow.cs`**（菜单：**VNovelizer → 命令元数据检查器**）

两个用途：
1. **验证节点化契约**——确认特性被正确读取、反射推导特征（`async`/`Sim`/`Intr`/`链尾`）与实现一致
2. **标注进度看板**——进度条 + 「仅未标注」筛选，一眼看出哪些命令仍是通用节点形态

行演出编辑器的命令面板将复用同一份数据源（`CommandMetaReader`），因此检查器里显示正确 = 面板里显示正确。

**已标注命令（首批 5 个，验证契约可用性）**：

| 命令 | 分类 | 参数签名（**以实现为准核实**） |
|------|------|------|
| `shake` | Performance | `target(Enum: screen\|dialogue\|L\|ML\|M\|MR\|R)`, `duration(0~10, 默认0.5, 可选)`, `intensity(0~100, 默认10, 可选)` |
| `wait` | Performance | `seconds(0~30, 默认0.5)` |
| `charmove` | Performance | `pos(SlotCode)`, `x(±960)`, `y(±540)`, `duration(默认0.5, 可选)` |
| `bgfade` | Performance | `background(BackgroundName)`, `duration(默认1.0, 可选)` |
| `jump` | Flow | `targetLineId(LineId)` |

> 标注时**必须读实现核实签名**，不可凭命令名猜测。例如 `shake` 的 target 实际含 `dialogue`（对话框震动，UI 层独立实现），初稿若按"screen/char/ui"猜会错。

---

## 11. 已确认的实现决策汇总

| # | 决策点 | 结论 |
|---|--------|------|
| d1 | 画布范式 | 裸 GraphView（`UnityEditor.Experimental.GraphView`），非 NodeGraphProcessor/xNode |
| d2 | 并行表达 | 显式 FORK/JOIN 胶囊节点 + 紫/灰双色边 |
| d3 | 双链结构 | 进入段（主链）+ 出口段（`@Confirm:`）平级布局 |
| d4 | 布局与拖拽 | AutoLayout 初始排布 + GraphView 自由拖拽 + sidecar 位置持久化 |
| **d5** | **图 → AST** | **独立 `GraphToAst.cs` 专职 SP 分解**；校验/分解/序列化三者职责单一 |
| s1 | 校验阻断 | 分两级：致命阻断+标红，警告不阻断+高亮 |
| **s2a** | **节点身份** | **深度优先序号 + 命令名签名，最佳努力匹配**（文本无节点身份，硬约束）；位置永不阻塞保存 |
| **s2b** | **位置存储** | **独立 `.csv.graphpos.json`**（原「合并进 `.csv.cmdmap.json`」已否——纯缓存不应与合并基准同居） |
| s3 | 内联切换 | 断开隐式绑定改内联 → 强制确认弹窗 |
| **s4′** | **节点化契约** | **`[VNCommandMeta]`+`[VNParam]` Runtime 特性 + 反射读取 + 通用节点永久兼容层**（原「Editor-only 15 个 Schema」**已废止**） |
| **tpl** | **默认模板** | **双分支 `Par{ showDialogue(typewriter) & [其他系统命令 -> 用户链] }`**；「提升不改变演出」= **硬契约** |
| **dlg** | **showDialogue** | **`direct`（瞬时不阻塞）/ `typewriter`（打字且阻塞，空参默认）**；阻塞由并行链隔离；**文本不允许内联**；`Interrupt()` = 立即全显 |
| **s5** | **隐式绑定上下文** | **`VNManager.CurrentLineContext`**（ResolveLine 解析后值，Simulate/Execute 两路径统一赋值）；`VNCommand` 签名不变 |
| **s6** | **硬契约验收** | **新增「模板等价性」回归测试**（EventCenter 事件时序逐帧对比） |
| **s7** | **幂等层级** | **AST 等价（忽略括号/空白）+ 未编辑不写回** |
| **s8** | **模板展示** | **默认折叠为单个「默认演出」胶囊，双击展开** |
| **s9** | **Undo** | **自建文本快照栈，整图重建**（不接 Unity 原生 Undo） |
| **s10** | **跨行复用** | **Phase 1 链复制/粘贴**；命名预设库排 Phase 2 |

---

## 12. 实施顺序（Phase 1 任务分解）

**✅ P0 前置（2026-08-26 已完成）**：
- `ChainParser.FlowCommands` 补入条件跳转族 + 新增公开 `IsFlowCommand()`
- `ChainParser` 深度超限从 `Errors` 改入 `Warnings`（否则 3 层嵌套无法保存）
- `ChainLexer` 转义越界 + `parenDepth` 负值两个词法边界修复
- `ScriptParser.SplitConfirmSection` 改引号感知
- `VNManager.FastForwardToLine` 的 `lastLine` 提到 `SimulateCommands` 之前（隐式绑定前置依赖）

**Phase 1 主体**：

| # | 任务 | 依赖 |
|---|------|------|
| ① | ✅ **已完成** `VNCommandMeta`/`VNParam`/`VNParamType` 特性定义 + `CommandMetaReader` 反射读取器 + `CommandManager` 枚举接口 + 元数据检查器窗口 + 首批 5 命令标注 | P0 |
| ② | ✅ **已完成** `VNLineContext` + `VNManager.CurrentLineContext`（4 个赋值点均早于命令执行/模拟，2 个清理点）+ `VNAPI.GetCurrentLineContext()/GetCurrentLineColumn()` | P0 |
| ③ | ✅ **已完成** 系统命令族 6 个（`Core/Commands/SystemCommands/`）+ `VNManager` 的 `SysXxx` 复用入口 + `CommandManager.IsSystemCommand()/ContainsSystemCommand()`（三层形态判定） | ② |
| ④ | ✅ **已完成** 引擎三路径执行分流（`PlayCurrentLine` / `PlayCurrentLineImmediately` / `FastForwardToLine` 主循环 + choice 分支）+ `IsCustomPerformanceRow` 带缓存判定 + `ExecuteCommandsInstant` | ③ |
| ⑤ | ✅ **已完成** `PerformanceEventRecorder`（Runtime）+ `TemplateEquivalenceTestWindow`（Editor）+ `VNManager.ReplayImplicitPerformanceForTest()` | ④ |
| ⑥ | ✅ **已完成** `ChainGraph`（UI 无关的图数据模型）+ `GraphToAst`（SP 分解）+ `AstToGraph`（加载方向） | P0 |
| ⑦ | ✅ **已完成** `ChainSerializer`（三条括号规则 + `SerializeAndVerify` 幂等自校验 + `AreStructurallyEqual`）+ `ChainRoundTripTestWindow` 往返验证 + `DefaultPerformanceTemplate` | ⑥ |
| ⑧ | ✅ **已完成** `ChainGraphValidator`（11 规则 + `Fatal`/`Warning` 分级） | ⑥ |
| ⑨ | 节点视图五类 + USS（语义角标、模板胶囊、通用节点态） | ① |
| ⑩ | `RowGraphView`：双泳道 + 拖拽 + `ChainAutoLayout` | ⑨ |
| ⑪ | `InspectorBuilder`（元数据驱动表单 + 隐式绑定切换） | ①⑨ |
| ⑫ | `RowPromotion`：三层形态判定 + 模板折叠胶囊 + 提升 | ⑩ |
| ⑬ | `GraphPosStore` + `GraphUndoStack` + 链复制粘贴 | ⑩ |
| ⑭ | 窗口集成：`RowPerfEditorWindow` + `ScriptManager` 入口 + 原子写 + 轮询挂起 | 全部 |

**并行性**：①②③④⑤ 是 Runtime 线，⑥⑦⑧ 是转换线，⑨⑩⑪⑫⑬⑭ 是 UI 线。Runtime 线与转换线可并行推进，UI 线依赖两者。

---

## 13. 遗留风险

| 风险 | 缓解 |
|------|------|
| 特性标注是渐进工程，39 个命令逐步标注期内图体验不均质（结构化表单 vs 通用节点混杂） | 通用节点是永久契约而非临时态，功能完整（可连线/拖拽/序列化）；按 §10.4 优先级推进标注 |
| 模板双分支的认知成本（「对话在另一泳道」） | 折叠胶囊默认隐藏内部结构；展开时用泳道标签明示「对话独立分支，不阻塞其他演出」 |
| Undo 粒度粗（整图重建，选中态丢失） | 可接受；若后续反馈强烈再评估细粒度方案 |
| 保存竞态（`AutoExcelConverter` 2 秒轮询） | 原子写（临时文件 + `File.Replace`）+ 保存期挂起轮询 |
| `GraphToAst` 的 `FindCommonJoin` 在畸形图上的健壮性 | 校验器致命规则（1/2/4/5）先行拦截，分解器只处理已验证的合法 SP 图；仍加防御性递归深度上限 |
