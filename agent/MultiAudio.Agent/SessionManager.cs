using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using MultiAudio.Agent.Audio;
using MultiAudio.Agent.Devices;
using MultiAudio.Agent.Protocol;
using CoreAudioDeviceState = NAudio.CoreAudioApi.DeviceState;
using ProtocolDeviceState = MultiAudio.Agent.Protocol.DeviceState;

namespace MultiAudio.Agent
{
    public class ProtocolException : Exception
    {
        public ErrorCode Code { get; }
        public string? DeviceId { get; }

        public ProtocolException(ErrorCode code, string message, string? deviceId = null)
            : base(message)
        {
            Code = code;
            DeviceId = deviceId;
        }
    }

    /// <summary>
    /// Owns device discovery, hardware change notifications, multi-device session state,
    /// and coordinates the AudioEngine for low-latency multi-output playback.
    /// </summary>
    public class SessionManager : IDisposable
    {
        private readonly object _lock = new();
        private readonly Dictionary<string, IAudioOutputDevice> _devices = new();
        private readonly Dictionary<string, Session> _sessions = new();
        private readonly AudioEngine _audioEngine = new();
        private readonly MMDeviceEnumerator _enumerator = new();
        private readonly DeviceNotificationClient _notificationClient = new();

        public event Action? DevicesChanged;

        private class Session
        {
            public string Id { get; init; } = "";
            public string Name { get; init; } = "";
            public PlaybackState PlaybackState { get; set; } = PlaybackState.Stopped;
            public double Position { get; set; }
            public double Volume { get; set; } = 1.0;
            public AudioSourceType AudioSource { get; set; } = AudioSourceType.System;
            // deviceId -> enabled-within-session
            public Dictionary<string, bool> DeviceEnabled { get; } = new();
        }

        public SessionManager()
        {
            RefreshDevices();

            // Register Windows CoreAudio notification callback
            try
            {
                _notificationClient.DeviceStateChanged += (id, state) => OnHardwareDeviceChanged();
                _notificationClient.DeviceAdded += id => OnHardwareDeviceChanged();
                _notificationClient.DeviceRemoved += id => OnHardwareDeviceChanged();
                _notificationClient.DefaultDeviceChanged += (flow, role, id) => OnHardwareDeviceChanged();
                _enumerator.RegisterEndpointNotificationCallback(_notificationClient);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SessionManager] Could not register IMMNotificationClient: {ex.Message}");
            }
        }

        private void OnHardwareDeviceChanged()
        {
            lock (_lock)
            {
                RefreshDevices();
            }
            DevicesChanged?.Invoke();
        }

