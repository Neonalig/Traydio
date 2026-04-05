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
    private RadioPlayerState _latestRadioState;

    public ObservableCollection<StationItem> Stations { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedStation))]
    [NotifyPropertyChangedFor(nameof(IsStationEditorEnabled))]
    [NotifyPropertyChangedFor(nameof(CanEditSelectedStation))]
    [NotifyPropertyChangedFor(nameof(CanSaveOrDiscardSelectedStation))]
    [NotifyPropertyChangedFor(nameof(CanAddStation))]
    private StationItem? _selectedStation;

    [ObservableProperty]
    private string _newStationName = string.Empty;

    [ObservableProperty]
    private string _newStationUrl = string.Empty;

    [ObservableProperty]
    private bool _isAddFlyoutOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStationEditorEnabled))]
    [NotifyPropertyChangedFor(nameof(CanEditSelectedStation))]
    [NotifyPropertyChangedFor(nameof(CanSaveOrDiscardSelectedStation))]
    private bool _isEditingSelectedStation;

    public bool HasSelectedStation => SelectedStation is not null;

    public bool IsStationEditorEnabled => !HasSelectedStation || IsEditingSelectedStation;

    public bool CanEditSelectedStation => HasSelectedStation && !IsEditingSelectedStation;

    public bool CanSaveOrDiscardSelectedStation => HasSelectedStation && IsEditingSelectedStation;

    public bool CanAddStation => !HasSelectedStation;

    public void PrefillNewStation(string name, string url)
    {
        SelectedStation = null;
        IsEditingSelectedStation = false;
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
        _latestRadioState = _radioPlayer.State;
        _stationRepository.Changed += (_, _) => RefreshStations();
        _radioPlayer.StateChanged += (_, state) =>
        {
            _latestRadioState = state;
            RefreshStations();
        };
        RefreshStations();
    }

    [RelayCommand]
    private void AddStation()
    {
        if (!CanAddStation)
        {
            return;
        }

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

    [RelayCommand]
    private void BeginEditSelectedStation()
    {
        if (!HasSelectedStation)
        {
            return;
        }

        IsEditingSelectedStation = true;
    }

    [RelayCommand]
    private void SaveSelectedStationEdits()
    {
        if (SelectedStation is null || !CanSaveOrDiscardSelectedStation)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(NewStationName) || string.IsNullOrWhiteSpace(NewStationUrl))
        {
            return;
        }

        if (!_stationRepository.UpdateStation(SelectedStation.Station.Id, NewStationName, NewStationUrl))
        {
            return;
        }

        IsEditingSelectedStation = false;
    }

    [RelayCommand]
    private void DiscardSelectedStationEdits()
    {
        if (SelectedStation is null || !CanSaveOrDiscardSelectedStation)
        {
            return;
        }

        NewStationName = SelectedStation.Name;
        NewStationUrl = SelectedStation.StreamUrl;
        IsEditingSelectedStation = false;
    }

    private void RefreshStations()
    {
        void Update()
        {
            var activeStationId = _stationRepository.ActiveStationId;
            var isPlaying = _radioPlayer.IsPlaying;
            var hasPlaybackError = !string.IsNullOrWhiteSpace(_latestRadioState.LastError);
            var selectedStationId = SelectedStation?.Station.Id;

            Stations.Clear();
            var orderedStations = _stationRepository.GetStations().ToList();
            if (isPlaying && !string.IsNullOrWhiteSpace(activeStationId))
            {
                var activeIndex = orderedStations.FindIndex(station => string.Equals(station.Id, activeStationId, StringComparison.Ordinal));
                if (activeIndex > 0)
                {
                    var activeStation = orderedStations[activeIndex];
                    orderedStations.RemoveAt(activeIndex);
                    orderedStations.Insert(0, activeStation);
                }
            }

            foreach (var station in orderedStations)
            {
                var isActiveStation = string.Equals(station.Id, activeStationId, StringComparison.Ordinal);
                Stations.Add(new StationItem(
                    station,
                    isActiveStation,
                    isActiveStation && isPlaying,
                    isActiveStation && hasPlaybackError));
            }

            SelectedStation = !string.IsNullOrWhiteSpace(selectedStationId)
                ? Stations.FirstOrDefault(station => string.Equals(station.Station.Id, selectedStationId, StringComparison.Ordinal))
                : null;
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            Update();
            return;
        }

        Dispatcher.UIThread.Post(Update);
    }

    partial void OnSelectedStationChanged(StationItem? value)
    {
        if (value is null)
        {
            IsEditingSelectedStation = false;
            return;
        }

        NewStationName = value.Name;
        NewStationUrl = value.StreamUrl;
        IsEditingSelectedStation = false;
    }

    public sealed class StationItem(RadioStation station, bool isActiveStation, bool isActivePlaying, bool isRecentFailure)
    {
        public RadioStation Station { get; } = station;

        public string Name => Station.Name;

        public string StreamUrl => Station.StreamUrl;

        public string? IconPath => Station.IconPath;

        public bool IsActiveStation { get; } = isActiveStation;

        public bool IsActivePlaying { get; } = isActivePlaying;

        public bool IsRecentFailure { get; } = isRecentFailure;
    }
}

