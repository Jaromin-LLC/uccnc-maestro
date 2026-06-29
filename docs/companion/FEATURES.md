# Companion App - Feature Backlog

Researched from what operators want in CNC remote-monitoring/control apps (UCCNC pendants,
Carbide Motion, gSender, Mach mobile pendants, OpenBuilds CONTROL, FluidNC WebUI, etc.),
scoped to a local-network shop tool.

## MVP (this effort)

- [x] Multi-machine: add / remove / rename / switch. Units (mm / SAE) are configured on the
      machine (Mobile tab) and reported via the API, so phones adopt them automatically.
- [x] LAN auto-discovery: UDP beacon + `/api/peers`; "Found on your network" tap-to-connect
      list with add-by-IP fallback.
- [x] Live status: DROs (X/Y/Z/A work + machine), homed flags, cycle/feed-hold/alarm state.
- [x] Jog: continuous (default, dead-man) + step, selectable mode; units-aware step size + feed.
      The JOG FEED slider sets the speed for both modes (continuous uses UCCNC jog feedrate field 913).
- [x] Home all, Park (G28/G30/custom), Auto-zero, manual spindle on/off + RPM.
- [x] Feed hold / resume / stop / E-STOP (stop + E-STOP always available).
- [x] Maestro projects: pick, Run All / Run Step / Reset / Abort.
- [x] Job progress: line-based progress bar, time remaining, elapsed, ETA clock time.
- [x] Remote confirm/cancel of tool-change & gate prompts (with photo + instructions).
- [x] Pairing (PIN -> token), active-controller lock, watchdog.
- [x] Local testability via simulator + test host.
- [x] LAN setup helper (`make.ps1 net-setup`) + build id surfaced in `/api/info`.

## High-value extras (Phase 3, prioritized)

1. **Live camera view** - embed a shop camera (MJPEG/RTSP/HLS URL in settings) on Jog +
   Status. Most-requested "watch the cut from across the shop" feature.
2. **Notifications** - local/push on: job complete, prompt waiting (tool change / gate),
   probe failure, alarm / E-STOP, axis-not-homed. Lets the operator leave the machine.
3. **Feed / spindle override sliders** - adjust overrides live *during a cut* (a manual
   spindle slider already exists on the jog screen for setup; override-during-job is the
   remaining piece).
4. **MDI command box** - guarded (hold-to-confirm + settings toggle) for quick manual codes.
5. **Completion time-of-day** readout and per-step ETAs.
6. **Job history / log** - recent runs with durations and outcome.
7. **View-only / kiosk mode toggle** - for a shared shop phone or wall tablet.
8. ~~**mDNS / Bonjour discovery** - auto-find machines on the LAN when adding.~~ **Done** via
   a lightweight UDP beacon (`MaestroBeacon` + `/api/peers`); browsers can't do UDP/mDNS, so
   the server collects peers and the PWA reads the list.
9. **QR pairing** - scan the PC's QR to prefill host + machineId, then PIN.
10. **Haptics** on jog/confirm; **wake lock** while a job runs.

## Considered but deferred / out of scope

- Cloud relay / remote-over-internet access (explicit non-goal).
- Multi-user accounts, roles, audit trails.
- Editing workflows/tools from the phone (Admin stays on the PC for now; could be a later
  "remote admin" mode).
- G-code file upload from the phone.
