# MultiAudio --- Project Memory

## 1. Project Identity

Project name: **MultiAudio**

Current product concept:

> A Master application that allows one host device to manage and play
> the same audio through multiple compatible Bluetooth audio outputs.

## 2. Core User Requirement

The target experience is similar in concept to connecting two
AirPods/headsets to one phone or PC and having both receive the same
audio.

The user specifically wants multiple **physical Bluetooth audio
devices**, not multiple phones acting as speakers.

## 3. Master Device Concept

The Master is the host running the main application.

Responsibilities:

-   audio source;
-   microphone input in future versions;
-   playback control;
-   output selection;
-   synchronization;
-   session management.

## 4. Web Application Direction

The project should have a web-based user interface so users can access
the control experience from phones and PCs.

However:

> The browser should not be treated as the component responsible for
> unrestricted Bluetooth audio routing.

Instead:

``` text
Web App
   |
Local Control API
   |
Native Agent
   |
OS Audio APIs
   |
Bluetooth Audio Devices
```

## 5. Native Agent Concept

A lightweight platform-specific native application/service handles
privileged operations.

Potential implementations:

-   Windows → C#/.NET initially.
-   Android → Kotlin.
-   macOS → Swift.
-   Linux → Rust/C++.

The web frontend remains as platform-independent as practical.

## 6. Initial Development Target

The first technical target is:

> Windows PC + two Bluetooth audio devices.

Do not begin with every platform.

The first objective is to prove simultaneous multi-output playback and
determine actual Windows audio-stack limitations.

## 7. Core Technical Risk

The largest risk is not the UI.

The largest risk is:

> Whether and how the host OS permits an application to render the same
> audio stream to multiple independently connected Bluetooth audio
> endpoints, and whether those endpoints can be synchronized closely
> enough for practical use.

This must be experimentally validated.

## 8. Synchronization Requirement

Different Bluetooth devices can have different effective latency.

Therefore the system needs:

-   buffering;
-   timing;
-   latency estimation;
-   per-device compensation;
-   drift correction.

Never assume equal latency.

## 9. Long-Term Technology Direction

Investigate:

-   Classic Bluetooth audio;
-   Bluetooth LE Audio;
-   Auracast;
-   OS-native multi-output audio;
-   future network-based receivers.

Auracast/LE Audio should be treated as an additional
transport/capability path rather than assuming it solves every
legacy-device scenario.

## 10. Product Positioning

Do not define the product merely as:

> "Two Bluetooth headphones."

Define it as:

> **A universal multi-output audio control and synchronization
> platform.**

The number of outputs and supported transports can grow over time.

## 11. Development Strategy

Preferred sequence:

``` text
Feasibility
→ Two-output prototype
→ Synchronization
→ Native agent
→ Web UI
→ Productization
→ Microphone
→ Android
→ macOS
→ LE Audio/Auracast
→ Advanced multi-output features
```

## 12. Architecture Principle

Keep the following independent:

``` text
UI
Control API
Session Manager
Audio Engine
Synchronization Engine
Platform Adapter
Operating System
```

## 13. Important Constraints

-   A web app alone should not be assumed capable of arbitrary
    multi-Bluetooth audio routing.
-   Bluetooth connectivity does not imply audio-routing capability.
-   Platform capabilities differ.
-   Device compatibility must be detected.
-   OS restrictions must not be bypassed.
-   Privacy should be local-first.
-   Cloud infrastructure is not required for the core local use case.

## 14. Future Features

Potential future capabilities:

-   microphone + music mixing;
-   per-device volume;
-   saved device groups;
-   automatic reconnect;
-   latency calibration;
-   device health diagnostics;
-   master handover;
-   Auracast support;
-   more than two outputs;
-   Android and macOS support;
-   local network receivers.

## 15. Current Priority

The immediate engineering question is:

> **Can a Windows native agent reliably send the same audio to two
> independently connected Bluetooth audio devices, and can the resulting
> streams be synchronized well enough to avoid an obvious echo?**

Everything else should be built around the answer to this question.

