# JokiAutomation Architecture Baseline (Phase 1)

> Documentation-only snapshot. No production code, test code, or behavior was changed while producing this artifact.
> Reference state: current `master`, after rollback of the Forwarder/Mutex-Reevaluation fix (Main returns immediately on `PipeConnectFailed`, no local `safeArgs` fallback).

---

## 1. Aktuelle Architekturübersicht

JokiAutomation is a WinForms (.NET Framework 4.8) single-instance application (`JokiAutomation/Program.cs`, `JokiAutomation/Form1.cs` + partials) that controls stage hardware (audio, PTZ/position, ATEM video switcher, Raspberry Pi relays/IR, beamer) for church services.

Two call surfaces exist side by side:

- **Legacy direct path**: `Form1.CommandInterpreterAsync(string[] commands)` — one big `switch` over the first argument, directly invoking audio/ATEM/PTZ/RasPi/IR helpers. This is where almost all actual hardware I/O happens today.
- **New partial infrastructure** in `Program.cs`: `ICommandDispatcher` / `CommandDispatcher`, `ScenePlan`, `PipelineCommand`, `SceneExecutionContext`, `SceneDispatchRequest/Result`. This layer currently does **not** replace the legacy path — all four of its pipeline executors (`_audioExecutor`, `_videoExecutor`, `_positionExecutor`, `_uiExecutor`) are wired back to `ExecuteCommandQueuedAsync(form, ...)`, which still calls `Form1.CommandInterpreterAsync`. The dispatcher is a routing/bookkeeping wrapper, not a hardware-execution replacement.

External callers (CLI, named-pipe/Forwarder, EventTimer) all end up calling `ExecuteCommandQueuedAsync` → `ExecuteDispatchedCommandAsync` → `Form1.CommandInterpreterAsync`, either directly (CLI, legacy pipe path) or via `CommandDispatcher.DispatchAsync` → scene planner → single-command `ScenePlan` → one of the four executors → `ExecuteCommandQueuedAsync` again. Both paths converge on the same legacy switch.

UI buttons in `Form1.cs` (button1_Click, button2_Click, button7/8_Click, etc.) call hardware helpers **directly** (audio, IR, event timer send) and largely bypass `CommandInterpreterAsync` entirely — they are a third, independent trigger surface.

---

## 2. Entry Points

| Entry Point | Mechanism | File / Method | Reaches CommandInterpreterAsync? |
|---|---|---|---|
| CLI | `JokiAutomation.exe <args>` | `Program.Main` (~line 43) → `ExecuteCommandQueuedAsync(form, safeArgs, cliRequestId, "CLI", ...)` (line 440) | Yes, directly |
| Pipe/Forwarder (secondary instance) | Named pipe client (`SingleInstanceCommandClient.ForwardAsync`, ~2226+) → server `HandleClientAsync` (2377+) | `Program.cs` 2338-2440 | Yes, via `CommandDispatcher.DispatchAsync` → scene plan → executor → `ExecuteCommandQueuedAsync` |
| Pipe (legacy/no-dispatcher paths) | Direct `ExecuteCommandQueuedAsync(JA, incomingArgs, ...)` call sites | `Program.cs` lines 117, 374 | Yes, directly |
| EventTimer (separate process) | `Process.Start("JokiAutomation.exe", Arguments)` with `JOKI_COMMAND_SOURCE=EventTimer` | `EventTimer/Form1.cs` `StartJoKiAutomationAsync` (~1121-1170), wrapped by `StartJoKiAutomationWithHandlingAsync` (~1242) | Yes, becomes a new CLI/forwarded process invocation |
| PowerPoint | Documented in `README.md` (CLI invocation), no VBA/add-in files found in this repo | — | Indirect, via CLI/forwarder, same as above |
| UI (Form1 buttons/listbox) | Direct method calls, no pipe/CLI involved | `Form1.cs` button1_Click (1558-1615, EventTimer send), button2_Click (1619-1622, IR only), button7/8_Click (1638-1670, audio only), button4_Click (1672-1681, channel toggle) | **No** — these call hardware helpers directly, not through the command switch |
| ListBox scene selection (Predigt/Altar etc.) | `Form1.Designer.cs` items (625, 943) wired to handlers that ultimately route into the same command path used by CLI | `Form1.cs` / `Form1.Designer.cs` | Needs confirmation per handler; treat as UI-origin entry distinct from CLI/Pipe |

---

## 3. Scene / Command Matrix (Form1.CommandInterpreterAsync)

