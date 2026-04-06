using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Traydio.Views.Controls;

// ReSharper disable once PartialTypeWithSinglePart
public partial class DirtySettingLabel : UserControl
{
    public static readonly StyledProperty<string?> LabelTextProperty =
        AvaloniaProperty.Register<DirtySettingLabel, string?>(nameof(LabelText));

    public static readonly StyledProperty<string?> LabelToolTipProperty =
        AvaloniaProperty.Register<DirtySettingLabel, string?>(nameof(LabelToolTip));

    public static readonly StyledProperty<bool> IsDirtyProperty =
        AvaloniaProperty.Register<DirtySettingLabel, bool>(nameof(IsDirty));

    public string? LabelText
    {
        get => GetValue(LabelTextProperty);
        set => SetValue(LabelTextProperty, value);
    }

    public string? LabelToolTip
    {
        get => GetValue(LabelToolTipProperty);
        set => SetValue(LabelToolTipProperty, value);
    }

    public bool IsDirty
    {
        get => GetValue(IsDirtyProperty);
        set => SetValue(IsDirtyProperty, value);
    }

    public DirtySettingLabel()
    {
        AvaloniaXamlLoader.Load(this);
    }
}


