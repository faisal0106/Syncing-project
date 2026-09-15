# MultiAudio --- Engineering Rules

## 1. Core Rule

Do not confuse **Bluetooth device connectivity** with **audio routing
capability**.

A device being visible over Bluetooth does not imply that an application
can independently route audio to it.

## 2. Platform Rule

Every platform must have an explicit capability layer.

Never assume:

``` text
Windows capability == Android capability == iOS capability
```

## 3. Browser Rule

The web application is primarily the **control and presentation layer**.

Do not depend on browser APIs for functionality that requires privileged
OS-level audio routing unless that capability has been verified on the
target browser/platform.

## 4. Native Agent Rule

All privileged audio/device operations must be isolated behind a native
platform agent.

## 5. Synchronization Rule

Never assume that two outputs have equal latency.

Every output must be treated as having potentially different:

-   connection latency;
-   buffering latency;
-   device processing latency;
-   clock drift.

## 6. Audio Quality Rule

Do not sacrifice audio quality merely to simplify synchronization.

Prefer:

-   stable sample formats;
-   controlled resampling;
-   bounded buffers;
-   proper underrun handling.

## 7. Latency Rule

Latency should be measured rather than guessed.

If a value is hardware-dependent, expose it as a measured/estimated
property.

## 8. Recovery Rule

A single output failure must not automatically terminate the entire
session unless continuing would produce unsafe or corrupted behavior.

## 9. Security Rule

Never expose a localhost control API without authentication/origin
validation.

Do not accept arbitrary browser commands.

## 10. Privacy Rule

Audio must remain local by default.

Do not upload microphone or media data to a cloud server unless the user
explicitly enables a feature requiring it.

## 11. Dependency Rule

Do not build the project around undocumented operating-system behavior.

If an implementation depends on undocumented behavior, isolate it and
mark it as experimental.

## 12. Compatibility Rule

Use capability detection:

``` text
if supported:
    enable feature
else:
    explain limitation
```

Never silently fail.

## 13. Testing Rule

Every audio feature must be tested with:

-   at least two devices;
-   different device models;
-   reconnect scenarios;
-   long playback;
-   pause/resume;
-   seeking;
-   high CPU conditions.

## 14. Synchronization Rule

Do not synchronize by repeatedly starting devices manually.

Use:

-   shared timing;
-   scheduling;
-   buffering;
-   measured latency;
-   controlled drift correction.

## 15. Architecture Rule

Keep these layers independent:

``` text
UI
|
Control API
|
Session Manager
|
Audio Engine
|
Platform Adapter
|
OS
```

## 16. Naming Rule

Use precise terminology:

-   Master = authoritative controller.
-   Output = physical audio destination.
-   Agent = native platform component.
-   Session = one synchronized playback instance.
-   Device = discovered physical/logical audio endpoint.
-   Transport = mechanism used to deliver audio.

## 17. Scope Rule

Do not add cloud infrastructure merely because the product has a web
interface.

Local operation should be possible without Internet access whenever
platform capabilities permit.

## 18. Experimental Feature Rule

Auracast, LE Audio, Android multi-output behavior and iOS capabilities
must be treated as separate capability tracks until experimentally
verified.

## 19. Documentation Rule

Every platform limitation discovered during development must be
documented in Architecture.md or a platform-specific technical note.

## 20. Product Rule

The product is not defined by "two AirPods."

The product is defined by:

> One Master → multiple synchronized audio outputs.

The number and type of outputs are implementation capabilities, not the
fundamental product definition.
