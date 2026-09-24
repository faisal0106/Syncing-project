using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using MultiAudio.Agent.Audio;
using MultiAudio.Agent.Protocol;
using ProtocolDeviceState = MultiAudio.Agent.Protocol.DeviceState;

namespace MultiAudio.Agent.Devices
{
    /// <summary>
    /// Windows implementation of <see cref="IAudioOutputDevice"/> using WASAPI.
    /// Operates in low-latency shared mode (~20-25ms buffer) and receives real-time
    /// audio blocks from the AudioEngine for multi-device synchronized playback.
    /// </summary>
    public class WindowsAudioOutputDevice : IAudioOutputDevice
    {
        public string Id { get; }
        public string Name { get; }
        public DeviceType Type { get; private set; } = DeviceType.Unknown;
        public ProtocolDeviceState State { get; set; } = ProtocolDeviceState.Available;
        public string[] Capabilities { get; private set; } = Array.Empty<string>();
        public double? LatencyMs { get; private set; }

        private MMDevice? _endpoint;
        private WasapiOut? _output;
        private BufferedWaveProvider? _bufferedWaveProvider;
        private WaveFormat? _mixFormat;
        private readonly object _lock = new();

        // Real synchronization telemetry (rules.md #7, Architecture.md §4).
        private AudioClient? _internalAudioClient;
        private AudioClockClient? _audioClock;
        private ulong _audioClockFrequency;
        private Stopwatch? _playbackClock;
        private readonly DeviceClockTracker _syncTracker = new();
        private string? _lastError;

        // Startup scheduling offset (Architecture.md §4 --
        // device_play_time = target_time + device_specific_compensation).
        private double _schedulingHoldMs;
        private Stopwatch? _schedulingHoldClock;

        // Steady-state buffer target for gentle drift correction
        // (rules.md #14 -- buffering/timing adjustment, not stop/restart).
        private const double TargetBufferMs = 150.0;
        private const double CorrectionToleranceMs = 40.0;
        private const double MaxCorrectionFraction = 0.02;

        public WindowsAudioOutputDevice(string id, string name, ProtocolDeviceState initialState = ProtocolDeviceState.Available)
        {
            Id = id;
            Name = name;
            State = initialState;
            Type = ClassifyType(name);
            Capabilities = new[] { "wasapi", "shared-mode", "low-latency" };
        }

        public Task ConnectAsync()
        {
            lock (_lock)
            {
                try
                {
                    using var enumerator = new MMDeviceEnumerator();
                    _endpoint = enumerator.GetDevice(Id);

                    if (_endpoint.State != NAudio.CoreAudioApi.DeviceState.Active)
                    {
                        State = ProtocolDeviceState.Disconnected;
                        throw new InvalidOperationException($"Device '{Name}' is not active or powered off (Windows state: {_endpoint.State}).");
                    }

                    // Do not open AudioClient during device selection. Some
                    // Windows endpoints reject metadata reads until a render
                    // client is initialized; PLAY performs that probe safely.
                    LatencyMs = null;
                    State = ProtocolDeviceState.Connected;
                }
                catch (Exception ex)
                {
                    State = ProtocolDeviceState.Disconnected;
                    _lastError = ex.Message;
                    Console.WriteLine($"[WindowsAudioOutputDevice] ConnectAsync error for {Name}: {ex.Message}");
                    throw;
                }
            }
            return Task.CompletedTask;
        }

        public Task DisconnectAsync()
        {
            StopAsync();
            lock (_lock)
            {
                _endpoint?.Dispose();
                _endpoint = null;
                State = ProtocolDeviceState.Disconnected;
            }
            return Task.CompletedTask;
        }

