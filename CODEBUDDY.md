# CODEBUDDY.md This file provides guidance to CodeBuddy when working with code in this repository.

## Common Commands

This is a Unity UPM package without traditional CLI build/lint/test commands. All workflows operate through the Unity Editor.

- **One-time project setup**: In Unity, run **VNovelizer → 一键初始化 (Setup Wizard)**. This registers package default resources from `Runtime/PackageDefault/VNovelizerRes/` into the Addressables "VNovelizer" group (zero file copy, GUID-based) and configures dependencies (PrimeTween, TMP, Input System). No files are written to `Assets/`. The `VNProjectConfig` asset is auto-created on first opening **Edit → Project Settings → VNovelizer**; gallery data containers are created on first opening the gallery editor.

- **Debug a script**: The engine is scene-independent. Either (a) attach the `VNRuntimeInitializer` component to any GameObject in any scene, enter a script name (e.g., `Chapter1`) and optional line ID in the Inspector, then press Play; or (b) click the "试玩" (Quick Play) button in the script manager — it enters Play mode in the current scene and auto-starts the script via a PlayerPrefs flag consumed by `VNRuntimeInitializer.AutoPlayOnPlayMode`.

- **Start the game from code**: `VNManager.StartGame(scriptName, lineID)` runs in the current scene without scene switching (engine roots — theater camera, EventSystem, AudioListener, BGM audio source, transition root — are all auto-created on demand and persist across scenes). "Return to main menu" shows the `MainMenuPanel` instead of loading a scene.

- **Author scripts**: Use **VNovelizer → 剧本管理器** to create new Excel scripts. After editing, click "转换" to convert Excel to CSV, then use **VNovelizer → 资源管理器** to verify assets. Scripts are parsed from CSV at runtime via `ScriptParser.Parse()`.

- **Manage resources**: Use the visual editors under the VNovelizer menu — **角色编辑器** for `CharacterProfile` ScriptableObjects, **画廊编辑器** for gallery content. UI prefab overrides are managed in **Edit → Project Settings → VNovelizer → UI 模板覆写** (via `VNUIPrefabTemplateCreator`, "从模板创建自定义 UI…" copies a package default prefab and assigns the override field). The **资源管理器** validates all resource references.

- **Localization workflow**: Enable `VNProjectConfig.EnableLocalization`, then use **VNovelizer → Localization → 剧情本地化管理器** to generate `StringTableCollection` per script and sync keys from CSV. The `VN_LOCALIZATION` scripting define is automatically set when Unity Localization is installed (via `versionDefines` in the `.asmdef`).

- **Assembly definitions**: The project has five assemblies. `VNovelizer.Runtime` (runtime core, references PrimeTween.Runtime + LitJson + Unity.InputSystem + TextMeshPro + Localization + Coffee.UIParticle + Alchemy). `VNovelizer.Editor` (editor-only tools, references Runtime + ExcelDataReader + Unity.Localization.Editor + Alchemy.Editor). `LitJson` (third-party JSON, no references). `Alchemy` (bundled Inspector attribute definitions, references Unity built-in modules). `Alchemy.Editor` (bundled Inspector/Hierarchy/EditorWindow drawing, editor-only, references Alchemy).

