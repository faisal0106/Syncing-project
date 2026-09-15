using System;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace MultiAudio.Agent.Devices
{
    /// <summary>
    /// Listens for real-time Windows CoreAudio endpoint events (e.g. Bluetooth devices
    /// connecting, disconnecting, powering on/off, or default endpoint changing).
    /// </summary>
    public class DeviceNotificationClient : IMMNotificationClient
    {
        public event Action<string, DeviceState>? DeviceStateChanged;
        public event Action<string>? DeviceAdded;
        public event Action<string>? DeviceRemoved;
        public event Action<DataFlow, Role, string>? DefaultDeviceChanged;

        public void OnDeviceStateChanged(string deviceId, DeviceState newState)
        {
            DeviceStateChanged?.Invoke(deviceId, newState);
        }

        public void OnDeviceAdded(string pwstrDeviceId)
        {
            DeviceAdded?.Invoke(pwstrDeviceId);
        }

        public void OnDeviceRemoved(string deviceId)
        {
            DeviceRemoved?.Invoke(deviceId);
        }

        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
            DefaultDeviceChanged?.Invoke(flow, role, defaultDeviceId);
        }

        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key)
        {
        }
    }
}

