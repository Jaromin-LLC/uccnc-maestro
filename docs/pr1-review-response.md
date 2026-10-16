# Response to the PR #1 code review

Responses to the external review in [`pr1-review.md`](pr1-review.md) of `fix/debounce-cycle-finish`, checked against the branch and `main`.

The review's guiding principle is adopted: **an uncertain controller state stops Maestro and asks the operator; it never becomes a reason to advance.** A false stop costs a minute. A false completion can ruin a part or crash the machine.

Line numbers cited in the review are approximate (several point a few lines away from the code they quote). The findings were checked against the actual code, not the links.

## Changes made on this branch in response

| Change | Review finding |
| --- | --- |
| Completion requires the program to reach its end line, or operator confirmation | 1, 2, 3, 6 |
| Idle window measured with a `Stopwatch` (fixes the one-poll shortfall) | 4 |
| File-load timeout fails the step instead of pressing Cycle Start | 8 |
| `"Run finished."` no longer overwrites the reason a run stopped | 11 |

### End-of-file check

Before a step's post-ops run, Maestro compares how far the program got with where it ends:

- **Program end:** the first `M2`/`M30` block in the file (comments ignored), or the last line with code if there is neither.
- **Furthest line reached:** the highest line UCCNC reported while the file ran. The highest value is used because UCCNC may rewind to line 0 at `M30`, so the line at the moment of completion can't be trusted.
- **Tolerance:** 5 lines, to cover the final retract/`M5`/`M30` executing between polls and a possible off-by-one between UCCNC's numbering and the file's.

If the furthest line is short of the end, the workflow holds with a dialog: *Did this file really run to completion?* **No (stop the workflow)** is the default button. The step is marked Stopped, not Done, and post-ops and the next file do not run.

This applies to every way a file can be declared finished: the finish callback, the confirmed-idle fallback, and the "finished before start was seen" path at Cycle Start. If the file can't be read or no line was ever observed, the answer is "not confirmed", so the operator is asked.

**To verify on the machine (no tool, no stock):** that a normal run ends without the dialog, that a very short file is handled acceptably, and that stopping a file mid-run and letting the idle window expire produces the dialog.

## Finding-by-finding

### 1. Completion still possible without reaching the end of the g-code — *Agree; addressed*

Correct that "the controller looks idle" is not proof the program finished. Field history (three single dark reads over many hours) makes a two-second false idle unlikely, but "unlikely" isn't the standard for advancing to the next operation. The end-of-file check above is the corroborating program-position evidence the review recommends, and an unconfirmed end now holds the workflow for the operator.

### 2. M0 handling contains an event race — *Agree it's possible; mitigated, needs a machine test*

The likely version of the race is on *entering* the pause rather than resuming it. The loop clears the finish signal on every poll while the M0 LED (229) is on, so a stale finish surviving a resume is unlikely. But if UCCNC raises the finish callback a few milliseconds before it lights LED 229, one poll can see "finished" with the M0 LED still off.

That case is now caught by the end-of-file check: an M0 mid-file is far from the program end, so it produces the operator dialog instead of advancing.

Still open: confirm on the installed UCCNC version whether the finish callback fires at M0 at all, and the order of the LED and callback. The code comment assumes it does; nothing else in the repo proves it.

### 3. Cycle-start detection has an early-success path — *Agree; addressed*

The "finish event before start was seen" path in `LoadAndRunFile` is pre-existing on `main`. It now goes through the same end-of-file check, so a stray finish callback during start can't complete the step on its own. Side effect: a file that runs start to finish within one 50 ms sample is never seen running, so it will ask the operator. That's acceptable for real machining files.

### 4. Debounce uses poll counts, not elapsed time — *Agree; fixed*

The extra work in each poll makes the real wait longer than the count, so the inaccuracy was mostly on the safe side. The real defect was that the count reached 2000 ms one poll early (about 1.8 s of continuous idle). The idle window now uses a `Stopwatch`, started at the first idle sample and reset by any activity.

### 5. Not enough diagnostics — *Agree; partially addressed, more later*

Every completion now logs which signal ended the file, the line, and, when held, the furthest line reached and the program end line. A durable diagnostics log file (LED/motion/M0 state, start/finish events, timing) is worthwhile but deferred to a separate change.

### 6. Reported symptom doesn't fully match the suspected cause — *Agree with the observation; explained*

Correct that a premature `WaitForCycleFinish` return still runs the current step's post-ops and the next step's pre-ops. The likely mechanism is that those ops run *while the original file is still active*. `WaitForIdle` only watches `IsMoving()`, so if the axes are momentarily still, spindle-off and the tool-change moves return immediately without the machine leaving the cut, and the next `Loadfile` + Cycle Start follows. The tool-change prompt is the only hard stop, and only on steps that have one.

The observed incident (a facing file moving from offset 1 to offset 2, then cutting in a different direction like the next operation) fits that: the end of the facing file was declared mid-file, between offsets. The end-of-file check stops exactly that case, because the move between offsets is nowhere near the program end.

### 7. Motion waits have no abort or timeout — *Agree, lower priority*

True that `WaitForIdle` doesn't check the abort flag. An abort calls `UC.Stop()`, which normally ends motion and lets the loop exit, so this is a recovery issue, not a skip-ahead path. Deferred.

### 8. File load can time out and still press Cycle Start — *Agree; fixed*

Confirmed defect. A load still in progress after 30 seconds now shows an error, fails the step, and does not press Cycle Start.

### 9. Workflow start not protected against concurrent requests — *Agree; planned before the companion merges*

Confirmed: `_running` is set inside the new worker thread, after `RunStep`/`RunAll` have already checked it, so two start requests close together can both pass. On `main` this needs a fast double activation. The mobile companion (`feat/mobile-companion`) adds a second, independent source of start requests, so the start must become exclusive (set the running state atomically before the thread launches) before that branch merges.

### 10. Controller events are global, not tied to a run — *Agree in principle, low priority*

The start/finish signals are reset just before Cycle Start, which covers most stray callbacks. With the end-of-file check, a stray finish callback can no longer complete a step by itself. A fuller execution state machine is longer-term work.

### 11. Error reporting loses failure information — *Partially addressed*

Fixed the misleading status: when a step stops or an out-of-order step is declined, the run no longer finishes with `"Run finished."` over the actual reason. Richer exception logging and explicit run outcomes (Completed / Cancelled / Aborted / CompletionUnconfirmed) are deferred.

### Architecture (section 3) — *Agree as a direction*

An explicit execution lifecycle (loading, awaiting start, running, paused, completion pending, completion verified) is the right long-term shape. This branch adds the most important piece of it, "completion verified" as a gate before advancing, without a rewrite.

## Remaining work

| Priority | Item |
| --- | --- |
| Before deploying this branch | Machine tests without a cutter: normal multi-file run, short file, M0 pause/resume, stop mid-file |
| Before merging the companion | Exclusive workflow start (finding 9) |
| Later | Durable diagnostics log (5), abortable motion waits (7), explicit run outcomes and an execution state machine (10, 11, architecture) |
