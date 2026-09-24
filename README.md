# MultiAudio

> One Master device → multiple synchronized Bluetooth audio outputs.

This repo is past the scaffold stage: the agent has a real WASAPI
output implementation and a working control server, and the web UI
builds cleanly against it. It is **not yet a validated product** —
see "Current status" below for exactly what is and isn't proven.

## Layout

```
Bluetooth-sync/
├── docs/                    Product/architecture/design/rules docs (source spec)
├── shared/protocol/         The control-protocol contract (source of truth)
│   ├── PROTOCOL.md            human-readable spec
│   ├── types.ts               TypeScript mirror (web)
│   └── (Protocol.cs lives in agent/, C# mirror)
├── agent/                   Native, privileged, platform-specific agent
│   └── MultiAudio.Agent/      .NET project — Windows first (WASAPI)
└── web/                     Control UI (React + TypeScript + Vite)
```

This mirrors the layered architecture from `docs/Architecture.md` §1 and
the independence rule in `docs/rules.md` §15:

```
UI → Control API → Session Manager → Audio Engine → Platform Adapter → OS
```

The web UI never talks to Bluetooth/OS audio APIs directly — only the
agent does, over an authenticated localhost WebSocket (see
`shared/protocol/PROTOCOL.md`).

## Current status

- **Implemented:** `WindowsAudioOutputDevice.cs` is a real WASAPI shared-mode
  output (device enumeration, connect/start/stop, volume, format
  conversion). `SessionManager` enumerates real Windows render endpoints
  via `MMDeviceEnumerator`/`IMMNotificationClient` and drives them —
  `SimulatedAudioOutputDevice` is no longer wired into that path and is
  kept only as a reference/testing implementation of `IAudioOutputDevice`.
  The control server, protocol, and web UI build and talk to each other
  end-to-end (see `docs/memory.md` §16 for what's been exercised).
- **Implemented:** the Phase 2 synchronization engine (`docs/phases.md`).
  Real per-device WASAPI latency, hardware-clock-based drift/offset
  measurement, startup scheduling alignment, and bounded ongoing drift
  correction — see `docs/memory.md` §16's Phase 2 entry for the details
  and exactly which thresholds are a documented starting point rather
  than a verified-correct final answer.
- **Implemented:** basic real-run robustness — a persistent log file
  per run (`agent/MultiAudio.Agent/AgentLog.cs`), top-level crash
  handling with a clear message instead of a raw stack trace, and
  automatic cleanup when a device disconnects mid-playback instead of
  spamming errors every audio callback.
- **Not yet validated on real hardware.** None of the above has been
  run against actual Bluetooth devices in any environment used to
  build it (no Windows machine or Bluetooth hardware was available).
  Everything compiles (`dotnet build`, 0 warnings/errors, verified
  independently) and was exercised against loopback/simulated/file
  audio, but never a real WASAPI endpoint. The open question from
  `docs/memory.md` §15 —

  > Can a Windows native agent reliably send the same audio to two
  > independently connected Bluetooth audio devices, and can the resulting
  > streams be synchronized closely enough to avoid an obvious echo?

  — is still open. **This is the next step, and it has to happen on a
  real Windows PC with real Bluetooth devices** — see "Testing on real
  hardware" below.

## Getting started

### Agent (Windows, .NET 8 SDK required)

```
cd agent/MultiAudio.Agent
dotnet build
dotnet run
```

On startup it prints a pairing token and the port it's listening on,
and starts writing a timestamped log file to `logs/` next to the
executable. Ctrl+C stops it cleanly.

### Web UI (Node 18+)

```
cd web
npm install
npm run dev
```

Open the URL Vite prints (typically `http://127.0.0.1:5173`). It
connects to the agent at `ws://127.0.0.1:8787` and will show
"disconnected" until the agent above is running. The first time it
connects, paste in the pairing token the agent printed (or fetch it
yourself from `http://127.0.0.1:8787/pairing-token`).

## Testing on real hardware

This is the most important thing left to do — see "Current status"
above. What to actually do:

1.  Pair **two** Bluetooth audio devices (headphones/speakers) to the
    Windows PC normally, through Windows Bluetooth settings, first.
    The agent only renders to endpoints Windows already knows about —
    it doesn't do the Bluetooth pairing itself.
2.  Run the agent and web UI as above.
3.  In the web UI, connect to the agent, select both devices, and hit
    play with some real audio (system loopback or a file).
4.  Watch the sync panel in the UI (or the agent's console/log) for
    each device's `MeasuredLatencyMs`, `BufferDepthMs`,
    `DriftEstimateMsPerSec`, and `SyncState`. Listen for whether the
    two outputs sound noticeably out of sync (an echo/slap-back).
5.  Try disconnecting one device mid-playback (turn it off, walk out
    of range) and confirm the agent logs a clean disconnect message
    rather than spamming errors, and that the other device keeps
    playing.

**What's genuinely uncertain and worth watching for specifically:**
whether `IAudioClock`/`AudioClockClient` (the sync engine's real
measurement source) works as well on Bluetooth A2DP endpoints as it
does on wired ones — some Bluetooth drivers have weaker support for
it than wired audio hardware. If a device's `SyncState` never leaves
"Syncing", that's the first thing to check (the agent's log will note
if the clock service was unavailable for a device). Also worth tuning
if it doesn't feel right: the 150ms target buffer, the ±40ms
correction band, and the 2/8 ms/s Synced/Syncing/Degraded cutoffs in
`SessionManager.ToSyncInfo` and `WindowsAudioOutputDevice` — these are
reasoned-through starting points, not measured against real hardware.

If something crashes, the console will print a `[FATAL]` message and
the full detail is in `agent/MultiAudio.Agent/logs/` — that log file
is the most useful thing to save/share if something goes wrong.

## Where to add things

| You're building...              | It goes in...                                      |
|----------------------------------|-----------------------------------------------------|
| WASAPI / device enumeration      | `agent/MultiAudio.Agent/Devices/`                    |
| Sync/timing logic                | `agent/MultiAudio.Agent/` (new `Sync/` folder)       |
| A new control message            | `shared/protocol/PROTOCOL.md` first, then both mirrors |
| A UI panel/screen                | `web/src/components/`                                |
| Agent connection state           | `web/src/hooks/useAgentConnection.ts`                |

## Ground rules

See `docs/rules.md` in full, but the two that shape this scaffold most:

- **Rule 1**: Bluetooth *connectivity* ≠ audio *routing capability*. Don't
  assume a discovered device can be rendered to — capability must be
  detected (`docs/rules.md` #2, #12).
- **Rule 4**: All privileged audio/device operations are isolated behind
  the native agent. The web UI is presentation + control only.
