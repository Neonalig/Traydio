using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using Traydio.Models;
using Traydio.Services;
using Traydio.Common;
using Traydio.Views;

namespace Traydio.ViewModels;

[ViewModelFor(typeof(SettingsPage))]
public partial class SettingsPageViewModel : ViewModelBase
{
    private readonly IStationRepository _stationRepository;
    private readonly IProtocolRegistrationService _protocolRegistrationService;
    private readonly IRadioPlayer _radioPlayer;

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
    private string _status = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<AudioOutputDeviceOption> _audioOutputDevices = [];

    [ObservableProperty]
    private AudioOutputDeviceOption? _selectedAudioOutputDevice;

    public SettingsPageViewModel(
        IStationRepository stationRepository,
        IProtocolRegistrationService protocolRegistrationService,
        IRadioPlayer radioPlayer)
    {
        _stationRepository = stationRepository;
        _protocolRegistrationService = protocolRegistrationService;
        _radioPlayer = radioPlayer;
        Refresh();
    }

    [RelayCommand]
    private void Save()
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

        _stationRepository.AudioOutputDeviceId = SelectedAudioOutputDevice?.Id;
        _radioPlayer.SetAudioOutputDevice(_stationRepository.AudioOutputDeviceId);

        RefreshProtocolRegistration();
        Status = "Settings saved.";
    }

    [RelayCommand]
    private void InstallProtocol()
    {
        var scheme = ProtocolScheme.Trim().ToLowerInvariant();
        if (_protocolRegistrationService.Register(scheme, out var error))
        {
            Status = $"Protocol '{scheme}://' registered.";
            RefreshProtocolRegistration();
            return;
        }

        Status = "Registration failed: " + (error ?? "Unknown error.");
    }

    [RelayCommand]
    private void UninstallProtocol()
    {
        var scheme = ProtocolScheme.Trim().ToLowerInvariant();
        if (_protocolRegistrationService.Unregister(scheme, out var error))
        {
            Status = $"Protocol '{scheme}://' unregistered.";
            RefreshProtocolRegistration();
            return;
        }

        Status = "Unregistration failed: " + (error ?? "Unknown error.");
    }

    private void Refresh()
    {
        EnableNamedPipeRelay = _stationRepository.Communication.EnableNamedPipeRelay;
        EnableLoopbackRelay = _stationRepository.Communication.EnableLoopbackRelay;
        LoopbackHost = _stationRepository.Communication.LoopbackHost;
        LoopbackPort = _stationRepository.Communication.LoopbackPort;
        EnableProtocolUrlRelay = _stationRepository.Communication.EnableProtocolUrlRelay;
        ProtocolScheme = _stationRepository.Communication.ProtocolScheme;
        AudioOutputDevices = BuildAudioOutputOptions();
        SelectedAudioOutputDevice = AudioOutputDevices.FirstOrDefault(option =>
            string.Equals(option.Id, _stationRepository.AudioOutputDeviceId, StringComparison.Ordinal))
            ?? AudioOutputDevices.FirstOrDefault();
        RefreshProtocolRegistration();
    }

    private void RefreshProtocolRegistration()
    {
        IsProtocolRegistered = _protocolRegistrationService.IsRegistered(ProtocolScheme.Trim().ToLowerInvariant());
    }

    private IReadOnlyList<AudioOutputDeviceOption> BuildAudioOutputOptions()
    {
        var options = new List<AudioOutputDeviceOption>
        {
            new(null, "System default"),
        };

        foreach (var device in _radioPlayer.GetAudioOutputDevices())
        {
            if (options.Any(option => string.Equals(option.Id, device.Id, StringComparison.Ordinal)))
            {
                continue;
            }

            options.Add(new AudioOutputDeviceOption(device.Id, device.Name));
        }

        return options;
    }
}

public sealed record AudioOutputDeviceOption(string? Id, string Name);

