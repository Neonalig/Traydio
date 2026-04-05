using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Traydio.Commands;
using Traydio.Common;
using Traydio.Services;
using Traydio.Views;

namespace Traydio.ViewModels;

[ViewModelFor(typeof(MainWindow))]
public partial class MainWindowViewModel : ViewModelBase
{
    private readonly INavigationService _navigationService;
    private readonly IAppCommandDispatcher _commandDispatcher;

    [ObservableProperty]
    private object? _currentPageViewModel;

    public MainWindowViewModel(INavigationService navigationService, IAppCommandDispatcher commandDispatcher)
    {
        _navigationService = navigationService;
        _commandDispatcher = commandDispatcher;

        _navigationService.Changed += (_, _) => CurrentPageViewModel = _navigationService.CurrentPageViewModel;
        _navigationService.Navigate(AppPage.Stations);
        CurrentPageViewModel = _navigationService.CurrentPageViewModel;
    }

    [RelayCommand]
    private void OpenStations()
    {
        _commandDispatcher.Dispatch(new AppCommand { Kind = AppCommandKind.OpenStationManager });
    }

    [RelayCommand]
    private void OpenSearch()
    {
        _commandDispatcher.Dispatch(new AppCommand { Kind = AppCommandKind.OpenStationSearch });
    }

    [RelayCommand]
    private void OpenPlugins()
    {
        _commandDispatcher.Dispatch(new AppCommand { Kind = AppCommandKind.OpenPluginManager });
    }

    [RelayCommand]
    private void OpenSettings()
    {
        _commandDispatcher.Dispatch(new AppCommand { Kind = AppCommandKind.OpenSettings });
    }
}
