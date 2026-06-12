# Tool Touch-Off Setup (Jaromin CNC Maestro)

Maestro runs its own two-pass tool touch-off (auto zero) **automatically before
every operation** — the operator never runs it manually and cannot skip it. The
routine is built into the plugin (a `G31` probe against your fixed tool setter);
**no paid screenset or screenset macro is required.**

## Per-operation flow (built into every RUN / RUN ALL step)

1. Spindle stops, machine moves to the **tool change position** (safe Z first)
2. Operator is prompted with the exact bit (diameter, type, description)
   and confirms it is installed and tight
3. Machine **automatically** moves to the tool setter and runs the two-pass
   probe (auto zero) — work Z is re-zeroed for the new tool
4. The step's G-code file is loaded and cycle start is issued
5. When the file finishes, the spindle stops and the machine **returns to the
   tool change position**, ready for the next tool swap

Every tool change gets a fresh touch-off.

## One-time setup (Admin tab → machine settings)

Open the **Admin** tab in the Maestro window. Below the project/step editor you
will find **Global machine settings** (defaults for all projects) and, when a
project is selected, an optional **Project-specific overrides** section.

Fill in the global values (machine coordinates), then **Save All**. Hover any
field for a short description of what it does.

| Setting | Purpose |
|---------|---------|
| Plate X / Plate Y | Tool setter location (machine coords) |
| Probe dist | **Max** Z travel for each G31 pass (not plate thickness) |
| Retract dist | Retract after the fast pass |
| Fast feed / Slow feed | First (fast) and second (slow) pass feedrates |
| Plate rapid Z | Optional rapid-down height before probing |
| Plate offset from Z zero | Height of the plate top above work Z0; equals puck thickness when the puck sits on the Z0 surface |
| Tool change X / Y / Z | Where the machine parks for tool swaps |
| Safe Z | Retract height for rapids |
| Retract to Safe Z before TC / probe moves | Use Safe Z for tool-change moves |
| Rapid to Plate rapid Z before probing | Enable the optional rapid-down |

These values are stored in `C:\UCCNC\Maestro\projects.json` under `settings`
and travel with the plugin — they do **not** depend on any particular screenset.

### Per-project overrides (rare)

Select a project, then tick **Override global machine settings for this project**
to reveal a second copy of the same fields. Values are saved on that project
only (`overrideMachineSettings`, `probe`, `toolChangePos`, `useSafeZForTc` in
`projects.json`). When the box is unchecked, global defaults apply at runtime.

Maestro pre-flight refuses to start if the plate location, probe distance or
probe feed are unset (all zero), so a fresh config cannot probe at 0,0.

**Probe dist** is the maximum distance each G31 move travels downward from the
current Z before stopping. Set it slightly larger than the gap from the rapid
height to the tool setter (often 10–25 mm). If the probe does not trigger within
that distance, UCCNC aborts the touch-off and Maestro stops — it will not set
work zero or continue the step.

If the probe input is noisy or stuck active, or if a failed probe is not caught,
the next `G0 Z` move can command Z=0 and cause a dangerous plunge. The plugin
validates UCCNC `#5060` (probe success), LED 244 (ProbedOK), and `#5063`
(touch Z) before any post-probe moves.

### Optional: read values from your screenset instead

If you already run a UCCNC screenset whose probing settings page exposes these
values, tick **"Use screenset probing/tool-change fields"** in the Admin tab.
Maestro then reads the plate location, distances, feeds and tool-change position
from those screenset fields instead of the values above. Leave it **off** to keep
Maestro fully self-contained.

## M6 mode (for T# M6 lines inside G-code files)

1. Open **Configuration → General settings → Function settings**
2. Set **On tool change code (M6)** to **Run the tool change macro (M6)**

Maestro sets the current tool number *before* starting each file, so the
leading `T# M6` inside the file no-ops instead of re-prompting.

## Test / Demo mode (dev/demo machines only)

Tap the **DEMO MODE** button in the top-right of the Operator dashboard
(it turns orange when ON). It is also available in the **Admin** tab as
**Test mode (skip probing checks)**; both persist to `projects.json`.

In demo mode Maestro runs the full guided flow **without touching the machine**:

- preflight homing/probe-config checks are skipped
- move-to-tool-change and go-to-work-zero moves are skipped
- the fixed-plate touch-off (auto zero) is skipped
- the g-code file run is simulated with a short delay (no file required)

This lets you walk the entire operator experience on a machine with no probe,
no homing, and no g-code files. Never enable it on a cutting machine.

## Verify on production

```text
[ ] Probing / Tool Change settings filled in the Admin tab (plate X/Y, distances, feeds, TC position)
[ ] M6 mode = Run tool change macro
[ ] RUN on step 1: moves to TC position, prompts for bit, probes, cuts
[ ] After the file ends, machine returns to the TC position
[ ] Next RUN prompts the next bit at the TC position
[ ] Test mode in Admin tab is OFF
```
