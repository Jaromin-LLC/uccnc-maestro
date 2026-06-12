Jaromin CNC Maestro - UCCNC Plugin
==================================

This package installs the Maestro plugin into your UCCNC installation.
No build tools or internet connection are required.

WHAT'S IN HERE
--------------
  JarominMaestro.dll   The plugin (copied into <UCCNC>\Plugins)
  config\projects.json Seed workflow config (copied into <UCCNC>\Maestro)
  config\tools.json    Seed tool library (copied into <UCCNC>\Maestro)
  Install.bat          Double-click installer (opens the setup window)
  Install.ps1          The actual installer logic
  README.txt           This file

INSTALL
-------
  1. Close UCCNC if it is running.
  2. Double-click Install.bat. A setup window opens.
  3. Confirm the UCCNC folder (auto-detected; click Browse to change it).
  4. Choose what happens to workflow data (projects.json / tools.json):
       - Keep existing files if present (recommended, default)
       - Overwrite with the bundled seed files (existing projects/tools are lost)
  5. Click Install and review the log.
  6. Start UCCNC.
  7. Configuration -> Plugins -> enable "JarominMaestro" and check "Call startup".
  8. Restart UCCNC. The Maestro window opens automatically.

UNATTENDED INSTALL (no window)
------------------------------
  Open a terminal in this folder and run:
      Install.bat -UccncRoot "D:\Path\To\UCCNC" -Yes
  Add -OverwriteConfigs to replace existing projects.json / tools.json.

NOTES
-----
  - With the default choice, existing projects.json / tools.json in
    <UCCNC>\Maestro are always preserved.
  - If an older "JarominWizard.dll" is present it is removed automatically.
  - To uninstall: disable the plugin in UCCNC, then delete
    <UCCNC>\Plugins\JarominMaestro.dll. Your workflow data in <UCCNC>\Maestro
    is left untouched.
