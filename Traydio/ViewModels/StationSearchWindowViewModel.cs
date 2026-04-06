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
    private int _activePageIndex;

    public ObservableCollection<ProviderOption> Providers { get; } = [];
    public ObservableCollection<SearchModeOption> SearchModes { get; } = [];

    public ObservableCollection<DiscoveredStation> Results { get; } = [];

    [ObservableProperty]
    private ProviderOption? _selectedProvider;

    [ObservableProperty]
    private DiscoveredStation? _selectedResult;

    [ObservableProperty]
    private string _query = string.Empty;

    [ObservableProperty]
    private SearchModeOption? _selectedSearchMode;

    [ObservableProperty]
    private string _countryFilter = string.Empty;

    [ObservableProperty]
    private string _genreFilter = string.Empty;

    [ObservableProperty]
    private string _languageFilter = string.Empty;

    [ObservableProperty]
    private string _orderFilter = string.Empty;

    [ObservableProperty]
    private bool _preferHighQuality = true;

    [ObservableProperty]
    private int _pageSize = 50;

    [ObservableProperty]
    private string _filterText = string.Empty;

    [ObservableProperty]
    private string _pluginDllPath = string.Empty;

    [ObservableProperty]
    private string _status = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _hasPreviousPage;

    [ObservableProperty]
    private bool _hasNextPage;

    public int CurrentPage => _activePageIndex + 1;

    public bool SupportsModes => SelectedProvider?.Features.SupportsModes ?? false;

    public bool SupportsCountryFilter => SelectedProvider?.Features.SupportsCountryFilter ?? false;

    public bool SupportsGenreFilter => SelectedProvider?.Features.SupportsGenreFilter ?? false;

    public bool SupportsLanguageFilter => SelectedProvider?.Features.SupportsLanguageFilter ?? false;

    public bool SupportsOrderFilter => SelectedProvider?.Features.SupportsOrderFilter ?? false;

    public bool SupportsHighQualityPreference => SelectedProvider?.Features.SupportsHighQualityPreference ?? false;

    public bool SupportsPagination => SelectedProvider?.Features.SupportsPagination ?? false;

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
        _activePageIndex = 0;
        await ExecuteSearchAsync().ConfigureAwait(false);
    }

    [RelayCommand]
    private async Task NextPageAsync()
    {
        if (!SupportsPagination || !HasNextPage)
        {
            return;
        }

        _activePageIndex++;
        await ExecuteSearchAsync().ConfigureAwait(false);
    }

    [RelayCommand]
    private async Task PreviousPageAsync()
    {
        if (!SupportsPagination || _activePageIndex == 0)
        {
            return;
        }

        _activePageIndex--;
        await ExecuteSearchAsync().ConfigureAwait(false);
    }

    [RelayCommand]
    private void ResetAdvancedFilters()
    {
        CountryFilter = string.Empty;
        GenreFilter = string.Empty;
        LanguageFilter = string.Empty;
        OrderFilter = string.Empty;
        PreferHighQuality = true;
        _activePageIndex = 0;
        OnPropertyChanged(nameof(CurrentPage));
    }

    private async Task ExecuteSearchAsync()
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
        HasPreviousPage = _activePageIndex > 0;
        HasNextPage = false;
        Status = "Searching...";
        try
        {
            var pageSize = Math.Clamp(PageSize, 1, 200);
            var mode = SelectedSearchMode?.Mode ?? StationSearchMode.Query;
            var request = new StationSearchRequest
            {
                Mode = mode,
                Query = Query,
                Country = SupportsCountryFilter ? NormalizeFilterValue(CountryFilter) : null,
                Genre = SupportsGenreFilter ? NormalizeFilterValue(GenreFilter) : null,
                Language = SupportsLanguageFilter ? NormalizeFilterValue(LanguageFilter) : null,
                Order = SupportsOrderFilter ? NormalizeFilterValue(OrderFilter) : null,
                PreferHighQuality = SupportsHighQualityPreference ? PreferHighQuality : null,
                Offset = SupportsPagination ? _activePageIndex * pageSize : 0,
                Limit = pageSize,
            };

            _lastSearchResults.Clear();
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Results.Clear();
                Status = "Searching...";
                OnPropertyChanged(nameof(CurrentPage));
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

            HasPreviousPage = SupportsPagination && _activePageIndex > 0;
            HasNextPage = SupportsPagination && _lastSearchResults.Count >= pageSize;

            var modeLabel = mode switch
            {
                StationSearchMode.Featured => "featured",
                StationSearchMode.Random => "random",
                _ => "search",
            };

            Status = SupportsPagination
                ? $"Loaded {modeLabel} page {CurrentPage} with {_lastSearchResults.Count} station(s), showing {shown}."
                : $"Loaded {modeLabel} results: {_lastSearchResults.Count} station(s), showing {shown}.";
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

    partial void OnSelectedProviderChanged(ProviderOption? value)
    {
        UpdateSearchModes(value?.Features ?? StationSearchProviderFeatures.Basic);
        OnPropertyChanged(nameof(SupportsModes));
        OnPropertyChanged(nameof(SupportsCountryFilter));
        OnPropertyChanged(nameof(SupportsGenreFilter));
        OnPropertyChanged(nameof(SupportsLanguageFilter));
        OnPropertyChanged(nameof(SupportsOrderFilter));
        OnPropertyChanged(nameof(SupportsHighQualityPreference));
        OnPropertyChanged(nameof(SupportsPagination));

        if (value is not null)
        {
            PageSize = Math.Clamp(value.Features.DefaultPageSize, 1, 200);
        }

        _activePageIndex = 0;
        HasPreviousPage = false;
        HasNextPage = false;
        OnPropertyChanged(nameof(CurrentPage));
    }

    private void UpdateSearchModes(StationSearchProviderFeatures features)
    {
        SearchModes.Clear();
        var supportedModes = features.SupportsModes
            ? features.SupportedModes
            : [StationSearchMode.Query];

        foreach (var mode in supportedModes.Distinct())
        {
            SearchModes.Add(new SearchModeOption(mode, ToModeLabel(mode)));
        }

        SelectedSearchMode = SearchModes.FirstOrDefault(mode => mode.Mode == StationSearchMode.Featured)
            ?? SearchModes.FirstOrDefault(mode => mode.Mode == StationSearchMode.Query)
            ?? SearchModes.FirstOrDefault();
    }

    private static string ToModeLabel(StationSearchMode mode)
    {
        return mode switch
        {
            StationSearchMode.Featured => "Featured",
            StationSearchMode.Random => "Random",
            _ => "Search",
        };
    }

    private static string? NormalizeFilterValue(string value)
    {
        var trimmed = value.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private void RefreshProviders()
    {
        void Update()
        {
            Providers.Clear();
            var providers = _pluginManager.GetPlugins()
                .SelectMany(plugin => plugin.Capabilities
                    .OfType<IStationDiscoveryCapability>()
                    .Select(capability => new ProviderOption(
                        plugin.Id,
                        capability.ProviderId,
                        capability.DisplayName,
                        capability.Features)))
                .OrderBy(option => option.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (var provider in providers)
            {
                Providers.Add(provider);
            }

            SelectedProvider ??= Providers.FirstOrDefault();

            if (SelectedProvider is not null && _lastSearchResults.Count == 0 && !IsBusy)
            {
                SearchAsync().ForgetWithErrorHandling("Station provider initial search", showDialog: true);
            }
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            Update();
            return;
        }

        Dispatcher.UIThread.Post(Update);
    }

    public sealed class ProviderOption(string pluginId, string id, string displayName, StationSearchProviderFeatures features)
    {
        public string PluginId { get; } = pluginId;

        public string Id { get; } = id;

        public string DisplayName { get; } = displayName;

        public StationSearchProviderFeatures Features { get; } = features;
    }

    public sealed class SearchModeOption(StationSearchMode mode, string displayName)
    {
        public StationSearchMode Mode { get; } = mode;

        public string DisplayName { get; } = displayName;
    }
}

