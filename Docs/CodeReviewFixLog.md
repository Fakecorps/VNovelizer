# VNovelizer 全量代码审查修复记录（Code Review Fix Log）

> **日期**：2026-09-11
> **范围**：2026-09-11 全面代码审查（5 分区并行审查 + 关键证据逐行复核）发现的全量缺陷修复
> **留痕约定**：每处修复均在源码中以 `【Fix-NN】` 注释标记（NN 对应本文件修复编号），
> 可用 `grep -rn "Fix-" Runtime/ Editor/` 全局检索追溯；本文件是修复的权威说明文档。

---

## 一、修复总览

| 优先级 | 主题 | 修复数 | 状态 |
|--------|------|--------|------|
| P0 | 存档/加密安全链 | 7 | ✅ 已完成 |
| P1 | 核心流程卡死类 | 8 | ✅ 已完成 |
| P2 | 命令系统/演出/UI 正确性 | 18 | ✅ 已完成 |
| P3 | Editor 工具链安全与体验 | 13 | ✅ 已完成 |
| P4 | 基础框架/包分发/文化兼容 | 12 | ✅ 已完成 |
| — | 降级/遗留（见 §五） | 3 | ⚠️ 已评估记录 |

共修改 **约 45 个文件**，全部改动通过静态 lint 校验（0 error / 0 warning）。

---

## 二、P0 存档与加密安全（数据完整性底线）

### Fix-34｜AES 加密失败不再静默降级明文落盘
- **文件**：`Runtime/Scripts/VNovelizer/Core/Utils/AESUtil.cs`
- **问题**：`Encrypt` 内部 catch 所有异常并 `return plainText`，`SaveManager` 的 try-catch 形同虚设——用户开启 `UseAES` 实际落盘明文。
- **修复**：删除 catch 兜底，异常显式上抛；由 `SaveManager.WriteSaveData` 已有的 try-catch 拒绝落盘（LogError + return false）。

### Fix-35｜密钥/IV 去除硬编码默认值 + 随机 IV
- **文件**：`Runtime/Scripts/VNovelizer/Core/Utils/AESUtil.cs`
- **问题**：`DefaultKey1234567890123456789012` / `DefaultIV1234567` 双份硬编码；固定 IV 使相同明文产生相同密文（可差分分析）。
- **修复**：
  - `GetKey()` 未配置时抛 `InvalidOperationException`（不再兜底）；
  - `Encrypt` 每次用 `RandomNumberGenerator` 生成随机 IV，输出格式 `base64(IV):base64(密文)`；
  - `Decrypt` 兼容新格式（按 `:` 拆分）与旧格式（回退配置 IV），**旧存档可正常读取**。

### Fix-36｜存档/全局数据原子写入 + .bak 轮换备份
- **文件**：`SaveManager.cs`（`WriteSaveData`）、`GlobalDataManager.cs`（`SaveGlobalData`）
- **问题**：`File.WriteAllText` 直接截断覆盖，写一半断电/杀进程即损坏旧档。
- **修复**：写临时文件 → `File.Replace(tmp, dest, dest + ".bak")` 原子替换（不存在时 `File.Move`）。

### Fix-37｜global_data.json 损坏不再导致游戏无法启动
- **文件**：`GlobalDataManager.cs`（`LoadGlobalData`）
- **问题**：`ReadAllText`/`ToObject` 无 try-catch，异常沿 `Init()` 上抛——主菜单都进不去。
- **修复**：异常时备份坏文件为 `.corrupted_{Ticks}` 并重建默认值，与 `SaveManager` 容错对齐。

### Fix-38｜损坏存档读取全链路容错
- **文件**：`SaveManager.cs`（`ReadSaveData`）
- **问题**：`File.ReadAllText` 在 try 外（文件被占用即穿透）；重试路径二次 `ToObject` 无保护（坏加密档异常逃逸到主菜单/存档面板）。
- **修复**：整个流程（读/解密/解析/重试）包进单一 try-catch，任何失败按"坏档"降级返回 null。

### Fix-4｜读档 Characters 判空
- **文件**：`VNManager.cs`（`RestoreGameStateFromSave`）
- **问题**：旧版存档缺 `Characters` 字段时 LitJson 反序列化为 null，字典拷贝构造抛 `ArgumentNullException` 中断读档。
- **修复**：`saveData.Characters != null ? new Dictionary(...) : new Dictionary(...)`。

