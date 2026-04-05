using System;
using System.Linq;
using Avalonia.Controls.ApplicationLifetimes;
using Traydio.Services;

namespace Traydio.Commands;

public sealed class AppCommandDispatcher : IAppCommandDispatcher
{
    private readonly IRadioPlayer _radioPlayer;
    private readonly IStationRepository _stationRepository;
    private readonly IWindowManager _windowManager;
    private IClassicDesktopStyleApplicationLifetime? _lifetime;

    public AppCommandDispatcher(
        IRadioPlayer radioPlayer,
        IStationRepository stationRepository,
        IWindowManager windowManager)
    {
        _radioPlayer = radioPlayer;
        _stationRepository = stationRepository;
        _windowManager = windowManager;
    }

    public void Initialize(IClassicDesktopStyleApplicationLifetime lifetime)
    {
        _lifetime = lifetime;
    }

    public void Dispatch(AppCommand command)
    {
        switch (command.Kind)
        {
            case AppCommandKind.Play:
                PlayActiveStation();
                break;
            case AppCommandKind.Pause:
                _radioPlayer.Pause();
                break;
            case AppCommandKind.TogglePause:
                _radioPlayer.TogglePause();
                break;
            case AppCommandKind.PlayStation:
                PlayStation(command.StationId);
                break;
            case AppCommandKind.VolumeUp:
                AdjustVolume(Math.Abs(command.Value ?? 5));
                break;
            case AppCommandKind.VolumeDown:
                AdjustVolume(-Math.Abs(command.Value ?? 5));
                break;
            case AppCommandKind.SetVolume:
                if (command.Value.HasValue)
                {
                    SetVolume(command.Value.Value);
                }
                break;
            case AppCommandKind.OpenStationManager:
                _windowManager.ShowStationManager();
                break;
            case AppCommandKind.Exit:
                _radioPlayer.Stop();
                _lifetime?.Shutdown();
                break;
        }
    }

    private void PlayActiveStation()
    {
        if (!string.IsNullOrWhiteSpace(_stationRepository.ActiveStationId))
        {
            PlayStation(_stationRepository.ActiveStationId);
            return;
        }

        var first = _stationRepository.GetStations().FirstOrDefault();
        if (first is not null)
        {
            PlayStation(first.Id);
        }
    }

    private void PlayStation(string? stationId)
    {
        if (string.IsNullOrWhiteSpace(stationId))
        {
            return;
        }

        var station = _stationRepository.GetStation(stationId);
        if (station is null)
        {
            return;
        }

        _stationRepository.ActiveStationId = station.Id;
        _radioPlayer.Play(station);
    }

    private void AdjustVolume(int delta)
    {
        SetVolume(_stationRepository.Volume + delta);
    }

    private void SetVolume(int value)
    {
        _stationRepository.Volume = Math.Clamp(value, 0, 100);
        _radioPlayer.SetVolume(_stationRepository.Volume);
    }
}


