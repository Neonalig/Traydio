using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
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
    private readonly IWindowManager _windowManager;
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

    [ObservableProperty]
    private bool _isNamedPipeRelayDirty;

    [ObservableProperty]
    private bool _isLoopbackRelayDirty;

    [ObservableProperty]
    private bool _isLoopbackHostDirty;

    [ObservableProperty]
    private bool _isLoopbackPortDirty;

    [ObservableProperty]
    private bool _isProtocolUrlRelayDirty;

    [ObservableProperty]
    private bool _isProtocolSchemeDirty;

    [ObservableProperty]
    private bool _isAudioOutputDeviceDirty;

    [ObservableProperty]
    private bool _isRadioPlayerEngineDirty;

    [ObservableProperty]
    private bool _isClassicThemeDirty;

    public bool HasUnsavedChanges => IsNamedPipeRelayDirty
                                     || IsLoopbackRelayDirty
                                     || IsLoopbackHostDirty
                                     || IsLoopbackPortDirty
                                     || IsProtocolUrlRelayDirty
                                     || IsProtocolSchemeDirty
                                     || IsAudioOutputDeviceDirty
                                     || IsRadioPlayerEngineDirty
                                     || IsClassicThemeDirty;

    public string DirtySummaryText => HasUnsavedChanges
        ? "Unsaved changes are highlighted with reset buttons."
        : "All settings are currently saved.";

    public string ProtocolActionText => IsProtocolRegistered
        ? "Uninstall URL Handler"
        : "Install URL Handler";

    public string ProtocolActionIconPath => IsProtocolRegistered
        ? "/Assets/Icons9x/remove.ico"
        : "/Assets/Icons9x/add.ico";

    public SettingsPageViewModel(
        IStationRepository stationRepository,
        IProtocolRegistrationService protocolRegistrationService,
        IRadioPlayer radioPlayer,
        IPluginManager pluginManager,
        IWmicExtendedFunctionalityService wmicExtendedFunctionalityService,
        IWindowManager windowManager)
    {
        _stationRepository = stationRepository;
        _protocolRegistrationService = protocolRegistrationService;
        _radioPlayer = radioPlayer;
        _pluginManager = pluginManager;
        _wmicExtendedFunctionalityService = wmicExtendedFunctionalityService;
        _windowManager = windowManager;
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

        RefreshDirtyState();
    }

    [RelayCommand]
    private void DiscardChanges()
    {
        Refresh();
        Status = "Discarded unsaved changes.";
    }

    [RelayCommand]
    private void ResetAllDefaults()
    {
        EnableNamedPipeRelay = true;
        EnableLoopbackRelay = false;
        LoopbackHost = "127.0.0.1";
        LoopbackPort = 38473;
        EnableProtocolUrlRelay = true;
        ProtocolScheme = "traydio";
        SelectedAudioOutputDevice = AudioOutputDevices.FirstOrDefault(option => option.Id is null) ?? AudioOutputDevices.FirstOrDefault();
        SelectedRadioPlayerEngine = RadioPlayerEngines.FirstOrDefault(option => string.IsNullOrWhiteSpace(option.Id)) ?? RadioPlayerEngines.FirstOrDefault();
        SelectedClassicTheme = ClassicThemes.FirstOrDefault(option => string.Equals(option.Key, "Default", StringComparison.OrdinalIgnoreCase))
                               ?? ClassicThemes.FirstOrDefault();
        RefreshDirtyState();
        Status = "Reset settings to defaults. Save to persist.";
    }

    [RelayCommand]
    private void ResetNamedPipeRelaySetting()
    {
        EnableNamedPipeRelay = true;
    }

    [RelayCommand]
    private void ResetLoopbackRelaySetting()
    {
        EnableLoopbackRelay = false;
    }

    [RelayCommand]
    private void ResetLoopbackHostSetting()
    {
        LoopbackHost = "127.0.0.1";
    }

    [RelayCommand]
    private void ResetLoopbackPortSetting()
    {
        LoopbackPort = 38473;
    }

    [RelayCommand]
    private void ResetProtocolUrlRelaySetting()
    {
        EnableProtocolUrlRelay = true;
    }

    [RelayCommand]
    private void ResetProtocolSchemeSetting()
    {
        ProtocolScheme = "traydio";
    }

    [RelayCommand]
    private void ResetAudioOutputDeviceSetting()
    {
        SelectedAudioOutputDevice = AudioOutputDevices.FirstOrDefault(option => option.Id is null) ?? AudioOutputDevices.FirstOrDefault();
    }

    [RelayCommand]
    private void ResetPlaybackEngineSetting()
    {
        SelectedRadioPlayerEngine = RadioPlayerEngines.FirstOrDefault(option => string.IsNullOrWhiteSpace(option.Id)) ?? RadioPlayerEngines.FirstOrDefault();
    }

    [RelayCommand]
    private void ResetClassicThemeSetting()
    {
        SelectedClassicTheme = ClassicThemes.FirstOrDefault(option => string.Equals(option.Key, "Default", StringComparison.OrdinalIgnoreCase))
                               ?? ClassicThemes.FirstOrDefault();
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
    private void ApplyThemeWindow()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime lifetime)
        {
            _windowManager.ShowSettings();
            return;
        }

        if (lifetime.MainWindow is { } mainWindow)
        {
            mainWindow.Close();
        }

        Dispatcher.UIThread.Post(() => _windowManager.ShowSettings(), DispatcherPriority.Background);
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

    [RelayCommand]
    private void ToggleProtocolRegistration()
    {
        if (IsProtocolRegistered)
        {
            UninstallProtocol();
            return;
        }

        InstallProtocol();
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
        return new[] { new RadioPlayerEngineOption(null, "Auto (default)") }
            .Concat(_pluginManager.GetPlugins()
            .SelectMany(plugin => plugin.Capabilities.OfType<IRadioPlayerEngineCapability>())
            .GroupBy(capability => capability.EngineId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(capability => capability.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(capability => new RadioPlayerEngineOption(capability.EngineId, capability.DisplayName)))
            .ToArray();
    }

    private static IReadOnlyList<ClassicThemeOption> BuildClassicThemeOptions()
    {
        return ClassicThemeService.SupportedThemeKeys
            .Select(key => new ClassicThemeOption(key, GetClassicThemeDisplayName(key)))
            .ToArray();
    }

    private static string GetClassicThemeDisplayName(string key)
    {
        if (string.Equals(key, "Sprouce", StringComparison.OrdinalIgnoreCase))
        {
            return "Spruce";
        }

        if (string.Equals(key, "ClassicWAindows", StringComparison.OrdinalIgnoreCase))
        {
            return "Classic Windows";
        }

        var withSpaces = Regex.Replace(key, "(?<!^)([A-Z])", " $1").Trim();
        withSpaces = Regex.Replace(withSpaces, "\\s+", " ");
        withSpaces = withSpaces.Replace(" And ", " & ", StringComparison.OrdinalIgnoreCase);
        return withSpaces;
    }

    partial void OnSelectedClassicThemeChanged(ClassicThemeOption? value)
    {
        _ = value;
        UpdateThemeRestartVisibility();
        RefreshDirtyState();
    }

    partial void OnEnableNamedPipeRelayChanged(bool value)
    {
        _ = value;
        RefreshDirtyState();
    }

    partial void OnEnableLoopbackRelayChanged(bool value)
    {
        _ = value;
        RefreshDirtyState();
    }

    partial void OnLoopbackHostChanged(string value)
    {
        _ = value;
        RefreshDirtyState();
    }

    partial void OnLoopbackPortChanged(int value)
    {
        _ = value;
        RefreshDirtyState();
    }

    partial void OnEnableProtocolUrlRelayChanged(bool value)
    {
        _ = value;
        RefreshDirtyState();
    }

    partial void OnProtocolSchemeChanged(string value)
    {
        _ = value;
        RefreshDirtyState();
    }

    partial void OnSelectedAudioOutputDeviceChanged(AudioOutputDeviceOption? value)
    {
        _ = value;
        RefreshDirtyState();
    }

    partial void OnSelectedRadioPlayerEngineChanged(RadioPlayerEngineOption? value)
    {
        _ = value;
        RefreshDirtyState();
    }

    partial void OnIsProtocolRegisteredChanged(bool value)
    {
        _ = value;
        OnPropertyChanged(nameof(ProtocolActionText));
        OnPropertyChanged(nameof(ProtocolActionIconPath));
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
            || !string.Equals(LoopbackHost.Trim(), _stationRepository.Communication.LoopbackHost, StringComparison.Ordinal)
            || LoopbackPort != _stationRepository.Communication.LoopbackPort
            || EnableProtocolUrlRelay != _stationRepository.Communication.EnableProtocolUrlRelay
            || !string.Equals(ProtocolScheme.Trim(), _stationRepository.Communication.ProtocolScheme, StringComparison.OrdinalIgnoreCase))
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

    private void RefreshDirtyState()
    {
        IsNamedPipeRelayDirty = EnableNamedPipeRelay != _stationRepository.Communication.EnableNamedPipeRelay;
        IsLoopbackRelayDirty = EnableLoopbackRelay != _stationRepository.Communication.EnableLoopbackRelay;
        IsLoopbackHostDirty = !string.Equals(LoopbackHost.Trim(), _stationRepository.Communication.LoopbackHost, StringComparison.Ordinal);
        IsLoopbackPortDirty = LoopbackPort != _stationRepository.Communication.LoopbackPort;
        IsProtocolUrlRelayDirty = EnableProtocolUrlRelay != _stationRepository.Communication.EnableProtocolUrlRelay;
        IsProtocolSchemeDirty = !string.Equals(ProtocolScheme.Trim(), _stationRepository.Communication.ProtocolScheme, StringComparison.OrdinalIgnoreCase);
        IsAudioOutputDeviceDirty = !string.Equals(SelectedAudioOutputDevice?.Id, _stationRepository.AudioOutputDeviceId, StringComparison.Ordinal);
        IsRadioPlayerEngineDirty = !string.Equals(SelectedRadioPlayerEngine?.Id, _stationRepository.RadioPlayerEngineId, StringComparison.OrdinalIgnoreCase);
        IsClassicThemeDirty = !string.Equals(SelectedClassicTheme?.Key, _stationRepository.ClassicThemeKey, StringComparison.OrdinalIgnoreCase);

        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(DirtySummaryText));
    }
}

public sealed record AudioOutputDeviceOption(string? Id, string Name);

public sealed record RadioPlayerEngineOption(string? Id, string Name);

public sealed record ClassicThemeOption(string Key, string Name);

