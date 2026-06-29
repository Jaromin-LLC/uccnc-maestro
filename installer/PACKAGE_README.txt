(uc)CNC Maestro - UCCNC Plugin
==============================

This package installs the Maestro plugin into your UCCNC installation.
No build tools or internet connection are required.

WHAT'S IN HERE
--------------
  UccncMaestro.dll     The plugin (copied into <UCCNC>\Plugins)
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
  5. Mobile companion (phone/tablet access):
       - Leave "Allow phones on the Wi-Fi to connect" checked (default) to let
         phones reach the companion. This reserves the HTTP port and opens an
         inbound firewall rule, so Windows will show a UAC prompt - click Yes.
       - Adjust the port only if you changed it in the plugin (default 8723).
  6. Click Install and review the log.
  7. Start UCCNC.
  8. Configuration -> Plugins -> enable "UccncMaestro" and check "Call startup".
  9. Restart UCCNC. The Maestro window opens automatically.

UNATTENDED INSTALL (no window)
------------------------------
  Open a terminal in this folder and run:
      Install.bat -UccncRoot "D:\Path\To\UCCNC" -Yes
  Add -OverwriteConfigs to replace existing projects.json / tools.json.
  Add -EnableLan to open phone/tablet access (optionally -Port 8723).
  Tip: run an elevated terminal so -EnableLan finishes without a UAC prompt.

MOBILE COMPANION OVER WI-FI
---------------------------
  - The "Allow phones on the Wi-Fi to connect" option reserves
    http://+:<port>/ for the companion and opens that inbound TCP port for
    ALL network profiles (incl. Public). It needs administrator rights (UAC).
  - It also opens an inbound UDP rule on the same port for LAN auto-discovery
    (machines find each other; the app shows them under "Found on your
    network"). Discovery is optional - without it you add machines by IP.
  - On the phone, open the address WITH the http:// prefix, e.g.
    http://<this-PC-IP>:<port>/  (a bare address can make the browser try
    HTTPS, which the server does not speak, so the page just hangs).
  - To open access later, re-run the installer (or, from the repo, run
    .\make.ps1 net-setup).

MULTIPLE MACHINES
-----------------
  - Run the installer on each shop PC. Once two or more are powered on with
    the Wi-Fi option enabled, each one appears on the others under the app's
    Machines tab -> "Found on your network". Tap Connect to pair.

TROUBLESHOOTING THE PHONE CONNECTION
------------------------------------
  - After installing, RESTART UCCNC so the plugin re-binds to the LAN.
  - "Page hangs" almost always means: (a) the browser auto-upgraded to HTTPS -
    type the address WITH http:// (e.g. http://192.168.1.50:8723/), or (b) the
    router has "AP isolation" / guest separation, or the phone is on a
    different SSID. Put both devices on the same Wi-Fi.
  - "Bad Request - Invalid Hostname" means the server only bound to localhost;
    re-run this installer (with the Wi-Fi option checked) and restart UCCNC.

NOTES
-----
  - With the default choice, existing projects.json / tools.json in
    <UCCNC>\Maestro are always preserved.
  - If an older "JarominMaestro.dll" or "JarominWizard.dll" is present it is
    removed automatically.
  - To uninstall: disable the plugin in UCCNC, then delete
    <UCCNC>\Plugins\UccncMaestro.dll. Your workflow data in <UCCNC>\Maestro
    is left untouched.
