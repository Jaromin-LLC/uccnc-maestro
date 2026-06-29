# Companion App - HTTP API

All endpoints are served by `MaestroServer` (embedded `HttpListener`) on the shop PC, over
plain HTTP on the LAN. JSON request/response unless noted. Telemetry is pushed via SSE.

Base: `http://<host>:<port>` (default port `8723`).

## Auth

- `GET /api/info`, `GET /api/health`, and `GET /api/peers` are **unauthenticated** (used by Add / discovery).
- All other endpoints require `Authorization: Bearer <token>`, obtained via pairing.
- Control endpoints additionally require the caller to hold the **active-controller lock**
  (acquired implicitly by the first controller; others get `423 Locked` on control calls
  but can still read status). E-STOP and Feed Hold bypass the lock.

### `GET /api/info`  (unauthenticated)
```json
{ "machineId": "b1c2...", "machineName": "Router 1", "version": "1.1.0",
  "build": "2026-06-27 15:19 (08d25b6)", "units": "mm", "requiresPin": true }
```
`build` is the stamped plugin build id (timestamp + git short hash); the test host reports
`"dev"`. Use it to confirm which build a device is talking to. `units` (`"mm"` | `"in"`) is
the machine's configured unit system; the phone adopts it automatically (see the units note
under Machine commands), so it isn't chosen per-device.

### `POST /api/pair`  (unauthenticated)
Request: `{ "pin": "4821", "client": "Patrick iPhone" }`
Response 200: `{ "token": "<opaque>", "machineId": "...", "machineName": "Router 1" }`
Response 401: `{ "error": "bad_pin" }`

### `GET /api/peers`  (unauthenticated)
LAN auto-discovery. The plugin broadcasts a small UDP beacon (port = HTTP port, default
`8723`) every ~5 s and listens for other machines' beacons, keeping a list of peers seen in
the last ~20 s. Because browsers can't do UDP, the PWA asks its connected server for the
list. Returns the machine itself plus discovered peers (peers exclude self):
```json
{
  "self": { "machineId": "b1c2...", "machineName": "Router 1" },
  "discoveryEnabled": true,
  "peers": [
    { "machineId": "9f3a...", "machineName": "Router 2", "host": "192.168.1.51",
      "port": 8723, "version": "1.1.0", "url": "http://192.168.1.51:8723/" }
  ]
}
```
`discoveryEnabled` is `false` if the UDP beacon couldn't bind (e.g. server not on LAN). The
beacon only runs when the server is bound LAN-wide (`openOnLan`).

## Status

### `GET /api/status`  -> `StatusSnapshot`
```json
{
  "machineId": "b1c2...",
  "machineName": "Router 1",
  "connected": true,
  "ts": 1719500000000,
  "machine": {
    "homed": { "x": true, "y": true, "z": true, "a": false },
    "cycleRunning": false,
    "feedHold": false,
    "moving": false,
    "alarm": false,
    "estopped": false,
    "units": "mm",
    "pos":      { "x": 101.0375, "y": -103.825, "z": 23.2, "a": 0.0 },
    "machinePos": { "x": 101.0375, "y": -103.825, "z": 23.2, "a": 0.0 },
    "feedRate": 0.0, "spindleRpm": 0.0, "spindleOn": false,
    "feedOverride": 100, "spindleOverride": 100, "rapidOverride": 100,
    "gcodeLine": 0
  },
  "maestro": {
    "running": false,
    "activeProjectId": "SAMPLE_PROJECT",
    "activeStepIndex": -1,
    "statusText": "Ready",
    "promptWaiting": false,
    "promptText": "", "promptIsGateOnly": false,
    "promptPhotoUrl": "",
    "steps": [
      { "index": 0, "label": "Example Operation", "type": "op", "toolLabel": "T1 - 0.25\" Flat",
        "status": "pending", "lastRunSeconds": 412 }
    ],
    "fileCurrentLine": 0, "fileTotalLines": 0,
    "estimateSeconds": 0, "elapsedSeconds": 0, "remainingSeconds": 0
  },
  "controller": { "heldBy": "Patrick iPhone", "youHoldControl": true },
  "jogStepSizes": [0.01, 0.1, 1, 10], "jogFeed": 1500
}
```

