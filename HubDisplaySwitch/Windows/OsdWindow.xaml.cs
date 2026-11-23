using HubDisplaySwitch.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using WinUIAppKit.Theming;
using WinUIAppKit.Windowing;

namespace HubDisplaySwitch.Windows;

/// <summary>
/// Volume/brightness-style popup at the bottom centre of the main display. While Working it shows a
/// spinner and stays up; Done/Error hold for a moment, then fade. The window itself is the kit's
/// <see cref="OsdController"/>: it never activates, is click-through, sizes to its text, parks off-screen
/// between uses, and is re-centred on WM_DISPLAYCHANGE so it follows the desktop onto whichever display
/// comes up. On Windows 10 it has square corners and a solid background instead of acrylic.
/// </summary>
public sealed partial class OsdWindow : Window
{
    private const double MinWidthDip = 320, MaxWidthDip = 640, HeightDip = 68;
    private static readonly TimeSpan DoneHold = TimeSpan.FromSeconds(2.5), MaxWorking = TimeSpan.FromSeconds(90);

    private readonly OsdController _osd;

    public OsdWindow()
    {
        InitializeComponent();
        _osd = new OsdController(this, WindowRoot, HeightDip, MinWidthDip, MaxWidthDip);

        // The spinner animates only while the popup can be seen.
        _osd.Hidden += () => Spinner.IsActive = false;
        AppTheme.Track(this);
    }

    /// <summary>
    /// Show a switch's progress, or update the popup already showing. Working stays up (at most
    /// <see cref="MaxWorking"/>) until the next state arrives; Done and Error hold briefly, then fade.
    /// </summary>
    public void Present(string title, string subtitle, OsdState state)
    {
        TitleText.Text = title;
        SubText.Text = subtitle;

        bool working = state == OsdState.Working;
        Spinner.IsActive = working;
        Spinner.Visibility = working ? Visibility.Visible : Visibility.Collapsed;
        StateIcon.Visibility = working ? Visibility.Collapsed : Visibility.Visible;
        StateIcon.Glyph = state == OsdState.Error ? "" : "";
        StateIcon.Foreground = (Brush)Application.Current.Resources[
            state == OsdState.Error ? "SystemFillColorCriticalBrush" : "AccentTextFillColorPrimaryBrush"];

        // Set the content first: the controller measures it for the width.
        _osd.Show(working ? MaxWorking : DoneHold);
    }

    /// <summary>Follow a display change: re-centre while showing, re-park while hidden.</summary>
    public void Reposition() => _osd.Reposition();
}
