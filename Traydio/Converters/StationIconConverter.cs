using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Data.Converters;
using Avalonia.Platform;

namespace Traydio.Converters;

public sealed class StationIconConverter : IValueConverter
{
    public string DefaultIcon { get; init; } = "/Assets/Icons9x/stations.ico";

    private readonly Dictionary<string, IImage> _fileIconCache = new(StringComparer.OrdinalIgnoreCase);
    private IImage? _defaultIcon;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string iconPath
            && !string.IsNullOrWhiteSpace(iconPath)
            && File.Exists(iconPath)
            && TryGetFileIcon(iconPath, out var fileIcon))
        {
            return fileIcon;
        }

        _defaultIcon ??= LoadDefaultIcon();
        return _defaultIcon;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value;
    }

    private bool TryGetFileIcon(string iconPath, out IImage icon)
    {
        if (_fileIconCache.TryGetValue(iconPath, out icon!))
        {
            return true;
        }

        try
        {
            using var stream = File.OpenRead(iconPath);
            icon = new Bitmap(stream);
            _fileIconCache[iconPath] = icon;
            return true;
        }
        catch
        {
            icon = null!;
            return false;
        }
    }

    private IImage LoadDefaultIcon()
    {
        var uriText = NormalizeAssetUri(DefaultIcon);
        using var stream = AssetLoader.Open(new Uri(uriText));
        return new Bitmap(stream);
    }

    private static string NormalizeAssetUri(string path)
    {
        if (path.StartsWith("avares://", StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        if (path.StartsWith('/'))
        {
            return "avares://Traydio" + path;
        }

        return path;
    }
}

