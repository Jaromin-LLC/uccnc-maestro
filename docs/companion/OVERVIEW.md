# Companion App - Overview

A local-network companion that lets a phone (iOS/Android) monitor and control one or
more CNC machines running UCCNC + the Maestro plugin.

## Goals

- Monitor live machine status (DROs, cycle state, feed/spindle) from a phone on the shop WiFi.
- See the currently running Maestro project: which step, progress bar, time remaining, ETA.
- Control the machine: jog (step/continuous), home, park, auto-zero, manual spindle,
  run/pause/stop jobs, feed hold, and E-STOP - with safety guards appropriate to remote control.
- Manage multiple machines from one phone (add / remove / rename / connect / switch), each
  with its own units (mm or SAE/inch).

> **End-user guide with screenshots + setup:** [REMOTE_APP.md](REMOTE_APP.md).

## Non-goals

- **No cloud.** Everything stays on the LAN. The phone talks directly to each shop PC.
- No multi-user accounts / RBAC. A single shared pairing PIN per machine is enough for a shop.
- No remote desktop / screen mirroring of UCCNC. We expose a purpose-built API + UI.

## Topology

Each machine is a separate shop PC running its own UCCNC + Maestro plugin, and therefore
its own embedded companion server. The phone (client) owns the list of machines and
switches between them; there is no server-to-server coordination.

```
Phone PWA  ---- LAN HTTP + SSE ---->  Shop PC A (UccncMaestro.dll : MaestroServer)
           ....saved, switchable....  Shop PC B (UccncMaestro.dll : MaestroServer)
```

## Components

| Component | Where | Responsibility |
|-----------|-------|----------------|
| `MaestroServer` | plugin (C#) | HttpListener: REST + SSE, auth, serves the PWA |
| `IMaestroController` | plugin (C#) | Abstraction the server depends on (status + control + workflow) |
| `PluginMaestroController` | plugin (C#) | Real implementation over `Plugininterface.Entry` + `WorkflowEngine` |
| `SimulatedMaestroController` | plugin (C#) | Self-contained simulation for **local testing** (no UCCNC) |
| PWA client | `app/` | Machine Manager + Jog / Status / Projects screens |
| Test host | `tools/testhost` | Console app that runs `MaestroServer` + simulator on `localhost` |

## Safety model (summary; see SECURITY.md)

- Token pairing per machine (PIN shown in the Maestro window).
- Single **active controller** lock; others connect read-only.
- E-STOP and Feed Hold are always one tap.
- Destructive actions (run, home, park, auto-zero, spindle on) use hold-to-confirm.
- Continuous jog is dead-man (motion stops on touch release / app blur / disconnect) and
  watchdog-guarded; continuous is the default mode, step is selectable.
- LAN access requires a one-time `.\make.ps1 net-setup` (URL ACL + firewall) on the PC.

## Local testing on localhost

`tools/testhost` starts the server with `SimulatedMaestroController`, serving the PWA from
the `app/` folder. Open `http://localhost:8723` (default), pair with the printed PIN, and
the simulated machine responds to jog/zero/home/park and runs simulated jobs with progress.
This lets the whole UI and API be exercised without UCCNC or a real machine.
