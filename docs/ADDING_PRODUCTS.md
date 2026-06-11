# Adding Products and Steps

Jaromin Maestro is edited from the **Admin** tab in the plugin window. Changes are saved to `C:\UCCNC\Maestro\projects.json`.

## Add a new project

1. Open UCCNC with the Jaromin Maestro plugin enabled
2. Switch to the **Admin** tab
3. Click **Add** in the project list
4. Set **Project ID** (unique, no spaces), **Project Name**, and **Description**
5. Add steps with **Add Step**
6. Click **Save All**

## Step types

| Type | Purpose |
|------|---------|
| `op` | Runs a G-code file with tool prompt, auto-zero, and cycle start |
| `gate` | Operator confirmation only (flip, register, inspect, etc.) |

## Operation step fields

- **G-code File**: path relative to G-code root (e.g. `Sample/example.nc`) or use **Browse**
- **Instructions**: shown in the guided panel before the operator confirms
- **Tool # / Type / Dia. / Desc / RPM**: shown in the tool install prompt
- **Minutes**: approximate run time (display only)
- **Photo / Video**: optional media copied into `C:\UCCNC\Maestro\Media\<projectId>\`

## Auto-operations (pre/post)

Each operation step can configure **Pre Ops** and **Post Ops**:

| ID | Action |
|----|--------|
| `moveToolChange` | Move to tool-change position (safe Z first) |
| `toolPrompt` | Show guided install/confirm panel |
| `autoZero` | Two-pass fixed-plate probe (every tool change) |
| `spindleOff` | M5 |
| `gotoWorkZero` | G0 X0 Y0 |
| `customMdi` | Run custom MDI from step (future field) |

Default for `op` steps: pre = moveToolChange, toolPrompt, autoZero; post = spindleOff, moveToolChange.

## Machine settings (Admin tab)

- **G-code Root**: `C:\UCCNC\Maestro\GCode`
- **Media Root**: `C:\UCCNC\Maestro\Media`
- **Test mode**: skips probing checks (demo only — never use when cutting)
- **Use machine tool-change fields**: read TC position from UCCNC probing screen fields

## Seed config in repo

Edit [plugin/config/projects.json](../plugin/config/projects.json) for version-controlled defaults, then reinstall:

```powershell
cd plugin
.\install-plugin.ps1 -ProfileName Default
```

Or edit live in Admin and **Save All** — no reinstall needed for workflow changes.