`CommandInterpreterAsync` spans `Form1.cs` 738-1389. All scene cases, after their body, fall through to a shared tail: `AwaitPendingAudioCompletionAsync()` then `return Success` (or a caught exception path producing `CommandFailed`).

| Scene/Command | Line range (approx.) | Audio | Video/ATEM | Position/PTZ | UI | Infra | Notes |
|---|---|---|---|---|---|---|---|
| Altar | 770-788 | QueueAudioProfileByNameAsync | ATEM switch | optional `ExecuteCanonSceneAsync` (PTZ) | — | — | Only scene with optional PTZ scene execution inline |
| Predigt | 815-834 | QueueAudioProfileByNameAsync | ATEM switch | — | — | — | |
| Text | 836-855 | QueueAudioProfileByNameAsync | ATEM/PiP | — | Diashow/Overlay-ish | — | |
| Gebet | 857-875 | QueueAudioProfileByNameAsync | DisablePictureInPicture + ATEM switch + `Task.Delay(1000)` | — | — | — | Synchronous PiP disable before switch |
| Band | 877-904 | QueueAudioProfileByNameAsync | ATEM switch | — | — | — | |
| BandMulti | 906-932 | QueueAudioProfileByNameAsync | EnablePictureInPicture + ATEM switch | — | — | — | |
| LesungMulti | 934-954 | QueueAudioProfileByNameAsync | PiP toggle + ATEM switch | — | — | — | |
| GoPro | 956-976 | QueueAudioProfileByNameAsync | ATEM Input Switch | — | — | — | |
| PositionControl | 1138-1239 | — | final ATEM switch (non-PTZ path) | `PositionControlPtzAudioOrchestrator.ExecuteAsync` (PTZ mode) **or** legacy `_positionControl.moveToPos` + synchronous polling (non-PTZ mode) | — | RasPi | Two distinct code paths depending on mode flag |
| BEAMER_* (LiveVideo/LiveStream/VideoClip/Mute/ON) | 978-1059 (various) | — | — | — | Beamer UI/display | `ExecuteIrSequenceAsync` (RasPi IR) | |
| Backup_Start | 1365-1369 | — | — | — | — | `ExecuteIrSequenceAsync` | |
| Backup_Switch | 1371-1388 | — | — | — | — | `ExecuteIrSequenceAsync` | |
| Backup_Stop | 1365-1388 region | — | — | — | — | `ExecuteIrSequenceAsync` | Exact sub-range to confirm against live file during Phase 2 |

All cases: audio queueing is **fire-and-forget relative to the switch case** (`QueueAudioProfileByNameAsync` enqueues and returns), with completion only awaited once, centrally, after the whole switch via `AwaitPendingAudioCompletionAsync()`. This is the existing serialization point for audio across all scenes.

---

## 4. Hardware Access Matrix (classification)

| Hardware Call | Category | Typical Caller(s) |
|---|---|---|
| `QueueAudioProfileByNameAsync` / `ApplyAudioProfileByNameAsync` | AUDIO | All scene cases in §3 |
| `AwaitPendingAudioCompletionAsync` | AUDIO (completion gate) | End of `CommandInterpreterAsync` |
| `PositionControlPtzAudioOrchestrator.ExecuteAsync`, `_positionControl.moveToPos`, Canon PTZ calls | POSITION | PositionControl case, Altar (optional PTZ scene) |
| `SwitchATEMInputAsync` / ATEM Program/Preview/PiP methods | VIDEO | Almost every scene case |
| `ExecuteIrSequenceAsync` / RasPi IR/SSH relay commands | INFRASTRUCTURE (treated as RasPi hardware trigger) | BEAMER_*, Backup_* |
| `rasPiExecute` / `RasPiExecuteAsync` / `rasPiThreadStart` | INFRASTRUCTURE | RasPi.cs / RasPi.Async.cs, used by audio+position+IR flows |
| Diashow/Overlay/Form actions | UI | Text scene, UI buttons |
| Pipe/Mutex/Logging/EventTimer/Dropbox/Lifecycle | INFRASTRUCTURE | Program.cs, EventTimer.cs |

Direct-call summary (YES = genuine production hardware call, not test-only):