## 16. Progress Log

-   **Agent control server implemented** (`ControlServer.cs`,
    `SessionManager.cs`, `PairingTokenStore.cs`): the full PROTOCOL.md
    message set now works over a real localhost WebSocket —
    HELLO/HELLO_ACK auth with a persisted pairing token, Origin
    validation, device list, session create/play/pause/stop/seek/
    volume/device-enable, and periodic SESSION_STATE/SYNC pushes.
-   **Real WASAPI output implemented** (`Devices/WindowsAudioOutputDevice.cs`):
    device connect/start/stop, shared-mode `WasapiOut`, volume control,
    and PCM format conversion (float↔16-bit) against the endpoint's mix
    format. `SessionManager` now enumerates real Windows render endpoints
    via `MMDeviceEnumerator` + `IMMNotificationClient` and drives
    `WindowsAudioOutputDevice` directly, as the swap-in was designed to
    (no changes needed to `SessionManager`'s or `ControlServer`'s shape).
    `SimulatedAudioOutputDevice.cs` still exists but is no longer on the
    live path — keep it if it's useful for non-Windows/no-hardware
    testing, otherwise it's dead code.
-   **Web hook updated** (`useAgentConnection.ts`) to fetch the pairing
    token from the agent's `/pairing-token` endpoint instead of a
    hardcoded placeholder.
-   **Web build fixed:** `web/src/vite-env.d.ts` (the `/// <reference
    types="vite/client" />` file) was missing, which made `tsc -b` fail
    on the CSS side-effect import in `main.tsx` with `TS2882`. Added it;
    `npm run build` (`tsc -b && vite build`) now completes with zero
    errors.
-   **Housekeeping:** removed two ad hoc build-output folders
    (`agent/agent-build-check-2/`, `agent/agent-build-final-3/`) and the
    gitignored `bin/`, `obj/`, `node_modules/`, `dist/` folders that had
    been included in a shared/zipped copy of the repo — none of these
    are source and they don't belong in version control per
    `.gitignore`.
-   **Still not done — Phase 0/2 feasibility and sync research:** no
    actual run against real Bluetooth hardware has happened in any
    environment available for this pass (no Windows/.NET runtime or
    Bluetooth devices). More importantly, the Phase 2 synchronization
    engine itself is unwritten: `AudioEngine` just fans the same PCM out
    to every registered device concurrently with no per-device latency
    measurement, target-timestamp scheduling, or drift correction —
    `WindowsAudioOutputDevice.GetDiagnostics()` hardcodes
    `ClockOffsetMs`/`DriftEstimateMsPerSec` to `0` rather than measuring
    them. Until that's built and tested on real hardware, whether two
    independently connected Bluetooth outputs can be kept in sync
    without an audible echo is still an open question, not an assumption
    to build further phases on. Real audio decode (beyond system
    loopback/tone/file/mic capture) and an installer/first-run UI flow
    are also still outstanding.
-   **Verified, not just written:** the agent was actually compiled
    (`dotnet build`, net8.0, 0 warnings/errors) and run, and exercised
    with a scripted WebSocket client covering the full message set —
    HELLO auth (good + bad token), Origin rejection, device connect,
    session create/play/pause/stop/seek/volume/device-enable, periodic
    SYNC pushes, and all documented error codes. The web app was built
    with `tsc -b && vite build` against the real agent-facing types
    with zero type errors. One real bug was caught and fixed this way:
    a global `JsonStringEnumConverter` in `ControlServer`'s
    `JsonSerializerOptions` was silently overriding every enum's
    per-type `[JsonConverter]` attribute (System.Text.Json checks
    options-level converters before type-level ones) — enums were
    serializing as `"Available"` instead of `"available"`. Fixed by
    removing the options-level fallback and giving `ErrorCode` its own
    explicit attribute instead.
