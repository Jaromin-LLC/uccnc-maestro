Jaromin CNC Maestro - UCCNC Plugin
==================================

This package installs the Maestro plugin into your UCCNC installation.
No build tools or internet connection are required.

WHAT'S IN HERE
--------------
  JarominMaestro.dll   The plugin (copied into <UCCNC>\Plugins)
  config\projects.json Seed workflow config (copied into <UCCNC>\Maestro)
  Install.bat          Double-click installer (launches Install.ps1)
  Install.ps1          The actual installer logic
  README.txt           This file

INSTALL
-------
  1. Close UCCNC if it is running.
  2. Double-click Install.bat.
  3. The installer shows the UCCNC folder it found and asks you to confirm.
     Press ENTER to accept, or type a different path (e.g. D:\UCCNC).
  4. Start UCCNC.
  5. Configuration -> Plugins -> enable "JarominMaestro" and check "Call startup".
  6. Restart UCCNC. The Maestro window opens automatically.

UNATTENDED INSTALL (no prompt)
------------------------------
  Open a terminal in this folder and run:
      Install.bat -UccncRoot "D:\Path\To\UCCNC" -Yes

NOTES
-----
  - An existing C:\UCCNC\Maestro\projects.json is preserved. To overwrite it
    with the bundled seed config, run:  Install.bat -Force
  - If an older "JarominWizard.dll" is present it is removed automatically.
  - To uninstall: disable the plugin in UCCNC, then delete
    <UCCNC>\Plugins\JarominMaestro.dll. Your workflow data in <UCCNC>\Maestro
    is left untouched.
