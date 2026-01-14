# 🚀 VNovelizer - Unity Visual Novel Framework

![Unity Version](https://img.shields.io/badge/Unity-6.0%2B-blue) ![License](https://img.shields.io/badge/License-MIT_LICENSE)

**VNovelizer** 是一款基于 Unity 的轻量级、高度可扩展的视觉小说（Visual Novel & AVG）制作框架。
即使你不懂编程，只需使用 **Excel** 编写剧本，即可快速制作出功能完整的视觉小说游戏！

> 📺 **作者 B 站首页**: [Fakecorps](https://space.bilibili.com/353379364)  
> 🔗 **GitHub 仓库**: [VNovelizer](https://github.com/Fakecorps/VNovelizer)
> 🔗 **VNoverlizer使用说明文档**:[VNovelizer飞书文档](https://my.feishu.cn/wiki/space/7589983850810346443?ccm_open_type=lark_wiki_spaceLink&open_tab_from=wiki_home)

---

## ✨ 核心特性 (Features)

*   **Excel 驱动工作流**：Excel -> CSV -> Json数据流完整，策划友好！无需触碰代码，就可以在 Excel 中编写对话、指令、逻辑。
*   **指令系统 (Command System)**：内置丰富的演出指令 (`jump`, `shake`, `BGfade`, `playanim`, `playparticle`   等)，支持扩展。
*   **全功能 UGUI 面板**：
    *   存档/读档 (SaveLoadPanel) + 截图预览
    *   异步加载面板 (LoadingProgressPanel) + 进度条
    *   标题界面面板 (MainMenuPanel)
    *   历史记录回放 (HistoryPanel)
    *   完整画廊功能 (CG/Scene/Music)
    *   设置面板 (音量、文本速度、全屏等设置已预设)
    *   双重确认面板/选项面板......
*   **高性能动画**：底层集成 **PrimeTween**，UI 动画 0 GC，流畅丝滑。
*   **状态预演 (Fast Forward)**：支持任意行跳转、读档，系统会自动模拟运行之前的逻辑，确保画面状态（背景、立绘、BGM）绝对正确。
*   **可视化编辑器**：
    *   **Script Manager**: 剧本一键转换、预览。
    *   **Resource Manager**: 资源可视化管理、导入。
    *   **Gallery/Character Editor**: 可视化配置 CG、立绘差分。

---

## 📦 安装指南 (Installation)

### 1. 前置依赖 (Prerequisites)

本插件依赖以下库，请确保你的项目满足要求：

*   **Unity 2022 或更高版本** (推荐 Unity 6.3)
*   **TextMeshPro** (Unity 内置，通常自动安装)
*   **Input System** (Unity 内置，通常自动安装)

**⚠️ 重要提示：手动安装 PrimeTween**
本插件动画系统依赖 **PrimeTween**。由于版权协议限制，无法内置分发。
请在导入插件前，前往 Asset Store 下载并导入（免费版即可）：
👉 **[下载 PrimeTween (Asset Store)](https://assetstore.unity.com/packages/tools/animation/primetween-high-performance-animations-252960)**

*(其他依赖如 ExcelDataReader, LitJson, UIParticle 已内置，无需额外操作)*

### 2. 导入 VNovelizer

打开 Unity Package Manager，点击 `+` -> `Add package from git URL`，输入：

```text
https://github.com/Fakecorps/VNovelizer.git
```

### 3. 一键初始化

导入完成后，点击顶部菜单栏：
**VNovelizer -> 🔧 一键初始化 (Setup Wizard)**

点击 **"🚀 一键初始化项目"**。这会自动为您：
1.  在 `Assets` 下创建所有必要的文件夹结构。
2.  生成全局配置文件 `VNProjectConfig`。
3.  复制核心预制体 (`VNPrefabs`) 和 示例场景 (`Scenes`) 到您的项目中。

---

## 📖 快速上手 (Quick Start)

### 1. 编写剧本 (Scripting)

1.  打开 **VNovelizer -> 📜 剧本管理器**。
2.  点击 **"➕ 新建"**，输入文件名（如 `Chapter1`）。Excel 会自动打开。
3.  **剧本表格规则详解**：

| 列名 (Column) | 说明 (Description) | 填写示例 | 注意事项 |
| :--- | :--- | :--- | :--- |
| **ID** | **行号 (必填)** | `1001` | 必须是唯一的数字或字符串。用于跳转 (`jump`) 和存档定位。 |
| **Speaker** | 说话人名字 | `艾米` / `??` | 如果留空，游戏会显示上一行的名字 (继承)。 |
| **HeadProfile** | 头像配置 | `Amy_Smile` | 对应角色ID_表情名。填 `hide` 可隐藏头像。 |
| **CharLeft/Mid/Right** | 立绘显示 | `Amy_Happy` | 对应角色ID_表情名。填 `hide` 隐藏该位置角色。 |
| **Text** | 对话内容 | `今天天气真好啊。` | 支持 RichText (如 `<color=red>危险</color>`)。 |
| **Background** | 背景图片名 | `School_Day` | 对应 Resources 里的图片名。留空则继承上一张背景。 |
| **BGM** | 背景音乐 | `Daily_Life` | 填 `stop` 停止音乐，填 `pause` 暂停。 |
| **Voice** | 语音文件名 | `1001_voice` | 留空会自动尝试加载与 ID 同名的文件。填 `false` 强制不播。 |
| **Command** | 演出指令 | `shake(screen)` | 多个指令用 `&` 连接。 |
| **Note** | 备注 | `这里是第一章` | 仅供策划备注，游戏内不读取。 |

> **💡 继承原则 (Inheritance Rule)**:
> VNovelizer 采用“状态继承”机制。为了减少填表工作量，**除了 ID 外，大部分列如果留空，系统会自动继承上一行的状态**。
> *   例如：如果第 1 行设置了背景为 `School`，第 2 行背景留空，那么第 2 行依然显示学校背景。
> *   如果想清除状态（如隐藏立绘），请显式填入 `hide` 或 `stop`。

4.  保存 Excel，回到 Unity，在剧本管理器中点击 **"🔄 转换"**。

### 2. 运行游戏
1.  打开 `Assets/Scenes/VNDebugScene` 场景。
2.  输入ScriptName(剧本名) + 行ID
3.  点击Start自动跳转到你的剧本的第ID行
4.  或者，你也可以在任意场景内,创建一个空物体，挂载启动脚本：
    ```csharp
    void Start() {
        // 启动 Chapter1
        VNManager.GetInstance().StartGame("Chapter1");
    }
    ```
---

## 🎮 常用指令 (Commands)

在 Excel 的 `Command` 列中填写，支持 `&` 连接多个指令。

| 指令 | 示例 | 说明 |
| :--- | :--- | :--- |
| **jump** | `jump(100)` | 跳转到 ID 为 100 的行 |
| **loadscript** | `loadscript(Chapter2)` | 加载并切换到另一个剧本文件 |
| **bgfade** | `bgfade(School_Night, 2.0)` | 背景淡入切换 (2秒) |
| **charjump** | `charjump(M)` | 中间立绘跳跃 |
| **charflip** | `charflip(L)` | 左侧立绘水平翻转 |
| **charfadein** | `charfadein(R, 1.0)` | 右侧立绘淡入 |
| **shake** | `shake(screen, 0.5, 20)` | 屏幕震动 (时间, 强度) |
| **playparticle** | `playparticle(Snow)` | 播放粒子特效 (如 Snow, Rain) |
| **playvideo** | `playvideo(OP.mp4)` | 播放全屏视频 (需在 StreamingAssets) |
| **choice** | `choice(yes｜jump(10))&choice(no｜jump(20))` | 显示分支选项,yse -> 跳转至ID10处,no -> 跳转到ID20处 |

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

本项目代码采用 **MIT LICENSE** 协议开源。
您可以免费用于任何开源或闭源商业项目

虽然协议不管，但还是请你不要直接打包进行售卖，红豆泥阿里嘎多

Copyright © 2026 Fakecorps. All rights reserved.