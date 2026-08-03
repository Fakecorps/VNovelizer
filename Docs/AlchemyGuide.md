# Alchemy — Inspector & Editor Enhancement Library

## 概述

**Alchemy** 是由 [Annulus Games](https://github.com/annulusgames) 开发的 Unity Inspector 与 EditorWindow 增强库（MIT 协议），版本 `2.1.1`。VNovelizer 以源码形式捆绑于 `Runtime/3rdParty/Alchemy/` 和 `Editor/3rdParty/Alchemy/`，为所有 Editor 工具提供现代化的 UI Toolkit 渲染能力。

### 核心价值

- **零成本美化**：通过 C# Attribute 声明式地美化 MonoBehaviour / ScriptableObject 的 Inspector 面板
- **EditorWindow 增强**：继承 `AlchemyEditorWindow` 即可让 EditorWindow 支持同样的属性系统
- **自动接管**：默认 fallback editor 接管所有 `MonoBehaviour` 和 `ScriptableObject` 的 Inspector 渲染
- **纯 Editor 层**：不增加运行时性能开销，所有绘制逻辑仅在 Editor 中生效

---

## 程序集结构

```
Runtime/3rdParty/Alchemy/
├── Alchemy.asmdef              → 程序集: Alchemy
├── Inspector/
│   ├── InspectorAttributes.cs  → 所有 Inspector 属性定义
│   ├── GroupAttributes.cs      → 分组属性 (Box, Tab, Foldout 等)
│   └── PropertyGroupAttribute.cs → 分组基类
├── Hierarchy/
│   ├── HierarchyHeader.cs      → Hierarchy 标题组件
│   ├── HierarchySeparator.cs   → Hierarchy 分隔线组件
│   ├── HierarchyObject.cs      → Hierarchy 对象基类
│   └── HierarchyObjectMode.cs  → 运行时行为枚举
└── Serialization/
    └── *.cs (6 files)           → 扩展序列化（需 ALCHEMY_SUPPORT_SERIALIZATION 宏）

Editor/3rdParty/Alchemy/
├── Alchemy.Editor.asmdef       → 程序集: Alchemy.Editor（Editor Only）
├── AlchemyEditor.cs            → MonoBehaviour/ScriptableObject 的 fallback Editor
├── AlchemyEditorWindow.cs      → EditorWindow 基类
├── AlchemySettings.cs          → 项目级设置（ProjectSettings 面板）
├── BuiltinAttributeDrawers.cs  → 所有 Attribute 的 UI Toolkit 绘制器
├── BuiltinGroupDrawers.cs      → 所有 Group 的 UI Toolkit 绘制器
├── Elements/ (12 .cs)          → 字段渲染元素（Button, List, Dictionary 等）
├── Hierarchy/ (10 .cs)         → Hierarchy 窗口装饰绘制器
└── Internal/ (12 .cs)          → 反射/序列化/UI 辅助工具
```

### 引用关系

```
VNovelizer.Editor
  └── Alchemy.Editor (Editor Only)
        └── Alchemy (Runtime, 仅属性定义)
```

---

## 功能分类

### 一、分组布局 (Groups)

将 Inspector 字段组织为结构化的视觉分组。同一组字段通过相同的 groupPath 关联。

| Attribute | 声明 | 视觉效果 |
|-----------|------|---------|
| `[Group]` | `[Group("基础设置")]` | 基本分组容器 |
| `[BoxGroup]` | `[BoxGroup("Group1")]` | 带边框方框分组，支持嵌套 `"Group1/Group2"` |
| `[TabGroup]` | `[TabGroup("Group1", "TabA")]` | Tab 页分组，第一个参数是组路径，第二个是标签名 |
| `[FoldoutGroup]` | `[FoldoutGroup("高级")]` | 可折叠/展开分组 |
| `[HorizontalGroup]` | `[HorizontalGroup("Row")]` | 水平排列同一组的字段 |
| `[InlineGroup]` | `[InlineGroup("path")]` | 内联分组 |

**使用示例**：

```csharp
public class CharacterEditor : MonoBehaviour
{
    [BoxGroup("基本信息")]
    public string characterName;

    [BoxGroup("基本信息")]
    public int age;

    [BoxGroup("基本信息/战斗属性")]
    public float attack;

    [BoxGroup("基本信息/战斗属性")]
    public float defense;

    [TabGroup("Tabs", "立绘")]
    public Sprite standingSprite;

    [TabGroup("Tabs", "头像")]
    public Sprite headSprite;
}
```

### 二、装饰属性 (Decorations)

| Attribute | 声明 | 效果 |
|-----------|------|------|
| `[Title]` | `[Title("标题", "副标题")]` | 加粗标题（副标题可选） |
| `[HelpBox]` | `[HelpBox("提示信息", HelpBoxMessageType.Warning)]` | 信息/警告/错误提示框 |
| `[LabelText]` | `[LabelText("自定义标签")]` | 自定义字段标签文字 |
| `[HideLabel]` | `[HideLabel]` | 隐藏字段标签 |
| `[HorizontalLine]` | `[HorizontalLine(0.5f, 0.5f, 0.5f)]` | 水平分割线（RGB 颜色） |
| `[Blockquote]` | `[Blockquote("引用文本")]` | 引用块文本 |
| `[Preview]` | `[Preview(64)]` | 资源预览图（尺寸/对齐可选） |

### 三、条件控制 (Conditionals)

支持通过字段名、属性名或方法名作为条件。方法名需以 `()` 结尾。

| Attribute | 效果 |
|-----------|------|
| `[ShowIf("condition")]` | 条件为 `true` 时显示 |
| `[HideIf("condition")]` | 条件为 `true` 时隐藏 |
| `[EnableIf("condition")]` | 条件为 `true` 时可编辑 |
| `[DisableIf("condition")]` | 条件为 `true` 时禁用 |
| `[HideInPlayMode]` | 运行模式下隐藏 |
| `[HideInEditMode]` | 编辑模式下隐藏 |
| `[DisableInPlayMode]` | 运行模式下禁用 |
| `[DisableInEditMode]` | 编辑模式下禁用 |

**使用示例**：

```csharp
public class ConditionalSample : MonoBehaviour
{
    public bool enableAdvanced;

    [ShowIf("enableAdvanced")]           // 引用同名字段
    public string advancedOption;

    [ShowIf("IsAdvancedEnabled")]        // 引用属性
    public int extraSetting;

    [EnableIf("HasValidData()")]         // 引用方法
    public float threshold;

    public bool IsAdvancedEnabled => enableAdvanced;
    public bool HasValidData() => threshold > 0;
}
```

### 四、交互功能 (Interaction)

| Attribute | 声明位置 | 效果 |
|-----------|---------|------|
| `[Button]` | 方法 | 在 Inspector 中渲染为可点击按钮，支持参数 |
| `[ShowInInspector]` | 字段/属性 | 将非序列化字段或属性显示在 Inspector |
| `[ReadOnly]` | 字段/属性/方法 | 只读显示，不可编辑 |
| `[Order]` | 字段/属性/方法 | 自定义排序值（默认按声明顺序） |
| `[Indent]` | 字段/属性/方法 | 增加缩进级别 |

**`[Button]` 示例**：

```csharp
public class ButtonSample : MonoBehaviour
{
    [Button]
    public void ResetData()
    {
        Debug.Log("Reset!");
    }

    [Button, LabelText("带参数按钮")]
    public void ApplyValue(float value)
    {
        Debug.Log("Value: " + value);
    }
}
```

### 五、验证 (Validation)

| Attribute | 效果 |
|-----------|------|
| `[Required("错误信息")]` | 字段不能为 null |
| `[ValidateInput("condition", "错误信息")]` | 输入值合法性校验 |

### 六、数据变更回调

| Attribute | 声明位置 | 触发时机 |
|-----------|---------|---------|
| `[OnValueChanged("MethodName")]` | 字段 | 字段值变更时调用指定方法 |
| `[OnInspectorEnable]` | 方法 | Inspector 激活时（类似 `OnEnable`） |
| `[OnInspectorDisable]` | 方法 | Inspector 停用时（类似 `OnDisable`） |
| `[OnInspectorDestroy]` | 方法 | Inspector 销毁时（类似 `OnDestroy`） |

### 七、List 增强

| Attribute | 效果 |
|-----------|------|
| `[ListViewSettings(Reorderable = true, ShowAddRemoveFooter = true, ...)]` | 自定义 ListView 外观 |
| `[OnListViewChanged(OnItemChanged = "MethodName", ...)]` | List 操作回调 |

**可配置项**：`ShowAddRemoveFooter`、`ShowAlternatingRowBackgrounds`、`ShowBorder`、`ShowBoundCollectionSize`、`ShowFoldoutHeader`、`SelectionType`、`Reorderable`、`ReorderMode`。

### 八、其他工具属性

| Attribute | 声明位置 | 效果 |
|-----------|---------|------|
| `[HideScriptField]` | Class | 隐藏 Inspector 顶部的 Script 字段 |
| `[DisableAlchemyEditor]` | Class/Field/Property | 禁止 Alchemy 接管该类型 |
| `[AssetsOnly]` | 字段 | 限制 ObjectField 只能选择项目资源（不能选场景对象） |
| `[InlineEditor]` | 字段 | 内联展开编辑子对象 |

---

## Hierarchy 窗口装饰

Alchemy 提供 Hierarchy 窗口的美化组件：

| 组件 | 添加方式 | 效果 |
|------|---------|------|
| `HierarchyHeader` | `GameObject → Alchemy → Hierarchy Header` | 在 Hierarchy 中显示为彩色标题行 |
| `HierarchySeparator` | `GameObject → Alchemy → Hierarchy Separator` | 在 Hierarchy 中显示为分隔线 |

运行时可配置行为（通过 `HierarchyObject.HierarchyObjectMode`）：
- **UseSettings**：使用 Project Settings 中的全局设置
- **None**：保留为普通 GameObject
- **RemoveInPlayMode**：进入 Play 模式时自动销毁
- **RemoveInBuild**：打包时自动移除

### 全局设置

**Project Settings → Alchemy** 面板可配置：
- 组件图标显示
- Tree Map 线条颜色/样式
- 行分隔线颜色
- 奇偶行交替着色

---

## EditorWindow 集成

### AlchemyEditorWindow

继承 `AlchemyEditorWindow` 替代 `EditorWindow` 即可在 EditorWindow 中使用所有 Alchemy 属性：

```csharp
using Alchemy.Editor;
using Alchemy.Inspector;

public class MyToolWindow : AlchemyEditorWindow
{
    [BoxGroup("配置")]
    public string configName;

    [ShowIf("showAdvanced")]
    public float advancedValue;
    private bool showAdvanced => advancedValue > 0;

    [Button]
    public void Execute() { ... }

    [MenuItem("VNovelizer/My Tool")]
    static void Open() => GetWindow<MyToolWindow>("我的工具");
}
```

**额外特性**：
- 数据自动持久化到 `ProjectSettings/{窗口类型全名}.json`
- 重写 `GetWindowDataPath()` 可自定义保存路径
- 重写 `SaveWindowData()` / `LoadWindowData()` 可自定义序列化逻辑

---

## 扩展序列化（可选功能）

> ⚠️ 此功能需要额外安装 `com.unity.serialization` 包。VNovelizer 默认不启用。

当 `com.unity.serialization` 安装后，`versionDefines` 自动设置 `ALCHEMY_SUPPORT_SERIALIZATION` 宏，启用以下能力：

| Attribute | 效果 |
|-----------|------|
| `[AlchemySerialize]` | 标记需要扩展序列化的 `partial class`/`struct` |
| `[AlchemySerializeField]` | 标记参与扩展序列化的字段（需配合 `[NonSerialized]`） |
| `[ShowAlchemySerializationData]` | 显示当前序列化数据 |

支持的额外类型：`HashSet<T>`、`Dictionary<K,V>`、`(T1, T2)` 元组、`T?` 可空类型、`UnityEngine.Object` 引用、`AnimationCurve`、`Gradient`。

> **注意**：SourceGenerator DLL（`Alchemy.SourceGenerator.dll`）已从捆绑中移除，因为其依赖 Roslyn 编译器 API 且 VNovelizer 未启用序列化功能。如需启用，需从 Alchemy 上游仓库重新获取该 DLL 并配置 Roslyn Analyzer 引用。

---

## 在 VNovelizer 中的使用规范

### 设计原则

1. **所有 Editor 工具优先使用 Alchemy 属性**，减少手写 IMGUI / 手动 UI Toolkit 布局。
2. **ScriptableObject 的 Inspector 自动获得 Alchemy 美化**（`AlchemyEditor` 的 fallback 机制），无需额外代码。
3. **EditorWindow 继承 `AlchemyEditorWindow`**，而非手动 `CreateGUI`。
4. 使用 `[DisableAlchemyEditor]` 仅在确有必要回退到默认 Inspector 时使用。

### 对 VNovelizer 现有 Editor 工具的建议改造

| 工具 | 改造方式 |
|------|---------|
| 剧本管理器 (ScriptManager) | 继承 `AlchemyEditorWindow`，用 `[TabGroup]` 分"生成"/"转换"标签 |
| 角色编辑器 (CharacterProfile) | 已自动接管，可按需添加 `[BoxGroup]`/`[Title]` 到 `CharacterProfile` 类 |
| 画廊编辑器 (GalleryEditor) | 继承 `AlchemyEditorWindow`，用条件控制 + `[ListViewSettings]` 优化列表 |
| 资源管理器 | 同 EditorWindow 模式，数据自动持久化 |
| UI预制体管理器 | 同上 |
| 本地化管理器 | 同上 |

### 兼容性说明

- **编译宏 `ALCHEMY_DISABLE_DEFAULT_EDITOR`**：定义此宏可禁止 Alchemy 的 fallback editor 接管所有 MonoBehaviour/ScriptableObject。
- **`[DisableAlchemyEditor]`**：在特定类型上禁用 Alchemy，回退到 Unity 默认 Inspector。
- Alchemy 最低 Unity 2021.2+，与 VNovelizer 的 Unity 2022.3+ 要求完全兼容。

---

## 文件清单

```
Runtime/3rdParty/Alchemy/Hierarchy/
  HierarchyHeader.cs, HierarchyObject.cs, HierarchyObjectMode.cs, HierarchySeparator.cs

Runtime/3rdParty/Alchemy/Inspector/
  GroupAttributes.cs, InspectorAttributes.cs, PropertyGroupAttribute.cs

Runtime/3rdParty/Alchemy/Serialization/
  AlchemyJsonAdapter.AnimationCurve+Keyframe.cs, AlchemyJsonAdapter.Gradient.cs,
  AlchemyJsonAdapter.UnityObject.cs, IAlchemySerializationCallbackReceiver.cs,
  SerializationAttributes.cs, SerializationHelper.cs

Editor/3rdParty/Alchemy/
  AlchemyAttributeDrawer.cs, AlchemyEditor.cs, AlchemyEditorUtility.cs,
  AlchemyEditorWindow.cs, AlchemyGroupDrawer.cs, AlchemySettings.cs,
  BuiltinAttributeDrawers.cs, BuiltinGroupDrawers.cs,
  CustomAttributeDrawerAttribute.cs, CustomGroupDrawerAttribute.cs,
  TrackSerializedObjectAttributeDrawer.cs

Editor/3rdParty/Alchemy/Elements/ (12 .cs)
  AlchemyPropertyField.cs, ClassField.cs, DictionaryField.cs, GenericField.cs,
  HashMapFieldBase.cs, HashSetField.cs, InlineEditorObjectField.cs, ListField.cs,
  MethodButton.cs, PropertyListView.cs, ReflectionField.cs, SerializeReferenceField.cs

Editor/3rdParty/Alchemy/Hierarchy/ (10 .cs + 4 .png in Textures/)
  HierarchyDrawer.cs, HierarchyDrawerInitializer.cs, HierarchyHeaderDrawer.cs,
  HierarchyObjectCreationMenu.cs, HierarchyObjectEditor.cs, HierarchyObjectProcessor.cs,
  HierarchyRowSeparatorDrawer.cs, HierarchySeparatorDrawer.cs, HierarchyToggleDrawer.cs,
  HierarchyTreeMapDrawer.cs

Editor/3rdParty/Alchemy/Internal/ (12 .cs)
  AssetHelper.cs, EditorColors.cs, EditorIcons.cs, GUIHelper.cs, InspectorHelper.cs,
  InternalAPIHelper.cs, MemberInfoExtensions.cs, RectHelper.cs, ReflectionHelper.cs,
  SerializedPropertyExtensions.cs, SerializeReferenceDropdown.cs, TypeHelper.cs
```

---

## 参考资料

- 上游仓库：[annulusgames/Alchemy](https://github.com/annulusgames/Alchemy)
- 协议：MIT (© Annulus Games)
- 集成方式：源码捆绑，参见 `Runtime/3rdParty/Alchemy/` 和 `Editor/3rdParty/Alchemy/`
- VNovelizer 的 Samples 工程中可导入 `Alchemy Samples` 查看全部属性的交互示例