| Symbol | File | Direct HW? | Comment |
|---|---|---|---|
| `QueueAudioProfileByNameAsync` | Form1.AudioProfile.cs | YES | central audio entry used by all scenes |
| `SwitchATEMInputAsync` | Form1.cs (ATEM helpers) | YES | used by nearly every scene case |
| PTZ / Canon PTZ calls | PositionControl.cs / Form1.cs | YES | PositionControl + Altar only |
| `RasPiExecuteAsync` / `rasPiExecute` | RasPi.cs / RasPi.Async.cs | YES | underlies audio RasPi commands, IR, PositionControl RasPi moves |
| `ExecuteIrSequenceAsync` | Form1.cs | YES | Beamer + Backup_* |
| Test-only fakes (`FakeRasPiCommandExecutor`) | JokiAutomation.Tests/Fakes | NO (test double) | used to intercept RasPi calls in unit/regression tests |

---

## 5. CommandDispatcher Ist-Zustand

- **Exists**: YES, in `Program.cs` (`ICommandDispatcher`, `CommandDispatcher`, lines ~2751-3130+).
- **Commands routed through it**: only requests arriving via the named-pipe server (`SingleInstanceCommandServer.HandleClientAsync` → `_dispatcher.DispatchAsync`). CLI (`ExecuteCommandQueuedAsync` called directly from `Main`) and the two direct pipe call sites at lines 117/374 bypass the dispatcher entirely and call `ExecuteCommandQueuedAsync` straight away.
- **Responsibility today**:
  - Builds a `ScenePlan` via an injected `_scenePlanner` (`CreateLegacyScenePlan`).
  - Tracks `SceneExecutionContext` per `SceneId` in `_sceneContexts`.
  - Supports `HandoffOnly` (fire-and-forget, tracked in `_backgroundSceneTasks`) vs `WaitForScene` wait modes.
  - Sequentially executes Audio → Video → Position → UI command lists (`ExecutePipelineCommandsSequentially`), each list currently containing at most one `PipelineCommand` (see §9/§13 gap).
  - Manages shutdown gating (`TryEnterDispatch`/`ExitDispatch`, `Program.IsShutdownInProgress`).
- **Does it execute hardware itself?** NO — all four executors (`_audioExecutor`, `_videoExecutor`, `_positionExecutor`, `_uiExecutor`) are injected as `command => ExecuteCommandQueuedAsync(form, ToDispatchArguments(command), ...)`, which re-enters the legacy queue and ultimately calls `Form1.CommandInterpreterAsync`.
- **Queueing**: YES, via `ExecuteCommandQueuedAsync`'s own queue-depth counter (`_pendingExternalCommands`, `MaxQueuedCommands`), independent of the dispatcher's scene bookkeeping.
- **RequestId**: YES — `PipelineCommand.RequestId`, `SceneDispatchRequest.ExternalRequestId`, propagated into `SceneExecutionContext`.
- **Completion**: YES — `SceneExecutionContext.Completion` (`TaskCompletionSource<SceneDispatchResult>`), resolved once pending command count reaches zero (`TryCompleteIfDone`).
- **ExitCodes**: YES — `SceneDispatchResult.ExitCode`, `PipelineCommandResult.ExitCode`, `CommandExitCodes.*`.
- **Still directly in Form1**: all actual hardware execution logic (every scene case in §3), `AwaitPendingAudioCompletionAsync`, UI-button-triggered hardware actions.

---

## 6. ScenePlan Ist-Zustand

- **Exists**: YES (`Program.cs` ~2719-2727).
- **Used by**: only the `CommandDispatcher` path (pipe requests going through `DispatchAsync`), via `CreateLegacyScenePlan`.
- **Actions describable today**: `AudioCommands`, `VideoCommands`, `PositionCommands`, `UiCommands` — each a list of `PipelineCommand`.
- **Current population**: `CreateLegacyScenePlan` maps an entire incoming command name to exactly **one** `CommandPipeline` value (`ResolvePhaseOneTargetPipeline`) and puts a single `PipelineCommand` into the corresponding list; the other three lists are always empty. This does not reflect scenes like Predigt/Gebet which in reality touch Audio + Video (and sometimes Position) simultaneously — see Gap in §13.
- **Hardware domains represented**: Audio/Video/Position/UI container fields exist, but only one is ever populated per legacy command today.
- **Sequencing/Dependencies**: fixed order Audio → Video → Position → UI (`ExecutePipelineCommandsSequentially`), no explicit dependency graph.
- **RequestId**: YES (`PipelineCommand.RequestId`, derived from `SceneDispatchRequest.ExternalRequestId` or newly generated).
- **SceneId**: YES (`ScenePlan.SceneId`, newly generated per dispatch in `CreateLegacyScenePlan`).
- **Completion/Result semantics**: YES, via `SceneExecutionContext`/`SceneDispatchResult`, but only meaningful for the dispatcher path; the CLI/direct-pipe paths have their own separate completion via `ExecuteDispatchedCommandAsync`'s `TaskCompletionSource<int>`.

