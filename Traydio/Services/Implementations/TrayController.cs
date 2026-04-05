using System;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Traydio.Commands;

namespace Traydio.Services;

public sealed class TrayController : ITrayController
{
    private readonly IStationRepository _stationRepository;
    private readonly IAppCommandDispatcher _commandDispatcher;
    private TrayIcon? _trayIcon;

    public TrayController(IStationRepository stationRepository, IAppCommandDispatcher commandDispatcher)
    {
        _stationRepository = stationRepository;
        _commandDispatcher = commandDispatcher;
        _stationRepository.Changed += (_, _) => RefreshTrayMenu();
    }

    public void Initialize(IClassicDesktopStyleApplicationLifetime lifetime)
    {
        using var iconStream = AssetLoader.Open(new Uri("avares://Traydio/Assets/Icons9x/stations.ico"));

        _trayIcon = new TrayIcon
        {
            IsVisible = true,
            ToolTipText = "Traydio",
            Icon = new WindowIcon(iconStream),
            Menu = BuildMenu(),
            Command = new RelayCommand(() => Dispatch(AppCommandKind.ToggleMuteOrOpenStationManager)),
        };
    }

    private NativeMenu BuildMenu()
    {
        return
        [
            // Playback controls

            CreateItem("Play / Resume", () => Dispatch(AppCommandKind.Play)),
            CreateItem("Pause", () => Dispatch(AppCommandKind.Pause)),
            new NativeMenuItemSeparator(),

            // Stations

            new NativeMenuItem("Stations")
            {
                Menu = BuildStationsMenu(),
            },

            CreateItem("Add Station...", () => Dispatch(AppCommandKind.OpenStationManager)),
            CreateItem("Find More Stations", () => Dispatch(AppCommandKind.OpenStationSearch)),

            new NativeMenuItemSeparator(),

            // Volume controls

            CreateItem("Volume +", () => _commandDispatcher.Dispatch(new AppCommand { Kind = AppCommandKind.VolumeUp, Value = 5 })),
            CreateItem("Volume -", () => _commandDispatcher.Dispatch(new AppCommand { Kind = AppCommandKind.VolumeDown, Value = 5 })),

            new NativeMenuItemSeparator(),

            // Exit

            CreateItem("Exit", () => Dispatch(AppCommandKind.Exit))
        ];
    }

    private NativeMenu BuildAddStationMenu()
    {
        return
        [
            CreateItem("Open Station Manager", () => Dispatch(AppCommandKind.OpenStationManager)),
            CreateItem("Find More Stations", () => Dispatch(AppCommandKind.OpenStationSearch))
        ];
    }

    private NativeMenu BuildStationsMenu()
    {
        var menu = new NativeMenu();
        var stations = _stationRepository.GetStations();

        if (stations.Count == 0)
        {
            menu.Add(new NativeMenuItem("No stations yet") { IsEnabled = false });
            return menu;
        }

        foreach (var station in stations)
        {
            var item = new NativeMenuItem(station.Name)
            {
                ToggleType = NativeMenuItemToggleType.Radio,
                IsChecked = string.Equals(station.Id, _stationRepository.ActiveStationId, StringComparison.Ordinal),
            };

            var stationId = station.Id;
            item.Click += (_, _) => _commandDispatcher.Dispatch(new AppCommand
            {
                Kind = AppCommandKind.PlayStation,
                StationId = stationId,
            });

            menu.Add(item);
        }

        return menu;
    }

    private static NativeMenuItem CreateItem(string header, Action onClick)
    {
        var item = new NativeMenuItem(header);
        item.Click += (_, _) => onClick();
        return item;
    }

    private void Dispatch(AppCommandKind kind)
    {
        _commandDispatcher.Dispatch(new AppCommand { Kind = kind });
    }

    private void RefreshTrayMenu()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_trayIcon is null)
            {
                return;
            }

            _trayIcon.Menu = BuildMenu();

            if (OperatingSystem.IsWindows())
            {
                _trayIcon.Command = new RelayCommand(() => Dispatch(AppCommandKind.ToggleMuteOrOpenStationManager));
            }
        });
    }
}


