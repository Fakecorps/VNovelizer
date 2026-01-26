
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
*   **状态继承机制**：智能识别上下文，无需每行重复填写背景或立绘，极大提高填表效率。

### ⚡ 强大的演出系统
*   **指令系统 (Command System)**：内置 30+ 种常用指令（震屏、淡入淡出、立绘运动、视频播放等），支持指令并行与串行执行。
*   **高性能动画**：底层集成 **PrimeTween**，实现 0 GC 的丝滑 UI 动画体验。
*   **状态预演 (Fast Forward)**：支持任意节点的存读档与跳转。系统会自动模拟并演算跳转点之前的逻辑，确保背景、BGM、立绘状态的绝对正确，杜绝“读档立绘消失”的常见 Bug。

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

### 2. 导入核心库 (必读 ⚠️)
> **注意**：本框架动画系统依赖 **PrimeTween**。受限于 Asset Store 协议，无法内置分发。
> 请在导入本框架前，务必先下载并导入 PrimeTween（免费版即可）：
>
> 👉 **[下载 PrimeTween (Asset Store)](https://assetstore.unity.com/packages/tools/animation/primetween-high-performance-animations-252960)**

*(ExcelDataReader, LitJson, UIParticle 等其他依赖已内置)*

### 3. 安装 VNovelizer
通过 Unity Package Manager 安装：
1.  点击左上角 `+` -> `Add package from git URL`
2.  输入：`https://github.com/Fakecorps/VNovelizer.git`

### 4. 一键初始化项目
导入完成后，执行顶部菜单：
**VNovelizer -> 🔧 一键初始化 (Setup Wizard)**

点击 **"🚀 一键初始化项目"**。向导将自动：
*   构建标准目录结构 (`Assets/VNovelizerRes/...`)
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

1.  打开顶部菜单 **VNovelizer -> 📜 剧本管理器**。
2.  点击 **"➕ 新建"**，输入文件名（如 `Chapter1`）。Excel 将自动打开。
3.  **剧本字段详解**：

| 字段 (Column) | 必填 | 说明 (Description) | 示例 |
| :--- | :---: | :--- | :--- |
| **ID** | ✅ | **行号**。必须唯一，用于跳转和存档定位。 | `1001` |
| **Speaker** | | 说话人 ID。留空则继承上一句说话人。 | `Amy` |
| **HeadProfile** | | 头像配置。格式：`ID_表情名`。填 `hide` 隐藏。 | `Amy_Smile` |
| **CharLeft/Mid/Right** | | 左/中/右位置的立绘。格式：`ID_表情名`。填 `hide` 隐藏。 | `Amy_Normal` |
| **Text** | | 对话文本。支持 TMP 富文本标签。 | `你好，<color=red>陌生人</color>。` |
| **Background** | | 背景图名 (需在 Resources 背景目录)。留空继承。 | `School_Day` |
| **BGM** | | 背景音乐名。填 `stop` 停止，`pause` 暂停。 | `Theme_Song` |
| **Voice** | | 语音文件名。留空自动尝试加载 `行ID.mp3`。`false` 为静音。 | `1001_v` |
| **Command** | | 演出指令集。多条指令用 `&` 分隔。 | `shake(screen)&wait(0.5)` |
| **Note** | | 策划备注（游戏内不加载）。 | `第一章结束` |

4.  编辑完成后保存 Excel。
5.  在剧本管理器中点击 **"🔄 转换"**，生成游戏所需的 `.asset` 数据文件。

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

指令不区分大小写，参数间用逗号分隔，多指令用 `&` 连接。

<details>
<summary><strong>📐 流程控制 (Flow Control)</strong></summary>

<br>

*   `jump(id)`: 跳转到当前剧本的指定行 ID。
*   `loadscript(filename)`: 加载并切换到新的剧本文件。
*   `choice(Text | Command)`: 创建分支选项。
    *   *示例*: `choice(去吃饭|jump(200)) & choice(去睡觉|jump(300))`
*   `wait(seconds)`: 等待指定秒数后再继续。

</details>

<details>
<summary><strong>🎬 视觉演出 (Visual Effects)</strong></summary>

<br>

*   `bgfade(imageName, duration)`: 背景图淡入切换。
*   `shake(target, duration, strength)`: 震动效果。
    *   *target*: `screen` (全屏) 或 `dialogue` (对话框)。
*   `playparticle(name)`: 播放粒子特效（需预制体）。
*   `stopparticle(name)`: 停止粒子特效。
*   `playvideo(filename)`: 播放全屏视频（需位于 StreamingAssets）。
*   `playanim(name)`: 播放 Animator 动画状态。
*   `stopanim(name)`: 停止动画。

</details>

<details>
<summary><strong>🧍 立绘操作 (Character Control)</strong></summary>

<br>

*   `charjump(pos)`: 让指定位置的立绘跳跃 (`L`, `M`, `R`)。
*   `charflip(pos)`: 水平翻转立绘。
*   `charfadein(pos, duration)`: 立绘淡入。
*   `charfadeout(pos, duration)`: 立绘淡出。
*   `charmove(pos, x, y, duration)`: 移动立绘到指定坐标。
*   `setchartrans(pos, x, y, scale)`: 精确设置立绘位置和缩放。

</details>

<details>
<summary><strong>🔢 逻辑与数值 (Logic & Variables)</strong></summary>

<br>

*   `setboolflag(key, value)`: 设置布尔变量 (`true`/`false`)。
*   `setintflag(key, value)`: 设置整数变量。
*   `setstringflag(key, value)`: 设置字符串变量。
*   `unlockcg(name)`: 解锁画廊中的 CG。
*   `unlockmusic(name)`: 解锁画廊中的音乐。
*   `unlockscene(name)`: 解锁回想场景。
*   `playsfx(name, times)`: 播放音效。
*   `config(key:value)`: 动态修改配置 (如 `textspeed:0.1`).

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

---

## 📜 许可证 (License)

本项目采用 **MIT LICENSE** 开源协议。
这意味着您可以免费将其用于任何 **开源** 或 **商业闭源** 项目，只需保留版权声明。

虽然协议不限制，但如果您使用本框架制作了游戏，**请不要直接将本框架源码作为资产包进行二次售卖**。
如果您觉得好用，欢迎在 GitHub 点亮 ⭐ Star，这对我们非常重要！

---

<div align="center">
Copyright © 2026 Fakecorps. All rights reserved.
</div>