---

## 7. RequestId / SceneId Traceability

| Entry Point | RequestId created at | Propagated to | Lost at |
|---|---|---|---|
| CLI | `cliRequestId` generated in `Program.Main` before calling `ExecuteCommandQueuedAsync` | Lifecycle log lines, `ExecuteDispatchedCommandAsync` | Not passed into a `ScenePlan`/`SceneId` — CLI path never creates a `SceneId` |
| Forwarder/Pipe (client) | `SingleInstanceCommandClient.ForwardAsync` generates a `Guid requestId` | Serialized into `PipeCommandRequest.RequestId`, read server-side | — |
| Forwarder/Pipe (server, dispatcher path) | Reused from `PipeCommandRequest.RequestId` as `SceneDispatchRequest.ExternalRequestId`, parsed into `PipelineCommand.RequestId` (or re-generated as new `Guid` if not parseable) | `ScenePlan`, `SceneExecutionContext`, lifecycle logs | If `ExternalRequestId` isn't a valid GUID, a **new** RequestId is silently generated, breaking traceability back to the original pipe request id |
| Direct pipe call sites (lines 117, 374) | Reuse `requestId` from caller context | `ExecuteCommandQueuedAsync` logs | Never enters dispatcher/ScenePlan — no SceneId ever created |
| EventTimer | New process invocation (CLI-style); EventTimer itself does not appear to generate/propagate a JokiAutomation RequestId, it only sets `JOKI_COMMAND_SOURCE=EventTimer` | Becomes a fresh CLI RequestId inside the started JokiAutomation process | Original EventTimer-side identity (if any) is not carried into JokiAutomation's RequestId |
| UI (buttons/listbox) | No RequestId concept at all — direct method calls | — | N/A, UI path doesn't use request/scene identifiers |

**SceneId present**: YES, but **only** for the dispatcher/pipe path (`CreateLegacyScenePlan` always mints a new `Guid SceneId`). CLI and direct-pipe-call-site paths never create a `SceneId` — they only have the `ExecuteDispatchedCommandAsync`/`TaskCompletionSource<int>` completion mechanism, with no `SceneId` at all.

- SceneId generated at: `CreateLegacyScenePlan` (dispatcher path only).
- SceneId propagated to: `SceneExecutionContext`, lifecycle log lines (`SCENE_BEGIN`, `DISPATCH_BEGIN`, `SCENE_PLAN`).
- SceneId lost at: never reaches CLI-path executions, never reaches Form1 UI-button actions, and is not round-tripped back out to the original Forwarder/EventTimer/PowerPoint caller.

Important distinction preserved per task instructions: technical RequestId (pipe/CLI correlation) and business/scene identity (SceneId, "this is semantically one Predigt-scene execution") are tracked by different fields today and are **not** reliably linked when RequestId parsing fails or when the CLI/direct-pipe-call paths are used instead of the dispatcher.

---

## 8. Bestehende Parallelitätsmatrix

Documented from current code behavior only; nothing changed.

**Generic scene shape (Altar/Predigt/Text/Gebet/Band/BandMulti/LesungMulti/GoPro):**

- Audio:
  - Start: `QueueAudioProfileByNameAsync` call inside the switch case (fire-and-forget relative to the case body — enqueues work on an internal audio queue).
  - Completion: only awaited once, centrally, at the very end of `CommandInterpreterAsync` via `AwaitPendingAudioCompletionAsync()`.
- Video/ATEM:
  - Start: synchronous/awaited ATEM switch call within the same case, executed after the audio enqueue call returns (no await on audio completion first).
  - Completion: awaited directly in the case (`await SwitchATEMInputAsync(...)` style calls).
- Position/PTZ (Altar only, optional path):
  - Start: `ExecuteCanonSceneAsync` awaited inline before/around the ATEM switch, if PTZ scene execution is enabled for that case.
  - Completion: awaited inline.
- UI:
  - No explicit separate UI start/completion tracked in these cases; any UI-visible effect is a side effect of the ATEM/PiP calls.
- Dependencies: Audio enqueue happens first but is **not awaited** before Video/ATEM executes → Audio and Video run concurrently with only a final, end-of-method audio completion barrier. PTZ (where present) is awaited before the final ATEM switch in that same case (e.g., Altar), i.e. **PTZ completion precedes the final ATEM switch for that one scene**.