### Fix-39｜截图写盘异常不再中断保存主流程
- **文件**：`SaveManager.cs`（`SaveScreenshot` / `WriteScreenshotThumbnail`）
- **问题**：`EncodeToPNG`/`WriteAllBytes` 无 try-catch，磁盘满/权限问题沿 `VNManager.SaveGame` 链路抛出。
- **修复**：try-catch 包裹，失败仅 LogError；返回原路径（文件不存在）而非 null，避免下游 `File.Exists(null)` 抛异常。

---

## 三、P1 核心流程卡死类（玩家可直接触发的可见故障）

### Fix-1｜TypingFinished 幂等订阅，修复自动播放跳 N 行
- **文件**：`VNManager.cs`（`InitializeManager`）
- **问题**：`InitializeManager` 每次开局/读档都叠加一份 `OnTypingFinished` 订阅（EventCenter 不去重），打字完成时 N 个回调各启动一个 AutoPlay 倒计时 → 一次跳过 N 行。
- **修复**：订阅前先 `RemoveEventListener` 再 `AddEventListener`。

### Fix-2｜读档特效双重恢复
- **文件**：`VNManager.cs`（`RestoreGameStateFromSave`）
- **问题**：`FastForwardToLine` 末尾已恢复特效，读档又按 `saveData.ActiveEffects` 恢复一遍；`PlayParticle` 的异步去重在回调前检查 → 两个同名粒子叠加。
- **修复**：特效恢复加 `&& encounteredChoice` 条件（choice 场景才补恢复）。

### Fix-3｜状态栈跨会话残留
- **文件**：`GameStateManager.cs`（新增 `ResetToGameplay`）、`VNManager.cs`（回主菜单/读档两处调用点）
- **问题**：`Pause→SaveLoad→读档` 后栈残留 `{Pause,Gameplay}`，后续面板 `PopState` 弹出旧状态对 → `CanInteractGameplay` 恒 false。
- **修复**：新增 `ResetToGameplay()`（清栈 + SetState(Gameplay)），开局/读档/回主菜单统一调用。

### Fix-6｜命令引用计数 try/finally
- **文件**：`VNCommand.cs`（`ExecuteSingleCommandAsync`）
- **问题**：命令 `ExecuteAsync` 抛异常时递减逻辑被跳过，`IsRunning` 恒 true → 自动播放/流程永久停摆。
- **修复**：递减逻辑移入 `finally`（与 `ChainExecutor.ExecuteCommand` 对齐）。

### Fix-7｜choice 选项链 / 重播路径 ctx 登记
- **文件**：`VNCommand.cs`（新增 `RegisterActiveChain/UnregisterActiveChain` + `InterruptAll` 遍历 `_extraChainContexts`）、`VNManager.cs`（`ExecuteChoiceChainCoroutine`、`ExecuteReplayFrom` 两处）
- **问题**：这两条路径自建 `ChainRunContext` 未登记，跳过时并行分支协程（`RunBranch`）不随主链死亡，残留 `wait/charmove` 污染下一行。
- **修复**：登记进 `_extraChainContexts` 列表，`InterruptAll` 一并 `ChainExecutor.Abort`；执行完毕（finally）解登记。

### Fix-23｜转场系统死锁兜底（黑幕挂死 + 输入永久禁用）
- **文件**：
  - `DarkFadeTransitionEffect.cs`：`FadeToAlpha` 的 `WaitUntil(tweenDone)` → 计时等待 + 超时强制 `SetAlpha(终态)`；`CoPlayTransitionAsync` 的 middleAction 等待加 600s 超时；
  - `TransitionManager.cs`：新增 `ForceReset()`（复位 `IsTransitionPlaying` + 恢复全部 UI 输入）；
  - `VNManager.cs`：`ReturnToMainMenu` 与 `EndReplay` 的 `AnimationCompat.StopAll()` 之后调用 `ForceReset()`；
  - `FadeBlackOutCommand.cs`：等待循环加超时 + `Interrupt` 兜底解除黑幕；
  - `FadeBlackInCommand.cs`：补 `Simulate`/`Interrupt`（快进跳过淡入时解除黑幕与 raycast 拦截，防止全屏黑死）。
