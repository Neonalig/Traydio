using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Traydio.Common;
using Traydio.Services;
using Traydio.Views;

namespace Traydio.ViewModels;

[ViewModelFor(typeof(StationSearchPage))]
public partial class StationSearchWindowViewModel : ViewModelBase
{
    private static readonly HttpClient _lookupHttpClient = new();
    private const string _STATUS_OVERRIDE_ID = "station-search";

    private readonly IStationDiscoveryService _stationDiscoveryService;
    private readonly IPluginManager _pluginManager;
    private readonly IPluginInstallDisclaimerService _pluginInstallDisclaimerService;
    private readonly IStationRepository _stationRepository;

    private CancellationTokenSource? _searchCancellation;
    private readonly List<DiscoveredStation> _lastSearchResults = [];
    private int _activePageIndex;

    public ObservableCollection<ProviderOption> Providers { get; } = [];
    public ObservableCollection<SearchModeOption> SearchModes { get; } = [];
    public ObservableCollection<CodeOption> CountryOptions { get; } = [];
    public ObservableCollection<CodeOption> LanguageOptions { get; } = [];

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
    private CodeOption? _selectedCountry;

    [ObservableProperty]
    private CodeOption? _selectedLanguage;

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
        IPluginInstallDisclaimerService pluginInstallDisclaimerService,
        IStationRepository stationRepository)
    {
        _stationDiscoveryService = stationDiscoveryService;
        _pluginManager = pluginManager;
        _pluginInstallDisclaimerService = pluginInstallDisclaimerService;
        _stationRepository = stationRepository;

        _pluginManager.PluginsChanged += (_, _) => RefreshProviders();
        InitializeLanguageOptions();
        LoadCountryOptionsAsync().ForgetWithErrorHandling("Load station search country list", showDialog: false);
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
        SelectedCountry = CountryOptions.FirstOrDefault();
        SelectedLanguage = LanguageOptions.FirstOrDefault();
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
                Country = SupportsCountryFilter ? SelectedCountry?.Code ?? NormalizeFilterValue(CountryFilter) : null,
                Genre = SupportsGenreFilter ? NormalizeFilterValue(GenreFilter) : null,
                Language = SupportsLanguageFilter ? SelectedLanguage?.Code ?? NormalizeFilterValue(LanguageFilter) : null,
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
    private void AddStation(DiscoveredStation? station)
    {
        var target = station ?? SelectedResult;
        if (target is null)
        {
            Status = "Select a station result first.";
            return;
        }

        _stationRepository.AddStation(target.Name, target.StreamUrl);
        Status = $"Added station '{target.Name}'.";
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
    private async Task AddPluginFromPath()
    {
        if (string.IsNullOrWhiteSpace(PluginDllPath))
        {
            Status = "Specify a plugin DLL path first.";
            return;
        }

        var installedBefore = _pluginManager.GetPluginInventory()
            .Select(item => item.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (_pluginManager.AddPlugin(PluginDllPath, out var error))
        {
            var newlyInstalled = _pluginManager.GetPlugins()
                .Where(plugin => !installedBefore.Contains(plugin.Id))
                .ToArray();

            foreach (var plugin in newlyInstalled)
            {
                var disclaimerCapability = plugin.Capabilities.OfType<IPluginInstallDisclaimerCapability>().FirstOrDefault();
                if (disclaimerCapability is null)
                {
                    continue;
                }

                var accepted = await _pluginInstallDisclaimerService
                    .EnsureAcceptedAsync(plugin.Id, disclaimerCapability.Disclaimer, CancellationToken.None)
                    .ConfigureAwait(false);
                if (accepted)
                {
                    continue;
                }

                _pluginManager.RemovePlugin(plugin.Id, out _);
                Status = $"Installation canceled: '{plugin.DisplayName}' conditions were rejected.";
                RefreshProviders();
                return;
            }

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

    private void InitializeLanguageOptions()
    {
        LanguageOptions.Clear();
        LanguageOptions.Add(new CodeOption(string.Empty, "Any language"));

        foreach (var option in new[]
                 {
                     new CodeOption("en", "English"),
                     new CodeOption("es", "Spanish"),
                     new CodeOption("fr", "French"),
                     new CodeOption("de", "German"),
                     new CodeOption("it", "Italian"),
                     new CodeOption("pt", "Portuguese"),
                     new CodeOption("nl", "Dutch"),
                     new CodeOption("sv", "Swedish"),
                     new CodeOption("no", "Norwegian"),
                     new CodeOption("da", "Danish"),
                     new CodeOption("fi", "Finnish"),
                     new CodeOption("pl", "Polish"),
                     new CodeOption("cs", "Czech"),
                     new CodeOption("tr", "Turkish"),
                     new CodeOption("ru", "Russian"),
                     new CodeOption("uk", "Ukrainian"),
                     new CodeOption("ja", "Japanese"),
                     new CodeOption("ko", "Korean"),
                     new CodeOption("zh", "Chinese"),
                     new CodeOption("ar", "Arabic"),
                 })
        {
            LanguageOptions.Add(option);
        }

        SelectedLanguage = LanguageOptions.FirstOrDefault();
    }

    private async Task LoadCountryOptionsAsync()
    {
        var countries = await FetchFmStreamItuCountriesAsync().ConfigureAwait(false);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            CountryOptions.Clear();
            CountryOptions.Add(new CodeOption(string.Empty, "Any country"));
            foreach (var country in countries)
            {
                CountryOptions.Add(country);
            }

            SelectedCountry = CountryOptions.FirstOrDefault();
        });
    }

    private static async Task<IReadOnlyList<CodeOption>> FetchFmStreamItuCountriesAsync()
    {
        try
        {
            var payload = await _lookupHttpClient.GetStringAsync("https://fmstream.org/itu.php").ConfigureAwait(false);
            var parsed = ParseCountryMapPayload(payload);
            if (parsed.Count > 0)
            {
                return parsed;
            }
        }
        catch
        {
            // Network or parsing issues fall back to a small static list.
        }

        return
        [
            new CodeOption("ABW", "Aruba (HOL)"),
            new CodeOption("AFG", "Afghanistan"),
            new CodeOption("AFS", "South Africa"),
            new CodeOption("AUS", "Australia"),
            new CodeOption("AUT", "Austria"),
            new CodeOption("BEL", "Belgium"),
            new CodeOption("BRA", "Brazil"),
            new CodeOption("CAN", "Canada"),
            new CodeOption("DEU", "Germany"),
            new CodeOption("ESP", "Spain"),
            new CodeOption("FRA", "France"),
            new CodeOption("GBR", "United Kingdom"),
            new CodeOption("ITA", "Italy"),
            new CodeOption("JPN", "Japan"),
            new CodeOption("NLD", "Netherlands"),
            new CodeOption("POL", "Poland"),
            new CodeOption("PRT", "Portugal"),
            new CodeOption("SWE", "Sweden"),
            new CodeOption("USA", "United States"),
        ];
    }

    private static IReadOnlyList<CodeOption> ParseCountryMapPayload(string payload)
    {
        var start = payload.IndexOf('{');
        var end = payload.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return [];
        }

        var json = payload.Substring(start, end - start + 1);
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        var list = new List<CodeOption>();
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var code = property.Name.Trim().ToUpperInvariant();
            var name = property.Value.GetString()?.Trim();
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            list.Add(new CodeOption(code, $"{name} ({code})"));
        }

        return list.OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
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

    partial void OnStatusChanged(string value)
    {
        var status = value.Trim();
        if (string.IsNullOrWhiteSpace(status))
        {
            RibbonStatusHub.RemoveOverride(_STATUS_OVERRIDE_ID);
            return;
        }

        // Keep station-search feedback visible, but lower priority than focused actions elsewhere.
        RibbonStatusHub.SetOverride(_STATUS_OVERRIDE_ID, status, priority: 10);
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

    public sealed class CodeOption(string code, string displayName)
    {
        public string Code { get; } = code;

        public string DisplayName { get; } = displayName;
    }
}

