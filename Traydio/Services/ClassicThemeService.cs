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
        "ClassicWindows",
        "Desert",
        "Eggplant",
        "Maple",
        "Marine",
        "Plum",
        "Pumpkin",
        "Rose",
        // ReSharper disable once StringLiteralTypo // Justification: Classic.Avalonia.Theme exposes this key with a typo.
        "Sprouce",
        "StandardWindows",
        "StarsAndStripes",
        "Storm",
        "Wheat",
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

        var normalizedInput = themeKey.Trim();
        var candidates = GetCandidateThemeKeys(normalizedInput);

        foreach (var candidate in candidates)
        {
            var property = classicThemeType.GetProperty(candidate, BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase);
            if (property?.GetValue(null) is ThemeVariant variant)
            {
                return variant;
            }
        }

        return null;
    }

    private static string[] GetCandidateThemeKeys(string key)
    {
        if (string.Equals(key, "Spruce", StringComparison.OrdinalIgnoreCase))
        {
            return ["Sprouce", "Spruce"];
        }

        if (string.Equals(key, "Sprouce", StringComparison.OrdinalIgnoreCase))
        {
            return ["Sprouce", "Spruce"];
        }

        if (string.Equals(key, "Lilac", StringComparison.OrdinalIgnoreCase))
        {
            return ["Plum", "Lilac"];
        }

        if (string.Equals(key, "RainyDay", StringComparison.OrdinalIgnoreCase))
        {
            return ["Storm", "RainyDay"];
        }

        if (string.Equals(key, "ClassicWAindows", StringComparison.OrdinalIgnoreCase))
        {
            return ["ClassicWindows", "ClassicWAindows"];
        }

        if (string.Equals(key, "ClassicWindows", StringComparison.OrdinalIgnoreCase))
        {
            return ["ClassicWindows", "ClassicWAindows"];
        }

        return [key];
    }
}

