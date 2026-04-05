using System;
using System.Reflection;
using Avalonia;
using Avalonia.Styling;

namespace Traydio.Services;

public static class ClassicThemeService
{
    public static readonly string[] SupportedThemeKeys =
    [
        "Default",
        "Brick",
        "Lilac",
        "Pumpkin",
        "RainyDay",
        "Spruce",
    ];

    public static void Apply(string? themeKey)
    {
        if (Application.Current is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(themeKey) || string.Equals(themeKey, "Default", StringComparison.OrdinalIgnoreCase))
        {
            Application.Current.RequestedThemeVariant = ThemeVariant.Light;
            return;
        }

        var variant = ResolveClassicThemeVariant(themeKey);
        Application.Current.RequestedThemeVariant = variant ?? ThemeVariant.Light;
    }

    private static ThemeVariant? ResolveClassicThemeVariant(string themeKey)
    {
        var classicThemeType = Type.GetType("Classic.Avalonia.Theme.ClassicTheme, Classic.Avalonia.Theme", throwOnError: false);
        if (classicThemeType is null)
        {
            return null;
        }

        var property = classicThemeType.GetProperty(themeKey.Trim(), BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase);
        if (property?.GetValue(null) is ThemeVariant variant)
        {
            return variant;
        }

        return null;
    }
}

