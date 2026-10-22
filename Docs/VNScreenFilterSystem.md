# VNovelizer 画面滤镜系统（ScreenFilterSystem）

> 设计定稿：2026-10-21。方案拍板记录：作用域=只滤剧场画面（UI 豁免）；调用=逐滤镜命令 `filter_xxx`（7 款内置，参数可独立调节）+ `unsetfilter` + 自定义 `VNFilterDefinition` 预设资产；首期内置 7 款；叠加=栈式（ping-pong Blit 链）。设计动因与选型对比见本文 §1。

## 1. 设计动机与选型

VNovelizer 画面 = 世界空间 3D 舞台（背景/立绘 `MeshActor`、L2D 网格、粒子，由唯一正交相机 `VN_TheaterCamera` 渲染）+ 纯 `ScreenSpaceOverlay` UI 双层。给游戏画面加鱼眼/VHS/反色等滤镜，候选方案与结论：

| 方案 | 结论 |
|---|---|
| `OnRenderImage` 相机后处理 | ❌ URP 不回调（Dev 工程即 URP），违背包内"零 URP 代码"铁律 |
| URP `ScriptableRendererFeature` | ❌ 需写 URP API + 侵入用户 Renderer 资产，与包内 shader 惯例冲突 |
| Unity PostProcessing / URP Volume | ❌ 增加管线依赖；自定义效果（鱼眼/VHS）仍需手写 shader |
| **相机 targetTexture 捕获 + 全屏 RawImage 滤镜层** ✅ | Built-in / URP 行为一致的引擎通用 API；滤镜材质可自由叠加；UI 天然豁免 |

最终架构（三层）：

1. **数据层** `VNFilterDefinition`（ScriptableObject 预设：FilterId + Shader + 参数默认值 + 默认时长）+ `VNFilterLibrary`（内置注册表 + 用户资产加载 + shader 双兜底解析）。
2. **运行时** `ScreenFilterManager`（MonoBehaviour 单例）：RT 捕获 → 每帧 ping-pong Blit 链（栈式叠加、权重渐变）→ 最终 RT 交给 `VN_EffectCanvas`（sortingOrder=5）下的全屏 RawImage。该层位于剧场画面之上、Gameplay 对话框(10)之下，因此**滤镜只作用于剧场画面，对话框等 UI 保持清晰**（与官方 camerafx 规划的作用域一致；`VNTheaterRefactoring.md` §4.5 已说明"转场只作用于剧场层"是同一哲学）。
3. **命令层** `filter_vhs` / `filter_fisheye` / `filter_invert` / `filter_grayscale` / `filter_vignette` / `filter_pixelate` / `filter_chromatic`（7 款内置逐滤镜命令，`VFXCommands/`）+ `unsetfilter`（关闭滤镜，同属「视效命令」分类；反射自动注册 + `[VNParam]` 编辑器元数据）。

## 2. 命令

### 2.1 逐滤镜命令（filter_xxx，2026-10-21 拆分）

每个内置滤镜一个命令，命令参数 = 该滤镜可调节的 shader 数值 + 可选 `duration`。参数缺省时用预设默认值补齐；重复开启同一滤镜 = 淡入到满权重并刷新参数（幂等）。图编辑器归类「视效命令」（VFX，青绿色带）。

| 命令 | 语法 | 可调参数（预设默认值） |
|---|---|---|
| `filter_vhs` | `filter_vhs([intensity[, duration]])` | `intensity` 故障强度 0~1（0.6）：行带抖动 + 色差 + 扫描线 + 胶片颗粒 |
| `filter_fisheye` | `filter_fisheye([strength[, duration]])` | `strength` 桶形畸变强度 0~2（0.6）：r' = r(1+k·r²) |
| `filter_invert` | `filter_invert([amount[, duration]])` | `amount` 反色量 0~1（1 = 完全反色） |
| `filter_grayscale` | `filter_grayscale([amount[, duration]])` | `amount` 去色量 0~1（1 = 完全黑白，Rec.709 加权） |
| `filter_vignette` | `filter_vignette([strength[, radius[, softness[, duration]]]])` | `strength` 0~1（0.8）/ `radius` 起点半径 0~1.5（0.85）/ `softness` 过渡软度 0.01~1（0.4）；颜色恒用预设默认（黑） |
| `filter_pixelate` | `filter_pixelate([pixelCount[, duration]])` | `pixelCount` 横向分块数 2~512（96，越小越马赛克，分辨率无关） |
| `filter_chromatic` | `filter_chromatic([amount[, duration]])` | `amount` RGB 径向色散量 0~0.05（0.012，中心无色差边缘最强） |

