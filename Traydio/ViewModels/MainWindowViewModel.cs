using Avalonia.Threading;
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
    private readonly IRadioPlayer _radioPlayer;

    private bool _suppressVolumeDispatch;

    [ObservableProperty]
    private object? _currentPageViewModel;

    [ObservableProperty]
    private bool _isMediaBarVisible;

    [ObservableProperty]
    private bool _isMediaLoading;

    [ObservableProperty]
    private bool _isMediaPlaying;

    [ObservableProperty]
    private bool _isMediaMuted;

    [ObservableProperty]
    private int _footerVolume = 60;

    [ObservableProperty]
    private string _playPauseIcon = "[PLAY]";

    [ObservableProperty]
    private string _playbackTimeText = "00:00";

    [ObservableProperty]
    private string _currentStationText = "No station";

    [ObservableProperty]
    private string _nowPlayingText = string.Empty;

    [ObservableProperty]
    private string _mediaErrorText = string.Empty;

    [ObservableProperty]
    private double _mediaControlsOpacity = 1.0;

    public MainWindowViewModel(
        INavigationService navigationService,
        IAppCommandDispatcher commandDispatcher,
        IRadioPlayer radioPlayer)
    {
        _navigationService = navigationService;
        _commandDispatcher = commandDispatcher;
        _radioPlayer = radioPlayer;

        _navigationService.Changed += (_, _) => CurrentPageViewModel = _navigationService.CurrentPageViewModel;
        _radioPlayer.StateChanged += (_, state) => UpdateMediaState(state);

        _navigationService.Navigate(AppPage.Stations);
        CurrentPageViewModel = _navigationService.CurrentPageViewModel;
        UpdateMediaState(_radioPlayer.State);
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

    [RelayCommand]
    private void TogglePlayPause()
    {
        if (IsMediaPlaying)
        {
            _commandDispatcher.Dispatch(new AppCommand { Kind = AppCommandKind.Pause });
            return;
        }

        _commandDispatcher.Dispatch(new AppCommand { Kind = AppCommandKind.Play });
    }

    [RelayCommand]
    private void ToggleMute()
    {
        _radioPlayer.ToggleMute();
    }

    partial void OnFooterVolumeChanged(int value)
    {
        if (_suppressVolumeDispatch)
        {
            return;
        }

        _commandDispatcher.Dispatch(new AppCommand { Kind = AppCommandKind.SetVolume, Value = value });
    }

    private void UpdateMediaState(RadioPlayerState state)
    {
        void Update()
        {
            IsMediaLoading = state.IsLoading;
            IsMediaPlaying = state.IsPlaying;
            IsMediaMuted = state.IsMuted;
            PlayPauseIcon = state.IsPlaying ? "[PAUSE]" : "[PLAY]";

            _suppressVolumeDispatch = true;
            FooterVolume = state.Volume;
            _suppressVolumeDispatch = false;

            var position = state.Position;
            var duration = state.Duration;
            PlaybackTimeText = duration.HasValue
                ? $"{position:mm\\:ss} / {duration.Value:mm\\:ss}"
                : $"{position:mm\\:ss}";

            CurrentStationText = string.IsNullOrWhiteSpace(state.CurrentStationName)
                ? "No station"
                : state.CurrentStationName;

            NowPlayingText = string.IsNullOrWhiteSpace(state.NowPlaying)
                ? string.Empty
                : state.NowPlaying;

            MediaErrorText = string.IsNullOrWhiteSpace(state.LastError)
                ? string.Empty
                : state.LastError;

            IsMediaBarVisible = state.IsLoading || !string.IsNullOrWhiteSpace(state.CurrentStationName);
            MediaControlsOpacity = state.IsLoading ? 0.55 : 1.0;
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            Update();
            return;
        }

        Dispatcher.UIThread.Post(Update);
    }
}
