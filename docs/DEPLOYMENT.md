# Deployment

```powershell
cd plugin
.\build-plugin.ps1
.\install-plugin.ps1 -ProfileName Default   # or your production profile
```

1. **Close UCCNC** before running install
2. After install: **Configuration → Plugins** → enable **JarominMaestro**, check **Call startup**
3. Restart UCCNC

M6 / probing setup: [M6_SETUP.md](M6_SETUP.md).

The installer leaves your active UCCNC screenset unchanged. It removes legacy Jaromin screenset artifacts if present (the old Jaromin screenset, tab images, and macros M20797–M20886).