- 中间参数可跳空（`filter_vignette(,,0.5,2)` = 仅覆盖 softness 与 duration），空段自动取预设默认。
- `duration` = 渐变秒数，缺省用预设默认时长（0.5），0 = 立即生效。

### 2.2 unsetfilter

| 命令 | 语法 | 语义 |
|---|---|---|
| `unsetfilter` | `unsetfilter(name[, duration])` | 关闭滤镜：权重 1→0 淡出后移除条目并销毁材质；`name=all` 关闭全部；未激活时幂等静默。`name` 在图编辑器为纯下拉选择（候选 = 内置 + 用户预设 + `all`），归类「视效命令」 |

> **变更记录 2026-10-21**：通用 `setfilter` 命令已移除（拆分动机见 §2.1 逐滤镜命令的引入）。
> 旧剧本中的 `setfilter(name, duration)` 请替换为对应 `filter_xxx(...)`；已创建的自定义
> `VNFilterDefinition` 预设资产仍被 `VNFilterLibrary` 扫描（`unsetfilter` 下拉可关闭），
> 其开启方式未来再补。

- `name` 大小写不敏感。内置：`fisheye` `vhs` `invert` `grayscale` `vignette` `pixelate` `chromatic`；自定义预设用其 `FilterId`。
- **命令语义**（与 charscale/bgtrans 同构）：`Execute`（同步/快进语境）= 立即到终态；`Simulate`（快进/读档重建）= 立即到终态，保证重建画面与真实播放一致；`Interrupt`（点击跳过）= 瞬间终态；`ExecuteAsync` 等待渐变时长后返回（保持 `&`/`->` 时序）。
- 生命周期：滤镜是**持久视觉状态**，持续到 `unsetfilter`/`all`/剧场清场（`TheaterManager.ClearTheater` → `ScreenFilterManager.ResetAllFilters()`：回到主菜单/结束演出即复位）。**不入存档**（瞬态演出状态，与 shake 同级；读档经 Simulate 重建）。

## 3. 内置滤镜

| FilterId | 名称 | 参数（预设默认值） | 说明 |
|---|---|---|---|
| `fisheye` | 鱼眼 | `_Strength` 0.6（0..2） | 桶形畸变 r' = r(1+k·r²)，圆形对称 |
| `vhs` | VHS 模拟 | `_Intensity` 0.6（0..1） | 行带抖动 + 色差 + 扫描线 + 胶片颗粒 |
| `invert` | 反色 | `_Amount` 1（0..1） | 1 = 完全反色 |
| `grayscale` | 黑白 | `_Amount` 1（0..1） | Rec.709 亮度加权去色 |
| `vignette` | 暗角 | `_Strength` 0.8 / `_Radius` 0.85 / `_Softness` 0.4 / `_Color` 黑 | 圆形暗角（`_Aspect` 校正） |
| `pixelate` | 像素化 | `_PixelCount` 96（2..512） | 横向分块数，分辨率无关 |
| `chromatic` | 色差 | `_Amount` 0.012（0..0.05） | R/B 径向色散，中心无色差 |

### 3.1 天气粒子（particle_snow / particle_rain）

> **变更记录 2026-10-22**：雪/雨从天气滤镜改回**代码生成的粒子系统**（`WeatherParticleFactory` + `WeatherParticleManager`：运行时纯代码构建 ParticleSystem + UIParticle，零美术资源）。`filter_snow`/`filter_rain` 命令、`VNFilterSnow/Rain.shader` 与 `WeatherFilterMap` 兼容映射已全部删除——旧剧本中的 `filter_snow/filter_rain` 会报"未找到命令"、`playparticle(SnowEffect/RainEffect)` 会报找不到资源，请改用 `particle_snow`/`particle_rain`。

