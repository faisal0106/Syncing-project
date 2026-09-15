# Testing the current build

Two layers to test separately: the **agent's control protocol** (works
today, no hardware needed) and **real audio to real devices** (not
built yet — see docs/memory.md §16).

## 0. Prerequisites

- .NET 8 SDK (`dotnet --version` should print `8.x`)
- Node.js 18+ (`node --version`)
- Python 3 with `pip install websockets` — only needed for the
  automated script in step 2

## 1. Start the agent

```bash
cd agent/MultiAudio.Agent
dotnet build      # should say "0 Warning(s), 0 Error(s)"
dotnet run
```

You should see:

```
MultiAudio Agent starting...
MultiAudio Agent listening on ws://127.0.0.1:8787/
Pairing token (first run only, see docs/design.md §16): <some token>
```

Leave this running. Restart it later and confirm the token is
**identical** — that proves persistence is working
(`~/.local/share/MultiAudio/pairing.token` on Linux/macOS,
`%LOCALAPPDATA%\MultiAudio\pairing.token` on Windows).

## 2. Automated protocol check (recommended first check)

In a second terminal, with the agent still running:

```bash
pip install websockets
python3 agent/test_agent_protocol.py
```

Expect `ALL CHECKS PASSED` at the end. This script:
- fetches the pairing token over HTTP,
- confirms a bad token is rejected (`UNAUTHORIZED`),
- confirms a non-local `Origin` is rejected (HTTP 403),
- runs the full flow: HELLO → device list → connect two simulated
  devices → create session → play → wait for a live `SYNC` push →
  set volume → disable a device → pause → stop → status,
- confirms unknown device/session IDs return the right error codes.

If this fails, something regressed in the agent — fix that before
testing the UI.

## 3. Web UI, against the real agent

In a second terminal:

```bash
cd web
npm install
npm run dev
```

Open the printed URL (typically `http://localhost:5173`). With the
agent still running from step 1, the UI should show:

- connection status → **connected**
- two devices: "Simulated Earbuds A" and "Simulated Headphones B"
- clicking a device connects it (state moves available → connecting →
  connected)
- creating a session + pressing play moves playback state to
  **playing**, and the sync panel numbers (latency/drift/buffer)
  update roughly once a second — they're randomized per device on
  purpose, so the two devices should show *different* numbers, never
  identical

If the UI shows "disconnected" or an error, check the browser console
first — most likely causes are the agent not running, port `8787`
already in use, or a browser extension blocking `ws://` to localhost.

## 4. Real hardware smoke test

The agent now enumerates active Windows WASAPI render endpoints. Pair and
connect two Bluetooth audio devices in Windows, then refresh the UI. They
should appear by their Windows endpoint names, not as simulated devices.

Select both devices, start a session, and press Play. The default source is
Windows system loopback, so audio currently playing on the PC is distributed
to both selected outputs. One selected device may also be the Windows default
output: the agent leaves that original stream alone and forwards a copy only
to the other selected devices. A tone, file, or microphone source can also
be selected through the `SET_AUDIO_SOURCE` protocol command.

This validates real endpoint discovery, opening, simultaneous output, and
shared audio distribution. Latency compensation and drift correction are
still separate synchronization work; the current diagnostics expose actual
WASAPI stream latency and output buffer depth but do not claim echo-free
Bluetooth synchronization.

## Troubleshooting

| Symptom | Likely cause |
|---|---|
| `dotnet build` fails trying to reach nuget.org | No internet, or a proxy blocking it. The project has zero package references so this should be rare; check your network. |
| Agent starts but browser says disconnected | Check the agent's own console for `[ControlServer] connection error: ...`; confirm you're browsing `http://localhost:5173` or `http://127.0.0.1:5173`, not some other origin. |
| `EADDRINUSE` / port 8787 already in use | Another agent instance is still running — find and stop it (`lsof -i :8787` on Linux/macOS, `netstat -ano | findstr 8787` on Windows). |
| Pairing token changes every restart | The token file isn't being written/read — check permissions on the `MultiAudio` folder under local app data. |
