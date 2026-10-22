# Audio-Pipeline Lifetime Analysis (Phase A)

## Symptom

Production log correlation (Windows `JokiAutomation.exe` lifecycle log + Raspberry `AudioControl.log`)
proved that the **first** `Predigt` CLI invocation (PID=9584, 18:39:50) completed with
`ExitCode=0` but **no** `COMMAND command=30 index=114` ever arrived at the Raspberry Pi.
The **second** `Predigt` invocation (PID=12216, 18:39:56) produced the expected
`COMMAND pid=5691 command=30 index=114` at 18:39:58 and completed/verified successfully.

This proves the audio SSH request for the first call was never sent (or never completed),
while the CLI process reported success.

## Call Chain (production path for `Predigt`)

| Stage | File | Method | Returns | Awaited by caller? | Who owns the Task? | Who logs failure? | Who decides ExitCode? | Can Form1/process end before this finishes? |
|---|---|---|---|---|---|---|---|---|
| 1 | `Program.cs` | `CommandInterpreter` / `ExecuteDispatchedCommandAsync` (677-705) | `Task` (via `TaskCompletionSource<int>`) | Yes, `completion.Task` is awaited at line 674 | `Program` (static) | `catch` at 695-699 sets `CommandFailed` | `completion.TrySetResult(exitCode)` at 693, immediately after `CommandInterpreterAsync` returns | **Yes** — `completion` resolves as soon as `CommandInterpreterAsync` returns, regardless of any still-running background audio continuation |
| 2 | `Form1.cs` | `CommandInterpreterAsync` (693-1337) | `Task<int>` | Yes (awaited in stage 1, line 690) | `Form1` instance | `catch (Exception ex)` at 1319-1324 | `exitCode = CommandExitCodes.Success` at 1316, set right after the `switch` completes | **Yes** — the `switch` case for `"Predigt"` only awaits `QueueAudioProfileByNameAsync`, which itself does not wait for the SSH round-trip (see stage 4) |
| 3 | `Form1.cs` (switch case `"Predigt"`, 861-887) | inline | `bool` (via `QueueAudioProfileByNameAsync`) | Yes, but the awaited call only confirms *queuing*, not completion | n/a | Only "Tonprofil konnte nicht gesetzt werden" if `false` is returned | returns `false`→`CommandFailed` only if queueing itself throws/fails synchronously | n/a |
| 4 | `Form1.AudioProfile.cs` | `QueueAudioProfileByNameAsync` (186-192) → `ApplyAudioProfileByNameAsync(waitForCompletion:false)` (32-78) → `SendAudioProfileAsync` (95-176) | `Task<bool>` | Yes, but returns **immediately after queuing**, not after send/verify | Caller only owns the "was-it-queued" result | Exceptions inside `SendAudioProfileAsync`'s synchronous queuing portion are caught/logged (163-175); exceptions inside the *background* continuation are **not** propagated to this Task | `return true` at line 161 happens **before** the SSH command is even attempted | **Yes** — `true` means "enqueued onto `_audioPipelineTail`", not "sent" or "confirmed" |
| 5 | `Form1.AudioProfile.cs` | `QueueAudioProfileDispatch` (194-227) | `void`, mutates `_audioPipelineTail` under `lock(_audioPipelineSync)` | **No** — fire-and-forget `ContinueWith(...).Unwrap()` chained onto a **shared, per-Form1-instance** field | Nobody — the continuation's `Task` is stored only in the private `_audioPipelineTail` field, overwritten on every call | Exceptions are caught inside the continuation and logged via `_logDat` (218-224), but never rethrown/surfaced to any caller | n/a | **Yes** — since nothing ever awaits this specific continuation, the CLI command can finish and the process can exit while this is still pending or not yet scheduled |
| 6 | `JokiAutomation\AudioMix.cs` | `ExecuteAudioAsync` (211-227) | `Task` | Yes, awaited inside the stage-5 continuation | n/a | Exceptions propagate up to stage 5's try/catch | n/a | n/a |
| 7 | `JokiAutomation\RasPi.Async.cs` | `RasPiExecuteAsync` (20-140) | `Task` | Yes, awaited by stage 6 | n/a | Logs `RASPI_ASYNC_FAIL`/rethrows (113-124) | n/a | n/a |
| 8 | `JokiAutomation\RasPi.cs` | `IRasPiCommandExecutor.ExecuteAsync` (SSH boundary) | `Task<RasPiCommandExecutionResult>` | Yes, awaited by stage 7 | n/a | n/a | n/a | n/a |

## Key Findings

1. **"queued" ≠ "sent"**: The boolean returned by `QueueAudioProfileByNameAsync`/`SendAudioProfileAsync`
   only confirms that the audio job was *appended* to `_audioPipelineTail`. It does **not** mean the
   SSH command was dispatched, received, or verified.
2. **`_audioPipelineTail` is per-`Form1`-instance, not per-request.** It is a single shared
   `Task` field (`Form1.cs:2275`) representing a FIFO continuation chain across *all* audio
   requests ever issued by this `Form1` instance. There is no per-request handle that
   `CommandInterpreterAsync` (or its caller in `Program.cs`) can await before reporting success.
3. **No one awaits `_audioPipelineTail` before the CLI reports success or the process exits.**
   `CommandInterpreterAsync` returns `Success` immediately after the visual/PTZ work in the
   `switch` case completes (Form1.cs:1316), and `ExecuteDispatchedCommandAsync` resolves the
   `TaskCompletionSource<int>` immediately after that (Program.cs:693). Nothing in the chain
   blocks on the background audio continuation.
4. **Exceptions in the background continuation are swallowed from the caller's perspective.**
   `QueueAudioProfileDispatch`'s `catch (Exception ex)` (218-224) only logs; it never surfaces the
   failure to the CLI's exit code or to any awaiting caller, because nobody is awaiting that
   specific continuation.
5. **Form1 is a long-lived singleton** reused across CLI command dispatches. If the queued
   continuation hasn't started running yet (e.g., scheduler contention right after process start,
   first command after idle) and the process/dispatch completes and tears down before the
   continuation is scheduled, the audio job is lost silently. This matches the observed production
   pattern: the first call's audio job is effectively abandoned, and only the second call's chain
   entry — now executing on an already "warmed up" chain/thread — is actually observed by the
   Raspberry Pi.

## Root Cause

`QueueAudioProfileByNameAsync` trades correctness for parallelism: it returns success as soon as
the job is enqueued, and nothing in the call chain (`Form1.CommandInterpreterAsync`,
`Program.ExecuteDispatchedCommandAsync`) ever awaits the specific background continuation that
was created for the current request before declaring the CLI command — and therefore the
process — complete. This makes the audio delivery effectively "fire-and-maybe-forget": under
most timing conditions, the chain's continuation catches up before anyone looks, but there is
no guarantee, and the first invocation after process start demonstrated the failure mode.

## Fix Direction (Phase B)

Track the `Task` created for *each* queued audio request (not just the shared chain tail) and
have `CommandInterpreterAsync` await that specific request's task before returning `Success`,
while still allowing visual/PTZ work to run in parallel with the audio send. This preserves the
`[PIPE_PARALLEL]` behavior (video starts immediately after queuing) while guaranteeing the CLI
cannot report success — and the process cannot exit — before the audio job has actually been
sent, verified, and its outcome (success/exception) observed.
