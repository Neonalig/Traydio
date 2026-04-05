using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Traydio.Commands;
using Traydio.Common;
using Traydio.Services;
using Traydio.Views;

namespace Traydio.ViewModels;

[ViewModelFor(typeof(StationSearchPage))]
public partial class StationSearchWindowViewModel : ViewModelBase
{
    private readonly IStationDiscoveryService _stationDiscoveryService;
    private readonly IPluginManager _pluginManager;
    private readonly IStationRepository _stationRepository;
    private readonly IAppCommandDispatcher _commandDispatcher;

    private CancellationTokenSource? _searchCancellation;
    private readonly List<DiscoveredStation> _lastSearchResults = [];

    public ObservableCollection<ProviderOption> Providers { get; } = [];

    public ObservableCollection<DiscoveredStation> Results { get; } = [];

    [ObservableProperty]
    private ProviderOption? _selectedProvider;

    [ObservableProperty]
    private DiscoveredStation? _selectedResult;

    [ObservableProperty]
    private string _query = string.Empty;

    [ObservableProperty]
    private string _filterText = string.Empty;

    [ObservableProperty]
    private string _pluginDllPath = string.Empty;

    [ObservableProperty]
    private string _status = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    public StationSearchWindowViewModel(
        IStationDiscoveryService stationDiscoveryService,
        IPluginManager pluginManager,
        IStationRepository stationRepository,
        IAppCommandDispatcher commandDispatcher)
    {
        _stationDiscoveryService = stationDiscoveryService;
        _pluginManager = pluginManager;
        _stationRepository = stationRepository;
        _commandDispatcher = commandDispatcher;

        _pluginManager.PluginsChanged += (_, _) => RefreshProviders();
        RefreshProviders();
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (SelectedProvider is null)
        {
            Status = "Select a provider first.";
            return;
        }

        if (_searchCancellation is not null)
            await _searchCancellation.CancelAsync();
        _searchCancellation = new CancellationTokenSource();

        IsBusy = true;
        Status = "Searching...";
        try
        {
            var request = new StationSearchRequest
            {
                Query = string.IsNullOrWhiteSpace(Query) ? "popular" : Query,
                Limit = 200,
            };

            _lastSearchResults.Clear();
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Results.Clear();
                Status = "Searching...";
            });

            var shown = 0;
            await foreach (var station in _stationDiscoveryService
                .SearchAsync(SelectedProvider.Id, request, _searchCancellation.Token)
                               .ConfigureAwait(false))
            {
                _lastSearchResults.Add(station);
                if (!MatchesFilter(station, FilterText))
                {
                    continue;
                }

                shown++;
                await Dispatcher.UIThread.InvokeAsync(() => Results.Add(station));
            }

            Status = string.IsNullOrWhiteSpace(Query)
                ? $"Showing popular stations ({shown}/{_lastSearchResults.Count})."
                : $"Found {_lastSearchResults.Count} station(s), showing {shown}.";
        }
        catch (OperationCanceledException)
        {
            Status = "Search canceled.";
        }
        catch (Exception ex)
        {
            Status = "Search failed: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void AddSelectedStation()
    {
        if (SelectedResult is null)
        {
            Status = "Select a station result first.";
            return;
        }

        _stationRepository.AddStation(SelectedResult.Name, SelectedResult.StreamUrl);
        Status = $"Added station '{SelectedResult.Name}'.";
    }

    [RelayCommand]
    private void RemoveSelectedProvider()
    {
        if (SelectedProvider is null)
        {
            Status = "Select a provider first.";
            return;
        }

        if (_pluginManager.RemovePlugin(SelectedProvider.PluginId, out var error))
        {
            Status = $"Provider '{SelectedProvider.DisplayName}' removed or disabled.";
            RefreshProviders();
            return;
        }

        Status = "Could not remove provider: " + (error ?? "Unknown error.");
    }

    [RelayCommand]
    private void AddPluginFromPath()
    {
        if (string.IsNullOrWhiteSpace(PluginDllPath))
        {
            Status = "Specify a plugin DLL path first.";
            return;
        }

        if (_pluginManager.AddPlugin(PluginDllPath, out var error))
        {
            PluginDllPath = string.Empty;
            Status = "Plugin added. Providers reloaded.";
            RefreshProviders();
            return;
        }

        Status = "Could not add plugin: " + (error ?? "Unknown error.");
    }

    [RelayCommand]
    private async Task ApplyFilterAsync()
    {
        var filtered = _lastSearchResults
            .Where(station => MatchesFilter(station, FilterText))
            .ToArray();

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            Results.Clear();
            foreach (var station in filtered)
            {
                Results.Add(station);
            }

            Status = $"Showing {Results.Count} filtered station(s) from {_lastSearchResults.Count}.";
        });
    }

    [RelayCommand]
    private void OpenPluginManagerWindow()
    {
        _commandDispatcher.Dispatch(new AppCommand { Kind = AppCommandKind.OpenPluginManager });
    }

    private static bool MatchesFilter(DiscoveredStation station, string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return true;
        }

        return station.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
               (station.Description?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false) ||
               (station.Genre?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false) ||
               (station.Country?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private void RefreshProviders()
    {
        void Update()
        {
            Providers.Clear();
            var providers = _pluginManager.GetPlugins()
                .SelectMany(plugin => plugin.Capabilities
                    .OfType<IStationDiscoveryCapability>()
                    .Select(capability => new ProviderOption(plugin.Id, capability.ProviderId, capability.DisplayName)))
                .OrderBy(option => option.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (var provider in providers)
            {
                Providers.Add(provider);
            }

            SelectedProvider ??= Providers.FirstOrDefault();

            if (SelectedProvider is not null && _lastSearchResults.Count == 0 && !IsBusy)
            {
                _ = SearchAsync();
            }
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            Update();
            return;
        }

        Dispatcher.UIThread.Post(Update);
    }

    public sealed class ProviderOption(string pluginId, string id, string displayName)
    {
        public string PluginId { get; } = pluginId;

        public string Id { get; } = id;

        public string DisplayName { get; } = displayName;
    }
}