- **问题**：`StopAll` 停掉黑幕 tween 不触发 OnComplete → `WaitUntil` 永久挂起 → `IsTransitionPlaying` 恒 true + `SetAllUIInputModulesEnabled(false)` 只在 onComplete 恢复 → 游戏彻底无法点击。

### Fix-45（相关）｜Kill Excel 前必须用户确认
- **文件**：`Editor/ExcelProcessHelper.cs`
- **问题**：镜像写回遇锁直接 `Process.Kill()`，用户 Excel 未保存修改（含其他工作簿）静默丢失。
- **修复**：Kill 前 `EditorUtility.DisplayDialog` 确认；取消则放弃本次写回（返回 false）。

---

## 四、P2 命令系统 / 演出 / UI 正确性

### Fix-8｜快进不再真实播放动画/音效/视频
- **文件**：`CharJumpCommand.cs`、`PlayAnimCommand.cs`、`PlaySFXCommand.cs`、`PlayVideoCommand.cs`、`StopParticleCommand.cs`
- **问题**：同步 `Execute`（快进 `ExecuteCommandsInstant` 路径）直接 `StartCoroutine(ExecuteAsync)` 启动**未登记**协程——快进时角色真的跳、动画/音效真的播、视频全屏播放，且 `InterruptAll` 无法终止。
- **修复**：`CharJump/PlayAnim/PlaySFX/PlayVideo` 的 `Execute` 改为快进语义（写终态/忽略播放）；`StopParticle` 的 `Execute` 改为同步注销 + 立即停止发射，仅"5 秒延迟回收"保留异步（清理性质，不污染演出）。

### Fix-9｜PlayVideo 中断契约 + 等待超时
- **文件**：`PlayVideoCommand.cs`
- **问题**：`Interrupt` 置 `isFinished=true` 后等待循环正常退出，**仍执行"结束后的命令"**（跳过视频会静默改写剧本流程）；等待无超时。
- **修复**：新增 `_interrupted` 标志，`Interrupt` 置位；执行后续命令前检查 `!_interrupted`；等待循环加 10 分钟超时兜底。

### Fix-10｜CharJump 中断标志 + 状态回写
- **文件**：`CharJumpCommand.cs`
- **问题**：跳跃循环无中断检查（Interrupt 后继续覆盖位置）；`Interrupt` 只改 actor 视觉不回写 `state.position` → 存档残留空中随机位置。
- **修复**：新增 `_interrupted` 标志（循环内每帧检查退出）；`Interrupt` 改用 `theater.SetPosition(aj.PosCode, aj.StartPos)` 写状态字典。

### Fix-11｜Shake 震动期间保持"运行中"
- **文件**：`ShakeCommand.cs`
- **问题**：震动是 fire-and-forget 命令，`shake(screen,1.5)&wait(1)` 中 0.5 秒后跳过时 `InterruptAll` 不命中 Shake → 震动侵入下一行。
- **修复**：重写 `ExecuteAsync`：解析启动后震动期间保持协程存活（占用引用计数），使跳过时 `InterruptAll` 能命中并归位。

### Fix-12｜StopParticle 异步路径注销特效注册
- **文件**：`StopParticleCommand.cs`
- **问题**：异步路径不调 `UnregisterEffect` → `activeEffects` 仍认为粒子在播 → 存档/读档"复活"已停止的粒子。
- **修复**：`ExecuteAsync` 开头补 `VNAPI.UnregisterEffect(effectName.Trim())`。

### Fix-13｜BgTrans 快进同步终态
- **文件**：`BgTransCommand.cs`、`TheaterManager.cs`（新增 `ApplyBackgroundImmediate`）
- **问题**：`Execute` 空实现 → 快进后背景不切换、`currentBG` 数据不更新，存档记旧背景。
- **修复**：`Execute` 写数据终态（`UpdateCurrentBG_OnlyData`）+ 新增同步即时应用入口（作废在途请求/过渡 → 同步加载 Sprite → `ApplyBackground`）。

### Fix-14｜PlayAnim RectTransform 判空
- **文件**：`PlayAnimCommand.cs`
- **问题**：无 RectTransform 的预制体直接 `.localScale` → NRE 并经 Fix-6 之前的链路卡死引用计数。
- **修复**：`rect == null` 时报错、回收对象、`yield break`；顺带补了特效层 null 保护。

