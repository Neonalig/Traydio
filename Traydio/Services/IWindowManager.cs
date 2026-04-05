namespace Traydio.Services;

public interface IWindowManager
{
    void ShowMainWindow();

    void ShowStationManager();

    void ShowStationSearch();

    void ShowPluginManager();

    void ShowSettings();

    void ShowCommandTester();

    void ShowAboutDialog();

    bool ShowPluginSettings(string pluginId, out string? error);
}

