using System;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace MultiAudio.Agent.Audio
{
    /// <summary>
    /// Captures live microphone input from the default recording device
    /// and broadcasts it in real time to all connected outputs.
    /// </summary>
    public sealed class MicrophoneAudioSource : IAudioSource
    {
        private WasapiCapture? _capture;
        private bool _isActive;
        private double _position;
        private readonly DateTime _startTime = DateTime.UtcNow;

        public WaveFormat WaveFormat => _capture?.WaveFormat ?? WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        public bool IsActive => _isActive;
        public double Position => _isActive ? (DateTime.UtcNow - _startTime).TotalSeconds : _position;
        public double Duration => 0; // Live input

        public event Action<byte[], int, int>? DataAvailable;

        public Task StartAsync()
        {
            if (_isActive) return Task.CompletedTask;

            try
            {
                using var enumerator = new MMDeviceEnumerator();
                var defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
                _capture = new WasapiCapture(defaultDevice);
                _capture.DataAvailable += (s, e) =>
                {
                    if (e.BytesRecorded > 0)
                    {
                        DataAvailable?.Invoke(e.Buffer, 0, e.BytesRecorded);
                    }
                };
                _capture.RecordingStopped += (s, e) => _isActive = false;
                _capture.StartRecording();
                _isActive = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MicrophoneAudioSource] Error starting capture: {ex.Message}");
                throw;
            }

            return Task.CompletedTask;
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
                Console.WriteLine($"[MicrophoneAudioSource] Error stopping mic: {ex.Message}");
            }
            return Task.CompletedTask;
        }

        public Task SeekAsync(double seconds) => Task.CompletedTask;

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
        }
    }
}