        public Task StartAsync()
        {
            lock (_lock)
            {
                try
                {
                    if (_endpoint == null)
                    {
                        using var enumerator = new MMDeviceEnumerator();
                        _endpoint = enumerator.GetDevice(Id);
                    }

                    if (_endpoint.State != NAudio.CoreAudioApi.DeviceState.Active)
                    {
                        throw new InvalidOperationException($"Cannot start '{Name}': device is not active.");
                    }

                    _mixFormat ??= _endpoint.AudioClient.MixFormat;

                    _output?.Dispose();

                    // Keep enough audio queued to absorb Bluetooth scheduling jitter.
                    _bufferedWaveProvider = new BufferedWaveProvider(_mixFormat)
                    {
                        DiscardOnBufferOverflow = true,
                        BufferDuration = TimeSpan.FromMilliseconds(500),
                        ReadFully = true
                    };

                    // Shared mode avoids exclusive-format failures on Bluetooth endpoints.
                    _output = new WasapiOut(_endpoint, AudioClientShareMode.Shared, false, 100);
                    _output.Init(_bufferedWaveProvider);
                    _output.Play();

                    State = ProtocolDeviceState.Playing;
                    _lastError = null;

                    // From here on: real synchronization telemetry
                    // (rules.md #7, Architecture.md §4) instead of the
                    // previous hardcoded zeros. A device that can't
                    // expose these (older driver, no IAudioClock
                    // service) still plays fine -- it just won't have
                    // confident drift/offset numbers, which is a
                    // diagnostics gap, not a playback error.
                    _internalAudioClient = NativeAudioClockAccess.GetAudioClient(_output);
                    LatencyMs = NativeAudioClockAccess.TryGetStreamLatencyMs(_internalAudioClient) ?? LatencyMs;
                    _audioClock = NativeAudioClockAccess.TryGetAudioClock(_internalAudioClient);
                    _audioClockFrequency = 0;
                    if (_audioClock != null)
                    {
                        try
                        {
                            _audioClockFrequency = _audioClock.Frequency;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[WindowsAudioOutputDevice] Could not read audio clock frequency for {Name}: {ex.Message}");
                            _audioClock = null;
                        }
                    }
                    _playbackClock = Stopwatch.StartNew();
                    _syncTracker.Reset();
                    _schedulingHoldMs = 0;
                    _schedulingHoldClock = null;
                }
                catch (Exception ex)
                {
                    _lastError = ex.Message;
                    throw;
                }
            }
            return Task.CompletedTask;
        }

        /// <summary>
        /// Holds this device's real audio output back by
        /// <paramref name="holdMs"/> before it starts feeding buffered
        /// audio to WASAPI, so a device with lower measured hardware
        /// latency doesn't produce its first audible sample noticeably
        /// before a device with higher latency (Architecture.md §4 --
        /// "device_play_time = target_time + device_specific_compensation").
        /// SessionManager calls this once per device, right after every
        /// device in the session has started and reported its real
        /// <see cref="LatencyMs"/> (rules.md #14 -- scheduling and
        /// buffering, not repeated stop/restart, is how devices get
        /// aligned).
        /// </summary>
        public void SetSchedulingOffset(double holdMs)
        {
            lock (_lock)
            {
                _schedulingHoldMs = Math.Max(0, holdMs);
                _schedulingHoldClock = _schedulingHoldMs > 0 ? Stopwatch.StartNew() : null;
            }
        }

        public Task StopAsync()
        {
            lock (_lock)
            {
                try
                {
                    _output?.Stop();
                    _output?.Dispose();
                }
                catch { }
                _output = null;
                _bufferedWaveProvider?.ClearBuffer();
                _bufferedWaveProvider = null;

                _internalAudioClient = null;
                _audioClock = null;
                _audioClockFrequency = 0;
                _playbackClock = null;
                _syncTracker.Reset();
                _schedulingHoldMs = 0;
                _schedulingHoldClock = null;

                if (State == ProtocolDeviceState.Playing)
                    State = ProtocolDeviceState.Connected;
            }
            return Task.CompletedTask;
        }

        public Task SetVolumeAsync(double volume)
        {
            if (volume is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(volume));
            lock (_lock)
            {
                if (_endpoint != null)
                {
                    try
                    {
                        _endpoint.AudioEndpointVolume.MasterVolumeLevelScalar = (float)volume;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[WindowsAudioOutputDevice] Volume error for {Name}: {ex.Message}");
                    }
                }
            }
            return Task.CompletedTask;
        }

