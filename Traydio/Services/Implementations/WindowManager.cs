using System;
using Microsoft.Extensions.DependencyInjection;
using Traydio.Views;

namespace Traydio.Services;

public sealed class WindowManager(IServiceProvider serviceProvider, INavigationService navigationService) : IWindowManager
{
    private MainWindow? _mainWindow;

    public void ShowMainWindow()
    {
        if (_mainWindow is { IsVisible: true })
        {
            _mainWindow.Activate();
            return;
        }

        _mainWindow = serviceProvider.GetRequiredService<MainWindow>();
        _mainWindow.Closed += (_, _) => _mainWindow = null;
        _mainWindow.Show();
    }

    public void ShowStationManager()
    {
        ShowMainWindow();
        navigationService.Navigate(AppPage.Stations);
    }

    public void ShowStationSearch()
    {
        ShowMainWindow();
        navigationService.Navigate(AppPage.Search);
    }

    public void ShowPluginManager()
    {
        ShowMainWindow();
        navigationService.Navigate(AppPage.Plugins);
    }

    public void ShowSettings()
    {
        ShowMainWindow();
        navigationService.Navigate(AppPage.Settings);
    }
}