**Gebet / BandMulti / LesungMulti specifics:**

- Synchronous PiP disable/enable call before the ATEM switch, plus a `Task.Delay(1000)` after the switch — a time-based serialization point rather than an event-based one.

**PositionControl:**

- PTZ-mode: `PositionControlPtzAudioOrchestrator.ExecuteAsync` orchestrates audio + PTZ together internally (internal serialization details are inside that orchestrator, not re-documented here — out of scope per instruction not to touch PositionControl.cs).
- Non-PTZ legacy mode: synchronous polling loop on `_positionControl.moveToPos`, followed by a final ATEM switch only after the polled move is confirmed complete — i.e. **PTZ MoveComplete → final ATEM switch** dependency is real and present today in the non-PTZ/legacy branch.

**BEAMER_*/Backup_*:**

- Single `await ExecuteIrSequenceAsync(...)` per case; no additional parallelism within the case.

**RasPi-Aufträge untereinander:**

- Audio RasPi commands and PositionControl RasPi/PTZ commands funnel through the same `RasPi.cs`/`RasPi.Async.cs` execution primitives (`rasPiExecute`, `RasPiExecuteAsync`, `rasPiThreadStart`); whether these serialize against each other depends on internal RasPi queueing not re-analyzed here (out of scope: RasPi.cs must not be touched/altered in this phase, but its interaction model should be revisited explicitly in Phase 2 planning).

---

## 9. Ist-Matrix

| Scene/Command | Entry Point | CommandInterpreter Case | Current Dispatcher | Current ScenePlan | Audio | Position | Video | UI | Infra | RequestId | SceneId | Current Parallelism | Dependencies | Completion | ExitCode | Target Pipeline |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Altar | CLI/Pipe/EventTimer/UI | 770-788 | Pipe-only | Pipe-only, single-pipeline mapping | Yes | Optional (PTZ scene) | Yes | No | No | Yes (pipe) / partial (CLI) | Pipe-only | Audio||Video concurrent; PTZ awaited before final switch | PTZ→final ATEM (if enabled) | End-of-method audio await + case-local awaits | Yes (CommandExitCodes) | Position (per `ResolvePhaseOneTargetPipeline`) |
| Predigt | CLI/Pipe/EventTimer/UI | 815-834 | Pipe-only | Pipe-only | Yes | No | Yes | No | No | Yes (pipe)/partial | Pipe-only | Audio||Video concurrent | none beyond default | same as above | Yes | Position |
| Text | CLI/Pipe/EventTimer/UI | 836-855 | Pipe-only | Pipe-only | Yes | No | Yes | Yes (Diashow/Overlay-ish) | No | Yes/partial | Pipe-only | Audio||Video concurrent | none | same | Yes | Audio (default mapping) |
| Gebet | CLI/Pipe/EventTimer/UI | 857-875 | Pipe-only | Pipe-only | Yes | No | Yes (PiP disable + switch) | No | No | Yes/partial | Pipe-only | Audio||Video; time-based delay after switch | PiP disable→switch | same | Yes | Audio (default mapping) |
| Band | CLI/Pipe/EventTimer/UI | 877-904 | Pipe-only | Pipe-only | Yes | No | Yes | No | No | Yes/partial | Pipe-only | Audio||Video concurrent | none | same | Yes | Video |
| BandMulti | CLI/Pipe/EventTimer/UI | 906-932 | Pipe-only | Pipe-only | Yes | No | Yes (PiP enable + switch) | No | No | Yes/partial | Pipe-only | Audio||Video; time-based delay | PiP enable→switch | same | Yes | Video |
| LesungMulti | CLI/Pipe/EventTimer/UI | 934-954 | Pipe-only | Pipe-only | Yes | No | Yes (PiP toggle + switch) | No | No | Yes/partial | Pipe-only | Audio||Video; time-based delay | PiP toggle→switch | same | Yes | Video |
| GoPro | CLI/Pipe/EventTimer/UI | 956-976 | Pipe-only | Pipe-only | Yes | No | Yes (input switch) | No | No | Yes/partial | Pipe-only | Audio||Video concurrent | none | same | Yes | Audio (default mapping) |
| PositionControl | CLI/Pipe/EventTimer/UI | 1138-1239 | Pipe-only | Pipe-only | No | Yes (PTZ or legacy move) | Yes (final switch) | No | Yes (RasPi, legacy move mode) | Yes/partial | Pipe-only | PTZ/move completion precedes final ATEM switch (legacy mode) | MoveComplete→ATEM switch | polling / orchestrator completion | Yes | Position |
| BEAMER_* | CLI/Pipe/EventTimer/UI | ~978-1059 | Pipe-only | Pipe-only (mapped to default/Audio) | No | No | No | Yes (beamer display) | Yes (`ExecuteIrSequenceAsync`) | Yes/partial | Pipe-only | Single sequential IR call | none | await IR call | Yes | Infrastructure (actual) vs Audio (current default mapping) — **gap** |
| Backup_Start | CLI/Pipe/EventTimer/UI | ~1365-1369 | Pipe-only | Pipe-only (default mapping) | No | No | No | No | Yes (`ExecuteIrSequenceAsync`) | Yes/partial | Pipe-only | Single sequential IR call | none | await IR call | Yes | Infrastructure (actual) vs Audio (current default) — **gap** |
| Backup_Switch | CLI/Pipe/EventTimer/UI | ~1371-1388 | Pipe-only | Pipe-only (default mapping) | No | No | No | No | Yes (`ExecuteIrSequenceAsync`) | Yes/partial | Pipe-only | Single sequential IR call | none | await IR call | Yes | Infrastructure (actual) vs Audio (current default) — **gap** |
| Backup_Stop | CLI/Pipe/EventTimer/UI | ~1365-1388 region | Pipe-only | Pipe-only (default mapping) | No | No | No | No | Yes (`ExecuteIrSequenceAsync`) | Yes/partial | Pipe-only | Single sequential IR call | none | await IR call | Yes | Infrastructure (actual) vs Audio (current default) — **gap** |

