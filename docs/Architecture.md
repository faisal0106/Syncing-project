# MultiAudio --- System Architecture

## 1. Architectural Principle

MultiAudio should use a **hybrid architecture**:

``` text
                 WEB / DESKTOP UI
                       |
                 Control Protocol
                       |
                LOCAL NATIVE AGENT
                       |
          +------------+------------+
          |            |            |
      Audio Engine  Device Layer  Sync Engine
          |            |            |
          +------------+------------+
                       |
              Operating System APIs
                       |
            Bluetooth / LE Audio /
             Supported audio APIs
```

The browser/UI should not be responsible for unrestricted Bluetooth
audio routing.

## 2. Major Components

### 2.1 Client UI

Responsibilities:

-   device list;
-   connection state;
-   playback controls;
-   synchronization status;
-   settings;
-   diagnostics.

Recommended technology:

-   React
-   TypeScript
-   Vite or equivalent
-   PWA support where useful

The UI should communicate with the local agent through a secure local
WebSocket or equivalent IPC mechanism.

### 2.2 Native Audio Agent

The native agent is the privileged platform-specific component.

Responsibilities:

-   enumerate audio devices;
-   interact with OS audio APIs;
-   manage output endpoints;
-   receive commands from UI;
-   feed synchronized audio to outputs;
-   report diagnostics.

Possible implementations:

-   Windows: C#/.NET initially, with C++/Rust where low-level
    integration becomes necessary.
-   Android: Kotlin.
-   macOS: Swift.
-   Linux: Rust/C++/PipeWire-oriented implementation.

## 3. Audio Engine

The audio engine should use a common internal representation.

Conceptual pipeline:

``` text
Audio Source
     |
     v
Decoder
     |
     v
Resampler / Format Normalizer
     |
     v
Audio Mixer
     |
     v
Synchronization Scheduler
     |
     +----------+----------+
     |                     |
 Output Buffer A       Output Buffer B
     |                     |
 Device A               Device B
```

The engine should preferably use a stable PCM representation internally.

## 4. Synchronization Engine

Every output can have a different effective latency.

Maintain:

``` text
device_id
measured_latency
buffer_depth
clock_offset
drift_estimate
sync_state
```

A target playback timestamp should be assigned to each audio block.

Conceptually:

``` text
target_time = master_clock + scheduling_offset

device_play_time =
target_time + device_specific_compensation
```

The system should use buffering and controlled timing adjustments rather
than repeatedly stopping/restarting audio.

## 5. Device Abstraction

Use a common interface:

``` text
AudioOutputDevice
├── id
├── name
├── state
├── capabilities
├── latency
├── connect()
├── disconnect()
├── start()
├── stop()
├── setVolume()
└── getDiagnostics()
```

Platform implementations should conform to this abstraction.

## 6. Control Protocol

Example:

``` json
{
  "type": "play",
  "sessionId": "abc123",
  "position": 0,
  "targetTimestamp": 1839201023
}
```

Other messages:

``` text
DEVICE_LIST
DEVICE_CONNECT
DEVICE_DISCONNECT
PLAY
PAUSE
STOP
SEEK
SET_VOLUME
SYNC
GET_STATUS
ERROR
```

## 7. Web-to-Agent Communication

Preferred development architecture:

``` text
Browser
   |
   | WebSocket over localhost
   v
Native Agent
```

The agent should reject unauthorized origins and validate all commands.

A session token or pairing mechanism should be used rather than exposing
unrestricted local control.

## 8. Audio Transport Strategy

### Classic Bluetooth

Where the operating system exposes multiple independent outputs, the
agent can use the OS audio layer to render to multiple endpoints.

### LE Audio / Auracast

Where supported, the system should investigate broadcast-oriented
architectures rather than maintaining a separate stream for every
receiver.

### Important constraint

MultiAudio must not assume that every Bluetooth device supports
arbitrary simultaneous connections. Capability detection is mandatory.

## 9. Master Device

The Master is the authoritative playback controller.

Responsibilities:

-   choose audio source;
-   establish session;
-   select outputs;
-   determine playback timestamp;
-   manage synchronization;
-   mix microphone audio in future versions;
-   maintain session state.

## 10. Failure Model

One output failing should not necessarily stop the entire session.

Example:

``` text
Device A  ✓
Device B  ✓
Device C  ✗

Session continues:
A + B
```

The UI should clearly report the failure.

## 11. Scalability

The architecture should distinguish:

-   independent Bluetooth output mode;
-   broadcast mode;
-   future network output mode.

Do not hard-code the system around exactly two devices.

## 12. Security Boundaries

``` text
Browser UI
    |
    | authenticated control
    v
Native Agent
    |
    | OS APIs
    v
Audio Devices
```

The agent must validate:

-   command type;
-   session;
-   device identifiers;
-   numeric ranges;
-   origin;
-   authorization state.

## 13. Recommended V1 Stack

``` text
Frontend: React + TypeScript
Desktop shell/control: Web UI
Native Agent: C#/.NET
Windows audio: WASAPI / appropriate Windows audio APIs
Communication: localhost WebSocket
Testing: two Bluetooth audio devices
```

The exact Windows API implementation must be validated experimentally
before committing to the final audio-routing design.
