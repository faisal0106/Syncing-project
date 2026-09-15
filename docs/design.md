# MultiAudio --- Product and UX Design

## 1. Design Philosophy

The interface should make a technically complicated system feel simple.

The user should think:

> "Select headphones → press Play."

The application should handle:

-   connection state;
-   audio routing;
-   synchronization;
-   buffering;
-   errors.

## 2. Primary Screen

``` text
+------------------------------------------------+
| MultiAudio                         ● Connected |
+------------------------------------------------+
|                                                |
| Audio Outputs                                  |
|                                                |
| [✓] AirPods Pro             Connected          |
|     Latency: 82 ms                              |
|                                                |
| [✓] Sony Earbuds            Connected          |
|     Latency: 116 ms                             |
|                                                |
| [ ] JBL Speaker             Available          |
|                                                |
+------------------------------------------------+
|                                                |
| Now Playing                                    |
|                                                |
|             Song / Audio Name                   |
|                                                |
|          ◀     ▶ / ❚❚     ▶                    |
|                                                |
| Volume                                         |
| ───────────────────────────────●────            |
|                                                |
+------------------------------------------------+
| Synchronization                                |
|                                                |
| AirPods Pro       0 ms                          |
| Sony Earbuds     +34 ms                         |
|                                                |
|                 [ START ]                       |
+------------------------------------------------+
```

## 3. Device Card

Each device card should show:

-   device name;
-   type;
-   connection state;
-   selected/unselected state;
-   latency estimate;
-   warning state if unsupported.

States:

``` text
Available
Connecting
Connected
Playing
Paused
Disconnected
Unsupported
Error
```

## 4. Color and Status Semantics

Use a restrained status system:

-   success = connected/healthy;
-   warning = degraded/synchronization issue;
-   error = unavailable/failure;
-   neutral = available but not selected.

Avoid excessive visual decoration.

## 5. Master Indicator

The UI should clearly indicate that the current device is the Master.

``` text
MASTER DEVICE
Windows PC
```

The Master controls the session.

## 6. Session Model

A user creates a session by selecting outputs.

Example:

``` text
Session: Bedroom

Outputs:
- AirPods Pro
- JBL Speaker

Status:
Synchronized
```

Saved groups can later be reused.

## 7. Playback Controls

Minimum controls:

-   Play
-   Pause
-   Stop
-   Seek
-   Global volume
-   Mute

Future controls:

-   microphone;
-   per-device volume;
-   balance;
-   latency adjustment;
-   audio effects.

## 8. Synchronization UX

Do not expose complex technical information by default.

Normal user:

``` text
✓ Synchronized
```

Advanced mode:

``` text
AirPods Pro
Latency: 82 ms
Buffer: 120 ms
Drift: +1.2 ms/s

Sony Earbuds
Latency: 116 ms
Buffer: 154 ms
Drift: -0.7 ms/s
```

## 9. Error UX

Bad:

> WASAPI initialization failed with HRESULT 0x...

Good:

> This audio device could not be started. Try reconnecting it or
> selecting another output.

Advanced diagnostics can expose the technical error separately.

## 10. Responsive Design

Desktop:

``` text
Sidebar | Devices | Player
```

Mobile:

``` text
Header
Devices
Player
Settings
```

The same information hierarchy should remain consistent.

## 11. PWA

The web application should be installable where supported.

However, installation does not grant additional OS-level Bluetooth/audio
privileges.

The native agent remains responsible for privileged functions.

## 12. Accessibility

Support:

-   keyboard navigation;
-   screen readers;
-   high contrast;
-   clear focus states;
-   non-color status indicators;
-   readable error messages.

## 13. Visual Identity

Recommended direction:

-   clean;
-   technical but approachable;
-   minimal;
-   dark/light mode;
-   strong device status hierarchy.

Avoid making the product look like a generic music streaming service.

## 14. Information Architecture

``` text
Dashboard
├── Devices
├── Current Session
├── Playback
├── Synchronization
├── Microphone
├── Saved Groups
└── Settings
```

## 15. Settings

Sections:

### Audio

-   default buffer;
-   output format where supported;
-   synchronization mode.

### Devices

-   automatic reconnect;
-   remembered devices;
-   device naming.

### Advanced

-   diagnostic logging;
-   latency test;
-   agent status.

## 16. First-Run Experience

1.  Install native agent.
2.  Open web application.
3.  Agent connection is detected.
4.  Discover audio outputs.
5.  Select two devices.
6.  Run synchronization test.
7.  Start playback.

The first-run flow should explicitly explain platform limitations rather
than implying every Bluetooth device is supported.

## 17. Core Design Principle

The interface should never expose implementation complexity unless the
user asks for advanced diagnostics.

The product experience is:

``` text
Discover
   ↓
Select
   ↓
Synchronize
   ↓
Play
```