Legend: "Pipe-only" = only requests routed through `SingleInstanceCommandServer`/`CommandDispatcher` get a `ScenePlan`/`SceneId`; CLI and direct-pipe-call-site paths (lines 117/374) get RequestId/ExitCode via `ExecuteDispatchedCommandAsync`'s `TaskCompletionSource<int>` but no `ScenePlan`/`SceneId`.

---

## 10. Ziel-Pipeline-Mapping (markieren, nicht implementieren)

| Scene/Command | Actual domains touched | `ResolvePhaseOneTargetPipeline` result today | Gap |
|---|---|---|---|
| Altar | Audio + Video + Position(optional) | Position | Audio/Video not represented |
| Predigt | Audio + Video | Position | Mismatch — Predigt has no position/PTZ but is mapped to Position pipeline |
| Gebet | Audio + Video | Position | Mismatch |
| GoPro | Audio + Video | Position | Mismatch |
| LesungMulti / Band / BandMulti | Audio + Video | Video | Audio not represented |
| Text | Audio + Video + UI | Audio (default/fallback) | Video/UI not represented |
| PositionControl | Position + Video (final switch) + Infra (RasPi legacy mode) | Position | Video/Infra not represented |
| BEAMER_* / Backup_* | Infrastructure (RasPi IR) | Audio (default/fallback) | Entirely mis-mapped; these are not audio commands at all |

This table should be read strictly as **current-state mapping gaps**, not an approved target design — no new pipeline/mapping logic was introduced.

---

## 11. Kritische Abhängigkeiten

| Source | Target | Warum aktuell erforderlich | Codebeleg | Späterer Milestone-Kandidat |
|---|---|---|---|---|
| PTZ MoveComplete (legacy PositionControl mode) | finaler ATEM Switch | Avoids switching to a camera input before the physical move has settled | `Form1.cs` PositionControl case, ~1138-1239 (polling loop before final switch) | Explicit "PositionPipeline → VideoPipeline" dependency edge |
| PiP disable/enable | ATEM Switch (Gebet/BandMulti/LesungMulti) | PiP state must be set before switching Program/Preview to avoid visible glitch, enforced today via synchronous call + `Task.Delay(1000)` | `Form1.cs` cases ~857-954 | Explicit ordering constraint in future VideoPipeline, replacing the time-based delay |
| Audio enqueue | (none, independent of Video today) | Audio is intentionally fire-and-forget relative to Video/ATEM in current scenes — no dependency enforced, audio only barrier at method end | `Form1.cs` scene cases + `AwaitPendingAudioCompletionAsync` at end of `CommandInterpreterAsync` | Decide explicitly in Phase 2 whether Audio↔Video should remain independent |
| UI (Diashow/Overlay in Text) | Audio/PTZ | No explicit dependency found; UI effects appear to run independently of audio timing | `Form1.cs` Text case ~836-855 | Confirm independence holds once UiPipeline is introduced |
| CommandDispatcher scene execution | `ExecuteCommandQueuedAsync` → `Form1.CommandInterpreterAsync` | Dispatcher pipeline executors are currently just forwarding wrappers around the legacy queue — this is a structural (not business) dependency that must be removed/replaced for true pipeline separation | `Program.cs` `CreateDispatcher` (~770-783), `ExecuteCommandQueuedAsync` (580+) | First real target of ScenePlan migration (see §15) |

