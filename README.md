# (uc)CNC Maestro (UCCNC Plugin)

Native [UCCNC](https://www.cncdrive.com/UCCNC.html) plugin with an **Operator dashboard** for guided job execution and an **Admin builder** for configuring workflows, instructions, photos, and videos.

Maestro runs in its own window alongside your existing UCCNC screenset, so you can always switch to Run / Jog for manual control. **No paid screenset is required** — Maestro brings its own tool touch-off (auto zero) and reads its probe/tool-change settings from its own config.

## What it does

- **Operator tab**: pick a project, see all steps in a table (status, tool, diameter, time), run one step or Run All
- **Guided panel**: instruction text, step photo, video launch, CONFIRM button (replaces MessageBox prompts)
- **Automatic per-op sequence**: move to tool-change → tool install confirm → auto zero → load file → cycle start → return to tool-change
- **Gate steps** (flip/register): pause with instructions until operator confirms
- **Admin tab**: CRUD projects/steps, g-code file picker, photo/video attach, pre/post auto-ops, test mode
- **Mobile companion**: monitor and control the machine from a phone on the shop Wi‑Fi (jog, auto-zero, park, spindle, run jobs, confirm prompts, E‑STOP) — a zero-install PWA served by the plugin. See [docs/companion/REMOTE_APP.md](docs/companion/REMOTE_APP.md).
- Progress persists in `C:\UCCNC\Maestro\state.json` across restarts

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
.\make.ps1 net-setup  # one-time: open URL ACL + firewall so phones can reach the companion
.\make.ps1 clean      # delete plugin\build and dist
.\make.ps1 testhost   # run the companion server + simulator on localhost (no UCCNC)
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

The installer copies the DLL into `<UCCNC>\Plugins` and removes a pre-rename `JarominMaestro.dll` (or the older `JarominWizard.dll`) if present.

For an unattended install (no window): `Install.bat -UccncRoot "C:\UCCNC" -Yes` (add `-OverwriteConfigs` to replace existing workflow data).

### B. Developer deploy (from the repo)

```powershell
.\make.ps1 install
```

Compiles the DLL, deploys it to `C:\UCCNC\Plugins`, and seeds the `Maestro` config folder on a first install (existing `projects.json` / `tools.json` are never overwritten). Then enable the plugin in UCCNC as in step 5–6 above.

One-time machine setup (probing / tool-change positions): [docs/M6_SETUP.md](docs/M6_SETUP.md).

To use the mobile companion over Wi‑Fi, run `.\make.ps1 net-setup` once (opens the URL ACL +
firewall), then follow [docs/companion/REMOTE_APP.md](docs/companion/REMOTE_APP.md).

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
| `make.ps1` / `make.bat` | Single entry point: `build`, `install`, `package`, `net-setup`, `testhost`, `clean` |
| `plugin/src/` | Plugin source (WinForms UI + workflow engine) |
| `plugin/src/Companion/` | Mobile companion server (HTTP/SSE) + controllers |
| `app/` | Companion PWA (HTML/CSS/JS), embedded into the DLL at build time |
| `tools/testhost/` | Console host: companion server + simulator on localhost |
| `plugin/config/projects.json` | Seed workflow config (v3 schema) |
| `plugin/config/tools.json` | Seed tool library |
| `installer/Install.ps1` | Graphical target-machine installer (shipped in the zip, no build) |
| `installer/Install.bat` | Double-click launcher for `Install.ps1` |
| `docs/M6_SETUP.md` | Tool setter / auto-zero setup |
| `docs/ADDING_PRODUCTS.md` | Adding projects via the Admin tab |
| `docs/DEPLOYMENT.md` | Deployment notes |
| `docs/companion/` | Mobile companion: [user guide](docs/companion/REMOTE_APP.md), overview, API, security, UX, features |

## Runtime paths

| Path | Role |
|------|------|
| `C:\UCCNC\Plugins\UccncMaestro.dll` | Plugin binary |
| `C:\UCCNC\Maestro\projects.json` | Workflow definitions |
| `C:\UCCNC\Maestro\state.json` | Step completion / last project |
| `C:\UCCNC\Maestro\Media\` | Step photos and videos |

G-code files are referenced by full path in each step and can live anywhere (e.g. your CAM output folder); Maestro does not copy or move them.

> **Note:** the plugin currently reads its data from `C:\UCCNC\Maestro` regardless of where UCCNC is installed. If you install UCCNC on a non-`C:` drive, the installer warns you and you may need to copy the `Maestro` folder to `C:\UCCNC`.

## Example workflow

The shipped `projects.json` demonstrates the two step types:

1. **Operation** (`op`) — runs a G-code file with the standard auto-sequence: move to tool change → tool prompt → auto zero → cycle start → return to tool change
2. **Gate** (`gate`) — pauses for operator confirmation (e.g. inspect the part or flip the stock onto registration pins)

Replace or extend the shipped projects in the **Admin** tab.

## Adding products

Use the **Admin** tab in the Maestro window, or see [docs/ADDING_PRODUCTS.md](docs/ADDING_PRODUCTS.md).
