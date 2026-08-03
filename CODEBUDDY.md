# CODEBUDDY.md This file provides guidance to CodeBuddy when working with code in this repository.

## Common Commands

This is a Unity UPM package without traditional CLI build/lint/test commands. All workflows operate through the Unity Editor.

- **One-time project setup**: In Unity, run **VNovelizer → 一键初始化 (Setup Wizard)**. This copies default resources from `Runtime/PackageDefault/VNovelizerRes/` to `Assets/Resources/VNovelizerRes/`, generates `VNProjectConfig`, and imports core UI prefabs and sample scenes.

- **Debug a script**: Open `Assets/Scenes/VNDebugScene`, enter a script name (e.g., `Chapter1`) and starting line ID in the Inspector, then press Play. The game launches directly into that line without going through the main menu.

- **Author scripts**: Use **VNovelizer → 剧本管理器** to create new Excel scripts. After editing, click "转换" to convert Excel to CSV, then use **VNovelizer → 资源管理器** to verify assets. Scripts are parsed from CSV at runtime via `ScriptParser.Parse()`.

- **Manage resources**: Use the visual editors under the VNovelizer menu — **角色编辑器** for `CharacterProfile` ScriptableObjects, **画廊编辑器** for gallery content, **UI预制体管理器** for UI prefabs. The **资源管理器** validates all resource references.

- **Localization workflow**: Enable `VNProjectConfig.EnableLocalization`, then use **VNovelizer → Localization → 剧情本地化管理器** to generate `StringTableCollection` per script and sync keys from CSV. The `VN_LOCALIZATION` scripting define is automatically set when Unity Localization is installed (via `versionDefines` in the `.asmdef`).

- **Assembly definitions**: The project has three assemblies. `VNovelizer.Runtime` (runtime core, references PrimeTween.Runtime + LitJson + Unity.InputSystem + TextMeshPro + Localization + Coffee.UIParticle). `VNovelizer.Editor` (editor-only tools, references Runtime + ExcelDataReader + Unity.Localization.Editor). `LitJson` (third-party JSON, no references).

