using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
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
    private readonly IPluginManager _pluginManager;
    private readonly IWmicExtendedFunctionalityService _wmicExtendedFunctionalityService;
    private string? _appliedClassicThemeKey;

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

    [ObservableProperty]
    private IReadOnlyList<RadioPlayerEngineOption> _radioPlayerEngines = [];

    [ObservableProperty]
    private RadioPlayerEngineOption? _selectedRadioPlayerEngine;

    [ObservableProperty]
    private IReadOnlyList<ClassicThemeOption> _classicThemes = [];

    [ObservableProperty]
    private ClassicThemeOption? _selectedClassicTheme;

    [ObservableProperty]
    private bool _isThemeRestartVisible;

    [ObservableProperty]
    private bool _isWmicSupported;

    [ObservableProperty]
    private bool _isWmicInstalled;

    [ObservableProperty]
    private bool _isInstallingWmic;

    [ObservableProperty]
    private string _wmicStatus = string.Empty;

    public bool HasUnsavedChanges => ComputeHasUnsavedChanges();

    public SettingsPageViewModel(
        IStationRepository stationRepository,
        IProtocolRegistrationService protocolRegistrationService,
        IRadioPlayer radioPlayer,
        IPluginManager pluginManager,
        IWmicExtendedFunctionalityService wmicExtendedFunctionalityService)
    {
        _stationRepository = stationRepository;
        _protocolRegistrationService = protocolRegistrationService;
        _radioPlayer = radioPlayer;
        _pluginManager = pluginManager;
        _wmicExtendedFunctionalityService = wmicExtendedFunctionalityService;
        Refresh();
    }

    [RelayCommand]
    private void Save()
    {
        var previousEngineId = _stationRepository.RadioPlayerEngineId;

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

        _stationRepository.RadioPlayerEngineId = SelectedRadioPlayerEngine?.Id;
        _stationRepository.ClassicThemeKey = SelectedClassicTheme?.Key;
        ClassicThemeService.Apply(_stationRepository.ClassicThemeKey);
        _appliedClassicThemeKey = _stationRepository.ClassicThemeKey;
        UpdateThemeRestartVisibility();

        RefreshProtocolRegistration();

        var engineChanged = !string.Equals(previousEngineId, _stationRepository.RadioPlayerEngineId, StringComparison.OrdinalIgnoreCase);
        Status = engineChanged
            ? "Settings saved. Restart Traydio to apply the new playback engine."
            : "Settings saved.";
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

    [RelayCommand]
    private async Task InstallWmicExtendedFunctionalityAsync()
    {
        if (IsInstallingWmic)
        {
            return;
        }

        if (!IsWmicSupported)
        {
            WmicStatus = "WMIC extended functionality is only available on Windows.";
            return;
        }

        if (IsWmicInstalled)
        {
            WmicStatus = "WMIC is already installed.";
            return;
        }

        IsInstallingWmic = true;
        WmicStatus = "Starting WMIC capability install...";

        try
        {
            var result = await _wmicExtendedFunctionalityService.InstallAsync().ConfigureAwait(true);
            IsWmicInstalled = _wmicExtendedFunctionalityService.IsInstalled();
            WmicStatus = result.Message;

            if (result.RequiresRestart)
            {
                Status = "WMIC installed. Restart Windows to finish activation.";
            }
            else if (result.Success)
            {
                Status = "WMIC extended functionality installed.";
            }
            else
            {
                Status = "WMIC installation failed.";
            }
        }
        catch (Exception ex)
        {
            WmicStatus = "WMIC installation failed: " + ex.Message;
            Status = WmicStatus;
        }
        finally
        {
            IsInstallingWmic = false;
        }
    }

    [RelayCommand]
    private void RestartApp()
    {
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
        {
            Status = "Could not determine application path for restart.";
            return;
        }

        var restartArguments = "--cmd settings";
        // Intentionally do not pass debugger re-attach arguments on restart.

        if (OperatingSystem.IsWindows())
        {
            var escapedPath = processPath.Replace("\"", "\"\"");
            var delayedCommand = $"/c timeout /t 1 /nobreak >nul && start \"\" \"{escapedPath}\" {restartArguments}";
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = delayedCommand,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
        }
        else
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = processPath,
                Arguments = restartArguments,
                UseShellExecute = true,
            });
        }

        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
        {
            lifetime.Shutdown();
        }
    }

    private void Refresh()
    {
        EnableNamedPipeRelay = _stationRepository.Communication.EnableNamedPipeRelay;
        EnableLoopbackRelay = _stationRepository.Communication.EnableLoopbackRelay;
        LoopbackHost = _stationRepository.Communication.LoopbackHost;
        LoopbackPort = _stationRepository.Communication.LoopbackPort;
        EnableProtocolUrlRelay = _stationRepository.Communication.EnableProtocolUrlRelay;
        ProtocolScheme = _stationRepository.Communication.ProtocolScheme;

        RadioPlayerEngines = BuildRadioPlayerEngineOptions();
        SelectedRadioPlayerEngine = RadioPlayerEngines.FirstOrDefault(option =>
            string.Equals(option.Id, _stationRepository.RadioPlayerEngineId, StringComparison.OrdinalIgnoreCase))
            ?? RadioPlayerEngines.FirstOrDefault();

        AudioOutputDevices = BuildAudioOutputOptions();
        SelectedAudioOutputDevice = AudioOutputDevices.FirstOrDefault(option =>
            string.Equals(option.Id, _stationRepository.AudioOutputDeviceId, StringComparison.Ordinal))
            ?? AudioOutputDevices.FirstOrDefault();

        ClassicThemes = BuildClassicThemeOptions();
        _appliedClassicThemeKey = _stationRepository.ClassicThemeKey;
        SelectedClassicTheme = ClassicThemes.FirstOrDefault(option =>
            string.Equals(option.Key, _stationRepository.ClassicThemeKey, StringComparison.OrdinalIgnoreCase))
            ?? ClassicThemes.FirstOrDefault();
        UpdateThemeRestartVisibility();

        RefreshProtocolRegistration();
        RefreshWmicStatus();
    }

    private void RefreshProtocolRegistration()
    {
        IsProtocolRegistered = _protocolRegistrationService.IsRegistered(ProtocolScheme.Trim().ToLowerInvariant());
    }

    private void RefreshWmicStatus()
    {
        IsWmicSupported = _wmicExtendedFunctionalityService.IsSupported;
        IsWmicInstalled = _wmicExtendedFunctionalityService.IsInstalled();

        if (!IsWmicSupported)
        {
            WmicStatus = "WMIC extended functionality is only available on Windows.";
            return;
        }

        WmicStatus = IsWmicInstalled
            ? "WMIC is installed and available."
            : "WMIC is not installed. Install to enable extended functionality.";
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

    private IReadOnlyList<RadioPlayerEngineOption> BuildRadioPlayerEngineOptions()
    {
        return _pluginManager.GetPlugins()
            .SelectMany(plugin => plugin.Capabilities.OfType<IRadioPlayerEngineCapability>())
            .GroupBy(capability => capability.EngineId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(capability => capability.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(capability => new RadioPlayerEngineOption(capability.EngineId, capability.DisplayName))
            .ToArray();
    }

    private static IReadOnlyList<ClassicThemeOption> BuildClassicThemeOptions()
    {
        return ClassicThemeService.SupportedThemeKeys
            .Select(key => new ClassicThemeOption(key, key))
            .ToArray();
    }

    partial void OnSelectedClassicThemeChanged(ClassicThemeOption? value)
    {
        _ = value;
        UpdateThemeRestartVisibility();
    }

    private void UpdateThemeRestartVisibility()
    {
        IsThemeRestartVisible = !string.Equals(
            SelectedClassicTheme?.Key,
            _appliedClassicThemeKey,
            StringComparison.OrdinalIgnoreCase);
    }

    private bool ComputeHasUnsavedChanges()
    {
        if (EnableNamedPipeRelay != _stationRepository.Communication.EnableNamedPipeRelay
            || EnableLoopbackRelay != _stationRepository.Communication.EnableLoopbackRelay
            || !string.Equals(LoopbackHost?.Trim(), _stationRepository.Communication.LoopbackHost, StringComparison.Ordinal)
            || LoopbackPort != _stationRepository.Communication.LoopbackPort
            || EnableProtocolUrlRelay != _stationRepository.Communication.EnableProtocolUrlRelay
            || !string.Equals(ProtocolScheme?.Trim(), _stationRepository.Communication.ProtocolScheme, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.Equals(SelectedAudioOutputDevice?.Id, _stationRepository.AudioOutputDeviceId, StringComparison.Ordinal)
            || !string.Equals(SelectedRadioPlayerEngine?.Id, _stationRepository.RadioPlayerEngineId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(SelectedClassicTheme?.Key, _stationRepository.ClassicThemeKey, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }
}

public sealed record AudioOutputDeviceOption(string? Id, string Name);

public sealed record RadioPlayerEngineOption(string Id, string Name);

public sealed record ClassicThemeOption(string Key, string Name);