### Fix-15｜PlayParticle 异步去重
- **文件**：`PlayParticleCommand.cs`
- **问题**：去重检查在异步回调之前，并行触发同名特效时叠加；加载失败前已注册特效状态。
- **修复**：`_pendingRequests` 占位集合同步去重 + 回调内二次去重（挂点销毁/同名已建时销毁新实例）+ 加载失败回滚 `UnregisterEffect`。

### Fix-17｜bgtrans 令牌校验提前
- **文件**：`TheaterManager.cs`（`TransitionBackgroundCoroutine`）
- **问题**：令牌校验在整个过渡播完之后，旧协程复活会覆盖新过渡的共享字段（临时演员泄漏 + 等待死锁）。
- **修复**：异步加载完成后**第一件事**校验令牌，不等价立即退出。

### Fix-18｜black/hide 作废在途背景加载
- **文件**：`TheaterManager.cs`（`OnChangeBackground`）
- **问题**：black/hide 分支不作废在途异步加载，黑幕被上一行的背景加载完成后覆盖。
- **修复**：与 `OnHideBackground` 对齐：`_bgRequestToken++` + 停协程 + 取消过渡。

### Fix-19｜背景 Sprite 包装缓存
- **文件**：`TheaterManager.cs`（`LoadTextureAsSprite`）
- **问题**：每次调用 `Sprite.Create` 且从不销毁，长剧本/多次读档 Sprite 对象单调累积。
- **修复**：`Dictionary<Texture2D, Sprite>` 缓存，同一纹理复用包装对象。

### Fix-20｜BGM 异步加载代际令牌
- **文件**：`MusicManager.cs`
- **问题**：快速切歌时旧请求回调后到覆盖新歌，同名跳过逻辑使错误 BGM 无法自愈。
- **修复**：`_bgmRequestId` 代际校验（回调到达时丢弃过期结果）；`StopBGM` 时递增作废在途加载。

### Fix-21｜截图后恢复 RenderTexture.active
- **文件**：`SaveManager.cs`（`CaptureCurrentScreen`）
- **问题**：`RenderTexture.active = null` 硬置而非恢复先前值，破坏其他系统的 RT 渲染。
- **修复**：保存 prev 并恢复（与 `CreateThumbnail` 一致）。

### Fix-22｜快进 Time.timeScale 恢复不对称
- **文件**：`VNGameplayPanel.cs`
- **问题**：按住 Skip 打开暂停/存档面板时只复位 `isSkipping` 不恢复 `Time.timeScale` → 全游戏 10 倍速。
- **修复**：`Update` 两处改走 `StopSkip()`；`StopSkip` 增加防御分支（timeScale 恰为 10 时恢复）；`OnDisable` 追加 `StopSkip()`。

### Fix-24｜HistoryPanel 异步池回调守护 + 模板节点判空
- **文件**：`HistoryPanel.cs`
- **问题**：面板关闭后飞行中的异步回调产生孤儿 GameObject；`Find(...).GetComponent<T>()` 无判空。
- **修复**：回调首行守护（面板已销毁则销毁新实例并返回）；模板节点 `?.` 判空，缺失时 LogError 跳过。

### Fix-25｜HistoryPanel 关闭时停止回放语音
- **文件**：`HistoryPanel.cs`
- **问题**：关闭面板后历史语音继续播放，干扰 AutoPlay 推进判定。
- **修复**：`OnCloseButtonClick`/`OnDestroy` 统一 `StopReplayVoice()`（`IsVoicePlaying` 时 `StopVoice`）。

### Fix-26｜SettingsPanel closeBtn 监听器不再累积
- **文件**：`SettingsPanel.cs`
- **问题**：`UnbindEvents` 排除 closeBtn，每次打开面板叠加 3 个监听器，点一次关闭执行 3 次。
- **修复**：`UnbindEvents` 中成对 `RemoveListener(OnCloseBtnClick)`。

