namespace Ben.Desktop.App.Library.Kit;

/// <summary>
/// Where somebody types an authenticator code or a recovery code.
/// </summary>
/// <remarks>
/// The normalising below is the whole reason this is a control rather than a bare Entry. Recovery
/// codes are printed with a hyphen and people read app codes aloud in groups of three, so a code
/// typed the way it is written arrives with spaces in it. The server strips them; refusing it here
/// would be a failure we caused, against somebody who typed the right thing.
/// </remarks>
// NOTE on naming inside these controls: an x:Name in the XAML generates a field of that name on
// this same partial class, so an element named "Hint" and a BindableProperty named "Hint" collide
// at compile time (CS0102, then CS0229 on every use). Elements here carry a Label/Field suffix for
// that reason; the public property keeps the clean name, because that is the one XAML consumers
// type. The web side has the identical trap with RenderFragment names in Razor.
public partial class BenCodeEntry : ContentView
{
    public BenCodeEntry()
    {
        InitializeComponent();
        Field.TextChanged += (_, e) => Code = Normalize(e.NewTextValue);
    }

    /// <summary>Strips the punctuation a printed or spoken code arrives with.</summary>
    public static string Normalize(string? typed) =>
        string.IsNullOrEmpty(typed)
            ? string.Empty
            : typed.Replace(" ", string.Empty).Replace("-", string.Empty).Trim();

    public static readonly BindableProperty CodeProperty = BindableProperty.Create(
        nameof(Code), typeof(string), typeof(BenCodeEntry), string.Empty,
        defaultBindingMode: BindingMode.TwoWay);

    public string Code
    {
        get => (string)GetValue(CodeProperty);
        set => SetValue(CodeProperty, value);
    }

    public static readonly BindableProperty LabelProperty = BindableProperty.Create(
        nameof(Label), typeof(string), typeof(BenCodeEntry), "Code",
        propertyChanged: (b, _, v) => ((BenCodeEntry)b).CaptionLabel.Text = (string)v);

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public static readonly BindableProperty HintProperty = BindableProperty.Create(
        nameof(Hint), typeof(string), typeof(BenCodeEntry), null,
        propertyChanged: (b, _, v) =>
        {
            var control = (BenCodeEntry)b;
            control.HintLabel.Text = v as string;
            control.HintLabel.IsVisible = !string.IsNullOrWhiteSpace(v as string);
        });

    public string? Hint
    {
        get => (string?)GetValue(HintProperty);
        set => SetValue(HintProperty, value);
    }
}
