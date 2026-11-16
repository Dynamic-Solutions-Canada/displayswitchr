using WinUIAppKit.Localization;
using HubDisplaySwitch.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace HubDisplaySwitch.Pages;

/// <summary>
/// "Unplug the hub, then plug it back in": polls USB devices and lists the ones that disappeared,
/// hubs first, so the user can pick the hub. Shown from the settings window (large enough for a ContentDialog).
/// </summary>
internal static class DetectHubDialog
{
    public static async Task<UsbDevice?> ShowAsync(XamlRoot root)
    {
        var info = new TextBlock { Text = UiText.Get("Unplug the USB hub now…"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
        var list = new ListView { SelectionMode = ListViewSelectionMode.Single, MinHeight = 120, MaxHeight = 240 };
        var panel = new StackPanel { Width = 440 };
        panel.Children.Add(info);
        panel.Children.Add(list);

        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = UiText.Get("Detect USB hub"),
            Content = panel,
            PrimaryButtonText = UiText.Get("Use this device"),
            CloseButtonText = UiText.Get("Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = false,
        };
        list.SelectionChanged += (_, _) => dialog.IsPrimaryButtonEnabled = list.SelectedItem != null;

        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var removed = new List<UsbDevice>();
        HashSet<string>? baseline = null;
        bool busy = false;

        var timer = dialog.DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(1);
        timer.Tick += async (_, _) =>
        {
            if (busy) return;
            busy = true;
            try
            {
                var now = await Task.Run(UsbDevices.List);
                var ids = now.Select(d => d.DeviceId).ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (baseline != null)
                    foreach (var gone in baseline.Where(id => !ids.Contains(id)))
                        if (removed.All(r => r.DeviceId != gone))
                            removed.Add(new UsbDevice(gone, names.TryGetValue(gone, out var n) ? n : gone));
                foreach (var d in now) names[d.DeviceId] = d.Name;
                baseline = ids;

                if (removed.Count > 0 && list.Items.Count != removed.Count)
                {
                    var ordered = removed.OrderByDescending(r => r.IsHub).ToList();
                    list.ItemsSource = ordered;
                    list.SelectedIndex = 0;
                    info.Text = UiText.Format("Got it. These devices disappeared, with the hub selected. Plug it back in, then choose \"{{button}}\".",
                        ("button", UiText.Get("Use this device")));
                }
            }
            catch { /* WMI hiccup; try again next tick */ }
            finally { busy = false; }
        };
        timer.Start();

        var result = await dialog.ShowAsync();
        timer.Stop();
        return result == ContentDialogResult.Primary ? list.SelectedItem as UsbDevice : null;
    }
}
