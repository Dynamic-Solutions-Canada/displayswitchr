using HubDisplaySwitch.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace HubDisplaySwitch.Classes;

/// <summary>
/// XAML attached properties that show a <see cref="UiText"/> key and refresh in place when the language
/// changes (Persistent's pattern), e.g. <c>l10n:Localize.Text="Displays"</c>.
/// </summary>
public static class Localize
{
    private sealed record Binding(WeakReference<DependencyObject> Target, DependencyProperty Property);
    private static readonly List<Binding> Bindings = [];
    public static readonly DependencyProperty TextProperty = Register("Text");
    public static readonly DependencyProperty ContentProperty = Register("Content");
    public static readonly DependencyProperty ToolTipProperty = Register("ToolTip");
    public static readonly DependencyProperty PlaceholderProperty = Register("Placeholder");
    public static readonly DependencyProperty HeaderProperty = Register("Header");
    public static readonly DependencyProperty OnContentProperty = Register("OnContent");
    public static readonly DependencyProperty OffContentProperty = Register("OffContent");
    public static readonly DependencyProperty NameProperty = Register("Name");

    static Localize()
    {
        UiText.Changed += () => App.MainDispatcherQueue.TryEnqueue(() =>
        {
            Bindings.RemoveAll(binding => !binding.Target.TryGetTarget(out _));
            foreach (var binding in Bindings.ToList())
                if (binding.Target.TryGetTarget(out var target)) Apply(target, binding.Property);
        });
    }

    private static DependencyProperty Register(string name) => DependencyProperty.RegisterAttached(name, typeof(string), typeof(Localize), new PropertyMetadata(null, (target, args) =>
    {
        // Pages are rebuilt on every navigation; drop entries for collected elements now and then.
        if (Bindings.Count >= 512 && Bindings.Count % 128 == 0)
            Bindings.RemoveAll(binding => !binding.Target.TryGetTarget(out _));
        Bindings.Add(new Binding(new WeakReference<DependencyObject>(target), args.Property));
        Apply(target, args.Property);
    }));

    private static void Apply(DependencyObject target, DependencyProperty property)
    {
        if (target.GetValue(property) is not string key) return;
        string value = UiText.Get(key);
        if (property == TextProperty && target is TextBlock text) text.Text = value;
        else if (property == ContentProperty && target is ContentControl content) content.Content = value;
        else if (property == ToolTipProperty) ToolTipService.SetToolTip(target, value);
        else if (property == PlaceholderProperty && target is TextBox input) input.PlaceholderText = value;
        else if (property == HeaderProperty && target is ToggleSwitch toggle) toggle.Header = value;
        else if (property == HeaderProperty && target is ComboBox combo) combo.Header = value;
        else if (property == OnContentProperty && target is ToggleSwitch on) on.OnContent = value;
        else if (property == OffContentProperty && target is ToggleSwitch off) off.OffContent = value;
        else if (property == NameProperty) AutomationProperties.SetName(target, value);
    }

    public static string GetText(DependencyObject target) => (string)target.GetValue(TextProperty);
    public static void SetText(DependencyObject target, string value) => target.SetValue(TextProperty, value);
    public static string GetContent(DependencyObject target) => (string)target.GetValue(ContentProperty);
    public static void SetContent(DependencyObject target, string value) => target.SetValue(ContentProperty, value);
    public static string GetToolTip(DependencyObject target) => (string)target.GetValue(ToolTipProperty);
    public static void SetToolTip(DependencyObject target, string value) => target.SetValue(ToolTipProperty, value);
    public static string GetPlaceholder(DependencyObject target) => (string)target.GetValue(PlaceholderProperty);
    public static void SetPlaceholder(DependencyObject target, string value) => target.SetValue(PlaceholderProperty, value);
    public static string GetHeader(DependencyObject target) => (string)target.GetValue(HeaderProperty);
    public static void SetHeader(DependencyObject target, string value) => target.SetValue(HeaderProperty, value);
    public static string GetOnContent(DependencyObject target) => (string)target.GetValue(OnContentProperty);
    public static void SetOnContent(DependencyObject target, string value) => target.SetValue(OnContentProperty, value);
    public static string GetOffContent(DependencyObject target) => (string)target.GetValue(OffContentProperty);
    public static void SetOffContent(DependencyObject target, string value) => target.SetValue(OffContentProperty, value);
    public static string GetName(DependencyObject target) => (string)target.GetValue(NameProperty);
    public static void SetName(DependencyObject target, string value) => target.SetValue(NameProperty, value);
}
