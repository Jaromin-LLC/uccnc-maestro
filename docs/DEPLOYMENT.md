# Deployment

```powershell
.\make.ps1 install     # builds, deploys the DLL, seeds configs on first install
```

Existing `projects.json` / `tools.json` in `C:\UCCNC\Maestro` are never overwritten by `make.ps1 install`. To hand off to a shop PC instead, run `.\make.ps1 package` and use the graphical installer in the resulting zip.

1. **Close UCCNC** before running install
2. After install: **Configuration → Plugins** → enable **UccncMaestro**, check **Call startup**
3. Restart UCCNC

M6 / probing setup: [M6_SETUP.md](M6_SETUP.md).

The installer leaves your active UCCNC screenset unchanged. It removes legacy screenset artifacts from earlier builds if present (the old screenset, tab images, and macros M20797–M20886).