---

## 12. Bestehende Testabdeckung

| Scene/Area | Protecting tests |
|---|---|
| Audio pipeline lifetime / completion ordering | `JokiAutomation.Tests/Regression/AudioPipelineLifetimeRegressionTests.cs`, companion analysis `AudioPipelineLifetimeAnalysis.md` (documents `ExecuteDispatchedCommandAsync` / `CommandInterpreterAsync` completion semantics referenced in §5-§8 above) |
| Lifecycle / forwarder / pipe / mutex behavior | `JokiAutomation.Tests/JokiAutomation.LifecycleTests.cs` |
| PositionControl + Audio interaction | `JokiAutomation.Tests/Integration/PositionControlAudioIntegrationTests.cs` |
| Audio profile mapping/execution | `JokiAutomation.Tests/Unit/AudioProfileMappingTests.cs`, `Unit/AudioProfileExecutionTests.cs`, `Regression/AudioProfileRegressionTests.cs` |
| Hardware.Audio explicit/log-based tests | `JokiAutomation.Tests/Hardware/AudioProfileHardwareTests.cs`, `Hardware/AudioControlLogParser(Tests).cs` |
| RasPi execution primitives | `JokiAutomation.Tests/Unit/RasPiExecuteAsyncTests.cs`, `Fakes/FakeRasPiCommandExecutor.cs` |
| Dropbox/backup upload | `JokiAutomation.Tests/DropboxApiUploaderTests.cs` |

**Coverage gaps (markieren, nicht füllen):**

- No tests found covering `CommandDispatcher`/`ScenePlan` mapping logic itself (`CreateLegacyScenePlan`, `ResolvePhaseOneTargetPipeline`).
- No tests found covering BEAMER_*/Backup_* IR sequence cases.
- No tests found covering Gebet/BandMulti/LesungMulti PiP ordering/delay behavior specifically.
- No tests found covering RequestId-parse-failure fallback (new GUID generation) behavior in `CreateLegacyScenePlan`.
- No tests found covering the CLI-path vs Pipe-path divergence (CLI never produces a `SceneId`).

---

## 13. Identifizierte Mapping-Lücken

1. `CreateLegacyScenePlan` maps a whole legacy command to **one** pipeline only; real scenes are multi-domain (Audio+Video, sometimes +Position/+UI). Current single-pipeline model cannot represent this without changes.
2. BEAMER_*/Backup_* commands are infrastructure (RasPi IR) but default-map onto the Audio pipeline today — a purely incidental fallback classification, not a deliberate design choice.
3. CLI and the two direct `ExecuteCommandQueuedAsync` call sites (Program.cs lines 117, 374) never go through `CommandDispatcher`/`ScenePlan` at all — no `SceneId`, no multi-domain plan, inconsistent tracing vs the Pipe/dispatcher path.
4. UI button-driven actions in `Form1.cs` bypass `CommandInterpreterAsync` and the dispatcher entirely — a third execution surface with no RequestId/SceneId/ExitCode semantics.
5. RequestId parse failures in `CreateLegacyScenePlan` silently mint a new GUID, breaking traceability back to the Forwarder/pipe caller's original id.
6. EventTimer does not appear to propagate its own identity into JokiAutomation's RequestId — each EventTimer-triggered run starts a fresh correlation chain.

---

## 14. Risiken für Phase 2

- Any ScenePlan migration that changes `ResolvePhaseOneTargetPipeline`'s single-pipeline mapping must keep `ExecutePipelineCommandsSequentially`'s current Audio→Video→Position→UI ordering intact, or must explicitly re-validate the documented dependencies in §11 (PTZ→ATEM, PiP→ATEM).
- CLI and direct-pipe-call-site paths bypassing the dispatcher mean any dispatcher-only migration leaves two execution surfaces with diverging behavior/tracing — must be addressed before CLI-triggered production usage is considered "migrated".
- UI-button direct hardware calls are a blind spot: migrating only the dispatcher path will not affect them, and they must be explicitly scoped in/out of later phases.
- Time-based serialization (`Task.Delay(1000)` after PiP toggle) is fragile; replacing with pipeline dependency edges changes real-world timing behavior and needs careful validation against hardware, not just unit tests.
- RasPi-internal queueing/serialization across Audio and Position requests was explicitly out of scope for this baseline (RasPi.cs must not be touched) and needs a dedicated investigation before any RasPi-related pipeline split.

