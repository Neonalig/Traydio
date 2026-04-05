using System;
using Microsoft.Extensions.DependencyInjection;
using Traydio.Views;

namespace Traydio.Services;

public sealed class WindowManager(IServiceProvider serviceProvider) : IWindowManager
{
    private MainWindow? _mainWindow;

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
}