        public void EnqueueAudio(byte[] buffer, int offset, int count, WaveFormat sourceFormat)
        {
            if (State != ProtocolDeviceState.Playing || _bufferedWaveProvider == null || _mixFormat == null)
                return;

            lock (_lock)
            {
                if (_bufferedWaveProvider == null || _mixFormat == null) return;

                if (_schedulingHoldClock != null)
                {
                    if (_schedulingHoldClock.Elapsed.TotalMilliseconds < _schedulingHoldMs)
                    {
                        // Holding this lower-latency device back so its
                        // first audible sample lands alongside the
                        // highest-latency output in the session
                        // (Architecture.md §4).
                        return;
                    }
                    _schedulingHoldClock = null;
                }

                // If format matches exactly, add samples directly
                if (sourceFormat.SampleRate == _mixFormat.SampleRate &&
                    sourceFormat.Channels == _mixFormat.Channels &&
                    sourceFormat.Encoding == _mixFormat.Encoding &&
                    sourceFormat.BitsPerSample == _mixFormat.BitsPerSample)
                {
                    AddWithDriftCorrection(buffer, offset, count);
                    return;
                }

                // Convert IEEE float to 16-bit PCM if target is 16-bit
                if (sourceFormat.Encoding == WaveFormatEncoding.IeeeFloat &&
                    _mixFormat.Encoding == WaveFormatEncoding.Pcm &&
                    _mixFormat.BitsPerSample == 16)
                {
                    var floatSpan = MemoryMarshal.Cast<byte, float>(new ReadOnlySpan<byte>(buffer, offset, count));
                    var pcmBytes = new byte[floatSpan.Length * 2];
                    for (int i = 0; i < floatSpan.Length; i++)
                    {
                        var sample = Math.Clamp(floatSpan[i], -1.0f, 1.0f);
                        var pcmVal = (short)(sample * 32767.0f);
                        pcmBytes[i * 2] = (byte)(pcmVal & 0xff);
                        pcmBytes[i * 2 + 1] = (byte)((pcmVal >> 8) & 0xff);
                    }
                    AddWithDriftCorrection(pcmBytes, 0, pcmBytes.Length);
                    return;
                }

                // Convert 16-bit PCM to IEEE float if target is float
                if (sourceFormat.Encoding == WaveFormatEncoding.Pcm &&
                    sourceFormat.BitsPerSample == 16 &&
                    _mixFormat.Encoding == WaveFormatEncoding.IeeeFloat)
                {
                    var pcmSpan = MemoryMarshal.Cast<byte, short>(new ReadOnlySpan<byte>(buffer, offset, count));
                    var floatBytes = new byte[pcmSpan.Length * 4];
                    var floatSpan = MemoryMarshal.Cast<byte, float>(floatBytes);
                    for (int i = 0; i < pcmSpan.Length; i++)
                    {
                        floatSpan[i] = pcmSpan[i] / 32768.0f;
                    }
                    AddWithDriftCorrection(floatBytes, 0, floatBytes.Length);
                    return;
                }

                // Fallback direct copy if formats are broadly compatible
                try
                {
                    AddWithDriftCorrection(buffer, offset, count);
                }
                catch
                {
                    // Ignore format mismatch in edge cases
                }
            }
        }

