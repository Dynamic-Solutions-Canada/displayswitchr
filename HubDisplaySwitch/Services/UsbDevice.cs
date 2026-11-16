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

/// <summary>Hub matching: whether a setup's saved hub is among the present USB devices.</summary>
public sealed partial class UsbDevices
{
    /// <summary>The model part of a PnP id ("USB\VID_1A40&amp;PID_0101"), the same on every port.</summary>
    public static string ModelPrefix(string deviceId)
    {
        var parts = deviceId.Split('\\');
        return parts.Length >= 2 ? $"{parts[0]}\\{parts[1]}" : deviceId;
    }

    /// <summary>
    /// True when the saved hub is present: the exact device (same port) when <paramref name="exactPort"/> is set,
    /// otherwise any device of the same model on any port. An empty id never matches.
    /// </summary>
    public static bool IsPresent(IReadOnlyCollection<UsbDevice> devices, string deviceId, bool exactPort)
    {
        if (string.IsNullOrEmpty(deviceId))
        {
            return false;
        }

        string prefix = ModelPrefix(deviceId) + "\\";
        return devices.Any(d => exactPort
            ? string.Equals(d.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase)
            : d.DeviceId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }
}
