using System;
using System.Threading;
using System.Threading.Tasks;
using MultiAudio.Agent.Protocol;

namespace MultiAudio.Agent.Devices
{
    /// <summary>
    /// A fake <see cref="IAudioOutputDevice"/> that never touches real
    /// audio hardware. It exists so the rest of the stack — control
    /// protocol, session manager, synchronization telemetry, web UI —
    /// can be built and exercised end-to-end before Phase 0's WASAPI
    /// feasibility research (phases.md) lands.
    ///
    /// rules.md #11: do not build around undocumented OS behavior, and
    /// isolate/mark anything that does as experimental. The inverse
    /// applies here too — this class must never be mistaken for a real
    /// implementation. It is only ever selected by
    /// <see cref="AgentPlatform.IsSimulated"/> callers, and
    /// <see cref="Type"/>/<see cref="Capabilities"/> always self-report
    /// as "simulated" so the UI/tests can tell.
    ///
    /// Latency/drift numbers are deliberately randomized per instance
    /// (rules.md #5 — never assume equal latency across outputs) rather
    /// than hardcoded to the same value, so downstream sync-state logic
    /// can't accidentally rely on outputs being identical.
    /// </summary>
    public class SimulatedAudioOutputDevice : IAudioOutputDevice
    {
        private static readonly Random Rng = new();

        private readonly double _simulatedConnectLatencyMs;
        private readonly double _simulatedDriftMsPerSec;
        private double _bufferDepthMs;
        private double _clockOffsetMs;
        private CancellationTokenSource? _driftLoopCts;

        public string Id { get; }
        public string Name { get; }
        public DeviceType Type { get; }
        public DeviceState State { get; private set; } = DeviceState.Available;
        public string[] Capabilities { get; } = { "simulated" };
        public double? LatencyMs { get; private set; }

        public SimulatedAudioOutputDevice(string id, string name, DeviceType type)
        {
            Id = id;
            Name = name;
            Type = type;

            // Spread simulated devices out so they don't look artificially
            // identical (rules.md #5, #7 — latency is measured/estimated,
            // never assumed uniform).
            _simulatedConnectLatencyMs = 60 + Rng.NextDouble() * 140;   // 60-200ms
            _simulatedDriftMsPerSec = (Rng.NextDouble() - 0.5) * 0.4;   // +/-0.2 ms/s
        }

        public async Task ConnectAsync()
        {
            if (State is DeviceState.Connected or DeviceState.Playing or DeviceState.Paused)
                return;

            State = DeviceState.Connecting;
            // Simulate the real-world variability of a BT connect handshake.
            await Task.Delay(TimeSpan.FromMilliseconds(_simulatedConnectLatencyMs));

            LatencyMs = Math.Round(_simulatedConnectLatencyMs, 1);
            _bufferDepthMs = 120;
            _clockOffsetMs = 0;
            State = DeviceState.Connected;
        }

        public Task DisconnectAsync()
        {
            StopDriftLoop();
            State = DeviceState.Disconnected;
            LatencyMs = null;
            return Task.CompletedTask;
        }

        public Task StartAsync()
        {
            if (State is not (DeviceState.Connected or DeviceState.Paused))
                throw new InvalidOperationException($"Cannot start device '{Id}' from state {State}.");

            State = DeviceState.Playing;
            StartDriftLoop();
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            StopDriftLoop();
            if (State is DeviceState.Playing or DeviceState.Paused)
                State = DeviceState.Connected;
            return Task.CompletedTask;
        }

        public Task SetVolumeAsync(double volume)
        {
            if (volume is < 0 or > 1)
                throw new ArgumentOutOfRangeException(nameof(volume), "Volume must be within 0.0-1.0.");
            // Nothing to set on a simulated device; real implementations
            // apply this to the endpoint/session volume (Architecture.md §5).
            return Task.CompletedTask;
        }

        public DeviceDiagnostics GetDiagnostics() => new()
        {
            BufferDepthMs = Math.Round(_bufferDepthMs, 1),
            ClockOffsetMs = Math.Round(_clockOffsetMs, 2),
            DriftEstimateMsPerSec = Math.Round(_simulatedDriftMsPerSec, 3),
            LastError = null
        };

        /// <summary>
        /// Nudges clock offset over time so SessionManager's periodic SYNC
        /// broadcasts show believable, non-static drift while a device is
        /// "playing" (Architecture.md §4). Runs until stopped/disposed.
        /// </summary>
        private void StartDriftLoop()
        {
            StopDriftLoop();
            _driftLoopCts = new CancellationTokenSource();
            var token = _driftLoopCts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        await Task.Delay(500, token);
                        _clockOffsetMs += _simulatedDriftMsPerSec * 0.5;
                    }
                }
                catch (OperationCanceledException)
                {
                    // expected on stop/disconnect
                }
            }, token);
        }

        private void StopDriftLoop()
        {
            _driftLoopCts?.Cancel();
            _driftLoopCts?.Dispose();
            _driftLoopCts = null;
        }
    }
}
