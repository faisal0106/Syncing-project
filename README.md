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
- **Not yet implemented:** the Phase 2 synchronization engine
  (`docs/phases.md`). `AudioEngine` fans the same PCM out to every
  registered output concurrently, but there is no per-device latency
  measurement, target-timestamp scheduling, or drift correction —
  `GetDiagnostics()` reports `ClockOffsetMs`/`DriftEstimateMsPerSec` as
  hardcoded zero, not measured values. Two Bluetooth outputs with
  different effective latency can therefore still audibly echo.
- **Not yet validated on real hardware:** the code above has not been run
  against actual Bluetooth devices in this environment (no Windows/.NET
  runtime or Bluetooth hardware is available here). The open question
  from `docs/memory.md` §15 —

  > Can a Windows native agent reliably send the same audio to two
  > independently connected Bluetooth audio devices, and can the resulting
  > streams be synchronized closely enough to avoid an obvious echo?

  — is still open on the synchronization half; the "send the same audio
  to two devices" half is implemented but untested on real hardware.

## Getting started

### Agent (Windows, .NET 8 SDK required)

```
cd agent/MultiAudio.Agent
dotnet build
dotnet run
```

Currently just starts and idles — no control server or real device
support yet (see TODOs in `Program.cs`).

### Web UI (Node 18+)

```
cd web
npm install
npm run dev
```

Renders the primary screen from `docs/design.md` §2. It will try to
connect to `ws://127.0.0.1:8787` and show "disconnected" until the agent
implements the control server and that port.

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
