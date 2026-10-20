# VNovelizer 剧情本地化指南（单一 Collection 方案）

> 设计权威文档：`VNLocalizationRefactoring.md`。本指南面向使用者（作者/翻译团队）。

## 0. 总览

- 翻译真源 = **独立 CSV 文本文件**（git 可 diff、外部翻译团队直接填）
- 一种语言一张表，格式固定三列：`ID, Text_Origin, {locale}`
- 可建任意多张表（按剧本/章节/模块自由切分），保存后**自动聚合**为单一 StringTableCollection `"VNLocalization"`
- 剧情/命令 key 由模板生成器自动生成，作者零维护

## 1. 开启与配置

1. `Edit → Project Settings → VNovelizer → 四、剧情本地化`：
   - 勾选 `启用剧情本地化`
   - `翻译表文件夹`：默认 `Assets/Localization/Translations/`
   - `源语言 (Text_Origin)`：如 `zh-Hans`（原文语言，fallback 链根）
   - `目标语言列表`：逗号分隔，如 `en,ja`（模板生成器为每个语言出表）
2. `Project Settings → Localization` 确认已添加对应的 Locale（源语言 + 全部目标语言）

## 2. 工作流

1. 剧本管理器点「转换」（或自动转换）——CSV 写出时**自动生成/更新翻译表模板**：
   - 每剧本 × 每目标语言一张：`{TranslationFolder}/{scriptName}_{locale}.csv`
   - 新增行自动补入（译文列留空）；原文变化只刷新 `Text_Origin`；**译文列永不动**
   - 行删除后旧行保留在模板末尾（译文不丢，人工清理）
2. 翻译团队打开模板 CSV（Excel 可直接编辑），在第三列填译文，保存
3. 保存即自动聚合进 Unity 本地化资产（无需任何手动步骤）；也可以菜单
   `VNovelizer → Localization → 导入翻译表` 手动兜底
4. `VNovelizer → Localization → 剧情本地化管理器` 查看聚合结果、key 冲突、补生成模板

## 3. 翻译表格式

```
ID,                             Text_Origin,          en
Chapter1.1001.text,             今天天气真好。,          What a nice day today.
Chapter1.1001.speakername,      艾米,                  Amy
Chapter1.1001.command.showprompt.0, 好感度+1,          Affection +1
Chapter1.1001.command.choice.0.option.1, 去天台,       Go to the rooftop
ui.mainmenu.start,              开始游戏,              Start Game
```

- 第 1 列 ID：全局唯一 key（**不要改动**，由模板生成器维护）
- 第 2 列 Text_Origin：原文参考列（翻译人员只看不改；聚合器用其填源语言表）
- 第 3 列：该语言译文，列名 = locale 代码（`en` / `ja` / `zh-Hans`）
- 表头必须严格三列；空译文行留空即可（运行时自动回退源语言）

## 4. Key 体系（自动生成）

| 域 | key 规则 | 示例 |
|---|---|---|
| 剧情正文 | `{script}.{lineID}.text` | `Chapter1.1001.text` |
| 说话人 | `{script}.{lineID}.speakername` | `Chapter1.1001.speakername` |
| 命令文本 | `{script}.{lineID}.command.{name}.{同名序号}` | `Chapter1.1001.command.showprompt.0` |
| choice 选项 | `{script}.{lineID}.command.choice.{i}.option.{j}` | `Chapter1.1001.command.choice.0.option.1` |
| 选项链内命令 | `{script}.{lineID}.command.choice.{i}.option.{j}.command.{name}.{链内序号}` | `Chapter1.1001.command.choice.0.option.1.command.showprompt.0` |
| UI | `ui.*`（手工清单 `ui.csv` 维护，独立集合 `VNUI`） | `ui.mainmenu.start` |

- 同名序号 = 该命令在行内的第几次出现（0 起）；进入段与 @Confirm: 出口段独立计数
- 只有声明了「本地化文本」参数的命令参与（当前：`showprompt` 的 text、`choice` 的选项描述）
- 选项链内命令的 key 带 `{choice 序号}.option.{选项序号}` 前缀——不同选项链的同类命令互不冲突
- 嵌套 choice（选项链内再含 choice）的选项文本与链内命令文本**都不自动生成**，请用 `@loc:key` 显式引用

## 5. 显式 @loc: 引用（自由 key）

命令参数可直接写 `@loc:key` 引用任意 key（优先级高于自动 key）：

```
showprompt(@loc:ui.hint.affection_up)
choice(@loc:ui.choice.rooftop|jump(C_100))
```

翻译缺失时回退显示 key 的可读尾段（不会显示 `@loc:` 原样）。

## 6. UI 本地化（开箱即送）

包内模板 UI 预制体已预装官方 `LocalizeStringEvent`（表引用 `VNUI` + `ui.*` key），随包开箱即用，无需任何运行时挂载：

1. 开启剧情本地化后，在 `Project Settings → VNovelizer → 四、剧情本地化` 点 **「导入本地化资源」**：
   - 包内 `UI_Locale_en.csv`（英文译文现成）复制到翻译表目录
   - 其余目标语言自动生成 `UI_Locale_{locale}.csv` 空译文模板（填译文即可用）
   - 缺失的源/目标 Locale 资产**自动创建**，目标 Locale 的 fallback 链**自动指向源语言**
   - 自动聚合生成 `VNUI` StringTableCollection
2. **语言切换**：SettingsPanel 自动出现「语言」下拉（目标语言多于 1 种时），选择即时生效并持久化（`GlobalData.LanguageCode`）
3. **未开启本地化**：UI 显示中文原文、零本地化痕迹、Console 零报错（官方组件在表缺失时静默保持原文；`LocalizationSettings` 资产由编辑器自动兜底创建）
4. **译文缺失**：fallback 链自动回退中文原文，永不显示裸 key
5. UI key 清单真源 = 包内 `Runtime/PackageDefault/VNovelizerRes/UI_Localization/UI_Locale_en.csv`；包开发者在新增静态文本后重跑 `VNovelizer → Localization → 装配 UI 本地化组件（开发用）` 同步 key（动态文本如页码/音量值/对话内容不参与装配）

## 7. 常见问题

- **翻译没生效**：确认目标语言 Locale 已添加到 Project Settings → Localization；确认模板第三列已填；打开剧情本地化管理器查看最近导入结果
- **key 冲突**：同语言多张表出现了同 key 不同值——后加载覆盖 + 管理器列出冲突文件，删掉其中一张的重复行
- **未开本地化时面板报错/异常**：不应发生——`LocalizationSettings` 资产在编辑器加载时已自动兜底创建（缺失时组件会抛异常的唯一根因）；表缺失时组件静默保持原文
- **改了剧本行号/ID**：key 会变，旧译文行保留在模板末尾（Text_Origin 为空），人工把译文搬过去即可。导入时这些失效的旧 key 会**自动跳过**（不写进 StringTable，译文仍留在 CSV 里不会丢），管理器会列出「已跳过的失效 key」清单
- **运行时日志没有 "No translation found"**：本门面直接查表取值，缺失时静默走 fallback（设计如此）