### Fix-27/28/30｜面板控件判空（模板覆写健壮性）
- **文件**：`VNGameplayPanel.cs`（`UpdateSkipButtonState`）、`SaveLoadPanel.cs`（Close/Prev/Next 按钮）、`ConfirmPanel.cs`（Yes/No）、`MainMenuPanel.cs`（`OnLoadGameBtnClick`）、`ChoicePanel.cs`（`ShowChoices` 预制体/容器）
- **问题**：`GetControl` 依赖命名契约，模板覆写缺控件即 Awake/点击路径 NRE。
- **修复**：统一判空 + LogError 提示。

### Fix-29｜LoadingProgressPanel 自主隐藏兜底生效
- **文件**：`LoadingProgressPanel.cs`
- **问题**：`HideMe` 不 `SetActive(false)`，"无驱动者流程"兜底路径下加载面板永远停在屏幕上。
- **修复**：`HideMe` 末尾补 `gameObject.SetActive(false)`（UIManager 重复调用无害）。

### Fix-31｜SaveLoadPanel 销毁不依赖销毁顺序
- **文件**：`SaveLoadPanel.cs`（`OnDestroy`）
- **问题**：场景切换时 PausePanel 先销毁（状态仍是 SaveLoad 不恢复），SaveLoadPanel `PopState` 只弹回 Pause → 状态栈残留卡死交互。
- **修复**：`PopState` 后若当前状态为 Pause 则再弹一次，不依赖销毁顺序。

### Fix-32｜VNAPI._activeVideo 自然播完清空
- **文件**：`API.cs`（`PlayVideo`）
- **问题**：视频自然播完自毁后静态引用残留。
- **修复**：`Play` 回调中 `if (_activeVideo == player) _activeVideo = null;` 再转发用户回调。

### Fix-33｜SaveSlot 中断时 Dispose UnityWebRequest
- **文件**：`SaveSlot.cs`
- **问题**：`StopCoroutine` 丢弃迭代器时 `using` 的 Dispose 不执行，翻页/快速开关累积网络句柄；`file://` 未转义中文/空格路径。
- **修复**：持有 `_pendingRequest` 字段，中断处显式 Dispose；`LoadScreenshot` 改 try/finally；URI 用 `System.Uri.EscapeUriString`。

### Fix-41/40｜EventCenter 监听者隔离 + 签名冲突保护
- **文件**：`EventCenter.cs`
- **问题**：任一监听者抛异常中断其余监听者；同名事件泛型/非泛型混用注册时 `as` 失败 NRE。
- **修复**：`GetInvocationList()` 逐个 try-catch 调用；注册/移除处 `is` 类型校验，冲突时 LogError 忽略。

### Fix-42｜对象池上限/去重/复位
- **文件**：`PoolManager.cs`（`poolData`）
- **问题**：无上限（反复 Get/Push 无限膨胀）、重复 Push 无去重、取出不复位变换。
- **修复**：`MaxPooledPerKey = 20`（超限销毁）、重复推回忽略、取出时复位 localPosition/localScale。

### Fix-43｜MonoManager 宿主自愈
- **文件**：`MonoManager.cs`
- **问题**：关闭 Domain Reload 后静态 instance 保留但 MonoController 已销毁 → 全部协程/监听 MissingReferenceException。
- **修复**：`EnsureController()` 每次访问校验重建；构造函数补 `DontDestroyOnLoad`。

### Fix-44｜SingletonMono 惰性查找 + 跨场景
- **文件**：`SingletonMono.cs`
- **问题**：Awake 前调用返回 null；销毁后返回假 null 引用；无跨场景策略。
- **修复**：`GetInstance` 判空后 `FindFirstObjectByType<T>()` 兜底；`Awake` 补 `DontDestroyOnLoad`。

### Fix-61｜JsonManager 平台与静默丢数据
- **文件**：`JsonManager.cs`
- **问题**：JsonUtility 分支含字典数据静默输出 "{}"；Android StreamingAssets 路径读取必然失败；解析无 try-catch。
- **修复**：序列化后检测字典字段并告警；读取/写入 try-catch 降级默认值。

### Fix-62｜数值解析统一 InvariantCulture
- **文件**：`ConditionParser.cs`、`FlagService.cs`、`WaitCommand.cs`、`ShakeCommand.cs`、`BgTransCommand.cs`、`CharJumpCommand.cs`、`FadeBlackOutCommand.cs`、`FadeBlackInCommand.cs`
- **问题**：de-DE/fr-FR 等小数点为逗号的系统上剧本 `"0.5"` 解析失败（条件分支失效/震动静默 0 秒/注册表默认值归零）。
- **修复**：全部 `TryParse` 补 `NumberStyles.Float/Integer, CultureInfo.InvariantCulture`。