        public void RefreshDevices()
        {
            lock (_lock)
            {
                try
                {
                    // Enumerate all render devices: Active, Disabled, NotPresent, Unplugged
                    var endpoints = _enumerator.EnumerateAudioEndPoints(DataFlow.Render, CoreAudioDeviceState.All);
                    var seenIds = new HashSet<string>();

                    foreach (var endpoint in endpoints)
                    {
                        try
                        {
                            var id = endpoint.ID;
                            var name = endpoint.FriendlyName;
                            var state = endpoint.State;
                            seenIds.Add(id);

                            // Skip endpoints without friendly name
                            if (string.IsNullOrWhiteSpace(name))
                                continue;

                            if (_devices.TryGetValue(id, out var existing))
                            {
                                // Update state if changed
                                if (existing is WindowsAudioOutputDevice winDev)
                                {
                                    if (state == CoreAudioDeviceState.Active)
                                    {
                                        if (winDev.State == ProtocolDeviceState.Disconnected)
                                            winDev.State = ProtocolDeviceState.Available;
                                    }
                                    else if (state == CoreAudioDeviceState.NotPresent || state == CoreAudioDeviceState.Unplugged)
                                    {
                                        if (winDev.State == ProtocolDeviceState.Playing)
                                        {
                                            // The endpoint vanished mid-playback (Bluetooth
                                            // headphones powered off, walked out of range,
                                            // etc.). Stop feeding it and pull it out of the
                                            // AudioEngine's fan-out immediately, instead of
                                            // leaving its state as Playing and letting every
                                            // audio callback hit-and-log a write to a dead
                                            // WASAPI endpoint (rules.md #12 -- never fail
                                            // silently, but a real disconnect shouldn't turn
                                            // into per-callback log spam either).
                                            _audioEngine.UnregisterDevice(id);
                                            try
                                            {
                                                // StartAsync/StopAsync do their real work
                                                // synchronously under their own lock and
                                                // only wrap it in Task.CompletedTask, so
                                                // this cannot deadlock or block on I/O.
                                                winDev.StopAsync().GetAwaiter().GetResult();
                                            }
                                            catch (Exception stopEx)
                                            {
                                                Console.WriteLine($"[SessionManager] Error stopping vanished device '{name}': {stopEx.Message}");
                                            }
                                            Console.WriteLine($"[SessionManager] '{name}' disconnected while playing -- removed from active playback.");
                                        }
                                        winDev.State = ProtocolDeviceState.Disconnected;
                                    }
                                }
                            }
                            else
                            {
                                // New device found
                                var protoState = state == CoreAudioDeviceState.Active
                                    ? ProtocolDeviceState.Available
                                    : ProtocolDeviceState.Disconnected;

                                var dev = new WindowsAudioOutputDevice(id, name, protoState);
                                _devices[id] = dev;
                            }
                        }
                        catch (Exception endpointError)
                        {
                            Console.WriteLine($"[SessionManager] Skipping unavailable endpoint: {endpointError.Message}");
                        }
                        finally
                        {
                            endpoint.Dispose();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[SessionManager] Error refreshing devices: {ex.Message}");
                }
            }
        }

        // ---------- Devices ----------

        public List<AudioOutputDevice> GetDeviceListSnapshot()
        {
            lock (_lock)
            {
                RefreshDevices();
                return _devices.Values.Select(ToDto).ToList();
            }
        }

        public async Task<AudioOutputDevice> ConnectDeviceAsync(string deviceId)
        {
            var device = GetDeviceOrThrow(deviceId);
            try
            {
                await device.ConnectAsync();
            }
            catch (Exception ex) when (ex is not ProtocolException)
            {
                throw new ProtocolException(ErrorCode.DEVICE_CONNECT_FAILED, ex.Message, deviceId);
            }
            return ToDto(device);
        }

        public async Task<AudioOutputDevice> DisconnectDeviceAsync(string deviceId)
        {
            var device = GetDeviceOrThrow(deviceId);
            await device.DisconnectAsync();
            return ToDto(device);
        }

        private IAudioOutputDevice GetDeviceOrThrow(string deviceId)
        {
            lock (_lock)
            {
                if (_devices.TryGetValue(deviceId, out var device))
                    return device;
            }
            throw new ProtocolException(ErrorCode.DEVICE_NOT_FOUND, $"No device with id '{deviceId}'.", deviceId);
        }

        private static AudioOutputDevice ToDto(IAudioOutputDevice d) => new()
        {
            Id = d.Id,
            Name = d.Name,
            Type = d.Type,
            State = d.State,
            Capabilities = d.Capabilities.ToList(),
            LatencyMs = d.LatencyMs
        };

        // ---------- Sessions ----------

        public string CreateSession(string name, List<string> deviceIds, AudioSourceType audioSource = AudioSourceType.System)
        {
            if (deviceIds.Count == 0)
                throw new ProtocolException(ErrorCode.INVALID_MESSAGE, "CREATE_SESSION requires at least one deviceId.");

            lock (_lock)
            {
                foreach (var id in deviceIds)
                {
                    if (!_devices.ContainsKey(id))
                        throw new ProtocolException(ErrorCode.DEVICE_NOT_FOUND, $"No device with id '{id}'.", id);
                }

                var session = new Session
                {
                    Id = Guid.NewGuid().ToString("n"),
                    Name = name,
                    AudioSource = audioSource
                };
                foreach (var id in deviceIds)
                    session.DeviceEnabled[id] = true;

                _sessions[session.Id] = session;
                return session.Id;
            }
        }

        public async Task SetAudioSourceAsync(string sessionId, AudioSourceType source, string? filePath = null)
        {
            var session = GetSessionOrThrow(sessionId);
            session.AudioSource = source;
            await _audioEngine.SetSourceAsync(source, filePath);
        }

        public async Task PlayAsync(string sessionId, double position)
        {
            var (session, devices) = GetSessionAndEnabledDevices(sessionId);
            session.Position = position;

            // Phase 1: start each device's native output. This is also
            // when a WindowsAudioOutputDevice measures its real WASAPI
            // latency (rules.md #7), so every device's LatencyMs is
            // known by the time this finishes -- before any device is
            // registered with the AudioEngine and real audio starts
            // flowing to it.
            var failures = new List<(string DeviceId, Exception Error)>();
            await Task.WhenAll(devices.Select(async d =>
            {
                try
                {
                    await d.StartAsync();
                }
                catch (Exception ex)
                {
                    lock (failures) failures.Add((d.Id, ex));
                }
            }));

            // If all devices failed, report failure
            if (failures.Count > 0 && failures.Count == devices.Count)
            {
                session.PlaybackState = PlaybackState.Stopped;
                throw new ProtocolException(ErrorCode.AUDIO_INIT_FAILED,
                    $"All {failures.Count} device(s) failed to start: {failures.First().Error.Message}");
            }

            var started = devices.Where(d => !failures.Any(f => f.DeviceId == d.Id)).ToList();
            var winDevices = started.OfType<WindowsAudioOutputDevice>().ToList();

            // Phase 2: now that every device's real hardware latency is
            // known, compute each one's scheduling offset (Architecture.md
            // §4 -- target_time = master_clock + scheduling_offset) so a
            // device with lower latency than the slowest one in the
            // session holds its first real audio back by the difference,
            // rather than racing ahead of it. rules.md #14: this is the
            // scheduling/buffering-based alignment that rule asks for
            // instead of stopping and restarting devices to resync them.
            var measuredLatencies = winDevices.Where(d => d.LatencyMs.HasValue).Select(d => d.LatencyMs!.Value).ToList();
            if (measuredLatencies.Count > 0)
            {
                var maxLatency = measuredLatencies.Max();
                foreach (var d in winDevices)
                {
                    d.SetSchedulingOffset(maxLatency - (d.LatencyMs ?? maxLatency));
                }
            }

            foreach (var d in winDevices)
            {
                _audioEngine.RegisterDevice(d);
            }

            // Start real-time audio distribution
            await _audioEngine.StartAsync();
            session.PlaybackState = PlaybackState.Playing;
        }

        public async Task PauseAsync(string sessionId)
        {
            var (session, devices) = GetSessionAndEnabledDevices(sessionId);
            await _audioEngine.PauseAsync();
            await Task.WhenAll(devices.Select(d => d.StopAsync()));
            session.PlaybackState = PlaybackState.Paused;
        }

        public async Task StopAsync(string sessionId)
        {
            var (session, devices) = GetSessionAndEnabledDevices(sessionId);
            await _audioEngine.StopAsync();
            foreach (var d in devices)
            {
                _audioEngine.UnregisterDevice(d.Id);
                await d.StopAsync();
            }
            session.PlaybackState = PlaybackState.Stopped;
            session.Position = 0;
        }

        public async Task SeekAsync(string sessionId, double position)
        {
            var (session, _) = GetSessionAndEnabledDevices(sessionId);
            session.Position = Math.Max(0, position);
            await _audioEngine.SeekAsync(session.Position);
        }

        public async Task SetVolumeAsync(string sessionId, double volume)
        {
            if (volume is < 0 or > 1)
                throw new ProtocolException(ErrorCode.INVALID_MESSAGE, "volume must be within 0.0-1.0.");

            var (session, devices) = GetSessionAndEnabledDevices(sessionId);
            session.Volume = volume;
            await Task.WhenAll(devices.Select(d => d.SetVolumeAsync(volume)));
        }

        public async Task SetDeviceEnabledAsync(string sessionId, string deviceId, bool enabled)
        {
            Session session;
            lock (_lock)
            {
                session = GetSessionOrThrow(sessionId);
                if (!_devices.ContainsKey(deviceId))
                    throw new ProtocolException(ErrorCode.DEVICE_NOT_FOUND, $"No device with id '{deviceId}'.", deviceId);
                session.DeviceEnabled[deviceId] = enabled;
            }

            var device = _devices[deviceId];
            if (session.PlaybackState == PlaybackState.Playing)
            {
                if (enabled)
                {
                    await device.StartAsync();
                    if (device is WindowsAudioOutputDevice winDev)
                    {
                        // Align this newly-joining device against
                        // whatever the session's slowest-latency output
                        // currently is, the same startup alignment
                        // PlayAsync does for a fresh session
                        // (Architecture.md §4).
                        var latencies = _audioEngine.RegisteredDevices
                            .Where(d => d.LatencyMs.HasValue)
                            .Select(d => d.LatencyMs!.Value)
                            .ToList();
                        if (winDev.LatencyMs.HasValue && latencies.Count > 0)
                        {
                            var maxLatency = Math.Max(latencies.Max(), winDev.LatencyMs.Value);
                            winDev.SetSchedulingOffset(maxLatency - winDev.LatencyMs.Value);
                        }
                        _audioEngine.RegisterDevice(winDev);
                    }
                }
                else
                {
                    _audioEngine.UnregisterDevice(deviceId);
                    await device.StopAsync();
                }
            }
        }

        public SessionState GetSessionStateSnapshot(string sessionId)
        {
            var (session, devices) = GetSessionAndEnabledDevices(sessionId, includeDisabled: true);
            lock (_lock)
            {
                return new SessionState
                {
                    SessionId = session.Id,
                    PlaybackState = session.PlaybackState,
                    Position = session.PlaybackState == PlaybackState.Playing ? _audioEngine.Position : session.Position,
                    Volume = session.Volume,
                    AudioSource = session.AudioSource,
                    Devices = session.DeviceEnabled.Keys
                        .Where(id => _devices.ContainsKey(id))
                        .Select(id => ToSyncInfo(_devices[id]))
                        .ToList()
                };
            }
        }

        public List<DeviceSyncInfo> GetSyncSnapshot(string sessionId)
        {
            var (session, _) = GetSessionAndEnabledDevices(sessionId, includeDisabled: true);
            lock (_lock)
            {
                return session.DeviceEnabled.Keys
                    .Where(id => _devices.ContainsKey(id))
                    .Select(id => ToSyncInfo(_devices[id]))
                    .ToList();
            }
        }

        public IEnumerable<string> ActiveSessionIds
        {
            get { lock (_lock) return _sessions.Keys.ToList(); }
        }

        private static DeviceSyncInfo ToSyncInfo(IAudioOutputDevice d)
        {
            var diag = d.GetDiagnostics();

            // Real per-device sync classification (Architecture.md §4):
            // give the drift estimator a few seconds of real samples
            // before trusting it, so startup noise doesn't get reported
            // as "Synced" or "Degraded". Thresholds are deliberately
            // loose -- consumer audio clocks (crystal-oscillator
            // tolerance) are usually well under 1 ms/s; several ms/s
            // sustained means something is actually wrong, not just
            // normal hardware variation.
            var syncState = diag.LastError != null ? SyncState.Degraded
                : !diag.DriftEstimateConfident ? SyncState.Syncing
                : Math.Abs(diag.DriftEstimateMsPerSec) < 2.0 ? SyncState.Synced
                : Math.Abs(diag.DriftEstimateMsPerSec) < 8.0 ? SyncState.Syncing
                : SyncState.Degraded;

            return new DeviceSyncInfo
            {
                DeviceId = d.Id,
                MeasuredLatencyMs = d.LatencyMs ?? 25.0,
                BufferDepthMs = diag.BufferDepthMs,
                ClockOffsetMs = diag.ClockOffsetMs,
                DriftEstimateMsPerSec = diag.DriftEstimateMsPerSec,
                SyncState = syncState
            };
        }

        private (Session Session, List<IAudioOutputDevice> Devices) GetSessionAndEnabledDevices(
            string sessionId, bool includeDisabled = false)
        {
            lock (_lock)
            {
                var session = GetSessionOrThrow(sessionId);
                var deviceIds = includeDisabled
                    ? session.DeviceEnabled.Keys
                    : session.DeviceEnabled.Where(kv => kv.Value).Select(kv => kv.Key);
                var devices = deviceIds.Where(id => _devices.ContainsKey(id)).Select(id => _devices[id]).ToList();
                return (session, devices);
            }
        }

        private Session GetSessionOrThrow(string sessionId)
        {
            if (_sessions.TryGetValue(sessionId, out var session))
                return session;
            throw new ProtocolException(ErrorCode.SESSION_NOT_FOUND, $"No session with id '{sessionId}'.");
        }

        public void Dispose()
        {
            _audioEngine.Dispose();
            try
            {
                _enumerator.UnregisterEndpointNotificationCallback(_notificationClient);
                _enumerator.Dispose();
            }
            catch { }
        }
    }
}
