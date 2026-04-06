using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Traydio.Views.Controls;

// ReSharper disable once PartialTypeWithSinglePart
public partial class IconLabelContent : UserControl
{
    public static readonly StyledProperty<string?> IconSourceProperty =
        AvaloniaProperty.Register<IconLabelContent, string?>(nameof(IconSource));

    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<IconLabelContent, string?>(nameof(Text));

    public string? IconSource
    {
        get => GetValue(IconSourceProperty);
        set => SetValue(IconSourceProperty, value);
    }

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public IconLabelContent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}