---

## 五、P3 Editor 工具链

### Fix-46｜Setup Wizard 不再字符串拼接改写 manifest.json
- **文件**：`VNovelizerSetup.cs`
- **问题**：`LastIndexOf('}')` 猜测 dependencies 闭合位置，嵌套对象/注释差异会生成非法 JSON 毁掉包清单。
- **修复**：新增 `FindJsonValueCloseBrace`（字符串感知的括号匹配器，跳过引号与转义）精确定位；写回前备份 `.bak`；无 BOM UTF-8 写入。

### Fix-3（Editor）｜Input System 配置走 SerializedObject
- **文件**：`VNovelizerSetup.cs`（`ConfigureInputSystemBoth`）
- **问题**：全局文本替换 `activeInputHandler: 0` 可能误伤文件内其他同名文本，且与 Unity 运行中的整体重写冲突。
- **修复**：优先 `SerializedObject.FindProperty("activeInputHandler")` + `ApplyModifiedProperties`；API 不可用时回退文本替换。

### Fix-47｜自动转换失败不再永久遗忘修改
- **文件**：`AutoExcelConverter.cs`
- **问题**：转换**前**就记账时间戳，转换失败（Excel 独占等）后修改永不再触发。
- **修复**：改动清单携带旧时间戳，成功后才记账，失败恢复旧值让下一轮重试。

### Fix-48｜自动转换每轮批处理上限
- **文件**：`AutoExcelConverter.cs`
- **问题**：每 2 秒主线程同步全目录扫描 + 全量转换大 Excel（11MB 对白），周期性冻结编辑器。
- **修复**：`BatchSizePerCycle = 3`，每轮最多转 3 个文件，未处理的下一轮继续（2 秒间隔自然节流）。

### Fix-50｜进 Play 只落盘 CSV，镜像写回推迟
- **文件**：`RowPerfEditorWindow.cs`、`ExcelToCSVConverter.cs`
- **问题**：`ExitingEditMode` 同步执行读 xlsx → 写回 →（可能弹 Kill 确认）→ `Thread.Sleep(500)×3`，点 Play 冻结数秒。
- **修复**：`ExitingEditMode` 只原子写 CSV（`skipMirrorWriteBack: true`），`ExitingPlayMode` 补镜像写回；`TrySaveWorkbook` 重试降为 3 次 × 200ms（≤400ms）。

### Fix-49｜CSV 保存临时文件卫生
- **文件**：`RowPerfEditorWindow.cs`（`TryWriteCsv`）
- **问题**：临时文件 `.tmp` 在 Assets 内残留会被导入为垃圾资产；`File.Replace` 失败无清理。
- **修复**：后缀改 `.tmp~`（AssetDatabase 不导入）；`Replace` 失败降级 `Copy`；`finally` 清理残留。

### Fix-51｜ScriptManager 预览注册 CodePages + 真实错误展示
- **文件**：`ScriptManager.cs`（`LoadPreview`）
- **问题**：预览旧格式 `.xls` 抛 `NotSupportedException`，UI 却显示"文件被占用"。
- **修复**：入口注册 `CodePagesEncodingProvider`；catch 展示 `e.Message` 原文。

### Fix-52｜CreateGUI 空路径防护
- **文件**：`ScriptManager.cs`（`CreateGUI`）
- **问题**：配置文件夹引用失效（Missing）时 `Path.GetFullPath("")` 抛异常窗口白屏。
- **修复**：先取 asset path 判空，空则显示红字提示并返回。

### Fix-53｜重命名非法字符校验 + 全量异常捕获
- **文件**：`ScriptManager.cs`（`RenameScript`）
- **问题**：输入 `/ : * ?` 等字符抛 DirectoryNotFound/ArgumentException，误报"文件被占用"或穿透报错。
- **修复**：`GetInvalidFileNameChars()` 校验弹窗；catch 区分 IOException 与其他异常。

