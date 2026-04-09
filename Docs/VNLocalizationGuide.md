# VNovelizer 剧情本地化指南（一剧本一表）

## 0. 目标与总开关
当 `VNProjectConfig.EnableLocalization = true` 时：
- 每个剧本使用自己的 `StringTableCollection`
- **每一行独立取词**：`speaker.{lineID}` / `text.{lineID}` 无 entry 或 value 为空时，不沿用上一句译文
- 此时若开启 `FallbackToCsvWhenMissing`，则显示**本行 CSV** 的 Speaker/Text；否则对应字段显示为空

当 `EnableLocalization = false` 时：行为保持旧工作流（完全等同 CSV）。

## 1. Collection 命名与 key 约定
Collection 命名：
- `CollectionName = ScriptTablePrefix + scriptName`
- 默认前缀 `ScriptTablePrefix = "VNScript_"`
- 例如剧本 `VN03` -> Collection `VNScript_VN03`

正文 key（剧本内局部 key）：
- `text.{lineID}`
- `speaker.{lineID}`

Choice key（建议）：
- `choice.rooftop`
- 运行时写法：`choice(@loc:choice.rooftop|jump(C_100))`

## 2. 如何填表（推荐流程）
1. `com.unity.localization` 已作为 VNovelizer 的 UPM 依赖声明；在 Package Manager 中确认已解析即可（通常无需再手动添加）
2. 打开 `VNProjectConfig`：
   - 勾选 `启用本地化`
   - 确认 `ScriptTablePrefix = VNScript_`
3. 在脚本管理器先把 Excel 转为 CSV
4. 打开菜单：`VNovelizer/Localization/剧情本地化管理器`
   - 输入 `scriptName`（不含扩展名）
   - 点击 `准备当前剧本 Collection`
   - 点击 `从 CSV 同步 Key`
5. 在该剧本 Collection 的各语言表内填写 value；**需要显示对白的每一行**建议在目标语言填非空 value，否则将依赖 `FallbackToCsvWhenMissing` 或显示为空

## 3. Choice 本地化
旧写法（兼容）：
```text
choice(去天台|jump(C_100))
```

推荐写法（一剧本一表）：
```text
choice(@loc:choice.rooftop|jump(C_100))
```

规则：
- 命中翻译：显示翻译 value
- 缺失翻译：显示可读 fallback（不会显示 `@loc:` 原样）

## 4. 运行
- `EnableLocalization=false`：按 CSV 原文显示
- `EnableLocalization=true`：按“当前剧本专属 Collection + 局部 key”读取

