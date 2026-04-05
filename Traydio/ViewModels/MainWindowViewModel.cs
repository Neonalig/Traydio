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

    public ObservableCollection<RadioStation> Stations { get; } = new();

    [ObservableProperty]
    private string _stationName = string.Empty;

    [ObservableProperty]
    private string _stationUrl = string.Empty;

    [ObservableProperty]
    private RadioStation? _selectedStation;

    [ObservableProperty]
    private int _volume;

    public MainWindowViewModel(IStationRepository stationRepository, IAppCommandDispatcher commandDispatcher)
    {
        _stationRepository = stationRepository;
        _commandDispatcher = commandDispatcher;

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
