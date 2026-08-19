
--- START OF FILE README.md ---

<div align="center">

# 🚀 VNovelizer - Unity Visual Novel Framework

![Unity Version](https://img.shields.io/badge/Unity-2022.3%2B-blue?logo=unity)
![License](https://img.shields.io/badge/License-MIT-green)
![Status](https://img.shields.io/badge/Status-Active-brightgreen)

**轻量级 · 高性能 · 数据驱动 · 零代码工作流**

[📖 使用文档 (飞书)](https://my.feishu.cn/wiki/space/7589983850810346443) | [📺 作者 B 站](https://space.bilibili.com/353379364) | [🐛 提交 Issue](https://github.com/Fakecorps/VNovelizer/issues)

</div>

---

## 📖 简介 (Introduction)

**VNovelizer** 是一款专为 Unity 开发的高度可扩展视觉小说（Visual Novel）引擎框架。它旨在打破技术壁垒，通过 **Excel 数据驱动** 的核心设计，让编剧和策划无需编写一行 C# 代码，即可构建出包含复杂演出、分支剧情和完整 UI 系统的视觉小说游戏。

无论是制作独立 AVG、文字冒险游戏，还是为现有项目添加剧情模块，VNovelizer 都能提供开箱即用的解决方案。

---

## ✨ 核心特性 (Features)

### 🎨 零代码创作流
*   **Excel 驱动**：从对话到逻辑跳转，全部在 Excel 中完成。支持一键转换为游戏数据。
*   **富文本支持**：完美支持 TextMeshPro，轻松实现颜色、大小、字体变化。
*   **场景状态延续**：**背景、BGM** 等留空可沿用上一有效状态，减少重复填表；**说话人、正文、头像、三槽立绘**需按行显式填写（立绘列留空视为隐藏该槽）。

### ⚡ 强大的演出系统
*   **指令系统 (Command System)**：内置 30+ 种常用指令（震屏、淡入淡出、立绘运动、视频播放等），支持指令并行与串行执行。
*   **高性能动画**：底层集成 **PrimeTween**，实现 0 GC 的丝滑 UI 动画体验。
*   **状态预演 (Fast Forward)**：支持任意节点的存读档与跳转。系统会预演跳转点之前的剧本行以同步背景、BGM、立绘字典等；读档时另会用**存档中的立绘快照**与首帧逻辑对齐，避免「CSV 立绘列为空」误清空刚恢复的槽位。

### 🧩 完善的 UI 模块
*   **系统面板**：内置 标题界面、存档/读档（含截图）、设置（音量/画质）、历史记录（回放语音）、画廊（CG/BGM/剧情回放）。
*   **交互面板**：分支选项、双重确认弹窗、异步加载进度页。
*   **可视化编辑器**：提供 Character Editor、Gallery Editor 等可视化工具，资源配置直观便捷。

---

## 📦 安装指南 (Installation)

### 1. 环境要求
*   **Unity 2022.3 LTS** 或更高版本 (推荐 Unity 6)
*   **TextMeshPro** (Unity 内置)
*   **Input System** (Unity 内置)
*   **Unity Localization** (`com.unity.localization`，本包已在 `package.json` 中声明依赖，UPM 会自动解析；剧情多语言见下文「本地化」)

### 2. 导入核心库 (必读 ⚠️)
> **注意**：本框架动画系统依赖 **PrimeTween**。受限于 Asset Store 协议，无法内置分发。
> 请在导入本框架前，务必先下载并导入 PrimeTween（免费版即可）：
>
> 👉 **[下载 PrimeTween (Asset Store)](https://assetstore.unity.com/packages/tools/animation/primetween-high-performance-animations-252960)**

*(ExcelDataReader, LitJson, UIParticle, Alchemy 等其他依赖已内置)*

### 3. 安装 VNovelizer
通过 Unity Package Manager 安装：
1.  点击左上角 `+` -> `Add package from git URL`
2.  输入：`https://github.com/Fakecorps/VNovelizer.git`

### 4. 一键初始化项目
导入完成后，执行顶部菜单：
**VNovelizer -> 一键初始化 (Setup Wizard)**

点击 **"🚀 一键初始化项目"**。向导将自动：
*   从包内 `Runtime/PackageDefault/VNovelizerRes` 复制默认资源到 **`Assets/Resources/VNovelizerRes`**（包内该目录**不在**名为 `Resources` 的文件夹下，避免与 Assets 产生重复 `Resources.Load` 键；运行时剧本/UI 等只从 **Assets 的 Resources** 加载）。
*   生成全局配置文件 `VNProjectConfig`
*   导入核心 UI 预制体与示例场景

---

## 🚀 快速上手 (Quick Start)

### 第一步：配置角色 (Character Setup)
VNovelizer 使用 `ScriptableObject` 管理角色资源，实现了逻辑 ID 与美术资源的解耦。

1.  在 `Resources/VNovelizerRes/Characters` 目录下（或自定义目录）。
2.  右键点击 -> **Create** -> **VNovelizer** -> **CharacterProfile**。
3.  **配置面板说明**：
    *   **Character ID**: 剧本中引用的唯一标识（如 `Player`, `Amy`）。**必须与 Excel 中的 Speaker 一致**。
    *   **Speaker Box**: (可选) 该角色的专属姓名框 UI 图片。
    *   **Head Frame**: (可选) 该角色的专属头像框 UI 图片。
    *   **Element Sprites (立绘)**:
        *   `Element`: 差分名（如 `Normal`, `Smile`, `Angry`）。
        *   `Sprite`: 对应的全身立绘图片。
    *   **Head Sprites (头像)**:
        *   `Element`: 差分名（需与立绘差分名对应）。
        *   `Sprite`: 对应的小头像图片（用于对话框显示）。

> 💡 **提示**：配置完成后，剧本中只需填写 `Amy` 和表情名 `Smile`，系统会自动查找对应的图片资源。

### 第二步：编写剧本 (Scripting)

1.  打开顶部菜单 **VNovelizer -> 剧本管理器**。
2.  点击 **"新建"**，输入文件名（如 `Chapter1`）。Excel 将自动打开。
3.  **剧本字段详解**：

| 字段 (Column) | 必填 | 说明 (Description) | 示例 |
| :--- | :---: | :--- | :--- |
| **ID** | ✅ | **行号**。必须唯一，用于跳转和存档定位。 | `1001` |
| **Speaker** | | 说话人 ID。每行独立；留空则无说话人名显示。 | `Amy` |
| **HeadProfile** | | 头像配置。格式：`ID#分组#表情名`（分组对应 CharacterProfile 中的立绘分组）。填 `hide` 隐藏；**留空则按隐藏头像处理**（不沿用上句）。 | `Amy#uniform#Smile` |
| **CharLeft/Mid_Left/Mid/Mid_Right/Right** | | 五个立绘槽位。格式：`ID#分组#表情名`。填 `hide` 或**留空**均隐藏该槽（不沿用上句）。 | `Amy#uniform#Normal` |
| **Text** | | 对话文本。支持 TMP 富文本标签。 | `你好，<color=red>陌生人</color>。` |
| **Background** | | 背景图名 (需在 Resources 背景目录)。留空继承。 | `School_Day` |
| **BGM** | | 背景音乐名。填 `stop` 停止，`pause` 暂停。 | `Theme_Song` |
| **Voice** | | 语音文件名。留空自动尝试加载 `行ID.mp3`。`false` 为静音，即该剧本不使用配音。 | `1001_v` |
| **Command** | | 演出指令集。多条指令用 `&` 分隔；可用 `@Confirm:` 声明行尾出口指令（见指令手册）。 | `shake(screen)&wait(0.5)` |
| **Note** | | 策划备注（游戏内不加载）。 | `第一章结束` |

4.  编辑完成后保存 Excel。
5.  在剧本管理器中点击 **"转换"**，生成游戏所需的 `.asset` 数据文件。

### 剧本：行级状态规则（延续 vs 显式）

以下约定直接影响画面与读档表现，建议策划与程序共同对齐。

#### 会「延续」的字段（空单元格 = 不改变当前状态）

*   **Background**：本行留空时，沿用当前已生效的背景（与 `VNManager` 内 `currentBG` 一致）。若需切黑/隐藏请使用表内约定值或指令。
*   **BGM**：本行 **BGM 列为空** 时，不会强制切换曲目（继续播放当前 BGM）；填写新曲名、`stop`、`pause`、`resume` 等才会改变播放状态。

#### 必须按行显式的字段（空 = 无该项，不沿用上句）

*   **Speaker**：留空则本行不显示说话人名。
*   **Text**：留空则本行正文为空字符串（是否允许纯演出行由剧本设计决定）。
*   **HeadProfile**：留空视为隐藏头像（`hide`）；不沿用上一行的头像配置。
*   **CharLeft / CharMid_Left / CharMid / CharMid_Right / CharRight**：留空或填 `hide` 均会**隐藏该槽**并从内部立绘状态中移除该位置；**不会**自动沿用上一行同槽立绘。连续多句同一角色出场时，需要在每一行重复填写立绘（或使用表格公式批量填充）。

#### 演出命令与立绘列（同行约束）

下列指令在运行时会作用在**已显示**的槽位 Rect 上；预演模式（`Simulate`）会通过 `VNManager.GetCharacterData` 判断该槽是否有角色：

*   `charmove`、`setchartrans`、`charflip` 等。

**规则**：若某行 `Command` 中使用了 `charmove(M, …)` 等，该行 Excel 的 **CharMid（或对应槽）必须写明立绘**（如 `Amy#uniform#Normal`），不能依赖「上一行填过、本行留空」的旧习惯，否则本行会判定该槽无角色，命令无效或仅打警告日志。

`charfadein` / `charfadeout` / `charjump` 等以当前 UI 对象为准；同样建议该行 CSV 已正确配置立绘或先由前序行显式显示。

#### 存档与读档

*   存档会写入当前 **三槽立绘**（`Characters` 字典）、翻转缩放（`CharacterScaleX`）、背景、BGM、特效与变量等。
*   **读档首帧**：若当前行 CSV 中某立绘列为空，但存档里该槽仍有数据，则**首帧播放会用存档中的槽位数据补全显示**，避免刚读档就被「空槽 = 隐藏」清掉画面；**进入下一行后**仍严格按 CSV 规则执行（空列即隐藏）。
*   存档**不包含** `HeadProfile`；读档后头像以**当前行 CSV 的 HeadProfile** 为准。若需在存档点精确还原头像，需在剧本该行写明头像字段。

#### 多语言（Unity Localization）

*   在 `VNProjectConfig` 中可开启剧情本地化；详细 Collection 命名、`text.{lineID}` / `speaker.{lineID}`、`choice(@loc:...)` 与回退策略见仓库内 **`Docs/VNLocalizationGuide.md`**。
*   运行时对外静态入口 **`VNAPI`**（界面引用、Flag、指令、流程、本地化等）见 **`Docs/VNAPIReference.md`**。
*   开启本地化后，**每一行独立解析**翻译条目，**不在行与行之间继承译文**；某语言缺失时可按 `FallbackToCsvWhenMissing` 回退到**本行 CSV** 的 Speaker/Text。

#### 从旧版「空槽 / 空说话人沿用」迁移

若旧剧本大量依赖「立绘或说话人列留空以沿用上一行」，升级本规则后需要：

*   为连续对白行**补全** Speaker、三槽立绘、HeadProfile（按策划意图逐行填写或批量生成）；
*   检查所有**同行含立绘类 Command** 的行是否已填写对应槽位。

---

### 第三步：运行游戏 (Run)

**调试模式**：
1.  打开 `Assets/Scenes/VNDebugScene` 场景。
2.  在 Inspector 面板输入剧本名（如 `Chapter1`）和起始行 ID。
3.  点击运行，即可直接跳转测试。

**代码调用**：
在任意脚本中调用以下代码即可启动流程：
```csharp
// 启动 Chapter1 剧本，从头开始
VNManager.GetInstance().StartGame("Chapter1");

// 或者从指定行号开始
VNManager.GetInstance().StartGame("Chapter1", "1005");
```

---

## 🎮 指令手册 (Command Reference)

指令不区分大小写，参数间用逗号分隔。同一行多条指令的时序编排支持两种写法：

*   **兼容模式**：用 `&` 连接，指令按顺序执行（连续的 `charfadein`/`charfadeout` 自动并行）。
*   **命令链语法**：`&` 严格并行、`->` 严格串行、`[]` 分组，详见 **`Docs/VNCommandChainSpec.md`**。

### ⏩ 行尾出口指令（@Confirm:）

Command 列支持用 `@Confirm:` 把指令切分为两段：**进入本行时执行的指令**（`@Confirm:` 之前）与**用户确认推进时执行的出口指令**（`@Confirm:` 之后）。

*   **未声明时**：点击确认后默认执行 `NextLine()`（推进到下一行）——与旧行为完全一致。
*   **声明后**：点击确认（或自动播放到时、命令请求推进时）先执行出口指令；出口指令产生跳转（`jump`/`jumpif`/`loadscript`）时播放目标行，未跳转则按默认行为推进下一行。
*   *示例*：
    *   `jumpif(Favor>=60,1010)&jumpif(Favor<60,1011)` —— 写在**上一行**的出口：播完上一行，点击时检查 Favor 决定去向（推荐写法，跳转行无需单独占一行）。
    *   `shake(0.5)&@Confirm:jump(1010)` —— 演出后**等待玩家点击**再跳转（与行内裸 `jump` 的"演出完自动跳"相区别）。
    *   `@Confirm:setintflag(Favor,+10)` —— 点击时先加分，再默认推进下一行。

> **注意**：
> *   `@Confirm:` 之后的内容整体属于出口段（其中的 `&` 不再切分进入段），一段内仍可用命令链语法。
> *   出口段禁止 `choice` 命令（解析期报错）；进入段含 `choice` 时出口段不会执行（选项命令优先生效，解析期警告）。
> *   出口指令的执行时机：点击推进、自动播放（AutoPlay）、命令驱动推进（如 `fadeBlackOut`）三个入口统一经由出口段。
> *   快进/读档预演会模拟出口段以同步跳转与 Flag 状态；读档后当前行的出口段保持"等点击"状态。

<details>
<summary><strong>📐 流程控制 (Flow Control)</strong></summary>

<br>

*   `jump(id)`: 跳转到当前剧本的指定行 ID。
*   `jumpif(condition, targetId)`: 条件为真时跳转，等价于 `jump(targetId)`；为假时无操作继续执行。
*   `jumpifnot(condition, targetId)`: 条件为假时跳转（`jumpif` 的取反版）。
*   `loadscript(scriptName[, startId])`: 加载并切换到新剧本，可指定起始行 ID（缺省从头开始）。
*   `loadscriptif(condition, scriptName[, startId])`: 条件为真时加载剧本；为假时无操作。
*   `loadscriptifnot(condition, scriptName[, startId])`: 条件为假时加载剧本（取反版）。
*   `loadscene(sceneName)`: 加载 Unity 场景（场景需加入 Build Settings）。
*   `choice(Text | Command)`: 创建分支选项。
    *   *示例*: `choice(去吃饭|jump(200)) & choice(去睡觉|jump(300))`
    *   本地化写法：`choice(@loc:choice.key|jump(200))`，详见 `Docs/VNLocalizationGuide.md`。
*   `exit()`: 结束当前游戏并返回主菜单场景（参数必须为空）。

> **条件语法**（`jumpif` / `loadscriptif` 系列）：支持 `Amy_Favor >= 50`、`Met_Amy`、`!Met_Amy`、`PlayerName == "Alice"` 等表达式，操作数来自 Flag。

</details>

<details>
<summary><strong>🎬 视觉演出 (Visual Effects)</strong></summary>

<br>

*   `bgfade(imageName[, duration])`: 背景图淡入切换（duration 缺省 1.0 秒）。
*   `shake(target[, duration, strength])`: 震动效果（duration 缺省 0.5，strength 缺省 10 像素）。
    *   *target*: `screen` (全屏)、`dialogue` (对话框) 或 `L`/`ML`/`M`/`MR`/`R`（对应槽位立绘）。
*   `wait(seconds)`: 等待指定秒数（只能异步执行，用于命令链中控制节奏）。
*   `playparticle(name)`: 播放粒子特效（需预制体）。
*   `stopparticle(name)`: 停止粒子特效（延迟 5 秒回收，让余粒飘完）。
*   `playvideo(filename[, nextCommand])`: 播放全屏视频（需位于 StreamingAssets），结束后可执行一条命令。
    *   *示例*: `playvideo(op.mp4, loadscript(Chapter2))`
*   `playanim(name[, pos[, loop]])`: 播放 Animator 动画。
    *   *pos*: 挂载槽位，缺省 `M`；追加 `,loop` 则循环播放（需配对 `stopanim` 回收）。
*   `stopanim(name)`: 停止并回收循环动画。
*   `fadeBlackIn(duration)`: 黑幕淡入（遮罩变透明，画面显现）。
*   `fadeBlackOut(duration)`: 黑幕淡出（画面变黑）；完成后**自动推进下一行**。
*   `hide()`: 隐藏对话框 UI，进入大图/CG 观赏模式（参数必须为空；点击后恢复）。

</details>

<details>
<summary><strong>🧍 立绘操作 (Character Control)</strong></summary>

<br>

*   `charfadein(pos[, duration])`: 立绘淡入（duration 缺省 0.5 秒）。
*   `charfadeout(pos[, duration])`: 立绘淡出（duration 缺省 0.5 秒）。
*   `charmove(pos, x, y[, duration])`: 平滑移动立绘到指定坐标（duration 缺省 0.5 秒）。
*   `charjump(pos[, duration, times, height])`: 立绘跳跃（缺省 0.4 秒 / 1 次 / 30 像素高）。
*   `charflip(pos[, dir])`: 水平翻转立绘。
    *   *dir* 缺省为切换翻转；可填 `1`/`-1` 或 `left`/`right` 强制指定朝向。
*   `setchartrans(pos, x, y, scale)`: 瞬间设置立绘位置与缩放（保留翻转朝向）。

> **提示**：使用 `charmove` / `setchartrans` / `charflip` 时，**该行 Excel 中对应槽位（CharLeft/Mid/Right）须已填写立绘**，详见上文「剧本：行级状态规则 → 演出命令与立绘列」。立绘位代码：`L` / `ML` / `M` / `MR` / `R`。

</details>

<details>
<summary><strong>🔊 音频 (Audio)</strong></summary>

<br>

*   `playsfx(name[, times])`: 播放音效（times 缺省 1 次，逐次等待播完）。

</details>

<details>
<summary><strong>🔢 逻辑与数值 (Logic & Variables)</strong></summary>

<br>

*   `setboolflag(key[, value])`: 设置布尔变量（value 缺省 `true`）。
*   `setintflag(key, value)`: 设置整数变量，支持相对运算：`+10` / `-10` / `*2` / `/2`（除法为整数除法）。
    *   *示例*: `setintflag(Amy_Favor, +10)` → 当前值 +10。
*   `setstringflag(key, value)`: 设置字符串变量（值含逗号时需用引号包裹）。
    *   *示例*: `setstringflag(message, "Hello, World")`
*   `unlockcg(name)`: 解锁画廊中的 CG。
*   `unlockmusic(name)`: 解锁画廊中的音乐。
*   `unlockscene(name)`: 解锁回想场景。

> Flag 的作用域（Global 持久 / Save 随档回退）由 Flag 注册表（`VNovelizer → Flag 编辑器`）定义，详见 `Docs/VNFlagSystemDesign.md`。

</details>

<details>
<summary><strong>✍️ 文本样式 (Text Styling)</strong></summary>

<br>

*   `t_color(R,G,B)`: 修改当前行字体颜色（0-255），效果不继承到下一行。
*   `t_size(fontSize)`: 修改当前行字体大小（10-200），效果不继承到下一行。
*   `settextspeed(secPerChar)`: 设置打字机速度（秒/字，如 `0.05`）。
*   `setautospeed(speed)`: 设置自动播放速度。
*   `showprompt(text[, duration])`: 在屏幕上显示临时提示文字（duration 缺省 2 秒）。
    *   *示例*: `showprompt(好感度+1)`

</details>

<details>
<summary><strong>⚙️ 配置 (Config)</strong></summary>

<br>

*   `config(key:value)`: 运行时修改全局配置。
    *   *key*: `voice`（`true`/`false` 语音开关）、`textspeed`（打字速度）、`autospeed`（自动播放速度）。
    *   *示例*: `config(voice:false)`

</details>

---

## 📂 目录结构规范

初始化后的建议目录结构如下，保持规范有助于资源管理：

```text
Resources/VNovelizerRes/
├── VNScripts/       # 剧本 Excel 和生成的数据
├── Characters/      # 角色配置文件 (CharacterProfile)
├── Backgrounds/     # 背景图片
├── Audio/
│   ├── BGM/         # 背景音乐
│   ├── SFX/         # 音效
│   └── Voice/       # 语音文件
├── VNPrefabs/       # UI 和物体预制体
├── VFX/             # 粒子与动画预制体
└── GalleryContent/  # 画廊缩略图配置
```

---

## 📂 资源致谢 (Credits)

本插件示例工程中使用的美术与音频资源来自以下优秀的创作者（遵循其开源协议使用）：

*   🎵 **Music**: [D-wheat Music](https://itch.io/profile/d-wheat-music) (moonlight chill, piano lofi)
*   🖼️ **UI Assets**: [One Level Studio](https://itch.io/profile/onelevelstudio)
*   📦 **Icons**: [Prinbles](https://prinbles.itch.io/)
*   🏙️ **Backgrounds**: [Noraneko Games](https://itch.io/profile/noranekogames)
*   🎨 **Frames**: [K-ramstack](https://k-ramstack.itch.io/)
*   🛠️ **Alchemy**: [Annulus Games](https://github.com/annulusgames) (MIT licensed, bundled)

---

## 📜 许可证 (License)

本项目采用 **MIT LICENSE** 开源协议。
这意味着您可以免费将其用于任何 **开源** 或 **商业闭源** 项目，只需保留版权声明。

本框架内置了以下 MIT 协议的第三方库：**LitJson**、**Coffee.UIParticle**、**Alchemy** (© Annulus Games)。各库的完整许可信息见源码目录内对应文件。

虽然协议不限制，但如果您使用本框架制作了游戏，**请不要直接将本框架源码作为资产包进行二次售卖**。
如果您觉得好用，欢迎在 GitHub 点亮 ⭐ Star，这对我们非常重要！

---

<div align="center">
Copyright © 2026 Fakecorps. All rights reserved.
</div>