### Fix-54｜删除剧本走 AssetDatabase
- **文件**：`ScriptManager.cs`（`DeleteScript`）
- **问题**：`File.Delete` + 手删 meta 绕过 AssetDatabase，Addressables 条目残留死引用。
- **修复**：Assets 内 CSV/Excel 一律 `AssetDatabase.DeleteAsset`（项目外文件才 File.Delete）。

### Fix-55｜AudioPlayerView 显式 Dispose
- **文件**：`AudioPlayerView.cs`、`ResourcesEditorManager.cs`
- **问题**：依赖终结器退订静态事件，窗口重建时订阅者越积越多。
- **修复**：新增 `Dispose()`；`CreateGUI` 重建前 `_audioPlayerView?.Dispose()`。

### Fix-56｜EditorUpdateService 无消费者时自动停
- **文件**：`AudioPreviewService.cs`（`EditorUpdateService`）
- **问题**：`StartTracking` 后全项目无停止调用点，update 回调常驻空转。
- **修复**：`RegisterCallback` 有回调才 `StartTracking`；`UnregisterCallback` 自动 `StopTracking`。

### Fix-57｜角色重命名校验与语义修正
- **文件**：`CharacterEditorPresenter.cs`
- **问题**：字符串拼接 newPath + 无非法字符校验；`RenameAsset` 第二参数只是新文件名，比较逻辑失真。
- **修复**：非法字符校验；直接 `RenameAsset(path, newName)`，去掉无意义的 newPath 比较。

### Fix-58｜路径统一规范化
- **文件**：`ExcelToCSVConverter.cs`、`VNLocalizationSyncUtility.cs`
- **问题**：`File.*`/`Directory.*` 基于 `Environment.CurrentDirectory`，被其他插件改变后写错位置或静默失败。
- **修复**：`ConvertAllExcelFiles`/`ConvertFile`/本地化两处入口统一 `Path.GetFullPath`。

---

## 六、P4 包分发与本地化

### Fix-59｜package.json 显式声明传递依赖
- **文件**：`package.json`
- **问题**：asmdef 硬引用 PrimeTween.Runtime / Unity.Serialization / Unity.Collections，但 package.json 未声明——缺包时编译失败，`PRIME_TWEEN_INSTALLED` 宏降级形同虚设。
- **修复**：新增 `com.unity.serialization: 3.1.1`、`com.unity.collections: 2.1.4`、`com.kyrylokuzyk.primetween: 1.4.11`（版本与开发工程 manifest 对齐）。
- **注意**：PrimeTween 来自 npm scoped registry（`com.kyrylokuzyk`），消费工程需在自身 manifest 配置该 registry（Setup Wizard 会自动添加）。

### Fix-60｜VNLocalizationWindow 本地化 API 宏保护
- **文件**：`VNLocalizationWindow.cs`
- **问题**：唯一一处裸用 `UnityEditor.Localization` 且无 `#if VN_LOCALIZATION` 保护，与同目录 `VNLocalizationSyncUtility` 的降级路径不一致。
- **修复**：`LocateByCollectionName` 加宏保护，宏未定义时弹提示。
- **已知边界**：`VNovelizer.Editor.asmdef` 仍无条件引用 `Unity.Localization.Editor` 程序集——asmdef 引用不受版本宏控制，彻底解决需拆独立 asmdef（见 §五遗留项 #3）。

---

## 六点五、追加修复（编译/运行验证阶段）

### Fix-63｜TransitionManagerRoot.prefab 脚本 GUID 失配 + 单例引导自愈
- **文件**：`Runtime/PackageDefault/VNovelizerRes/VNPrefabs/UI/TransitionManagerRoot.prefab`、`Runtime/Scripts/VNovelizer/Core/UI/Transition/TransitionManager.cs`
- **问题**：prefab 上 `TransitionManager`/`DarkFadeTransitionEffect` 两个组件的 `m_Script` GUID 是 Assembly-CSharp 时代的旧值（`c837e396...`/`aebba0b8...`），与包内脚本 meta（`90a95fab...`/`20afc314...`）失配——运行时加载时组件被 Unity 剥离，`GetComponent` 返回 null → `DontDestroyOnLoad(null.gameObject)` NRE。该潜在缺陷此前从未被触发，Fix-23 在 `ReturnToMainMenu` 中新增的 `TransitionManager.Instance` 调用首次暴露了它。
- **修复**：
  1. **根因**：prefab 两个组件的 `m_Script` GUID 改为包内脚本真实 GUID，`m_EditorClassIdentifier` 同步改为 `VNovelizer.Runtime::` 前缀；
  2. **防御**：`Instance` 引导路径 `GetComponent` 失败时 `AddComponent<TransitionManager>` 自愈；`Awake` 中 effectMap 缺 DarkFade 时自动补挂（防止组件被剥离后所有转场报"未找到转场效果"）。