### `GET /api/events`  (SSE)
`text/event-stream`. Emits `event: status` with the full `StatusSnapshot` payload **only when
the snapshot actually changes** (a DRO move, a state transition, or the once-a-second job
clock). On the ~500 ms heartbeat when nothing changed it instead writes a `: ping` comment
line to keep the connection alive (ignored by `EventSource`). Change detection ignores the
snapshot's `ts` field, so an idle machine produces pings rather than redundant status frames.

## Catalog

- `GET /api/projects` -> `{ "projects": [ WorkflowProject... ] }`
- `GET /api/tools` -> `{ "tools": [ ToolInfo... ] }`
- `GET /api/media?path=<relative-or-absolute>` -> binary (photos/videos for prompts; path
  is validated against the configured media root).

## Jog

### `POST /api/jog`
```json
{ "axis": "X", "dir": 1, "mode": "step", "step": 1.0, "feed": 1500 }
```
- `axis`: `X|Y|Z|A`, `dir`: `+1|-1`, `mode`: `step|cont`, `step`: distance (current units),
  `feed`: mm/min (or in/min).
- `step` mode issues one incremental move. `cont` mode starts continuous motion that
  continues until `POST /api/jog/stop`, the watchdog expires, or the controller disconnects.
- `feed` applies to **both** modes. For `cont`, the server sets UCCNC's jog feedrate
  (field `913`) to `feed` before starting motion, so continuous jog runs at the slider's
  speed rather than a fixed rate.

### `POST /api/jog/stop`  -> stops continuous jog.

### `POST /api/jog/keepalive`  -> resets the continuous-jog watchdog (sent ~every 250 ms while a jog button is held).

## Spindle (manual, jog screen)

### `POST /api/spindle`
```json
{ "on": true, "rpm": 18000 }
```
Starts (`M3 S<rpm>`) or stops (`M5`) the spindle for setup tasks. Requires the active-controller
lock; rejected with `409 conflict` while a job is running. `on:false` (or `rpm:0`) stops it.

## Machine commands

| Endpoint | Body | Action |
|----------|------|--------|
| `POST /api/zero` | `{ "axis": "X" }` or `{ "axis": "all" }` | Zero work offset for axis / all |
| `POST /api/home` | `{ "axis": "X" }` or `{ "axis": "all" }` | Reference axis / all |
| `POST /api/goto-zero` | - | Rapid to work X0 Y0 |
| `POST /api/park` | `{ "type": "g28" \| "g30" \| "custom" }` | Park |
| `POST /api/autozero` | - | Two-pass fixed-plate probe |
| `POST /api/feedhold` | - | Feed hold (bypasses lock) |
| `POST /api/resume` | - | Cycle start / resume |
| `POST /api/stop` | - | Stop current motion / program |
| `POST /api/estop` | - | Emergency stop (bypasses lock) |

> `units` (`mm`/`in`) is configured **on the machine** (Maestro's Mobile tab) and reported via
> `/api/info` and in the status snapshot (`machine.units`); the phone adopts it automatically.
> UCCNC is unit-agnostic (no G20/G21), so this just tells the app how to label/scale DROs,
> step presets, and the jog feed. `jog` distances/feeds are sent as plain numbers in those units.
> `POST /api/goto-zero` still exists but is **not** surfaced in the mobile UI (a one-tap rapid
> to zero is considered unsafe to expose remotely).

## Maestro workflow

| Endpoint | Body | Action |
|----------|------|--------|
| `POST /api/maestro/select` | `{ "projectId": "..." }` | Set active project |
| `POST /api/maestro/run-all` | `{ "fromIndex": 0 }` (optional) | Run all from first incomplete / given index |
| `POST /api/maestro/run-step` | `{ "index": 2 }` | Run single step |
| `POST /api/maestro/reset` | - | Clear completion flags |
| `POST /api/maestro/abort` | - | Abort current run |
| `POST /api/maestro/confirm` | - | Confirm a waiting tool-change / gate prompt |
| `POST /api/maestro/cancel` | - | Cancel a waiting prompt |

## Responses & errors

- Success: `200` with `{ "ok": true, ... }` or the requested resource.
- `400 bad_request`, `401 unauthorized`, `403 forbidden`, `404 not_found`,
  `409 conflict` (e.g. already running), `423 locked` (another controller holds control),
  `503 unavailable` (machine not ready, e.g. not homed). Body: `{ "error": "<code>", "message": "..." }`.
