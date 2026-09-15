# MultiAudio Control Protocol — v0

Transport: WebSocket over `localhost` (agent listens, UI connects).
Format: JSON, one message per frame.

This file is the source of truth. `types.ts` (web) and `Protocol.cs` (agent)
are hand-kept mirrors of it — if you change one, change all three.

## Envelope

Every message has:

```json
{
  "type": "PLAY",
  "sessionId": "abc123",
  "requestId": "uuid-optional",
  "payload": { }
}
```

- `type` — one of the message types below.
- `sessionId` — required for all messages except `HELLO`, `DEVICE_LIST`,
  `GET_STATUS`, `ERROR`.
- `requestId` — optional, echoed back in the response so the UI can
  correlate request/response pairs.

## Authentication (rules.md #9)

1. UI opens the WebSocket and immediately sends `HELLO` with a pairing
   token (obtained out-of-band on first run — see design.md §16 First-Run).
2. Agent replies `HELLO_ACK` or closes the connection.
3. No other message is processed before `HELLO_ACK`.
4. Agent validates the `Origin` header and rejects non-local origins.

## Message Types

### Client (Web UI) → Agent

| Type               | Payload                                      | Notes |
|--------------------|-----------------------------------------------|-------|
| `HELLO`             | `{ token: string, clientVersion: string }`    | must be first message |
| `DEVICE_LIST`        | `{}`                                          | request current device list |
| `DEVICE_CONNECT`     | `{ deviceId: string }`                        |       |
| `DEVICE_DISCONNECT`  | `{ deviceId: string }`                        |       |
| `OPEN_BLUETOOTH_SETTINGS` | `{}`                                    | opens the host OS Bluetooth settings |
| `CREATE_SESSION`     | `{ name: string, deviceIds: string[] }`       | returns `sessionId` |
| `PLAY`               | `{ position: number, targetTimestamp: number }` |     |
| `PAUSE`              | `{}`                                          |       |
| `STOP`               | `{}`                                          |       |
| `SEEK`               | `{ position: number }`                        | seconds |
| `SET_VOLUME`          | `{ volume: number }`                          | 0.0–1.0, global |
| `SET_DEVICE_ENABLED`  | `{ deviceId: string, enabled: boolean }`      | enable/disable within session |
| `SET_AUDIO_SOURCE`    | `{ source: string, filePath?: string }`        | "system" (loopback), "file", "mic", "tone" |
| `GET_STATUS`          | `{}`                                          |       |

### Agent → Client (Web UI)

| Type               | Payload                                                        | Notes |
|--------------------|------------------------------------------------------------------|-------|
| `HELLO_ACK`          | `{ agentVersion: string, platform: string }`                    |       |
| `DEVICE_LIST_RESULT`  | `{ devices: AudioOutputDevice[] }`                               |       |
| `DEVICE_STATE`        | `{ deviceId: string, state: DeviceState }`                       | pushed on any change |
| `SESSION_STATE`       | `{ sessionId, playbackState, position, volume, audioSource, devices: DeviceSyncInfo[] }` | pushed periodically + on change |
| `SYNC`                | `{ sessionId, devices: DeviceSyncInfo[] }`                       | latency/drift telemetry |
| `STATUS`              | `{ agentVersion, uptimeSeconds, activeSessionId?: string }`      | reply to `GET_STATUS` |
| `ERROR`               | `{ code: string, message: string, deviceId?: string }`           | see Error Codes |

## Shared Types (conceptual)

```
AudioOutputDevice
  id: string
  name: string
  type: "earbuds" | "headphones" | "speaker" | "unknown"
  state: DeviceState
  capabilities: string[]        // e.g. ["classic-bt", "le-audio"]
  latencyMs: number | null      // null until measured

DeviceState =
  "available" | "connecting" | "connected" | "playing" |
  "paused" | "disconnected" | "unsupported" | "error"

DeviceSyncInfo
  deviceId: string
  measuredLatencyMs: number
  bufferDepthMs: number
  clockOffsetMs: number
  driftEstimateMsPerSec: number
  syncState: "synced" | "syncing" | "degraded"
```

## Error Codes (rules.md #12 — never silently fail)

| Code                     | Meaning                                          |
|--------------------------|---------------------------------------------------|
| `DEVICE_NOT_FOUND`        | Unknown deviceId                                  |
| `DEVICE_UNSUPPORTED`      | Device lacks required capability                  |
| `DEVICE_CONNECT_FAILED`   | OS-level connection attempt failed                |
| `AUDIO_INIT_FAILED`       | Output endpoint could not be opened                |
| `PERMISSION_DENIED`       | Insufficient OS permissions                        |
| `UNAUTHORIZED`            | Bad/missing pairing token, or bad origin           |
| `SESSION_NOT_FOUND`       | sessionId does not exist                           |
| `INVALID_MESSAGE`         | Malformed message / failed validation              |

## Versioning

Bump this doc's version header when the wire format changes in a
backward-incompatible way. `HELLO` / `HELLO_ACK` exchange versions so
UI and agent can detect a mismatch early instead of failing obscurely.