---

## 15. Empfohlene Reihenfolge der ScenePlan-Migration

Recommended first, low-risk candidate: **BEAMER_* / Backup_* commands.**

Rationale:
- Single hardware domain (Infrastructure/RasPi IR only), no Audio/Video/Position interleaving to reconcile.
- No PTZ/PiP ordering dependency to preserve.
- Currently mis-mapped onto the Audio pipeline by default in `ResolvePhaseOneTargetPipeline` — correcting this to an explicit Infrastructure/Ui pipeline classification is a contained, mechanical change with minimal regression surface.
- Existing test gap (§12) is small and can be closed with a handful of focused tests before changing mapping behavior.

---

## Abschlussbericht

**Analysierte Szenen:** Altar, Predigt, Text, Gebet, Band, BandMulti, LesungMulti, GoPro, PositionControl, BEAMER_* (LiveVideo/LiveStream/VideoClip/Mute/ON), Backup_Start, Backup_Switch, Backup_Stop.

**Analysierte direkte Hardwarezugriffe:** `QueueAudioProfileByNameAsync`/`ApplyAudioProfileByNameAsync` (Audio), `SwitchATEMInputAsync`/PiP toggles (Video), PTZ/Canon PTZ calls + `PositionControlPtzAudioOrchestrator`/legacy `moveToPos` (Position), `ExecuteIrSequenceAsync` (Infrastructure/RasPi IR), `RasPiExecuteAsync`/`rasPiExecute`/`rasPiThreadStart` (Infrastructure, underlies Audio+Position+IR).

**CommandDispatcher bereits vorhanden:** PARTIAL (routing/bookkeeping only; hardware executors forward back into the legacy `Form1.CommandInterpreterAsync` queue; only the Pipe path uses it, CLI/direct-pipe-call-sites bypass it).

**ScenePlan bereits vorhanden:** PARTIAL (data model exists with 4 pipeline lists, but only one is ever populated per command today; no multi-domain representation).

**RequestId vollständig:** NO (CLI and direct-pipe-call-site paths never get a `ScenePlan`-level RequestId/SceneId; parse failures silently mint new GUIDs; EventTimer doesn't propagate its own identity).

**SceneId vollständig:** NO (only created for requests that go through `CommandDispatcher.DispatchAsync`, i.e. only part of the Pipe path).

**Direkte Hardwarezugriffe aus CommandInterpreter:** Audio queueing, ATEM/PiP switching, PTZ/Canon moves, RasPi IR sequences — all still executed directly inside `Form1.CommandInterpreterAsync`'s switch cases; none of this has moved behind the new `CommandDispatcher`/`ScenePlan` abstraction yet.

**Bestehende Parallelität:** Audio is fire-and-forget relative to Video within a scene case, with a single completion barrier at the end of `CommandInterpreterAsync`; Video/ATEM switches are awaited inline; PTZ (Altar, PositionControl legacy mode) is awaited before the final ATEM switch; Gebet/BandMulti/LesungMulti use synchronous PiP toggle + a fixed `Task.Delay(1000)` before/after the switch instead of an event-based gate.

**Kritische Dependencies:** PTZ MoveComplete → final ATEM Switch (PositionControl legacy mode, Altar optional PTZ); PiP toggle → ATEM Switch (Gebet/BandMulti/LesungMulti); CommandDispatcher pipeline executors → legacy `ExecuteCommandQueuedAsync`/`CommandInterpreterAsync` (structural, must be resolved for true pipeline separation).

**Größte Mapping-Lücken:** single-pipeline-per-command model cannot express multi-domain scenes; BEAMER_*/Backup_* mis-mapped to Audio pipeline by default; CLI/direct-pipe-call-sites and UI buttons bypass dispatcher/ScenePlan entirely; RequestId/SceneId traceability breaks on parse failure and across CLI/UI/EventTimer boundaries.

**Vorgeschlagene erste ScenePlan-Migration:** BEAMER_*/Backup_* commands (single Infrastructure/RasPi-IR domain, no PTZ/PiP ordering dependency, smallest regression surface).

**Produktionsdateien geändert:** NONE

**Testdateien geändert:** NONE

**Erzeugtes Artefakt:** `JokiAutomation_Architecture_Baseline.md`
