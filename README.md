# Jaromin CNC Maestro (UCCNC Plugin)

Native [UCCNC](https://www.cncdrive.com/UCCNC.html) plugin with an **Operator dashboard** for guided job execution and an **Admin builder** for configuring workflows, instructions, photos, and videos.

Maestro runs in its own window alongside your existing UCCNC screenset, so you can always switch to Run / Jog for manual control. **No paid screenset is required** — Maestro brings its own tool touch-off (auto zero) and reads its probe/tool-change settings from its own config.

## What it does

- **Operator tab**: pick a project, see all steps in a table (status, tool, diameter, time), run one step or Run All
- **Guided panel**: instruction text, step photo, video launch, CONFIRM button (replaces MessageBox prompts)
- **Automatic per-op sequence**: move to tool-change → tool install confirm → auto zero → load file → cycle start → return to tool-change
- **Gate steps** (flip/register): pause with instructions until operator confirms
- **Admin tab**: CRUD projects/steps, g-code file picker, photo/video attach, pre/post auto-ops, test mode
- Progress persists in `C:\UCCNC\Maestro\state.json` across restarts

## Requirements

Maestro is a UCCNC plugin, so everything is **Windows-only**.

| To build | To run |
|----------|--------|
| Windows + PowerShell 5.1+ | Windows |
| UCCNC installed (provides `C:\UCCNC\Plugininterface.dll`) | UCCNC installed |
| .NET Framework 4.x `csc.exe` (ships with Windows at `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\`) | .NET Framework 4.x (built into Windows) |

No Visual Studio or .NET SDK install is required — the build uses the in-box `csc.exe` compiler.

## Build from source

```powershell
git clone https://github.com/Jaromin-LLC/uccnc-maestro.git
cd uccnc-maestro\plugin
.\build-plugin.ps1
```

`build-plugin.ps1` compiles every `plugin\src\*.cs` file against UCCNC's `Plugininterface.dll` and the standard WinForms assemblies, producing:

```
plugin\build\JarominMaestro.dll
```

## Install

There are two paths. Most users want **option A**.

### A. Packaged installer (recommended for any shop PC)

Build a self-contained release on a machine that can compile, then hand the zip to the target machine:

```powershell
cd installer
.\package-release.ps1          # -> dist\JarominMaestro-<version>.zip
```

The zip contains the prebuilt `JarominMaestro.dll`, the seed `config\projects.json`, a standalone `Install.ps1` / `Install.bat`, and a README. **No build tools, source, or internet are needed on the target.** On the target machine:

1. Unzip the package
2. **Close UCCNC**
3. Double-click **`Install.bat`** — it auto-detects your UCCNC folder and **asks you to confirm it** (press ENTER to accept, or type a different path such as `D:\UCCNC`)
4. Start UCCNC → **Configuration → Plugins** → enable **JarominMaestro**, check **Call startup**
5. Restart UCCNC — the Maestro window opens

The installer copies the DLL into `<UCCNC>\Plugins`, seeds `<UCCNC>\Maestro` (preserving any existing `projects.json` unless `-Force`), and removes a stale `JarominWizard.dll` if present.

For an unattended install (no prompt): `Install.bat -UccncRoot "C:\UCCNC" -Yes`.

### B. Developer deploy (from the repo)

`install-plugin.ps1` is the development loop: it compiles the DLL, deploys it to `C:\UCCNC\Plugins`, and seeds the `Maestro` config folder.

```powershell
cd plugin
.\install-plugin.ps1
```

Then enable the plugin in UCCNC as in step 4–5 above.

One-time machine setup (probing / tool-change positions): [docs/M6_SETUP.md](docs/M6_SETUP.md).

## How the installer is built

```
plugin\src\*.cs            --build-plugin.ps1-->  plugin\build\JarominMaestro.dll
plugin\config\projects.json  ----------+
plugin\build\JarominMaestro.dll  -------+--package-release.ps1-->  dist\JarominMaestro\  -->  dist\JarominMaestro-<ver>.zip
installer\Install.ps1 / .bat  ----------+         (+ Install.ps1, Install.bat, README.txt)
```

`package-release.ps1` runs the build, stages the payload under `dist\`, and zips it. `Install.ps1` (shipped inside the zip) is the only thing that runs on the target — it does no compiling.

## Repository layout

| Path | Purpose |
|------|---------|
| `plugin/src/` | Plugin source (WinForms UI + workflow engine) |
| `plugin/config/projects.json` | Seed workflow config (v3 schema) |
| `plugin/build-plugin.ps1` | Compile the DLL with .NET 4.x `csc` |
| `plugin/install-plugin.ps1` | Developer deploy (build + copy to UCCNC) |
| `installer/Install.ps1` | Standalone target-machine installer (no build) |
| `installer/Install.bat` | Double-click launcher for `Install.ps1` |
| `installer/package-release.ps1` | Build + bundle the distributable zip |
| `docs/M6_SETUP.md` | Tool setter / auto-zero setup |
| `docs/ADDING_PRODUCTS.md` | Adding projects via the Admin tab |
| `docs/DEPLOYMENT.md` | Deployment notes |

## Runtime paths

| Path | Role |
|------|------|
| `C:\UCCNC\Plugins\JarominMaestro.dll` | Plugin binary |
| `C:\UCCNC\Maestro\projects.json` | Workflow definitions |
| `C:\UCCNC\Maestro\state.json` | Step completion / last project |
| `C:\UCCNC\Maestro\GCode\` | G-code files |
| `C:\UCCNC\Maestro\Media\` | Step photos and videos |

> **Note:** the plugin currently reads its data from `C:\UCCNC\Maestro` regardless of where UCCNC is installed. If you install UCCNC on a non-`C:` drive, the installer warns you and you may need to copy the `Maestro` folder to `C:\UCCNC`.

## Example workflow

The shipped `projects.json` contains a single **Sample Project** with two steps that demonstrate the schema:

1. **Example Operation** (`op`) — runs a G-code file with the standard auto-sequence: move to tool change → tool prompt → auto zero → cycle start → return to tool change
2. **Inspect / Flip** (`gate`) — pauses for operator confirmation (e.g. inspect the part or flip the stock)

Replace or extend it with your own projects in the **Admin** tab.

## Adding products

Use the **Admin** tab in the Maestro window, or see [docs/ADDING_PRODUCTS.md](docs/ADDING_PRODUCTS.md).
