using System;
using System.Linq;
using System.Threading;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Traydio.Services;

namespace Traydio.Commands;

public sealed class AppCommandDispatcher(
    IRadioPlayer radioPlayer,
    IStationRepository stationRepository,
    IWindowManager windowManager
) : IAppCommandDispatcher
{
    private readonly Lock _lifetimeGate = new();
    private IClassicDesktopStyleApplicationLifetime? _lifetime;

    public void Initialize(IClassicDesktopStyleApplicationLifetime lifetime)
    {
        lock (_lifetimeGate)
        {
            _lifetime = lifetime;
        }
    }

    public void Dispatch(AppCommand command)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => Dispatch(command), DispatcherPriority.Normal);
            return;
        }

        try
        {
            switch (command.Kind)
            {
                case AppCommandKind.Play:
                    PlayActiveStation();
                    break;
                case AppCommandKind.Pause:
                    radioPlayer.Pause();
                    break;
                case AppCommandKind.TogglePause:
                    radioPlayer.TogglePause();
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
                    windowManager.ShowStationManager();
                    break;
                case AppCommandKind.OpenStationSearch:
                    windowManager.ShowStationSearch();
                    break;
                case AppCommandKind.OpenPluginManager:
                    windowManager.ShowPluginManager();
                    break;
                case AppCommandKind.OpenSettings:
                    windowManager.ShowSettings();
                    break;
                case AppCommandKind.ToggleMuteOrOpenStationManager:
                    if (radioPlayer.IsPlaying)
                    {
                        radioPlayer.ToggleMute();
                    }
                    else
                    {
                        windowManager.ShowStationManager();
                    }

                    break;
                case AppCommandKind.Exit:
                {
                    radioPlayer.Stop();
                    IClassicDesktopStyleApplicationLifetime? lifetime;
                    lock (_lifetimeGate)
                    {
                        lifetime = _lifetime;
                    }

                    lifetime?.Shutdown();
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            AppErrorHandler.Report(ex, "Dispatch command: " + command.Kind, showDialog: true);
        }
    }

    private void PlayActiveStation()
    {
        if (!string.IsNullOrWhiteSpace(stationRepository.ActiveStationId))
        {
            PlayStation(stationRepository.ActiveStationId);
            return;
        }

        var first = stationRepository.GetStations().FirstOrDefault();
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

        var station = stationRepository.GetStation(stationId);
        if (station is null)
        {
            return;
        }

        stationRepository.ActiveStationId = station.Id;
        radioPlayer.Play(station);
    }

    private void AdjustVolume(int delta)
    {
        SetVolume(stationRepository.Volume + delta);
    }

    private void SetVolume(int value)
    {
        stationRepository.Volume = Math.Clamp(value, 0, 100);
        radioPlayer.SetVolume(stationRepository.Volume);
    }
}


