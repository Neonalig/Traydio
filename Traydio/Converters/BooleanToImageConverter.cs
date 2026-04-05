using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Traydio.Converters;

public sealed class BooleanToImageConverter : IValueConverter
{
    public IImage? TrueImage { get; set; }

    public IImage? FalseImage { get; set; }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? TrueImage : FalseImage;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return null;
    }
}

