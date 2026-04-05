using System;
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
    private const int MediaMarqueeMaxChars = 52;
    private const int MediaMarqueeHoldTicks = 8;

    private readonly INavigationService _navigationService;
    private readonly IAppCommandDispatcher _commandDispatcher;
    private readonly IRadioPlayer _radioPlayer;
    private readonly IWindowManager _windowManager;

    private bool _suppressVolumeDispatch;
    private readonly DispatcherTimer _mediaMarqueeTimer;
    private string _mediaMarqueeSourceText = "No station";
    private int _mediaMarqueeOffset;
    private int _mediaMarqueeHoldCounter;
    private bool _mediaMarqueeAtEnd;

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
    private string _playbackTimeText = "00:00";

    [ObservableProperty]
    private string _currentStationText = "No station";

    [ObservableProperty]
    private string _nowPlayingText = string.Empty;

    [ObservableProperty]
    private string _mediaErrorText = string.Empty;

    [ObservableProperty]
    private string _mediaCenterText = "No station";

    [ObservableProperty]
    private string _mediaCenterDisplayText = "No station";

    [ObservableProperty]
    private bool _hasMediaError;

    [ObservableProperty]
    private string _footerVolumeText = "60%";

    [ObservableProperty]
    private double _mediaControlsOpacity = 1.0;

    [ObservableProperty]
    private string _ribbonStatusText = "Ready";

    [ObservableProperty]
    private bool _isStationsTabChecked;

    [ObservableProperty]
    private bool _isSearchTabChecked;

    [ObservableProperty]
    private bool _isPluginsTabChecked;

    [ObservableProperty]
    private bool _isSettingsTabChecked;

    public MainWindowViewModel(
        INavigationService navigationService,
        IAppCommandDispatcher commandDispatcher,
        IRadioPlayer radioPlayer,
        IWindowManager windowManager)
    {
        _navigationService = navigationService;
        _commandDispatcher = commandDispatcher;
        _radioPlayer = radioPlayer;
        _windowManager = windowManager;

        _mediaMarqueeTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) => TickMediaMarquee());

        _navigationService.Changed += (_, _) =>
        {
            CurrentPageViewModel = _navigationService.CurrentPageViewModel;
            UpdateRibbonTabChecks();
        };
        _radioPlayer.StateChanged += (_, state) => UpdateMediaState(state);
        RibbonStatusHub.Changed += OnRibbonStatusChanged;

        _navigationService.Navigate(AppPage.Stations);
        CurrentPageViewModel = _navigationService.CurrentPageViewModel;
        UpdateRibbonTabChecks();
        RibbonStatusText = RibbonStatusHub.GetCurrentText("Ready");
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
    private void OpenCommands()
    {
        _windowManager.ShowCommandTester();
    }

    [RelayCommand]
    private void OpenAbout()
    {
        _windowManager.ShowAboutDialog();
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
        FooterVolumeText = $"{value}%";

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

            _suppressVolumeDispatch = true;
            FooterVolume = state.Volume;
            _suppressVolumeDispatch = false;
            FooterVolumeText = $"{state.Volume}%";

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
            HasMediaError = !string.IsNullOrWhiteSpace(MediaErrorText);

            if (HasMediaError && MediaErrorText.Contains("tags", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine($"[Traydio][ManagedBass] Metadata/status error: {MediaErrorText}");
                System.Diagnostics.Trace.WriteLine($"[Traydio][ManagedBass] Metadata/status error: {MediaErrorText}");
            }

            MediaCenterText = HasMediaError
                ? MediaErrorText
                : !string.IsNullOrWhiteSpace(state.NowPlaying)
                    ? state.NowPlaying
                    : CurrentStationText;
            UpdateMediaMarqueeSource(MediaCenterText);

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

    private void UpdateMediaMarqueeSource(string text)
    {
        var next = string.IsNullOrWhiteSpace(text) ? "No station" : text.Trim();
        if (string.Equals(next, _mediaMarqueeSourceText, StringComparison.Ordinal))
        {
            if (!_mediaMarqueeTimer.IsEnabled)
            {
                MediaCenterDisplayText = next;
            }

            return;
        }

        _mediaMarqueeSourceText = next;
        _mediaMarqueeOffset = 0;
        _mediaMarqueeHoldCounter = 0;
        _mediaMarqueeAtEnd = false;

        if (_mediaMarqueeSourceText.Length <= MediaMarqueeMaxChars)
        {
            _mediaMarqueeTimer.Stop();
            MediaCenterDisplayText = _mediaMarqueeSourceText;
            return;
        }

        MediaCenterDisplayText = SliceMediaMarqueeWindow(_mediaMarqueeSourceText, 0);
        _mediaMarqueeTimer.Start();
    }

    private void TickMediaMarquee()
    {
        if (_mediaMarqueeSourceText.Length <= MediaMarqueeMaxChars)
        {
            _mediaMarqueeTimer.Stop();
            MediaCenterDisplayText = _mediaMarqueeSourceText;
            return;
        }

        var maxOffset = _mediaMarqueeSourceText.Length - MediaMarqueeMaxChars;

        if (_mediaMarqueeHoldCounter < MediaMarqueeHoldTicks)
        {
            _mediaMarqueeHoldCounter++;
            return;
        }

        if (_mediaMarqueeAtEnd)
        {
            _mediaMarqueeOffset = 0;
            _mediaMarqueeAtEnd = false;
            _mediaMarqueeHoldCounter = 0;
            MediaCenterDisplayText = SliceMediaMarqueeWindow(_mediaMarqueeSourceText, _mediaMarqueeOffset);
            return;
        }

        if (_mediaMarqueeOffset < maxOffset)
        {
            _mediaMarqueeOffset++;
            MediaCenterDisplayText = SliceMediaMarqueeWindow(_mediaMarqueeSourceText, _mediaMarqueeOffset);

            if (_mediaMarqueeOffset == maxOffset)
            {
                _mediaMarqueeAtEnd = true;
                _mediaMarqueeHoldCounter = 0;
            }

            return;
        }

        _mediaMarqueeAtEnd = true;
        _mediaMarqueeHoldCounter = 0;
    }

    private static string SliceMediaMarqueeWindow(string text, int offset)
    {
        if (text.Length <= MediaMarqueeMaxChars)
        {
            return text;
        }

        var safeOffset = Math.Clamp(offset, 0, text.Length - MediaMarqueeMaxChars);
        return text.Substring(safeOffset, MediaMarqueeMaxChars);
    }

    private void OnRibbonStatusChanged(object? sender, EventArgs e)
    {
        var next = RibbonStatusHub.GetCurrentText("Ready");

        if (Dispatcher.UIThread.CheckAccess())
        {
            RibbonStatusText = next;
            return;
        }

        Dispatcher.UIThread.Post(() => RibbonStatusText = next);
    }

    private void UpdateRibbonTabChecks()
    {
        IsStationsTabChecked = _navigationService.CurrentPage == AppPage.Stations;
        IsSearchTabChecked = _navigationService.CurrentPage == AppPage.Search;
        IsPluginsTabChecked = _navigationService.CurrentPage == AppPage.Plugins;
        IsSettingsTabChecked = _navigationService.CurrentPage == AppPage.Settings;
    }
}
