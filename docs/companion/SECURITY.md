# Companion App - Security & Safety

This app can move a CNC machine over WiFi. The model below balances "useful in a shop" with
"don't let a stray phone crash the spindle".

## Network

- LAN only. The server binds to the PC's LAN interface(s) on a configurable port (default
  `8723`). No cloud, no port-forwarding guidance - if a user exposes it to the internet, that
  is explicitly out of scope and discouraged in docs.
- Plain HTTP (no TLS) is acceptable on a trusted shop LAN and avoids cert pain on phones.
  A future option may add a self-signed cert + pinning; not in MVP.
- On Windows, `HttpListener` on a non-localhost prefix requires a one-time URL ACL and an
  inbound firewall rule. Run **`.\make.ps1 net-setup`** (self-elevating) to reserve
  `http://+:<port>/` for `Everyone` (SID `WD`) and open the port for all network profiles.
  If the reservation is missing, the server falls back to `http://localhost:<port>` and logs
  the reason. See [REMOTE_APP.md](REMOTE_APP.md#one-time-setup-on-the-shop-pc).
- **Auto-discovery** uses an inbound **UDP** rule on the same port. The beacon only
  advertises non-sensitive identity (machine id/name, port, version) and is purely
  informational — discovery never moves the machine or bypasses pairing. It's optional;
  without the UDP rule, machines just won't auto-appear and are added by IP.

## Pairing & tokens

- Each machine shows a short numeric **PIN** in the Maestro window (regeneratable).
- `POST /api/pair {pin}` exchanges the PIN for an opaque bearer **token** (random 256-bit,
  stored hashed server-side with a friendly client label). The phone stores the token per
  machine in local storage.
- Tokens can be revoked from the Maestro window (per client) or by rotating the PIN+secret.
- `requiresPin=false` mode (open on trusted LAN) is available but off by default.

## Active-controller lock

- Only one client may issue **control** commands at a time. The first controller to send a
  control command acquires the lock (with its client label); other paired clients still get
  full live status (read-only) and see who holds control.
- A controller releases the lock on disconnect/idle timeout, or a user can "take control"
  (which is surfaced to the previous holder).
- **E-STOP** and **Feed Hold** bypass the lock - any paired client can always stop the machine.

## Action guards (client + server enforced)

| Action | Guard |
|--------|-------|
| E-STOP | One tap, always available, prominent. Server triggers immediately. |
| Feed Hold | One tap, always available. |
| Continuous jog | Dead-man: requires touch held; stops on release, app background/blur, or lost connection. Server watchdog stops motion if no keepalive within ~600 ms. The selectable **default mode**. |
| Step jog | Single bounded incremental move (selectable mode). |
| Spindle ON | Hold-to-confirm (spins a tool); rejected while a job is running. |
| Home / Park / Auto-zero / Run | Hold-to-confirm (press-and-hold ~1 s) on the client; server requires axes homed where relevant and rejects if a cycle is active. |

## Watchdog & connection loss

- The PWA sends jog keepalives during continuous jog. If the server misses keepalives it
  issues a stop.
- If the SSE stream drops, the client shows a disconnected banner and disables control until
  reconnected. The server stops any in-progress continuous jog when the controlling SSE
  client disconnects.

## Preconditions enforced server-side

- Run / auto-zero / probing require all axes homed and no active cycle (mirrors
  `MachineOps.Preflight`).
- Jog requires not-cycling. (UCCNC ignores jog during a cycle anyway.)

## What we deliberately do NOT do

- No arbitrary remote MDI in MVP control scope without a guard (MDI box is a Phase-3 extra,
  behind hold-to-confirm and a settings toggle).
- No firmware/EEPROM/macro editing.
