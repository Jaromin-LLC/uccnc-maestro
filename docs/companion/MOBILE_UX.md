# Companion App - Mobile UX

A dark, touch-first PWA. Installable to the home screen; runs full-screen. One persistent
top bar (active machine + connection + E-STOP reachable), a bottom tab bar for the screens,
and a Machine Manager reachable from the top bar.

## App shell

- **Top bar**: active machine name + connection dot (green/connected, amber/connecting,
  red/disconnected), tap to open the **machine switcher**. A small persistent E-STOP button
  is always visible in the top bar.
- **Bottom tabs**: Jog · Status · Projects · Machines.
- First launch: empty machine list -> auto-discovery ("Found on your network") + add-by-IP.

> A rendered walkthrough with screenshots lives in [REMOTE_APP.md](REMOTE_APP.md).

## Machine Manager

- List of saved machines: name, host:port, connection state, "controls"/"view-only" badge.
- **Found on your network**: machines discovered automatically on the LAN (via the server's
  UDP beacon / `/api/peers`) appear here with a **Connect** button that prefills the add
  flow; a **Rescan** button refreshes. Machines already saved are hidden from the list.
- **Add by IP address**: enter host/IP (and optional port) and **this device's name** (the
  control-holder label other clients see), then enter the PIN to pair. **Units are read from
  the machine** (its `/api/info` + status), not chosen here. On success the machine is saved
  with its token and friendly name (defaults to the server's `machineName`). (QR pairing is
  still Phase 3.)
- **Edit**: change the phone-side label (units are machine-driven and shown read-only).
- **Remove**: forget the machine + token.
- **Switch/Connect**: selecting a machine becomes the active machine; the app tears down the
  current SSE stream and opens one against the selected machine; all screens re-target it.

## Jog screen

A branded, button-style grid:

- **DRO cluster**: compact 2×2 of X / Y / Z / A work coords (large mono font, axis color
  edge). A ⚠ marks an un-referenced axis.
- **Jog pad**: bold color-coded buttons. The X/Y cross is the dominant control; **Z** is an
  equally-sized column beside it; **A** is a small, de-emphasized secondary control below.
- **Mode selector**: **CONT** (continuous, dead-man — the default) or **STEP** (one increment
  per tap). The step-size selector (units-aware: 0.01/0.1/1/10 mm or 0.001/0.01/0.1/1 in)
  dims while in continuous mode.
- **Jog feed** slider (mm/min or in/min) sets the speed for **both** step and continuous jog,
  alongside a **Spindle** slider + ON/OFF toggle (hold to start, tap to stop; disabled during a job).
- **Action chips**: Home All, Auto-Zero, Park — all hold-to-confirm.
- Persistent bottom bars: **FEED HOLD** (amber), **RESUME**, and **E-STOP** (red).

> Per-axis "Zero" and "Go To Zero" were intentionally removed from the phone: zeroing a
> single axis is meaningless without offsets to a phone user, and a one-tap rapid to zero is
> unsafe to expose remotely. Zeroing happens via Auto-Zero / the Maestro workflow.

## Status screen (job progress)

- Current project + active step label, machine state (Idle / Cycle running / Feed hold / Alarm).
- Progress bar: g-code line `current/total`; falls back to time-based when line count unknown.
- Big clock: estimated **time remaining** (from measured `lastRunSeconds`), elapsed, and
  computed **ETA clock time** ("done ~2:45 PM").
- Live feed rate, spindle RPM, feed/spindle overrides.
- **Prompt card**: when a tool-change / gate prompt is waiting, show its instructions + photo
  and **CONFIRM / CANCEL** buttons so the operator can respond from the phone.

## Projects screen

- Project picker (select sets the active project on that machine).
- Step list: status chip (pending/running/done/stopped), label, tool, last runtime.
- Controls: **Run All**, **Run From...**, per-step **Run**, **Reset**, **Abort**
  (hold-to-confirm on Run/Reset/Abort).

## PWA specifics

- `manifest.webmanifest` (name, icons, `display: standalone`, dark theme color).
- Service worker caches the app shell for instant load and offline UI (data still needs LAN).
- Service worker is **network-first**: always serves the latest UI from the shop PC when
  reachable, falling back to cache only when offline.
- Local storage: machine list + tokens (with per-machine units) + UI prefs (jog mode,
  step/feed per unit system, spindle rpm).
- Wake lock during an active job (best-effort) so the screen can stay on while monitoring.
- Vanilla HTML/CSS/JS (no build step) so assets embed directly into the DLL and the test host
  can serve them from `app/` during development.
