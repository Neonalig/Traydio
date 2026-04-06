namespace Traydio.Services;

/// <summary>
/// Provides navigation and top-level window/dialog operations.
/// </summary>
public interface IWindowManager
{
    /// <summary>
    /// Shows the main window or activates it when already visible.
    /// </summary>
    void ShowMainWindow();

    /// <summary>
    /// Shows the station manager page.
    /// </summary>
    void ShowStationManager();

    /// <summary>
    /// Shows the station search page.
    /// </summary>
    void ShowStationSearch();

    /// <summary>
    /// Shows the plugin manager page.
    /// </summary>
    void ShowPluginManager();

    /// <summary>
    /// Shows the settings page.
    /// </summary>
    void ShowSettings();

    /// <summary>
    /// Shows the command tester window.
    /// </summary>
    void ShowCommandTester();

    /// <summary>
    /// Shows the application about dialog.
    /// </summary>
    void ShowAboutDialog();

    /// <summary>
    /// Shows plugin settings UI for a plugin id.
    /// </summary>
    /// <param name="pluginId">Plugin identifier.</param>
    /// <param name="error">Error text when opening fails.</param>
    /// <returns><see langword="true"/> when a settings window is shown.</returns>
    bool ShowPluginSettings(string pluginId, out string? error);
}

