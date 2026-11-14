using System.Management;

namespace HubDisplaySwitch.Services;

public sealed record UsbDevice(string DeviceId, string Name)
{
    /// <summary>"USB\VID_1A40&amp;PID_0101" – identifies the model regardless of port.</summary>
    public string ModelPrefix => UsbDevices.ModelPrefix(DeviceId);
    public bool IsHub => Name.Contains("hub", StringComparison.OrdinalIgnoreCase);
    public override string ToString()
    {
        var vidPid = ModelPrefix.Replace("USB\\", "").Replace("VID_", "").Replace("&PID_", ":");
        return $"{Name}  ({vidPid})";
    }
}

/// <summary>USB device enumeration and change notifications via WMI.</summary>
public sealed class UsbDevices : IDisposable
{
    public static string ModelPrefix(string deviceId)
    {
        var parts = deviceId.Split('\\');
        return parts.Length >= 2 ? $"{parts[0]}\\{parts[1]}" : deviceId;
    }

    /// <summary>Present USB devices (top-level USB\ entries only).</summary>
    public static List<UsbDevice> List()
    {
        var list = new List<UsbDevice>();
        using var searcher = new ManagementObjectSearcher(@"SELECT DeviceID, Name FROM Win32_PnPEntity WHERE DeviceID LIKE 'USB\\%'");
        foreach (ManagementObject mo in searcher.Get())
        {
            using (mo)
            {
                var id = mo["DeviceID"] as string;
                if (string.IsNullOrEmpty(id) || id.StartsWith(@"USB\ROOT_HUB", StringComparison.OrdinalIgnoreCase)) continue;
                list.Add(new UsbDevice(id, mo["Name"] as string ?? id));
            }
        }
        return list;
    }

    public static bool IsPresent(string deviceId, bool exactPort) => IsPresent(List(), deviceId, exactPort);

    public static bool IsPresent(IReadOnlyCollection<UsbDevice> devices, string deviceId, bool exactPort)
    {
        if (string.IsNullOrEmpty(deviceId)) return false;
        string prefix = ModelPrefix(deviceId) + "\\";
        return devices.Any(d => exactPort
            ? string.Equals(d.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase)
            : d.DeviceId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    // ---- change notifications ------------------------------------------------------------------
    readonly ManagementEventWatcher _watcher;

    /// <summary>Raised (on a WMI thread) whenever any device is added or removed.</summary>
    public event Action? Changed;

    public UsbDevices()
    {
        _watcher = new ManagementEventWatcher(new WqlEventQuery("SELECT * FROM Win32_DeviceChangeEvent"));
        _watcher.EventArrived += (_, _) => Changed?.Invoke();
        _watcher.Start();
    }

    public void Dispose()
    {
        try { _watcher.Stop(); } catch { }
        _watcher.Dispose();
    }
}