-   **Phase 2 synchronization engine implemented** (`Audio/DeviceClockTracker.cs`,
    `Audio/NativeAudioClockAccess.cs`, `Devices/WindowsAudioOutputDevice.cs`,
    `SessionManager.PlayAsync`/`SetDeviceEnabledAsync`, `Audio/AudioEngine.cs`).
    Closes the gap the previous entry called out — `ClockOffsetMs` and
    `DriftEstimateMsPerSec` are no longer hardcoded to `0`:
    -   **Latency** (`LatencyMs`) is now read from WASAPI's own
        `IAudioClient::GetStreamLatency` right after the device starts,
        replacing the placeholder `null`. rules.md #7 — measured, not
        guessed.
    -   **Clock offset/drift** come from `DeviceClockTracker`, which
        compares `IAudioClient::GetPosition` (via the `AudioClockClient`
        service — the device's own hardware clock) against wall-clock
        time on a rolling ~15s window, and fits a linear regression for
        the drift slope. This is deliberately *not* derived from
        software buffer bookkeeping, which would only measure what this
        process handed to WASAPI, not what the hardware actually played.
        A `DriftEstimateConfident` flag (needs ≥5 real samples, ~5s)
        stops the first few seconds of startup noise from being reported
        as a real measurement — `SessionManager.ToSyncInfo`'s
        Synced/Syncing/Degraded thresholds now key off real magnitude
        (< 2 ms/s / < 8 ms/s / above) and off that confidence flag,
        instead of the old always-basically-false `< 0.05` check that
        was tuned for the previous hardcoded-zero placeholder.
    -   **Startup alignment**: `SessionManager.PlayAsync` now starts
        every device first (so every `LatencyMs` is known), then tells
        each `WindowsAudioOutputDevice` how long to hold its first real
        audio back (`SetSchedulingOffset`) so a low-latency device
        doesn't produce its first audible sample before a high-latency
        one — Architecture.md §4's `target_time = master_clock +
        scheduling_offset` model. `SetDeviceEnabledAsync` does the same
        against whichever devices are already registered when a device
        joins mid-session (needed `AudioEngine.RegisteredDevices`, a
        small new read-only accessor).
    -   **Ongoing drift correction**: `WindowsAudioOutputDevice` now
        routes every buffer write through `AddWithDriftCorrection`,
        which — only once `DriftEstimateConfident` is true — trims or
        pads a bounded slice (≤2% of a chunk) when the buffer has
        drifted outside ±40ms of a 150ms target. This is rules.md #14's
        "buffering and controlled timing adjustments, not repeated
        stop/restart" applied literally: small enough per chunk to be
        inaudible, gated so it never fires on ordinary jitter.
    -   **Isolated undocumented dependency (rules.md #11):** none of
        the above is exposed by NAudio's public `WasapiOut` API — the
        `AudioClient` it creates internally, which is where
        `StreamLatency`/`AudioClockClient` actually live, is a private
        field. `Audio/NativeAudioClockAccess.cs` reads it via reflection
        and is the *only* place that happens; every call is wrapped so
        a failure (e.g. a future NAudio version renaming the field)
        degrades sync telemetry to "unavailable" rather than breaking
        playback. Pinned to NAudio 2.2.1 — re-verify this file
        specifically before bumping that version.
-   **Still not verified: any of this against real hardware.** Same
    constraint as before — no Windows/.NET runtime or Bluetooth devices
    were available in any environment used for this pass, so the sync
    engine was built and compiled (`dotnet build`, 0 warnings/errors,
    verified independently against the actual committed files, not just
    a scratch copy) but never run against a live WASAPI endpoint. In
    particular: whether `IAudioClient::GetPosition`/`AudioClockClient`
    behave as expected on real Bluetooth A2DP endpoints (some drivers
    are known to have weaker `IAudioClock` support than wired ones),
    whether the reflection-based `NativeAudioClockAccess` lookup
    actually returns a populated field at runtime, and whether the
    chosen thresholds (150ms target buffer, ±40ms correction band, 2/8
    ms/s Synced/Syncing/Degraded cutoffs) hold up perceptually are all
    open questions a real-hardware pass needs to answer — treat the
    threshold constants as a documented starting point to tune, not a
    verified-correct final answer.
