using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Wave;
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
            }
            return Task.CompletedTask;
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

                // If format matches exactly, add samples directly
                if (sourceFormat.SampleRate == _mixFormat.SampleRate &&
                    sourceFormat.Channels == _mixFormat.Channels &&
                    sourceFormat.Encoding == _mixFormat.Encoding &&
                    sourceFormat.BitsPerSample == _mixFormat.BitsPerSample)
                {
                    _bufferedWaveProvider.AddSamples(buffer, offset, count);
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
                    _bufferedWaveProvider.AddSamples(pcmBytes, 0, pcmBytes.Length);
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
                    _bufferedWaveProvider.AddSamples(floatBytes, 0, floatBytes.Length);
                    return;
                }

                // Fallback direct copy if formats are broadly compatible
                try
                {
                    _bufferedWaveProvider.AddSamples(buffer, offset, count);
                }
                catch
                {
                    // Ignore format mismatch in edge cases
                }
            }
        }

        public DeviceDiagnostics GetDiagnostics()
        {
            var bufferDepth = _bufferedWaveProvider?.BufferedDuration.TotalMilliseconds ?? 0;
            return new DeviceDiagnostics
            {
                BufferDepthMs = Math.Round(bufferDepth, 1),
                ClockOffsetMs = 0,
                DriftEstimateMsPerSec = 0,
                LastError = null
            };
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
