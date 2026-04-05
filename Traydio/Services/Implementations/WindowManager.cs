using System;
using Microsoft.Extensions.DependencyInjection;
using Traydio.Views;

namespace Traydio.Services;

public sealed class WindowManager(IServiceProvider serviceProvider) : IWindowManager
{
    private MainWindow? _mainWindow;
    private StationSearchWindow? _stationSearchWindow;

    public void ShowStationManager()
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

    public void ShowStationSearch()
    {
        if (_stationSearchWindow is { IsVisible: true })
        {
            _stationSearchWindow.Activate();
            return;
        }

        _stationSearchWindow = serviceProvider.GetRequiredService<StationSearchWindow>();
        _stationSearchWindow.Closed += (_, _) => _stationSearchWindow = null;
        _stationSearchWindow.Show();
    }
}

