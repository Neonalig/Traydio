using System;
using Microsoft.Extensions.DependencyInjection;
using Traydio.Views;

namespace Traydio.Services;

public sealed class WindowManager : IWindowManager
{
    private readonly IServiceProvider _serviceProvider;
    private MainWindow? _mainWindow;

    public WindowManager(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public void ShowStationManager()
    {
        if (_mainWindow is { IsVisible: true })
        {
            _mainWindow.Activate();
            return;
        }

        _mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        _mainWindow.Closed += (_, _) => _mainWindow = null;
        _mainWindow.Show();
    }
}

