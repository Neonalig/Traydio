using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Traydio.Common;
using Traydio.Services;
using Traydio.Views;

namespace Traydio.ViewModels;

[ViewModelFor(typeof(StationSearchWindow))]
public partial class StationSearchWindowViewModel : ViewModelBase
{
    private readonly IStationDiscoveryService _stationDiscoveryService;
    private readonly IPluginManager _pluginManager;
    private readonly IStationRepository _stationRepository;

    private CancellationTokenSource? _searchCancellation;

    public ObservableCollection<ProviderOption> Providers { get; } = new();

    public ObservableCollection<DiscoveredStation> Results { get; } = new();

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
        IStationRepository stationRepository)
    {
        _stationDiscoveryService = stationDiscoveryService;
        _pluginManager = pluginManager;
        _stationRepository = stationRepository;

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

        _searchCancellation?.Cancel();
        _searchCancellation = new CancellationTokenSource();

        IsBusy = true;
        Status = "Searching...";
        try
        {
            var request = new StationSearchRequest
            {
                Query = Query,
                Limit = 200,
            };

            var results = await _stationDiscoveryService
                .SearchAsync(SelectedProvider.Id, request, _searchCancellation.Token)
                .ConfigureAwait(false);

            var filtered = ApplyFilter(results, FilterText);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Results.Clear();
                foreach (var station in filtered)
                {
                    Results.Add(station);
                }

                Status = $"Found {Results.Count} station(s).";
            });
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
            Status = $"Provider '{SelectedProvider.DisplayName}' disabled.";
            RefreshProviders();
            return;
        }

        Status = "Could not disable provider: " + (error ?? "Unknown error.");
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
        if (SelectedProvider is null)
        {
            return;
        }

        var request = new StationSearchRequest
        {
            Query = Query,
            Limit = 200,
        };

        var results = await _stationDiscoveryService
            .SearchAsync(SelectedProvider.Id, request, CancellationToken.None)
            .ConfigureAwait(false);

        var filtered = ApplyFilter(results, FilterText);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            Results.Clear();
            foreach (var station in filtered)
            {
                Results.Add(station);
            }

            Status = $"Showing {Results.Count} filtered station(s).";
        });
    }

    private static DiscoveredStation[] ApplyFilter(IReadOnlyList<DiscoveredStation> results, string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return results.ToArray();
        }

        return results.Where(r =>
                r.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                (r.Description?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (r.Genre?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (r.Country?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false))
            .ToArray();
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
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            Update();
            return;
        }

        Dispatcher.UIThread.Post(Update);
    }

    public sealed class ProviderOption
    {
        public ProviderOption(string pluginId, string id, string displayName)
        {
            PluginId = pluginId;
            Id = id;
            DisplayName = displayName;
        }

        public string PluginId { get; }

        public string Id { get; }

        public string DisplayName { get; }
    }
}

