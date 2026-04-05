using System.Collections.ObjectModel;
using System.Collections.Generic;
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

    public ObservableCollection<RadioStation> Stations { get; } = [];

    [ObservableProperty]
    private RadioStation? _selectedStation;

    [ObservableProperty]
    private int _volume;

    [ObservableProperty]
    private string _newStationName = string.Empty;

    [ObservableProperty]
    private string _newStationUrl = string.Empty;

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

    public StationManagerPageViewModel(IStationRepository stationRepository, IAppCommandDispatcher commandDispatcher)
    {
        _stationRepository = stationRepository;
        _commandDispatcher = commandDispatcher;

        _volume = _stationRepository.Volume;
        _stationRepository.Changed += (_, _) => RefreshStations();
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
    }

    [RelayCommand]
    private void FindMoreStations()
    {
        _commandDispatcher.Dispatch(new AppCommand { Kind = AppCommandKind.OpenStationSearch });
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
    private void ApplyVolume()
    {
        _commandDispatcher.Dispatch(new AppCommand
        {
            Kind = AppCommandKind.SetVolume,
            Value = Volume,
        });
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
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            Update();
            return;
        }

        Dispatcher.UIThread.Post(Update);
    }
}