- **Dev test project**: The dedicated Unity project for testing and adjusting the plugin is located at `D:\Unity\Unity项目\Vnovelizer_Dev`. It references VNovelizer as a local file dependency (`"com.fakecorps.vnovelizer": "file:D:/VNovelizer"` in its `Packages/manifest.json`). After modifying prefabs, fonts, or other resources in the Dev project, copy the modified files back to `Runtime/PackageDefault/` in the package to sync changes. When syncing prefabs, only copy the `.prefab` file itself, NOT the `.prefab.meta` (to preserve the package's own GUIDs).

## Architecture

### Package Structure

VNovelizer is a Unity UPM package (`com.fakecorps.vnovelizer`) structured as a **data-driven visual novel engine**. It separates authoring (Excel-based scripting) from runtime execution. Root-level `.cs` files (`MainMenuPanel.cs`, `PausePanel.cs`, `SettingsPanel.cs`, `PlayParticleCommand.cs`) are part of the `VNovelizer.Runtime` assembly and are placed at the package root for direct accessibility in the Unity Editor.

```
VNovelizer/
├── Runtime/Scripts/VNovelizer/     ← All runtime code
│   ├── Core/                       ← Engine core (Managers, Commands, Data, UI, API)
│   ├── ProjectBase/.../            ← Generic foundation (Singleton, EventCenter, ObjectPool, etc.)
│   └── Data Persistence/Json/      ← JSON serialization support
├── Runtime/PackageDefault/...Res/  ← Default resources copied to user's Assets on setup
├── Runtime/3rdParty/               ← LitJson + UIParticle (bundled)
├── Editor/                         ← Custom editors, importers, setup wizard, script manager
├── Docs/                           ← VNAPIReference.md + VNLocalizationGuide.md
└── package.json                    ← Unity 2022.3+, dependencies on TMPro, InputSystem, Localization
```

### Manager Singleton Pattern

All core managers inherit from `BaseManager<T>` (a pure C# lazy singleton — `GetInstance()` creates `new T()` on first access). They are **not** MonoBehaviours. They cannot run coroutines directly; instead, all async work is driven through `MonoManager.GetInstance().StartCoroutine()`. There is also `SingletonMono<T>` (requires scene pre-placement) and `SingletonAutoMono<T>` (auto-creates a GameObject) for managers that need MonoBehaviour lifecycle.

The six core managers:
- **`VNManager`** — The central orchestrator. Owns the script data (`List<StoryLine>`), current line index, and all visual/audio state snapshots. Drives `PlayCurrentLine()`, `FastForwardToLine()`, save/load, auto-play, and skip.
- **`GameStateManager`** — State machine with states: `Gameplay`, `AutoPlay`, `Choice`, `History`, `SaveLoad`, `Settings`, `Pause`, `System`. Supports a `PushState`/`PopState` stack for nested panels.
- **`GlobalDataManager`** — Persistent global data (volume, text speed, resolution, unlock flags, history log) serialized to JSON.
- **`SaveManager`** — 60 save slots with AES encryption, screenshot thumbnails, and JSON serialization.
- **`CharacterResManager`** — Loads all `CharacterProfile` ScriptableObjects from Resources and provides sprite lookups by character ID and emotion.
- **`VoiceManager`** — Creates a DontDestroyOnLoad AudioSource for voice playback.

### Data Pipeline: Excel → CSV → StoryLine → Runtime

```
Excel (.xlsx)  ──[ExcelDataReader + VNovelizer.Editor]──►  CSV
    │
    └── ScriptManager window: "新建" creates Excel, "转换" converts to CSV
                                                          │
                              Resources.Load<TextAsset>   │
                                                          ▼
                                              ScriptParser.Parse(fileName)
                                                          │
                                              Splits CSV with quote-aware state machine
                                              Maps 12 columns to StoryLine:
                                                ID | Speaker | HeadProfile | CharLeft/Mid/Right |
                                                Text | Background | BGM | Voice | Command | Note
                                                          │
                                              Returns ScriptData (List<StoryLine> + IDMap)
                                                          │
                                              VNManager.SetScriptData()
                                                          │
                                              VNManager.PlayCurrentLine()
```

### Inheritance Rules (Critical for Correctness)

The framework distinguishes between columns that **inherit** from the previous row and those that are **per-row explicit**:

- **Inherit (empty cell = keep current state)**: `Background`, `BGM`. If a row's BGM column is empty, the current BGM continues playing.
- **Explicit only (empty cell = none/hide)**: `Speaker`, `Text`, `HeadProfile`, `CharLeft`, `CharMid`, `CharRight`. An empty character slot always means "hide that slot" — it never auto-inherits the previous row's character. You must repeat the character expression on every line where they should appear.

Additionally, character-targeting commands (`charmove`, `setchartrans`, `charflip`) only work when the **same row's** corresponding character column (CharLeft/Mid/Right) is explicitly filled. If CharMid is empty on a row that also has `charmove(M, ...)`, the command will be ignored with a warning.

### Command System

Commands are the core extensibility mechanism. Each command inherits from the abstract `VNCommand` class:

```csharp
abstract string CommandName { get; }           // e.g., "bgfade", "shake"
abstract bool Execute(string args);            // Synchronous execution
virtual IEnumerator ExecuteAsync(string args); // Async (coroutine-based), default calls sync
virtual void Interrupt();                      // Skip/abort current animation
virtual void Simulate(string args);            // Update internal state only, no animation playback
```

**Registration**: `CommandManager.Init()` first hardcodes ~30 built-in commands, then uses **reflection** to scan all non-Unity/non-System assemblies for `VNCommand` subclasses and registers them by `CommandName` (case-insensitive).

**Parsing**: Commands in the Excel `Command` column use the format `cmd(args)` and are separated by `&`. Example: `bgfade(Beach,1.5)&charfadein(L,Amy_Normal,1)&wait(0.5)`.

**Execution flow**: `ExecuteCommandsAsync` iterates commands sequentially, but `CharFadeIn`/`CharFadeOut` commands are **batched and executed in parallel** (so left/mid/right characters fade in simultaneously). A reference counter tracks running async commands; the system waits for all to complete before advancing to the next line.

**The Simulate/Execute distinction** is fundamental: `FastForwardToLine` (used for save-load, jump-to-line, and game start) calls `Simulate()` on each command to build the correct internal state (background, BGM, character dictionary, flags) without playing animations or audio. Then `PlayCurrentLine()` calls `Execute()`/`ExecuteAsync()` on the target line to actually animate.

### Game Flow (VNManager Orchestration)

```
VNManager.StartGame(scriptName, lineID)
    │
    ├── If not in VNGamePlay scene → SceneManager.LoadScene("VNGamePlay")
    │   └── OnSceneLoaded → RunGameLogic()
    │
    ├── RunGameLogic()
    │   ├── LoadingProgressPanel displays progress (script 40% + UI 60%)
    │   ├── ExecuteCommand("loadscript(scriptName)")  → ScriptParser.Parse() → SetScriptData()
    │   ├── FastForwardToLine(targetIndex)            → Simulate all prior lines
    │   ├── Sync visual/audio state to UI             → EventCenter triggers
    │   └── PlayCurrentLine()                         → First visible frame
    │
    └── Per-line loop (NextLine)
        ├── ApplyInheritance(currentLine)   ← Fill empty BG/BGM/Voice from state
        ├── UpdateVisualState()             ← Background + characters via EventCenter
        ├── UpdateAudioState()              ← BGM via MusicManager, voice via VoiceManager
        ├── UpdateDialogue()                ← Speaker name + text (with optional localization)
        └── ExecuteActionsAndContinue()     ← CommandManager.ExecuteCommandsAsync()
```

### Save/Load

`SaveGame(slotIndex)` constructs a `SaveData` object containing: current script name, line ID, background, BGM, three-slot character dictionary (with scale), all flags, active effects list, history log, and a screenshot path. This is serialized to JSON (optionally AES-encrypted) and written to `Application.persistentDataPath/SaveData/`.

On load, `ContinueGame(SaveData)` restores the scene, then `FastForwardToLine` rebuilds the full state. A critical detail: **on the first frame after loading**, if the current CSV row has empty character columns but the save data has characters in those slots, the save data's characters are displayed temporarily to avoid visual flicker. The next line strictly follows CSV rules again.

### Character System

Characters are defined via `CharacterProfile` ScriptableObjects (Create → VNovelizer → CharacterProfile). Each has a `CharacterID` (matches the Excel Speaker column), `ElementSprites` (emotion → full-body sprite map), `HeadSprites` (emotion → head icon map), and optional `SpeakerBox`/`HeadFrame` UI images.

At runtime, `CharacterResManager` loads all profiles from `Resources/` and provides lookup: `GetCharacterSprite("Amy", "Smile")`. The Excel format `Amy_Normal` in CharLeft/Mid/Right columns is split into character ID and emotion at parse time.

### UI Panel System

The UI is modular with panels managed by a generic `UIManager` (in ProjectBase). Key panels:

| Panel | Purpose |
|-------|---------|
| `VNGameplayPanel` (56KB, the largest file) | Main game view: background layers, character slots (L/M/R), dialogue box with speaker name + text, effect layer |
| `MainMenuPanel` / `PausePanel` / `SettingsPanel` | System menus at package root level |
| `SaveLoadPanel` + `SaveSlot` | Save/load with screenshot thumbnails (60 slots) |
| `HistoryPanel` | Scrollable dialogue history with voice replay |
| `GalleryPanel` | CG gallery, music gallery, scene recollection |
| `ChoicePanel` | Branching choice buttons |
| `ConfirmPanel` | Confirmation dialogs |
| `LoadingProgressPanel` | Async loading with progress bar |
| `TransitionManager` | Scene transition effects (default: dark fade) |

### VNAPI (Public API Layer)

`VNAPI` (namespace `VNovelizer.Core.API`) is a **static facade** that external scripts and custom commands should use instead of directly accessing internal managers. It provides:

- **UI access**: `GetBG_F/B()`, `GetCharRect/Image(posCode)`, `GetDialogueText()`, `SetSpeaker()`, `GetEffectLayer()`
- **Text control**: `SetDialogueTextColor/Size()`, `CompleteDialogueTyping()`, `IsDialogueTyping()`
- **Flags**: `SetBoolFlag/GetBoolFlag`, `SetIntFlag/GetIntFlag`, `SetStringFlag/GetStringFlag`
- **Effects**: `RegisterEffect/UnregisterEffect()`, `ClearAllEffects()`, `PlayVideo()`, `ExecuteCommand()`
- **Flow**: `NextLine()`, `GetCurrentScriptName()`, `GetCurrentLineIndex()`, `CanInteractGameplay()`
- **Localization**: `TryGetLocalizedText/Speaker()` (proxied to `VNLocalizationService`)
- **Coroutines**: `StartCoroutine`/`StopCoroutine` (proxied to `MonoManager`)

Most methods gracefully handle missing `VNGameplayPanel` (return null / silent no-op). Use `VNAPI.TryGetGameplayPanel(out panel)` or `VNAPI.HasGameplayPanel()` for null-checking.

### Localization Architecture

Localization is optional and toggled via `VNProjectConfig.EnableLocalization`. When enabled:

- Each script gets its own `StringTableCollection` named `VNScript_{scriptName}` (e.g., `VNScript_VN03`).
- Keys use the pattern `text.{lineID}` and `speaker.{lineID}` — each row is independently resolved (no cross-row inheritance for translations).
- Choice localization uses `choice(@loc:choice.key|jump(...))` syntax.
- If a translation is missing and `FallbackToCsvWhenMissing` is true, the CSV text is used as a fallback.
- The `VN_LOCALIZATION` scripting define is automatically set via `versionDefines` in the `.asmdef` when `com.unity.localization` is installed (any version).

### Key Third-Party Dependencies

- **PrimeTween** (external, must be installed separately from Asset Store): High-performance 0-GC animation library used for all UI animations. The `VNovelizer.Runtime` assembly references `PrimeTween.Runtime`.
- **ExcelDataReader** (bundled in `Editor/Plugins/`): Reads `.xlsx`/`.xls` files for the Excel → CSV conversion pipeline (editor only).
- **LitJson** (bundled in `Runtime/3rdParty/`): JSON serialization used for save data and configuration.
- **Coffee.UIParticle** (bundled): UI particle effects.