- **Dev test project**: The dedicated Unity project for testing and adjusting the plugin is located at `D:\Unity\Unity项目\VNovelizer_Dev1` (NOT `Vnovelizer_Dev` — that's an older/separate project). It references VNovelizer as a local file dependency (`"com.fakecorps.vnovelizer": "file:D:/VNovelizer"` in its `Packages/manifest.json`). Live2D Cubism SDK (5-r.4.2) is imported directly into `Assets/Live2D/Cubism/` (not via Package Manager); the test model is the SDK's bundled `Samples/Models/Mao` (the old `Assets/miku/` model is deprecated). After modifying prefabs, fonts, or other resources in the Dev1 project, copy the modified files back to `Runtime/PackageDefault/` in the package to sync changes. When syncing prefabs, only copy the `.prefab` file itself, NOT the `.prefab.meta` (to preserve the package's own GUIDs).

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
├── Runtime/3rdParty/               ← LitJson + UIParticle + Alchemy (bundled)
├── Editor/                         ← Custom editors, importers, setup wizard, script manager
│   └── 3rdParty/Alchemy/           ← Alchemy.Editor (Inspector/Hierarchy drawing)
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
abstract string CommandName { get; }           // e.g., "bgtrans", "shake"
abstract bool Execute(string args);            // Synchronous execution
virtual IEnumerator ExecuteAsync(string args); // Async (coroutine-based), default calls sync
virtual void Interrupt();                      // Skip/abort current animation
virtual void Simulate(string args);            // Update internal state only, no animation playback
```

**Registration**: `CommandManager.Init()` first hardcodes ~30 built-in commands, then uses **reflection** to scan all non-Unity/non-System assemblies for `VNCommand` subclasses and registers them by `CommandName` (case-insensitive).

**Parsing**: Commands in the Excel `Command` column use the format `cmd(args)` and are separated by `&`. Example: `bgtrans(Beach,fade,1.5)&charfadein(L,Amy_Normal,1)&wait(0.5)`.

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
- **Alchemy** (bundled in `Runtime/3rdParty/Alchemy/` and `Editor/3rdParty/Alchemy/`, MIT licensed, originally by Annulus Games): Inspector and EditorWindow enhancement library providing rich C# attributes for grouping, conditional display, buttons, validation, and Hierarchy decoration. The `Alchemy` assembly (runtime) contains all attribute definitions; the `Alchemy.Editor` assembly (editor-only) handles UI Toolkit rendering. The optional serialization module (HashSet/Dictionary/Tuple support via SourceGenerator) is gated behind the `ALCHEMY_SUPPORT_SERIALIZATION` define, which auto-activates when `com.unity.serialization` is installed.

## Row Perf Editor — NextLine Template (R14 fixes, 2026-09-14)

The command-line editor (`Editor/RowPerformanceEditor/RowGraphView.cs`) synthesizes a **template nextline node** (`__template_nextline__`, view-only, NOT in `ChainGraph`) in the confirm lane whenever the row has no explicit `nextline()` command and the main-chain tail is not flow-only. Fixes applied in R14:

- **Fix-R14-A (chain-tail detection)**: `MainChainTailHasNonFlowCommand` no longer relies solely on `FindSinks()` — with FORK/JOIN merges the only sink is the End sentinel, so all tail commands were missed and the function wrongly returned false (template never created AND user-written `nextline` hidden via `display:None`). Now it walks main-chain nodes whose successors are all the End sentinel (JOIN / Fork / plain commands / dangling tails). Signature now takes `bool isConfirm`.
- **Fix-R14-B (position)**: Template position is no longer hardcoded `(startX+170, centerY-30)`. It is computed from the same source as `ChainAutoLayout.Layout`: one column left of the End sentinel + horizontal centering in the column (column stride = widest node + HorizontalGap), Y = `centerY - NodeHeight/2`. `savedPositions` (key `confirm/__template_nextline__`, persisted via `CollectPositions` → `GraphPosStore`) is restored when present — user drags of the template node now survive rebuilds (R13 persistence extended to the template).
- **Fix-R14-D / D2 (selection & delete interception)**: Selecting the template node no longer fires `OnNodeSelected` (Inspector must not treat it as a real command). When the template is deleted (promotion flow), its associated view edges are also removed from `elementsToRemove` — previously only the node was intercepted, so Unity deleted the edges and left the template orphaned when the user cancelled promotion.
- **Fix-R14-E (post-structural re-evaluation)**: New `RefreshNextLineTemplate` runs after every structural graph change — if the user deletes all confirm commands (including a real `nextline`), the template is re-created immediately; if the graph gains a real `nextline` or the tail becomes flow-only, the template is removed. `RemoveDirectSentinelBridge` deletes the `Start→End` bridge edge left by `BridgeRemovedNodes` (both view edge and graph data) which previously drew a "twisted line that skips the template". `BuildLane` also calls it because Undo snapshot restore bypasses `OnGraphViewChanged`.
- **Nested-suppression discipline**: `RemoveTemplateView` / `RemoveDirectSentinelBridge` / template re-creation now save & restore `_suppressChangeEvents` instead of blindly setting it false — they are called from both `BuildLane` (inside `Rebuild`'s suppression window) and `OnGraphViewChanged`.

`MainChainTailHasNonFlowCommand` semantics: returns true (show nextline) when the graph has no business commands, or any main-chain tail node (successors all → End sentinel) is NOT a flow command (`jump/jumpif/jumpifnot/loadscript/loadscriptif/loadscriptifnot/choice/loadscene`); choice nodes without a Main continuation end the main chain naturally (treated as flow termination). Fork/Join tails have null `CommandName` → `IsFlowCommand` false → nextline shown, correct.

## Live2D Fixes (Fix-L2D, 2026-09-14)

SDK behavior facts verified against Cubism 5-r.4.2 source (`CubismMotionController` / `CubismMotionLayer` in the Dev1 project):
- `PlayAnimation` triggers `AnimationBeginHandler` **synchronously** (inside `CreateFadePlayingMotion`); `AnimationEndHandler` fires from `CubismMotionLayer.Update`.
- On a looping motion the layer's `IsFinished` stays false forever; when a new motion starts, the previous `_playingMotions` entry is force-set non-looping and its end time shortened (official crossfade).
- `Update()` also resets `_motionPriorities[i] = 0` when a layer is finished (the reflection fallback in `L2DActor.ResetLayerPriorityImpl` remains defensive, harmless).

Fixes applied:
- **Fix-L2D-1 (critical, wait deadlock)**: `L2DActor.WaitMotionFinished` previously polled `IsPlayingAnimation(ActionLayer)`. Single-layer design replays the looping Idle synchronously in `OnMotionEnd`, so the layer never becomes idle → `l2dmotion(pos, id, wait)` deadlocked the command chain forever on any character with an Idle configured. Now waits on `_activeNonIdleInstanceId == -1` (the same signal `OnMotionEnd` uses; begin is synchronous so there is no race). `DestroyModel` also resets that signal so in-flight waits exit immediately.
- **Fix-L2D-2 (critical, model-rebuild path)**: `SetAppearance` swapping to another L2D profile on the same slot rebuilt the model but did NOT re-subscribe `AnimationBeginHandler`/`AnimationEndHandler`, did NOT replay Idle, and dropped position/scale/flip/zOrder/alpha/visibility. Now the rebuild path re-subscribes, replays Idle, resets motion/expression state caches, and reapplies `ApplyTransform`/`ApplyOpacity`/`SetDepth`/`SetActive`.
- **Fix-L2D-3 (wait + endless motion guard)**: `IL2DControllable.IsMotionEndless(motionID)` added (true for Idle-kind or `Loop` motions). `L2DMotionCommand` now warns and degrades to async when `wait` is combined with an endless motion (previously guaranteed deadlock).
- **Fix-L2D-4 (arg validation)**: `l2dexp` third parameter (fade) now validates numeric format with InvariantCulture, aligned with `l2dmotion`'s 4th param (value still not applied until P4).
- **Dev test asset**: `L2DMao.asset` idle entry `Loop` was 0 (false) while validation requires Idle Loop and the runtime hardcodes Idle as looping — fixed to `Loop: 1` so the character editor validation no longer shows a false red error.

Known P4 (not implemented): `l2dreset` command; per-motion FadeIn/FadeOut override; per-expression fade override; editor model preview. The `l2dmotion` 4th arg and `l2dexp` 3rd arg are parsed but ignored with a verbose log.

## L2D LipSync (2026-09-14)

Lip sync bridges VNovelizer voice playback with official Cubism mouth components (shared-source architecture — voice keeps playing on VoiceManager's global DontDestroyOnLoad AudioSource, zero changes to playback/volume/AutoPlay logic):

- **SDK facts verified**: `CubismMouthController.MouthOpening` (0~1) drives parameters tagged with `CubismMouthParameter` (default `MouthOpening = 1f` — must be zeroed!); `CubismAudioMouthInput` samples `AudioInput.GetOutputData()` each frame (RMS → SmoothDamp → MouthOpening) — `GetOutputData` only returns data for audio playing on THAT AudioSource and is affected by its volume, so `AudioInput` must point at the real playback source. The SDK does NOT auto-tag mouth parameters (importers skip copying the tag on reimport); Mao's mouth params use non-standard names (`ParamMouthUp/Down/Angry` — no `ParamMouthOpenY`).
- **Core (no Live2D dependency)**: new `ILipSyncHost` interface in `Core/Theater/ILipSyncHost.cs` (`SetLipSyncActive(bool)`); `TheaterManager.FindCharacterActor(characterId)` matches by appearance id first segment (`ID#...`); `VoiceManager` gained `VoicePlaybackStarted(string speakerId)` / `VoicePlaybackStopped` events (idempotent reset signal fired from `StopVoice` AND coroutine tail; StopVoice kills the coroutine so no double-fire), `PlayVoice(path, speakerId = null)` optional param, `GetVoiceAudioSource()` exposing the global source.
- **Call sites pass speaker**: `VNManager.UpdateAudioState` passes `line.Speaker`; `SysPlayVoice` (playvoice system command) resolves speaker from `VNAPI.GetCurrentLineContext()`; `HistoryPanel` replay passes `entry.Speaker`.
- **L2D side**: `L2DActor` now implements `ILipSyncHost` — `ConfigureLipSync()` (called at end of `ConfigureOfficialComponents`) auto-tags instance parameters whose ID contains "Mouth" with `CubismMouthParameter` (instance-level, never touches prefab asset), ensures `CubismMouthController` + `CubismAudioMouthInput` exist, zeroes MouthOpening, disables input by default. `SetLipSyncActive(true)` lazily binds `AudioInput` to VoiceManager's source and applies profile Gain/Smoothing; `false` disables + zeroes mouth. `L2DLipSyncDirector` (new file, `RuntimeInitializeOnLoadMethod(AfterAssembliesLoaded)`) subscribes to voice events and resolves the speaker: profile must be registered and non-sprite-based, actor on stage must implement `ILipSyncHost`; switches mouth off on the previous speaker and on the new one.
- **L2DCharacterProfile** gained `LipSyncGain` (1~10, default 5 — raw speech RMS is 0.05~0.2 and needs amplification) and `LipSyncSmoothing` (0~1, default 0.3); editor detail panel shows two sliders.
- **Graceful degradation**: speaker null / not L2D / not on stage → no lip sync (silent). Model destroyed mid-voice → mouth input destroyed with it, voice continues on the global source (no interruption). Lower voice volume naturally reduces mouth amplitude (GetOutputData is volume-affected).
