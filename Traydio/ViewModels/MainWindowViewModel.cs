using Traydio.Common;
using Traydio.Commands;
using Traydio.Models;
using Traydio.Services;
using Traydio.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using Avalonia.Threading;

namespace Traydio.ViewModels;

[ViewModelFor(typeof(MainWindow))]
public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IStationRepository _stationRepository;
    private readonly IAppCommandDispatcher _commandDispatcher;
    private readonly IProtocolRegistrationService _protocolRegistrationService;

    public ObservableCollection<RadioStation> Stations { get; } = new();

    [ObservableProperty]
    private string _stationName = string.Empty;

    [ObservableProperty]
    private string _stationUrl = string.Empty;

    [ObservableProperty]
    private RadioStation? _selectedStation;

    [ObservableProperty]
    private int _volume;

    [ObservableProperty]
    private bool _enableNamedPipeRelay;

    [ObservableProperty]
    private bool _enableLoopbackRelay;

    [ObservableProperty]
    private string _loopbackHost = string.Empty;

    [ObservableProperty]
    private int _loopbackPort;

    [ObservableProperty]
    private bool _enableProtocolUrlRelay;

    [ObservableProperty]
    private string _protocolScheme = string.Empty;

    [ObservableProperty]
    private bool _isProtocolRegistered;

    [ObservableProperty]
    private string _settingsStatus = string.Empty;

    public MainWindowViewModel(
        IStationRepository stationRepository,
        IAppCommandDispatcher commandDispatcher,
        IProtocolRegistrationService protocolRegistrationService)
    {
        _stationRepository = stationRepository;
        _commandDispatcher = commandDispatcher;
        _protocolRegistrationService = protocolRegistrationService;

        _volume = _stationRepository.Volume;
        RefreshStations();

        _stationRepository.Changed += (_, _) => RefreshStations();
    }

    [RelayCommand]
    private void AddStation()
    {
        if (string.IsNullOrWhiteSpace(StationName) || string.IsNullOrWhiteSpace(StationUrl))
        {
            return;
        }

        _stationRepository.AddStation(StationName, StationUrl);
        StationName = string.Empty;
        StationUrl = string.Empty;
    }

    [RelayCommand]
    private void RemoveSelectedStation()
    {
        if (SelectedStation is null)
        {
            return;
        }

        _stationRepository.RemoveStation(SelectedStation.Id);
    }

    [RelayCommand]
    private void PlaySelectedStation()
    {
        if (SelectedStation is null)
        {
            return;
        }

        _commandDispatcher.Dispatch(new AppCommand
        {
            Kind = AppCommandKind.PlayStation,
            StationId = SelectedStation.Id,
        });
    }

    [RelayCommand]
    private void Pause()
    {
        _commandDispatcher.Dispatch(new AppCommand { Kind = AppCommandKind.Pause });
    }

    [RelayCommand]
    private void OpenStationSearch()
    {
        _commandDispatcher.Dispatch(new AppCommand { Kind = AppCommandKind.OpenStationSearch });
    }

    [RelayCommand]
    private void ApplyVolume()
    {
        _commandDispatcher.Dispatch(new AppCommand
        {
            Kind = AppCommandKind.SetVolume,
            Value = Volume,
        });
    }

    [RelayCommand]
    private void ApplyCommunicationSettings()
    {
        _stationRepository.SaveCommunicationSettings(new CommunicationBridgeSettings
        {
            EnableNamedPipeRelay = EnableNamedPipeRelay,
            EnableLoopbackRelay = EnableLoopbackRelay,
            LoopbackHost = LoopbackHost,
            LoopbackPort = LoopbackPort,
            EnableProtocolUrlRelay = EnableProtocolUrlRelay,
            ProtocolScheme = ProtocolScheme,
        });

        RefreshProtocolRegistration();
        SettingsStatus = "Communication settings saved.";
    }

    [RelayCommand]
    private void InstallProtocolHandler()
    {
        var scheme = ProtocolScheme.Trim().ToLowerInvariant();
        if (!IsValidScheme(scheme))
        {
            SettingsStatus = "Protocol scheme must contain letters or digits and start with a letter.";
            return;
        }

        if (_protocolRegistrationService.Register(scheme, out var error))
        {
            SettingsStatus = $"Protocol '{scheme}://' registered.";
            RefreshProtocolRegistration();
            return;
        }

        SettingsStatus = "Protocol registration failed: " + (error ?? "Unknown error.");
    }

    [RelayCommand]
    private void UninstallProtocolHandler()
    {
        var scheme = ProtocolScheme.Trim().ToLowerInvariant();
        if (!IsValidScheme(scheme))
        {
            SettingsStatus = "Protocol scheme must contain letters or digits and start with a letter.";
            return;
        }

        if (_protocolRegistrationService.Unregister(scheme, out var error))
        {
            SettingsStatus = $"Protocol '{scheme}://' unregistered.";
            RefreshProtocolRegistration();
            return;
        }

        SettingsStatus = "Protocol unregistration failed: " + (error ?? "Unknown error.");
    }

    private static bool IsValidScheme(string scheme)
    {
        if (string.IsNullOrWhiteSpace(scheme) || !char.IsLetter(scheme[0]))
        {
            return false;
        }

        foreach (var character in scheme)
        {
            if (!(char.IsLetterOrDigit(character) || character is '+' or '-' or '.'))
            {
                return false;
            }
        }

        return true;
    }

    private void RefreshProtocolRegistration()
    {
        var scheme = ProtocolScheme.Trim().ToLowerInvariant();
        IsProtocolRegistered = IsValidScheme(scheme) && _protocolRegistrationService.IsRegistered(scheme);
    }

    private void RefreshStations()
    {
        void Update()
        {
            Stations.Clear();
            foreach (var station in _stationRepository.GetStations())
            {
                Stations.Add(station);
            }

            Volume = _stationRepository.Volume;

            EnableNamedPipeRelay = _stationRepository.Communication.EnableNamedPipeRelay;
            EnableLoopbackRelay = _stationRepository.Communication.EnableLoopbackRelay;
            LoopbackHost = _stationRepository.Communication.LoopbackHost;
            LoopbackPort = _stationRepository.Communication.LoopbackPort;
            EnableProtocolUrlRelay = _stationRepository.Communication.EnableProtocolUrlRelay;
            ProtocolScheme = _stationRepository.Communication.ProtocolScheme;
            RefreshProtocolRegistration();
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            Update();
            return;
        }

        Dispatcher.UIThread.Post(Update);
    }
}
