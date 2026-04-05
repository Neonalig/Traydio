namespace Traydio.Services;

public interface IWindowManager
{
    void ShowMainWindow();

    void ShowStationManager();

    void ShowStationSearch();

    void ShowPluginManager();

    void ShowSettings();

    bool ShowPluginSettings(string pluginId, out string? error);
}

