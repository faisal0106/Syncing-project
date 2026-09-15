# MultiAudio --- Development Phases

## Phase 0 --- Feasibility Research

### Objective

Determine exactly what the target operating system permits.

### Target

Windows desktop + two Bluetooth audio devices.

### Tasks

-   Investigate Windows audio endpoint APIs.
-   Enumerate Bluetooth audio devices.
-   Determine whether two independent Bluetooth audio endpoints can be
    rendered simultaneously.
-   Build a minimal proof of concept.
-   Measure latency differences.
-   Determine whether WASAPI/shared-mode/exclusive-mode approaches are
    suitable.
-   Document hard OS limitations.

### Exit Criteria

A technical prototype proves the minimum viable audio path or identifies
the precise blocker.

------------------------------------------------------------------------

## Phase 1 --- Two-Output Prototype

### Objective

Play the same local audio file through two supported Bluetooth outputs.

### Features

-   device discovery;
-   device selection;
-   playback;
-   pause;
-   stop;
-   basic volume;
-   diagnostics.

### No Web UI Yet

Keep this phase focused on the audio engine.

### Exit Criteria

Two outputs play continuously for at least 30 minutes without crashes or
major synchronization failure.

------------------------------------------------------------------------

## Phase 2 --- Synchronization Engine

### Objective

Reduce audible differences between outputs.

### Features

-   timestamped audio blocks;
-   per-device latency measurement;
-   buffering;
-   playback scheduling;
-   drift detection;
-   controlled correction.

### Metrics

Track:

``` text
latency_A
latency_B
difference
buffer_depth
drift
underruns
```

### Exit Criteria

Normal music and speech do not exhibit an obvious echo under tested
hardware configurations.

------------------------------------------------------------------------

## Phase 3 --- Native Agent

### Objective

Separate the audio engine from the UI.

### Architecture

``` text
Web UI
  |
WebSocket
  |
Native Agent
  |
Audio Engine
```

### Features

-   secure local API;
-   device enumeration;
-   playback commands;
-   status reporting;
-   diagnostics.

### Exit Criteria

A browser UI can control the native agent without directly implementing
privileged audio operations.

------------------------------------------------------------------------

## Phase 4 --- Web Application

### Objective

Create the cross-platform control interface.

### Features

-   responsive UI;
-   device cards;
-   connection state;
-   playback controls;
-   synchronization information;
-   settings;
-   error messages.

### Recommended Stack

``` text
React
TypeScript
PWA
WebSocket
```

### Exit Criteria

The same web frontend works with the Windows native agent.

------------------------------------------------------------------------

## Phase 5 --- Productization

### Features

-   installer;
-   background agent;
-   automatic startup option;
-   diagnostics;
-   logs;
-   crash recovery;
-   device reconnection;
-   saved device groups.

### Exit Criteria

A non-technical user can install and operate the system.

------------------------------------------------------------------------

## Phase 6 --- Microphone

### Objective

Add live microphone audio.

### Pipeline

``` text
Microphone
    |
Noise/format processing
    |
Mixer
    |
Synchronization Engine
    |
Outputs
```

### Features

-   microphone enable/disable;
-   music + voice mixing;
-   microphone gain;
-   mute.

### Exit Criteria

Voice is transmitted with acceptable latency and does not destabilize
music playback.

------------------------------------------------------------------------

## Phase 7 --- Android

### Objective

Implement an Android-specific native agent.

### Tasks

-   investigate Android Bluetooth audio capabilities;
-   evaluate multiple output support;
-   evaluate LE Audio;
-   implement Android-specific audio APIs;
-   reuse the existing web frontend.

### Exit Criteria

Supported Android configurations can operate as a Master where
technically permitted.

------------------------------------------------------------------------

## Phase 8 --- macOS

### Objective

Create a macOS audio agent.

### Tasks

-   Core Audio integration;
-   Bluetooth endpoint handling;
-   aggregate/multi-output research;
-   synchronization testing.

------------------------------------------------------------------------

## Phase 9 --- Bluetooth LE Audio / Auracast

### Objective

Investigate broadcast-oriented audio.

### Tasks

-   compatible hardware identification;
-   LE Audio capability detection;
-   Auracast transmitter feasibility;
-   broadcast discovery;
-   receiver compatibility;
-   latency characteristics.

### Exit Criteria

A tested prototype demonstrates broadcast audio on compatible hardware.

------------------------------------------------------------------------

## Phase 10 --- Advanced Product

Potential features:

-   more than two outputs;
-   device groups;
-   presets;
-   multi-room mode;
-   adaptive buffering;
-   master handover;
-   analytics stored locally;
-   accessibility features;
-   network-based receiver mode.

------------------------------------------------------------------------

## Phase Priorities

``` text
P0  Feasibility
 ↓
P1  Two-output audio
 ↓
P2  Synchronization
 ↓
P3  Native Agent
 ↓
P4  Web UI
 ↓
P5  Productization
 ↓
P6  Microphone
 ↓
P7  Android
 ↓
P8  macOS
 ↓
P9  LE Audio / Auracast
 ↓
P10 Advanced platform
```

Do not skip P0. The most important technical risk is the host operating
system's ability to render to multiple Bluetooth audio endpoints.
