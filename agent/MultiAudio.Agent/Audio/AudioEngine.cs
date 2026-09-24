using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using MultiAudio.Agent.Devices;
using MultiAudio.Agent.Protocol;
using NAudio.Wave;

namespace MultiAudio.Agent.Audio
{
    /// <summary>
    /// Central audio coordinator. Owns the active audio source (System Loopback,
    /// Tone, File, or Mic) and dispatches real-time PCM audio chunks concurrently
    /// to all connected output endpoints with ultra-low latency.
    /// </summary>
    public sealed class AudioEngine : IDisposable
    {
        private readonly object _lock = new();
        private readonly ConcurrentDictionary<string, WindowsAudioOutputDevice> _activeOutputs = new();

        private IAudioSource _currentSource;
        private AudioSourceType _sourceType = AudioSourceType.System;
        private bool _isPlaying;

        public AudioSourceType CurrentSourceType => _sourceType;
        public bool IsPlaying => _isPlaying;
        public double Position => _currentSource.Position;
        public double Duration => _currentSource.Duration;
        public string? SystemLoopbackEndpointId => (_currentSource as SystemLoopbackAudioSource)?.EndpointId;

        public AudioEngine()
        {
            // Default to SystemLoopback (mirrors live PC audio)
            _currentSource = new SystemLoopbackAudioSource();
            _currentSource.DataAvailable += OnAudioDataAvailable;
        }

        public async Task SetSourceAsync(AudioSourceType type, string? filePath = null)
        {
            IAudioSource newSource = type switch
            {
                AudioSourceType.System => new SystemLoopbackAudioSource(),
                AudioSourceType.Tone => new ToneAudioSource(),
                AudioSourceType.File => new FileAudioSource(filePath ?? ""),
                AudioSourceType.Mic => new MicrophoneAudioSource(),
                _ => new SystemLoopbackAudioSource()
            };

            var wasPlaying = _isPlaying;
            if (wasPlaying)
            {
                await _currentSource.StopAsync();
            }

            lock (_lock)
            {
                _currentSource.DataAvailable -= OnAudioDataAvailable;
                _currentSource.Dispose();
                _currentSource = newSource;
                _sourceType = type;
                _currentSource.DataAvailable += OnAudioDataAvailable;
            }

            if (wasPlaying)
            {
                await _currentSource.StartAsync();
            }
        }

        public void RegisterDevice(WindowsAudioOutputDevice device)
        {
            _activeOutputs[device.Id] = device;
        }

        public void UnregisterDevice(string deviceId)
        {
            _activeOutputs.TryRemove(deviceId, out _);
        }

        /// <summary>
        /// Devices currently registered to receive real-time audio.
        /// SessionManager uses this to compute a newly-joining device's
        /// scheduling offset (Architecture.md §4) against whatever is
        /// already playing, the same way it aligns every device at the
        /// start of a session.
        /// </summary>
        public IEnumerable<WindowsAudioOutputDevice> RegisteredDevices => _activeOutputs.Values;

        public async Task StartAsync()
        {
            lock (_lock)
            {
                _isPlaying = true;
            }
            await _currentSource.StartAsync();
        }

        public async Task PauseAsync()
        {
            lock (_lock)
            {
                _isPlaying = false;
            }
            await _currentSource.StopAsync();
        }

        public async Task StopAsync()
        {
            lock (_lock)
            {
                _isPlaying = false;
            }
            await _currentSource.StopAsync();
            await _currentSource.SeekAsync(0);
        }

        public Task SeekAsync(double position)
        {
            return _currentSource.SeekAsync(position);
        }

        private void OnAudioDataAvailable(byte[] buffer, int offset, int count)
        {
            if (!_isPlaying || count <= 0) return;

            var sourceFormat = _currentSource.WaveFormat;

            // Fan out audio chunks to all active output devices simultaneously
            foreach (var output in _activeOutputs.Values)
            {
                // The Windows default endpoint is already receiving the
                // original system audio. Do not feed its loopback capture
                // back into itself, which would create feedback/noise.
                if (output.Id == SystemLoopbackEndpointId)
                    continue;

                try
                {
                    output.EnqueueAudio(buffer, offset, count, sourceFormat);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[AudioEngine] Error dispatching to {output.Name}: {ex.Message}");
                }
            }
        }

        public void Dispose()
        {
            _isPlaying = false;
            _currentSource.DataAvailable -= OnAudioDataAvailable;
            _currentSource.Dispose();
            _activeOutputs.Clear();
        }
    }
}

