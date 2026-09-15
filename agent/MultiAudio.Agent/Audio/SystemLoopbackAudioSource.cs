using System;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace MultiAudio.Agent.Audio
{
    /// <summary>
    /// Captures PC system audio in real-time using Windows WASAPI Loopback.
    /// Captures Spotify, YouTube, games, browser, and media players directly
    /// with ultra-low latency.
    /// </summary>
    public sealed class SystemLoopbackAudioSource : IAudioSource
    {
        private WasapiLoopbackCapture? _capture;
        private bool _isActive;
        private double _position;
        private readonly DateTime _startTime = DateTime.UtcNow;

        public string? EndpointId { get; private set; }

        public WaveFormat WaveFormat => _capture?.WaveFormat ?? WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        public bool IsActive => _isActive;
        public double Position => _isActive ? (DateTime.UtcNow - _startTime).TotalSeconds : _position;
        public double Duration => 0; // Live stream has unbounded duration

        public event Action<byte[], int, int>? DataAvailable;

        public SystemLoopbackAudioSource()
        {
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                EndpointId = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia).ID;
            }
            catch
            {
                EndpointId = null;
            }
        }

        public Task StartAsync()
        {
            if (_isActive) return Task.CompletedTask;

            try
            {
                using var enumerator = new MMDeviceEnumerator();
                var defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                EndpointId = defaultDevice.ID;
                _capture = new WasapiLoopbackCapture(defaultDevice);
                _capture.DataAvailable += OnDataAvailable;
                _capture.RecordingStopped += OnRecordingStopped;
                _capture.StartRecording();
                _isActive = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SystemLoopbackAudioSource] Error starting capture: {ex.Message}");
                throw;
            }

            return Task.CompletedTask;
        }

        private void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (e.BytesRecorded > 0)
            {
                DataAvailable?.Invoke(e.Buffer, 0, e.BytesRecorded);
            }
        }

        private void OnRecordingStopped(object? sender, StoppedEventArgs e)
        {
            _isActive = false;
            if (e.Exception != null)
            {
                Console.WriteLine($"[SystemLoopbackAudioSource] Capture stopped with error: {e.Exception.Message}");
            }
        }

        public Task StopAsync()
        {
            if (!_isActive) return Task.CompletedTask;
            _isActive = false;
            _position = Position;

            try
            {
                _capture?.StopRecording();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SystemLoopbackAudioSource] Error stopping capture: {ex.Message}");
            }
            return Task.CompletedTask;
        }

        public Task SeekAsync(double seconds)
        {
            // System loopback is a live capture; seek is a no-op
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _isActive = false;
            try
            {
                _capture?.StopRecording();
                _capture?.Dispose();
            }
            catch { }
            _capture = null;
            EndpointId = null;
        }
    }
}