- 命令入口：`particle_snow([intensity[, duration]])` / `particle_rain([intensity[, duration]])`（intensity = 雪/雨量 0~1 缺省 0.6，映射到粒子发射率与亮度；duration = 渐变秒数缺省 0.5，0 = 立即）；关闭用 `unsetparticle(snow|rain)` 或 `unsetparticle(all)`。重复开启幂等（渐变到目标强度并刷新）。
- 视觉层级：天气粒子挂在 **VN_EffectCanvas 的 EffectLayer**（sortingOrder=5）——剧场画面之上、滤镜层之上（VHS 等画面滤镜不会扭曲天气）、对话框之下，即"在场景里"，对话与 UI 粒子保持清晰。
- 实现：`WeatherParticleFactory` 纯代码构建——雪 = 软圆点慢速飘落（80~180 px/s）+ 位置噪声风摆 + 出生/消亡淡入淡出；雨 = 长条雨丝（3×70~110px）高速下落（950~1500 px/s）+ ≈10° 风斜 ±3° 抖动；Box 发射区覆盖画布 + 四周边距；贴图 = 引擎内置 Default-Particle 软圆点，无任何美术资产依赖。`WeatherParticleManager` 懒单例：强度渐变 = lerp 发射率与亮度（unscaledTime），衰减到 0 后销毁条目，随剧场清场（ClearTheater → ResetAll）复位。
- 语义（与 filter 命令族同构）：Execute/Simulate/Interrupt = 立即终态；ExecuteAsync 等待渐变（保持 `&` 时序）；不入存档（快进/读档经 Simulate 重建终态）。

内置定义是**代码注册**（`VNFilterLibrary.BuiltinFilters`，运行时 ScriptableObject 实例，非打包资产）——任何资源模式（Addressables / Resources 存量项目）都可用，无需重新跑初始化向导。用户自定义预设（SO 资产）同名覆盖内置。

## 4. 运行时架构（ScreenFilterManager）

```
剧场相机 --targetTexture--> _cameraRT（屏幕尺寸）
   每帧 WaitForEndOfFrame：
     权重推进（MoveTowards，speed = 1/duration）
     → 移除权重到 0 的条目（销毁材质；栈空 → 恢复相机直接渲染 + 隐藏滤镜层）
     → ping-pong Blit 链：src=_cameraRT，逐个 weight>0 的滤镜
       Graphics.Blit(src, dst, material{_Weight=weight, _Aspect=aspect})，src/dst 交替
     → _filterImage.texture = 最终 RT
VN_EffectCanvas（sortingOrder=5）
   ├─ VNFilterLayer（全屏 RawImage，raycastTarget=false）← 滤镜结果（siblingIndex=0，特效层之下）
   └─ EffectLayer（UI 粒子等，保持清晰不受滤镜影响）
```

- 单例：`VN_ScreenFilter` GameObject（DontDestroyOnLoad，懒创建 `EnsureInstance()`），场景无关。
- RT 随 `Screen.width/height` 变化重建；`RenderTextureFormat.Default`（sRGB，与 backbuffer 视觉一致）。
- 已知 1 帧延迟（捕获帧的滤镜结果下一帧显示）：视觉小说场景无感知影响。
- URP 说明：`targetTexture` 是引擎通用 API，URP 相机渲染到 RT 行为一致，零 URP 代码；若用户开了 URP 后处理（Bloom 等）会在入 RT 前正常生效，与滤镜叠加。

## 5. 滤镜 Shader 契约（扩展必读）

新增滤镜 shader 必须满足：

1. **属性契约**：
   - `_MainTex`（输入纹理，Graphics.Blit 传入）+ 声明 `float4 _MainTex_ST` 并 `TRANSFORM_TEX`（平台垂直翻转校正，照抄 BGTransition）；
   - `_Weight`（Range 0..1）= 全局混合权重。**所有效果必须 `lerp(原图, 效果, 参数 × _Weight)`**——渐变/中断/叠加全靠它；
   - `_Aspect` = 屏幕宽高比（管理器每帧写入；圆形效果用 `uv-0.5` 后 `x *= _Aspect` 校正）。
