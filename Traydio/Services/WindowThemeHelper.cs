using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;

namespace Traydio.Services;

public static class WindowThemeHelper
{
    public static void ApplyClassicWindowTheme(Window window)
    {
        if (Application.Current is null)
        {
            return;
        }

        if (Application.Current.TryGetResource("ClassicWindow", Application.Current.ActualThemeVariant, out var theme)
            && theme is ControlTheme controlTheme)
        {
            window.Theme = controlTheme;
        }
    }
}

