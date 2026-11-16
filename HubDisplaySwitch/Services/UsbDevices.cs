using System.Management;

namespace HubDisplaySwitch.Services;

/// <summary>
/// USB device enumeration and change notifications via WMI. The device record and the hub-matching rules
/// are in UsbDevice.cs, so the portable policy harness can link them without WMI.
/// </summary>
public sealed partial class UsbDevices : IDisposable
{
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