2. **写法**：唯一 SubShader、Built-in 兼容 CGPROGRAM、**无 `RenderPipeline` tag**（URP 走官方兼容路径；加 URP tag 不 include Core.hlsl 会编译失败——BGTransition 实测教训）；`Cull Off / ZWrite Off / ZTest Always`。
3. **存放**：`Runtime/Resources/FilterShaders/`（包内 Resources 保证进构建）；命名 `VNFilter{名称}.shader` / Shader 名 `VNovelizer/Filters/{名称}`。
4. **加载**：内置走 `Shader.Find("VNovelizer/Filters/...")` → `Resources.Load<Shader>("FilterShaders/...")` 双兜底；用户预设资产直接引用 Shader 字段。

## 6. 如何新增滤镜

**方式 A：用户自定义预设（推荐，无需写代码到包内）**

1. Assets → Create → VNovelizer → 画面滤镜预设（或在 `Assets/VNovelizer/VFX/Filters/` 下创建，类别 `VNovelizerRes/VFX/Filters` 自动注册 Addressables）；
2. 填 `FilterId`（如 `dream`）、拖入 shader（自定义 shader 放用户工程任意位置，遵守 §5 契约）、配置 `DefaultParams` 默认值、`DefaultDuration`；
3. 预设资产会被 `VNFilterLibrary` 自动发现（`unsetfilter` 下拉可选）；开启方式待后续扩展。

**方式 B：内置注册（包开发者）**：`VNFilterLibrary.BuiltinFilters` 加一条（ID/显示名/shader 名/兜底路径/默认参数），shader 放 `Runtime/Resources/FilterShaders/`。

## 7. 编辑器集成

- **视效命令分类**：7 款 `filter_xxx` 命令标注 `VNCommandCategory.VFX`，行演出编辑器左侧命令面板单独分组「视效命令」（青绿色），节点标题栏同色带。
- **结构化参数表单**：`filter_xxx` 的可调参数声明为 `Float`/`Int` + Min/Max → Inspector 渲染滑块（带输入框），duration 为可选滑块；`filter_pixelate` 为整数滑块（2~512）。
- `VNParamType.FilterName`（`unsetfilter` 的 name 参数）渲染为**纯下拉选择**（不自由输入）——候选 = `VNFilterLibrary.GetAllIds()`（内置 + 用户预设）；`unsetfilter` 额外在最前插入 `all`（关闭全部）。
- `VNProjectConfig.FilterResPath`（引擎内部常量，默认 `VNovelizerRes/VFX/Filters`）；`VNAddressablesRegistrar` 已知类别已含该前缀（用户工作区预设资产自动注册，`VNFilterLibrary` 经 `VNResourceService.LoadAll` 发现）。

## 8. 已知边界

- 只滤剧场画面：对话框/按钮/UI 粒子特效（VN_EffectCanvas 的 EffectLayer 在滤镜层之上）保持清晰。
- 1 帧显示延迟；VHS 噪点/抖动为时间量化动画（块状跳变感是设计效果）。
- 滤镜不入存档；`loadscript` 换剧本不清滤镜（视觉状态延续，作者显式 `unsetfilter` 控制）。
- 快进（skip）路径经 `Execute` 立即到终态，滤镜强度与逐行播放一致。

## 9. 验证清单（Dev1 工程实测）

1. `filter_fisheye` → 画面鱼眼、对话框清晰；`unsetfilter(fisheye)` 恢复。
2. 参数调节：`filter_vhs(0.2)` 故障强度明显弱于 `filter_vhs(1)`；`filter_pixelate(16)` 马赛克更粗。
3. 叠加：`filter_vhs&filter_invert` 同时生效；`unsetfilter(all)` 全清。
4. 渐变：`filter_grayscale(1, 2)` 2 秒淡入；点击跳过 → 立即满强度。
5. 幂等：同一行 `filter_vhs(0.3, 0.5)&filter_vhs(0.9, 0.5)` 最终强度 0.9。
6. 快进一致性：滤镜开启状态下行跳转/读档，画面与逐行播放一致（含参数）；回到主菜单滤镜清空。
7. 图编辑器：命令面板出现「视效命令」分组（7 个 filter_xxx + unsetfilter + particle_snow/particle_rain/unsetparticle）；节点滑块可改参数；unsetfilter 的 name 与 unsetparticle 的 name 均为纯下拉（分别含 all）。
8. URP 工程（Dev1）验证 shader 无品红、Built-in 工程验证等效。
9. 屏幕分辨率切换（窗口拖拽）后滤镜不糊、比例正确。
