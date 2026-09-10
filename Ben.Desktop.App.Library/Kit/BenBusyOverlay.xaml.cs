namespace Ben.Desktop.App.Library.Kit;

/// <summary>A cover for whatever is underneath while a request is in flight.</summary>
public partial class BenBusyOverlay : ContentView
{
    public BenBusyOverlay() => InitializeComponent();

    public static readonly BindableProperty IsBusyProperty = BindableProperty.Create(
        nameof(IsBusy), typeof(bool), typeof(BenBusyOverlay), false,
        propertyChanged: (b, _, value) =>
        {
            var overlay = (BenBusyOverlay)b;
            var busy = (bool)value;
            overlay.Root.IsVisible = busy;
            overlay.Spinner.IsRunning = busy;
        });

    public bool IsBusy
    {
        get => (bool)GetValue(IsBusyProperty);
        set => SetValue(IsBusyProperty, value);
    }

    /// <summary>
    /// What is happening, when it is worth saying.
    /// </summary>
    /// <remarks>
    /// A spinner alone says only "wait". "Checking your details" and "Signing you in" are different
    /// waits, and the second one means the password was already accepted.
    /// </remarks>
    public static readonly BindableProperty CaptionProperty = BindableProperty.Create(
        nameof(Caption), typeof(string), typeof(BenBusyOverlay), null,
        propertyChanged: (b, _, value) =>
        {
            var overlay = (BenBusyOverlay)b;
            var text = value as string;
            overlay.CaptionLabel.Text = text;
            overlay.CaptionLabel.IsVisible = !string.IsNullOrWhiteSpace(text);
        });

    public string? Caption
    {
        get => (string?)GetValue(CaptionProperty);
        set => SetValue(CaptionProperty, value);
    }
}
