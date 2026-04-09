# VNAPI 参考

`VNAPI` 是运行时对外暴露的静态入口，便于自定义脚本、演出插件或外部系统在**不直接依赖内部 UI 类型**的情况下，安全地读取/修改常用状态。

- **命名空间**：`VNovelizer.Core.API`
- **用法**：`using VNovelizer.Core.API;`，然后调用 `VNAPI.xxx(...)`。

## 使用前提与空引用

多数接口依赖 **`VNGameplayPanel` 已存在**（通常在 `VNGamePlay` 场景或由框架创建的画布上）。若面板尚未加载，相关方法会返回 `null` 或静默不执行。

建议需要判空时使用：

- `VNAPI.TryGetGameplayPanel(out var panel)`
- `VNAPI.HasGameplayPanel()`

---

## 1. 界面与组件引用

| API | 说明 |
|-----|------|
| `TryGetGameplayPanel(out VNGameplayPanel panel)` | 尝试获取主游戏面板。 |
| `HasGameplayPanel()` | 是否存在可用主面板。 |
| `GetBG_F()` / `GetBG_B()` | 前/后背景 `Image`。 |
| `GetCharRect(posCode)` | 立绘槽位 `RectTransform`。`posCode` 支持 `L/M/R`、`LEFT/MID/RIGHT` 等（与面板内一致）。 |
| `GetCharImage(posCode)` | 立绘槽位 `Image`（`L/M/R` 或 `Left/Mid/Right` 等）。 |
| `GetCharScaleX` / `SetCharScaleX` | 角色朝向（缩放 X），委托 `VNManager`。 |
| `GetDialogueText()` | 对话正文 `TMP_Text`。 |
| `GetSpeakerBox()` / `SetSpeakerBox(Sprite)` | 说话人框 `Image` / 设置 Sprite。 |
| `SetSpeaker(string)` | 按角色配置更新说话人显示。 |
| `GetSpeakerText()` | 说话人名字 `TMP_Text`。 |
| `GetEffectLayer()` | 特效挂载父节点 `Transform`。 |
| `GetDialogueBoxRect()` | 对话框区域 `RectTransform`（震屏等）。 |
| `SetDialogueTextColor` / `SetDialogueTextSize` | 修改对话正文样式（会先记录默认值）。 |
| `RestoreDefaultDialogueTextProperties()` | 恢复正文默认颜色与字号。 |
| `IsDialogueTyping()` | 是否正在打字机播放。 |
| `CompleteDialogueTyping()` | 立即结束当前打字机动画。 |

---

## 2. 文本速度与自动播放

| API | 说明 |
|-----|------|
| `GetTextSpeed()` / `SetTextSpeed(float)` | 打字速度（秒/字，越小越快）。 |
| `GetAutoSpeed()` / `SetAutoSpeed(float)` | 自动播放句间等待（秒）。 |

数据来自 `GlobalDataManager` 的全局存档数据。

---

## 3. 游戏标志（Flag）

| API | 说明 |
|-----|------|
| `SetBoolFlag` / `GetBoolFlag` | 布尔标志。 |
| `SetIntFlag` / `GetIntFlag` | 整型标志。 |
| `SetStringFlag` / `GetStringFlag` | 字符串标志。 |

未定义时的默认返回值与 `GlobalDataManager` 行为一致（如 `GetBoolFlag` 为 `false`）。

---

## 4. 特效与演出

| API | 说明 |
|-----|------|
| `RegisterEffect(string)` / `UnregisterEffect(string)` | 向 `VNManager` 登记/注销特效名（与存档、流程一致）。 |
| `GetActiveEffectNames()` | 当前已登记特效名列表（**副本**）。 |
| `ClearAllEffects()` | 销毁特效层下所有子物体，并**同步注销**上述登记，避免状态残留。 |
| `ExecuteCommand(string)` | 执行一条指令字符串（与剧本中指令语法一致）。 |
| `UpdateBGData(string)` | 仅更新当前背景内部数据，不刷新 UI。 |
| `ShowPrompt(string text, float duration)` | 主界面提示条。 |
| `PlayVideo(string videoName, Action onComplete)` | 在系统层实例化视频预制并播放；需配置 `VNProjectConfig.VideoObjPath`。 |

---

## 5. 流程与游戏状态

| API | 说明 |
|-----|------|
| `GetCurrentScriptName()` | 当前剧本文件名。 |
| `GetCurrentLineIndex()` | 当前行索引（0-based），无效时为 `-1`。 |
| `TryGetCurrentLineId(out string lineId)` | 当前行的行 ID（Excel ID 列）。 |
| `NextLine()` | 推进到下一句（含动画逻辑）。 |
| `NextLineWithoutAnimation()` | 无动画推进。 |
| `GetGameState()` | 当前 `GameState`（如 `Gameplay`、`AutoPlay`、`Choice` 等）。 |
| `CanInteractGameplay()` | 是否允许主流程交互（如点击下一句）。 |

---

## 6. 本地化（可选）

需满足：`VNProjectConfig.EnableLocalization` 为 true，且工程定义了 `VN_LOCALIZATION` 并正确配置 Unity Localization。详见 **`VNLocalizationGuide.md`**。

| API | 说明 |
|-----|------|
| `IsLocalizationEnabled()` | 项目是否开启剧情本地化。 |
| `TryGetLocalizedText(lineId, out text)` | 当前剧本下键 `text.{lineId}`。 |
| `TryGetLocalizedSpeaker(lineId, out speaker)` | 当前剧本下键 `speaker.{lineId}`。 |
| `TryGetLocalizedByFullKey(fullKey, out text)` | 使用完整 entry key（如 Choice 等场景）。 |

上述三个 `TryGet*` 均基于 **`GetCurrentScriptName()`** 选择 String Table；若当前无剧本名或表缺失，返回 `false`。

---

## 7. 协程

| API | 说明 |
|-----|------|
| `StartCoroutine`（多重重载） | 委托 `MonoManager` 启动协程。 |
| `StopCoroutine` | 停止指定协程。 |
| `StopAllCoroutines()` | 停止全部协程（**慎用**，会影响 BGM、自动播放等）。 |

---

## 版本说明

- `ClearAllEffects` 会先清空 `VNManager` 内登记的特效名再销毁子物体，与读档/保存中的 `ActiveEffects` 保持一致。
- 调试向日志（如找不到系统层、配置缺失）使用 `VNDebug`，仅在 Editor / Development Build 中输出；严重错误（如预制体缺失）仍使用 `Debug.LogError`。
