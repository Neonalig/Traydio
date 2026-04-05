using System;
using Microsoft.Extensions.DependencyInjection;
using Traydio.ViewModels;

namespace Traydio.Services.Implementations;

public sealed class NavigationService(IServiceProvider serviceProvider) : INavigationService
{
    public event EventHandler? Changed;

    public AppPage CurrentPage { get; private set; }

    public object? CurrentPageViewModel { get; private set; }

    public void Navigate(AppPage page)
    {
        CurrentPage = page;
        CurrentPageViewModel = page switch
        {
            AppPage.Stations => serviceProvider.GetRequiredService<StationManagerPageViewModel>(),
            AppPage.Search => serviceProvider.GetRequiredService<StationSearchWindowViewModel>(),
            AppPage.Plugins => serviceProvider.GetRequiredService<PluginManagementWindowViewModel>(),
            AppPage.Settings => serviceProvider.GetRequiredService<SettingsPageViewModel>(),
            _ => null,
        };

        Changed?.Invoke(this, EventArgs.Empty);
    }
}

