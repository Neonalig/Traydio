using System.Collections.ObjectModel;
using System.Collections.Generic;
using System;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Traydio.Commands;
using Traydio.Common;
using Traydio.Models;
using Traydio.Services;
using Traydio.Views;

namespace Traydio.ViewModels;

[ViewModelFor(typeof(StationManagerPage))]
public partial class StationManagerPageViewModel : ViewModelBase
{
    private readonly IStationRepository _stationRepository;
    private readonly IAppCommandDispatcher _commandDispatcher;
    private readonly IRadioPlayer _radioPlayer;

    public ObservableCollection<StationItem> Stations { get; } = [];

    [ObservableProperty]
    private StationItem? _selectedStation;

    [ObservableProperty]
    private string _newStationName = string.Empty;

    [ObservableProperty]
    private string _newStationUrl = string.Empty;

    [ObservableProperty]
    private bool _isAddFlyoutOpen;

    public void PrefillNewStation(string name, string url)
    {
        NewStationName = name;
        NewStationUrl = url;
    }

    public void AddStationsFromDrop(IEnumerable<(string Name, string Url)> stations)
    {
        foreach (var (name, url) in stations)
        {
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url))
            {
                continue;
            }

            _stationRepository.AddStation(name, url);
        }
    }

    public void RemoveStations(IEnumerable<StationItem> stations)
    {
        foreach (var station in stations
                     .Where(static s => s is not null)
                     .DistinctBy(static s => s.Station.Id, StringComparer.Ordinal)
                     .ToArray())
        {
            _stationRepository.RemoveStation(station.Station.Id);
        }
    }

    public void SetStationIconPath(StationItem station, string? iconPath)
    {
        _stationRepository.SetStationIconPath(station.Station.Id, iconPath);
    }

    public StationManagerPageViewModel(
        IStationRepository stationRepository,
        IAppCommandDispatcher commandDispatcher,
        IRadioPlayer radioPlayer)
    {
        _stationRepository = stationRepository;
        _commandDispatcher = commandDispatcher;
        _radioPlayer = radioPlayer;
        _stationRepository.Changed += (_, _) => RefreshStations();
        _radioPlayer.StateChanged += (_, _) => RefreshStations();
        RefreshStations();
    }

    [RelayCommand]
    private void AddStation()
    {
        if (string.IsNullOrWhiteSpace(NewStationName) || string.IsNullOrWhiteSpace(NewStationUrl))
        {
            return;
        }

        _stationRepository.AddStation(NewStationName, NewStationUrl);
        NewStationName = string.Empty;
        NewStationUrl = string.Empty;
        IsAddFlyoutOpen = false;
    }

    [RelayCommand]
    private void FindMoreStations()
    {
        _commandDispatcher.Dispatch(new AppCommand { Kind = AppCommandKind.OpenStationSearch });
    }

    [RelayCommand]
    private void RemoveSelectedStation()
    {
        RemoveStation(SelectedStation);
    }

    [RelayCommand]
    private void PlaySelectedStation()
    {
        PlayStation(SelectedStation);
    }

    [RelayCommand]
    private void RemoveStation(StationItem? station)
    {
        if (station is null)
        {
            return;
        }

        _stationRepository.RemoveStation(station.Station.Id);
    }

    [RelayCommand]
    private void PlayStation(StationItem? station)
    {
        if (station is null)
        {
            return;
        }

        if (station.IsActivePlaying)
        {
            _commandDispatcher.Dispatch(new AppCommand { Kind = AppCommandKind.Pause });
            return;
        }

        _commandDispatcher.Dispatch(new AppCommand
        {
            Kind = AppCommandKind.PlayStation,
            StationId = station.Station.Id,
        });
    }

    private void RefreshStations()
    {
        void Update()
        {
            var activeStationId = _stationRepository.ActiveStationId;
            var isPlaying = _radioPlayer.IsPlaying;

            Stations.Clear();
            foreach (var station in _stationRepository.GetStations())
            {
                var isActiveStation = string.Equals(station.Id, activeStationId, StringComparison.Ordinal);
                Stations.Add(new StationItem(
                    station,
                    isActiveStation,
                    isActiveStation && isPlaying));
            }
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            Update();
            return;
        }

        Dispatcher.UIThread.Post(Update);
    }

    public sealed class StationItem(RadioStation station, bool isActiveStation, bool isActivePlaying)
    {
        public RadioStation Station { get; } = station;

        public string Name => Station.Name;

        public string StreamUrl => Station.StreamUrl;

        public string? IconPath => Station.IconPath;

        public bool IsActiveStation { get; } = isActiveStation;

        public bool IsActivePlaying { get; } = isActivePlaying;
    }
}

