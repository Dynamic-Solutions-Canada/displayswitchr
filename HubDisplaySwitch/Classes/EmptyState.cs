using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace HubDisplaySwitch.Classes;

/// <summary>
/// The app's one empty-list message (UI-13): what is missing and what to do, in secondary caption text,
/// used wherever a list built in code can have nothing to show (a setup's hubs, the flyout's setups).
/// </summary>
internal static class EmptyState
{
    /// <summary>Create the message for an empty list.</summary>
    /// <param name="text">Translated text saying what is missing and what to do about it.</param>
    /// <returns>A wrapping text block in the theme's secondary text colour.</returns>
    public static TextBlock Create(string text)
    {
        var resources = Application.Current.Resources;
        return new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)resources["CaptionTextBlockStyle"],
            Foreground = (Brush)resources["TextFillColorSecondaryBrush"],
        };
    }
}