---

## 七、降级处理与遗留项

| # | 事项 | 降级方式 | 原因 |
|---|------|----------|------|
| 1 | **#16 命令参数解析统一引号感知分割** | 未实施 | 跨全部命令替换 `Split(',')` 属高风险重构，无测试基建保护，盲改可能引入解析回归；已记录为优化方向 |
| 2 | **#48 完整后台线程化**（AutoExcelConverter/RowPerfEditor/本地化 11MB 扫描） | 部分实施 | 已做批处理节流（Fix-48）；完整后台线程 + delayCall 回主线程的方案涉及 Editor 生命周期协调，建议在 Dev1 工程实测后再切 |
| 3 | **#60 asmdef 拆分**（Localization 独立程序集） | 未实施 | 拆 asmdef 影响程序集引用链与 GUID 依赖，属架构级变更；当前 package.json 已声明 `com.unity.localization` 为必装依赖，实际风险低 |
| 4 | MusicManager 构造器 `AddUpdateListener` 无移除路径 | 接受现状 | 全局单例进程级常驻，无实际泄漏；已记录 |
| 5 | `MeshActor.RunFade/RunMove` 提前 yield break 不置空协程句柄 | 未实施 | 现被 RemoveActor/Dispose 先调 Interrupt 掩盖，属脆弱设计但无现网触发路径；建议后续统一 `finally` 置空 |

---

## 八、验证清单（建议在 Dev1 工程执行）

1. **编译**：`D:\Unity\Unity项目\VNovelizer_Dev1` 打开 Unity，确认 Runtime/Editor 两程序集零编译错误（静态 lint 已全绿）。
2. **P0 存档**：开启 AES 加密 → 存/读档正常；删除 ProjectSettings 中 Key/IV → 保存应报错且**不落盘明文**；用旧版（无 IV 前缀）存档读档兼容；手工写坏 global_data.json → 游戏正常启动并生成 `.corrupted_*` 备份。
3. **P1 流程**：连续开局/读档多次后开启自动播放 → 打字完成只推进一行；读档进 choice 后选择 → 特效不叠加；Pause→SaveLoad→读档→开设置→关闭 → 状态回 Gameplay 可点击。
4. **P2 命令**：快进含 `charjump/playanim/playsfx/playvideo` 的行 → 不播动画/视频；跳过 `playvideo(op.mp4, loadscript(X))` → 不执行后续命令；跳过 `shake(screen,2)&wait(1)` → 震动立即停止；`bgtrans` 快进 → 背景即时切换且存档记录新背景。
5. **P2 UI**：按住 Skip 时打开暂停 → timeScale 恢复 1；快进中 ESC 回主菜单 → 无黑幕残留、可点击；History 回放语音后立即关面板 → 语音停止。
6. **P3 Editor**：保存行演出且 Excel 正打开该文件 → 弹确认框，取消则放弃写回且 CSV 无损；删除剧本 → Addressables 无残留条目；重命名输入 `a/b` → 弹非法字符提示；Setup Wizard 在含嵌套依赖的 manifest 上运行 → JSON 合法。
7. **P4 基础**：关闭 Domain Reload 重复进出 Play → 引擎协程正常（MonoManager 自愈）；资源管理器切换状态栏多次 → 音频播放状态回调不重复触发。

---

## 九、回滚指引

- 所有改动集中在本次会话，`git diff` 可按文件/【Fix-NN】标记分组检视；
- 加密格式变更（Fix-35）向后兼容：旧密文（无 IV 前缀）仍可解密，新密文含 `base64(IV):` 前缀；
- 存档文件新增 `.bak` 轮换备份（Fix-36），回滚后旧存档不受影响；
- 若需整体回退，`git checkout -- <file>` 按本文件第二~六节列出的文件清单逐项恢复即可。