        /// <summary>
        /// Adds audio to the output buffer, gently trimming or padding a
        /// small (at most 2%) slice of each chunk when the buffer has
        /// drifted outside a tolerance band around the target depth.
        /// This is the "controlled timing adjustment" rules.md #14 asks
        /// for instead of stopping and restarting a device to resync
        /// it -- a few dropped/inserted milliseconds per chunk are
        /// inaudible; a stop/restart is not. Only engages once
        /// <see cref="_syncTracker"/> has enough real samples to be
        /// confident this is sustained drift and not startup jitter.
        /// </summary>
        private void AddWithDriftCorrection(byte[] buffer, int offset, int count)
        {
            if (_bufferedWaveProvider == null || _mixFormat == null)
                return;

            var blockAlign = _mixFormat.BlockAlign;
            if (_syncTracker.HasConfidentEstimate && blockAlign > 0)
            {
                var bufferedMs = _bufferedWaveProvider.BufferedDuration.TotalMilliseconds;
                var deviation = bufferedMs - TargetBufferMs;

                if (deviation > CorrectionToleranceMs)
                {
                    // Buffer running high -- this device is consuming
                    // slower than the source feeds it. Trim a small
                    // slice off this chunk instead of letting the
                    // buffer keep growing toward an overflow discard.
                    var maxTrim = AlignDown((int)(count * MaxCorrectionFraction), blockAlign);
                    if (maxTrim >= blockAlign && count > maxTrim)
                        count -= maxTrim;
                }
                else if (deviation < -CorrectionToleranceMs)
                {
                    // Buffer running low -- this device is consuming
                    // faster than the source feeds it and risks an
                    // underrun. Pad a small amount of silence ahead of
                    // this chunk to pull the buffer back up.
                    var maxPad = AlignDown((int)(count * MaxCorrectionFraction), blockAlign);
                    if (maxPad >= blockAlign)
                        _bufferedWaveProvider.AddSamples(new byte[maxPad], 0, maxPad);
                }
            }

            _bufferedWaveProvider.AddSamples(buffer, offset, count);
        }

        private static int AlignDown(int value, int blockAlign)
        {
            if (blockAlign <= 0) return value;
            return value - (value % blockAlign);
        }

        public DeviceDiagnostics GetDiagnostics()
        {
            lock (_lock)
            {
                SampleSyncTelemetryLocked();

                var bufferDepth = _bufferedWaveProvider?.BufferedDuration.TotalMilliseconds ?? 0;
                return new DeviceDiagnostics
                {
                    BufferDepthMs = Math.Round(bufferDepth, 1),
                    ClockOffsetMs = Math.Round(_syncTracker.ClockOffsetMs, 2),
                    DriftEstimateMsPerSec = Math.Round(_syncTracker.DriftEstimateMsPerSec, 3),
                    DriftEstimateConfident = _syncTracker.HasConfidentEstimate,
                    LastError = _lastError
                };
            }
        }

        /// <summary>
        /// Takes one real measured sample of this device's hardware
        /// playback clock vs wall-clock time (Architecture.md §4).
        /// No-ops harmlessly if the device isn't playing yet or
        /// IAudioClock isn't available -- a device that plays fine but
        /// can't expose IAudioClock is not itself in an error state,
        /// only its sync telemetry is degraded (rules.md #12: never
        /// silently fail, but also never let a diagnostics gap masquerade
        /// as a playback failure).
        /// </summary>
        private void SampleSyncTelemetryLocked()
        {
            if (State != ProtocolDeviceState.Playing) return;
            if (_audioClock == null || _playbackClock == null || _audioClockFrequency == 0) return;

            try
            {
                if (!_audioClock.GetPosition(out var position, out _)) return;
                var devicePositionMs = (double)position / _audioClockFrequency * 1000.0;
                var wallElapsedMs = _playbackClock.Elapsed.TotalMilliseconds;
                _syncTracker.AddSample(wallElapsedMs, devicePositionMs);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WindowsAudioOutputDevice] Sync telemetry sample failed for {Name}: {ex.Message}");
                // Stop retrying every second against an endpoint that
                // doesn't support this rather than logging forever.
                _audioClock = null;
            }
        }

        private static DeviceType ClassifyType(string name)
        {
            var lower = name.ToLowerInvariant();
            if (lower.Contains("headphone") || lower.Contains("headset")) return DeviceType.Headphones;
            if (lower.Contains("earbud") || lower.Contains("airpod") || lower.Contains("airdopes") || lower.Contains("buds")) return DeviceType.Earbuds;
            if (lower.Contains("speaker") || lower.Contains("soundbar")) return DeviceType.Speaker;
            return DeviceType.Unknown;
        }
    }
}
