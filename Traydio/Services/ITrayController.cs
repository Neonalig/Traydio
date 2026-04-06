using Avalonia.Controls.ApplicationLifetimes;

namespace Traydio.Services;

/// <summary>
/// Manages tray icon lifecycle and command menu wiring.
/// </summary>
public interface ITrayController
{
    /// <summary>
    /// Initializes tray resources for the desktop lifetime.
    /// </summary>
    /// <param name="lifetime">Desktop application lifetime.</param>
    void Initialize(IClassicDesktopStyleApplicationLifetime lifetime);
}

