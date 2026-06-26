# (uc)CNC Maestro - an automation plugin for UCCNC

## Why (uc)CNC Maestro?

I built this for a small shop that runs the same handful of products repeatedly — dozens of batches at a time. I know the operations by heart, but mistakes still happen: run a step out of order, forget a Z touch-off, or skip a manual step between cuts. Less experienced employees were working from a spreadsheet just to remember what came next.

There are also steps the machine cannot run — flipping a part onto alignment pins, gluing in pieces, inspecting before continuing. Those need clear instructions, not just a file name in a folder.

I do not have an ATC, so every tool change is manual. I could bake stops into one giant G-code file, but the same individual operations show up across several different products. I would rather maintain **separate CAM files per operation** and mix and match them per product. Sometimes I need to **run a single file on its own** to fix a problem without replaying an entire job.

**(uc)CNC Maestro** is a native [UCCNC](https://www.cncdrive.com/UCCNC.html) plugin that ties those pieces together:

- **One G-code file per operation**, configured into a project with ordered **pre-ops** and **post-ops** (move to tool change, auto zero, park, and so on) around each cut
- **Gate steps** for manual work — flip, register, glue, inspect — with instructions the operator must confirm before continuing
- **Photos and videos** on each step so newer operators can see what “done” looks like without asking every time
- **Per-step runtime history** and a **large progress view** (countdown + G-code progress bar) readable from across the shop

Maestro runs in its own window alongside your existing UCCNC screenset, so you can always switch to Run / Jog for manual control. **No paid screenset is required** — it includes tool touch-off (auto zero) and stores probe and tool-change settings in its own config.

## What it does

### Operator tab

- Pick a project and see all steps in a table (status, operation, tool storage label, description, runtime)
- **RUN** / **CHANGE TOOL** / **BEGIN** per step, plus **RUN ALL**, **RUN FROM…** (override recovery), **RESET**, and **ABORT**
- **Guided overlay** for tool install and gate steps: instructions, photo, optional video, CONFIRM / CANCEL
- **Progress overlay** during cuts: project/step/file, countdown estimate, and G-code line progress; tap the running step to reopen if closed
- Tap an **Operation** cell (photo/video icons) to open step media in a modal — photos and **in-app video playback**
- **Auto-reset** when the final step completes (prompt to reset for another run)
- Progress persists in `C:\UCCNC\Maestro\state.json` across restarts; per-step runtimes are kept across reset

### Automatic sequences (configurable)

Each operation step has an **ordered** pre-ops and post-ops list (drag-and-drop in Admin). Typical defaults:

- **Pre:** move to tool-change → tool install confirm → auto zero (probe)
- **Cut:** load G-code file → cycle start → wait for finish
- **Post:** spindle off → move to tool-change (customize per step)

Available ops include park (G28/G30/custom), go to work zero, spindle off, and **Custom MDI** (per-instance commands). Ops run in the order shown — including multiple copies of the same op.

**Gate steps** (flip/inspect/register) run their configured **pre-ops**, pause for operator confirmation, then run **post-ops** (e.g. park before the prompt, move home after).

### Admin tab

- CRUD projects and steps; tool library with storage labels and optional edge-probe settings
- G-code file picker; step photo/video attach; ordered pre/post ops editor
- Global and per-project machine settings (probe plate, tool-change position, custom park)
- **Test mode** (demo only — skips probing and machine moves)

## Requirements

Maestro is a UCCNC plugin, so everything is **Windows-only**.

| To build | To run |
|----------|--------|
| Windows + PowerShell 5.1+ | Windows |
| UCCNC installed (provides `C:\UCCNC\Plugininterface.dll`) | UCCNC installed |
| .NET Framework 4.x `csc.exe` (ships with Windows at `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\`) | .NET Framework 4.x (built into Windows) |

No Visual Studio or .NET SDK install is required — the build uses the in-box `csc.exe` compiler.

## Build, install, package: `make.ps1`

Everything is driven by a single make-style script at the repo root:

```powershell
git clone https://github.com/Jaromin-LLC/uccnc-maestro.git
cd uccnc-maestro
.\make.ps1            # build   - compile plugin\src\*.cs -> plugin\build\UccncMaestro.dll
.\make.ps1 install    # build + deploy DLL to C:\UCCNC + seed configs (first install only)
.\make.ps1 package    # build + create dist\UccncMaestro-<version>.zip for shop PCs
.\make.ps1 clean      # delete plugin\build and dist
```

(`make.bat` is a double-click/cmd wrapper for the same targets: `make install`.)

The build compiles every `plugin\src\*.cs` file against UCCNC's `Plugininterface.dll` and the standard WinForms assemblies using the in-box .NET 4.x `csc.exe`.

## Install

There are two paths. Most users want **option A**.

### A. Packaged installer (recommended for any shop PC)

Build a self-contained release on a machine that can compile, then hand the zip to the target machine:

```powershell
.\make.ps1 package          # -> dist\UccncMaestro-<version>.zip
```

The zip contains the prebuilt `UccncMaestro.dll`, the seed `config\projects.json` / `config\tools.json`, the graphical `Install.ps1` / `Install.bat`, and a README. **No build tools, source, or internet are needed on the target.** On the target machine:

1. Unzip the package
2. **Close UCCNC**
3. Double-click **`Install.bat`** — a setup window opens: it auto-detects your UCCNC folder (Browse to change it) and lets you choose whether existing workflow data (`projects.json` / `tools.json`) is **kept (default)** or **overwritten** with the bundled seeds
4. Click **Install**
5. Start UCCNC → **Configuration → Plugins** → enable **UccncMaestro**, check **Call startup**
6. Restart UCCNC — the Maestro window opens

The installer copies the DLL into `<UCCNC>\Plugins`.

For an unattended install (no window): `Install.bat -UccncRoot "C:\UCCNC" -Yes` (add `-OverwriteConfigs` to replace existing workflow data).

### B. Developer deploy (from the repo)

```powershell
.\make.ps1 install
```

Compiles the DLL, deploys it to `C:\UCCNC\Plugins`, and seeds the `Maestro` config folder on a first install (existing `projects.json` / `tools.json` are never overwritten). Then enable the plugin in UCCNC as in step 5–6 above.

One-time machine setup (probing / tool-change positions): [docs/M6_SETUP.md](docs/M6_SETUP.md).

## How the installer is built

```
plugin\src\*.cs              --make.ps1 build-->  plugin\build\UccncMaestro.dll
plugin\config\*.json  ------------+
plugin\build\UccncMaestro.dll  --+--make.ps1 package-->  dist\UccncMaestro\  -->  dist\UccncMaestro-<ver>.zip
installer\Install.ps1 / .bat  -----+      (+ Install.ps1, Install.bat, README.txt)
```

`make.ps1 package` runs the build, stages the payload under `dist\`, and zips it. `Install.ps1` (shipped inside the zip) is the only thing that runs on the target — it does no compiling.

## Repository layout

| Path | Purpose |
|------|---------|
| `make.ps1` / `make.bat` | Single entry point: `build`, `install`, `package`, `clean` |
| `plugin/src/` | Plugin source (WinForms UI + workflow engine) |
| `plugin/config/projects.json` | Seed workflow config |
| `plugin/config/tools.json` | Seed tool library |
| `installer/Install.ps1` | Graphical target-machine installer (shipped in the zip, no build) |
| `installer/Install.bat` | Double-click launcher for `Install.ps1` |
| `docs/M6_SETUP.md` | Tool setter / auto-zero setup |
| `docs/DEPLOYMENT.md` | Deployment notes |

## Runtime paths

| Path | Role |
|------|------|
| `C:\UCCNC\Plugins\UccncMaestro.dll` | Plugin binary |
| `C:\UCCNC\Maestro\projects.json` | Workflow definitions |
| `C:\UCCNC\Maestro\tools.json` | Tool library |
| `C:\UCCNC\Maestro\state.json` | Step completion / last project / runtimes |
| `C:\UCCNC\Maestro\Media\` | Step photos, videos, and tool images |

G-code files are referenced by full path in each step and can live anywhere (e.g. your CAM output folder); Maestro does not copy or move them.

> **Note:** the plugin currently reads its data from `C:\UCCNC\Maestro` regardless of where UCCNC is installed. If you install UCCNC on a non-`C:` drive, the installer warns you and you may need to copy the `Maestro` folder to `C:\UCCNC`.

## Example workflow

The shipped `projects.json` includes three guitar-shop examples:

| Project | Steps (summary) |
|---------|-----------------|
| **S-Type Body** | Rough top → flip → back cavities → belly cut (1/2\" ballnose) → detail → inspect |
| **T-Type Body** | Top profile → flip → back routes → ferrule holes → inspect |
| **C-Shape Neck** | Rough profile → flip → truss rod / back → ballnose finish → inspect |

Each **operation** step points at a G-code file under `C:\UCCNC\Maestro\GCode\<projectId>\` (paths you replace with your CAM output). **Gate** steps pause for flip, register, or inspect. Edit or replace these in the **Admin** tab.

## Adding products

Use the **Admin** tab in the Maestro window: add a project, add operation and gate steps, point each operation at its G-code file, and drag the pre/post auto-ops (tool change, tool prompt, auto-zero, spindle off, park, custom MDI) into the order you want them to run.

## License

Copyright (C) 2026 Jaromin LLC

This program is free software: you can redistribute it and/or modify it under
the terms of the **GNU General Public License v3.0** as published by the Free
Software Foundation. See [LICENSE](LICENSE) for the full text.

This program is distributed in the hope that it will be useful, but WITHOUT ANY
WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A
PARTICULAR PURPOSE. See the GNU General Public License for more details.

> UCCNC and the UCCNC plugin interface are products of CNCdrive. This project is
> an independent, unofficial plugin and is not affiliated with or endorsed by CNCdrive.
