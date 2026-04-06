using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RadioBrowser.Net.Entities;
using RadioBrowser.Net.Services;
using Traydio.Common;

namespace Traydio.Plugin.RadioBrowser;

[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public sealed class RadioBrowserPlugin : ITraydioPlugin
{
    public const string PLUGIN_ID = "plugin.radio-browser.info";

    private const string _USER_AGENT = "Traydio/2026";

    public static PluginInstallDisclaimer InstallDisclaimer { get; } = new()
    {
        Version = "2026-04-06",
        Title = "Radio Browser provider information",
        Message =
            "This plugin uses community-managed radio-browser station data.\n\n" +
            "Station entries and stream links may be incorrect or stale. Verify stream legality and safety before use.",
        LinkText = "Open radio-browser.info",
        LinkUrl = "https://www.radio-browser.info/",
        AcceptButtonText = "Accept",
        RejectButtonText = "Reject",
    };

    private static readonly Lazy<ServiceProvider> _serviceProvider = new(CreateServiceProvider, isThreadSafe: true);
    private static readonly Lazy<IStationService> _stationService = new(() => _serviceProvider.Value.GetRequiredService<IStationService>(), isThreadSafe: true);

    private static readonly StationSearchProviderFeatures _features = new()
    {
        SupportsPagination = true,
        SupportsModes = true,
        SupportsCountryFilter = true,
        SupportsGenreFilter = true,
        SupportsLanguageFilter = true,
        SupportsHighQualityPreference = true,
        SupportsOrderFilter = true,
        DefaultPageSize = 50,
        SupportedModes = [StationSearchMode.Query, StationSearchMode.Featured, StationSearchMode.Random],
    };

    private readonly ILogger<RadioBrowserPlugin> _logger;
    private readonly IPluginSettingsProvider? _settingsProvider;

    public RadioBrowserPlugin(ILogger<RadioBrowserPlugin> logger, IPluginSettingsProvider? settingsProvider = null)
    {
        _logger = logger;
        _settingsProvider = settingsProvider;
        Capabilities = [new StationDiscoveryCapability(this), new SettingsCapability()];
    }

    public string Id => PLUGIN_ID;

    public string DisplayName => "Radio Browser";

    public IReadOnlyList<IPluginCapability> Capabilities { get; }

    private static string _providerId => "radio-browser.info";

    private IReadOnlyDictionary<string, string> GetSettings()
    {
        return _settingsProvider?.GetPluginSettings(PLUGIN_ID) ?? new Dictionary<string, string>();
    }

    private static ServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddRadioBrowserServices(_USER_AGENT);
        return services.BuildServiceProvider();
    }

    private async IAsyncEnumerable<DiscoveredStation> SearchStationsAsync(
        StationSearchRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        IReadOnlyCollection<Station> stations;

        try
        {
            stations = await FetchStationsAsync(request, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            yield break;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Radio Browser search failed for mode={Mode}, query={Query}", request.Mode, request.Query);
            yield break;
        }

        foreach (var station in stations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var streamUrl = GetStreamUrl(station);
            if (!IsValidStreamUrl(streamUrl))
            {
                continue;
            }

            if (!seenUrls.Add(NormalizeUrl(streamUrl!)))
            {
                continue;
            }

            var genre = JoinValues(station.Tags, maxCount: 4);
            var country = !string.IsNullOrWhiteSpace(station.Country)
                ? station.Country
                : station.CountryCode;

            yield return new DiscoveredStation
            {
                Name = string.IsNullOrWhiteSpace(station.Name) ? "Unknown Station" : station.Name,
                StreamUrl = streamUrl!,
                Description = BuildDescription(station),
                Genre = genre,
                Country = country,
            };
        }
    }

    private static async Task<IReadOnlyCollection<Station>> FetchStationsAsync(StationSearchRequest request, CancellationToken cancellationToken)
    {
        var stationService = _stationService.Value;
        var filter = BuildSearchFilter(request);

        if (request.Mode == StationSearchMode.Random)
        {
            filter.Order = StationSortOrder.Random;
            filter.Reverse = false;
            return (await stationService.FetchAsync(filter, cancellationToken)).ToArray();
        }

        if (request.Mode == StationSearchMode.Featured || IsDefaultFeaturedRequest(request))
        {
            filter.Order = StationSortOrder.Votes;
            filter.Reverse = true;
            return (await stationService.FetchAsync(filter, cancellationToken)).ToArray();
        }

        var advanced = BuildAdvancedSearch(request, filter);
        return (await stationService.AdvancedSearchAsync(advanced, cancellationToken)).ToArray();
    }

    private static bool IsDefaultFeaturedRequest(StationSearchRequest request)
    {
        return string.IsNullOrWhiteSpace(request.Query)
               && string.IsNullOrWhiteSpace(request.Country)
               && string.IsNullOrWhiteSpace(request.Genre)
               && string.IsNullOrWhiteSpace(request.Language);
    }

    private static StationSearchFilter BuildSearchFilter(StationSearchRequest request)
    {
        var filter = new StationSearchFilter
        {
            HideBroken = true,
            Offset = Math.Max(0, request.Offset),
            Limit = Math.Clamp(request.Limit, 1, 500),
        };

        if (TryParseSortOrder(request.Order, out var parsedOrder, out var reverse))
        {
            filter.Order = parsedOrder;
            filter.Reverse = reverse;
        }

        return filter;
    }

    private static AdvancedStationSearch BuildAdvancedSearch(StationSearchRequest request, StationSearchFilter filter)
    {
        var genre = request.Genre?.Trim() ?? string.Empty;
        var language = request.Language?.Trim() ?? string.Empty;
        var country = request.Country?.Trim() ?? string.Empty;

        var advanced = new AdvancedStationSearch
        {
            Name = request.Query?.Trim() ?? string.Empty,
            NameExact = false,
            Language = language,
            LanguageExact = false,
            Tag = genre,
            TagExact = false,
            TagList = SplitValues(genre),
            Offset = filter.Offset,
            Limit = filter.Limit,
            HideBroken = filter.HideBroken,
            Order = filter.Order,
            Reverse = filter.Reverse,
            MinimumBitrate = request.PreferHighQuality == true ? 96 : 0,
            MaximumBitrate = 1_000_000,
        };

        if (country.Length == 2)
        {
            advanced.CountryCode = country;
        }
        else
        {
            advanced.Country = country;
        }

        return advanced;
    }

    private static bool TryParseSortOrder(string? rawOrder, out StationSortOrder order, out bool reverse)
    {
        order = default;
        reverse = false;

        if (string.IsNullOrWhiteSpace(rawOrder))
        {
            return false;
        }

        var token = rawOrder.Trim();
        if (token.StartsWith('-'))
        {
            reverse = true;
            token = token[1..];
        }

        if (token.EndsWith("_desc", StringComparison.OrdinalIgnoreCase))
        {
            reverse = true;
            token = token[..^5];
        }
        else if (token.EndsWith("_asc", StringComparison.OrdinalIgnoreCase))
        {
            token = token[..^4];
        }

        token = token.Trim();
        if (string.Equals(token, "top", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(token, "popular", StringComparison.OrdinalIgnoreCase))
        {
            order = StationSortOrder.Votes;
            reverse = true;
            return true;
        }

        if (Enum.TryParse<StationSortOrder>(token, ignoreCase: true, out var parsed))
        {
            order = parsed;
            return true;
        }

        return false;
    }

    private static IEnumerable<string> SplitValues(string value)
    {
        return value
            .Split([',', ';', '|'], StringSplitOptions.RemoveEmptyEntries)
            .Select(v => v.Trim())
            .Where(v => !string.IsNullOrWhiteSpace(v));
    }

    private static string? BuildDescription(Station station)
    {
        var chunks = new List<string>();

        var languages = JoinValues(station.Languages, maxCount: 2);
        if (!string.IsNullOrWhiteSpace(languages))
        {
            chunks.Add("Lang: " + languages);
        }

        if (station.Bitrate > 0)
        {
            chunks.Add("Bitrate: " + station.Bitrate + " kbps");
        }

        if (station.Votes > 0)
        {
            chunks.Add("Votes: " + station.Votes);
        }

        return chunks.Count == 0 ? null : string.Join(" | ", chunks);
    }

    private static string? JoinValues(IEnumerable<string>? values, int maxCount)
    {
        if (values is null)
        {
            return null;
        }

        var selected = values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(maxCount)
            .ToArray();

        return selected.Length == 0 ? null : string.Join(", ", selected);
    }

    private static string? GetStreamUrl(Station station)
    {
        if (IsValidStreamUrl(station.ResolvedUrl))
        {
            return station.ResolvedUrl;
        }

        return IsValidStreamUrl(station.Url)
            ? station.Url
            : null;
    }

    private static string NormalizeUrl(string value)
    {
        return value.Trim().TrimEnd('/');
    }

    private static bool IsValidStreamUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    private sealed class StationDiscoveryCapability(RadioBrowserPlugin plugin) : IStationDiscoveryCapability
    {
        public string CapabilityId => "station-discovery";

        public string ProviderId => _providerId;

        public string DisplayName => plugin.DisplayName;

        public StationSearchProviderFeatures Features => _features;

        public IAsyncEnumerable<DiscoveredStation> SearchAsync(StationSearchRequest request, CancellationToken cancellationToken)
        {
            return plugin.SearchStationsAsync(request, cancellationToken);
        }
    }

    private sealed class SettingsCapability : IPluginSettingsCapability
    {
        public string CapabilityId => "plugin-settings";

        public string DisplayName => "Radio Browser";

        public object CreateSettingsView(IPluginSettingsAccessor settingsAccessor)
        {
            return new RadioBrowserPluginSettingsView(settingsAccessor);
        }
    }

    private sealed class RadioBrowserPluginSettingsView : UserControl
    {
        private readonly IPluginSettingsAccessor _settingsAccessor;
        private readonly TextBox _baseUrlTextBox;
        private readonly TextBlock _statusText;

        public RadioBrowserPluginSettingsView(IPluginSettingsAccessor settingsAccessor)
        {
            _settingsAccessor = settingsAccessor;
            _statusText = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };

            var root = new Grid
            {
                Margin = new Avalonia.Thickness(12),
                RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto,Auto,Auto"),
                RowSpacing = 10,
            };

            root.Children.Add(new TextBlock
            {
                Text = "Radio Browser Provider Settings",
                FontSize = 18,
                FontWeight = Avalonia.Media.FontWeight.SemiBold,
            });

            var description = new TextBlock
            {
                Text = "RadioBrowser.Net selects official mirrors automatically. This custom endpoint field is kept for compatibility but is not currently used by the package client.",
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            };
            Grid.SetRow(description, 1);
            root.Children.Add(description);

            _baseUrlTextBox = new TextBox
            {
                Watermark = RadioBrowserPluginSettings.DEFAULT_API_BASE_URL,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            };
            _baseUrlTextBox.Text = _settingsAccessor.GetValue(RadioBrowserPluginSettings.API_BASE_URL_KEY)
                               ?? RadioBrowserPluginSettings.DEFAULT_API_BASE_URL;
            _baseUrlTextBox.LostFocus += OnBaseUrlLostFocus;

            var baseUrlPanel = new StackPanel { Spacing = 6 };
            baseUrlPanel.Children.Add(new TextBlock { Text = "Legacy API Base URL (not used)" });
            baseUrlPanel.Children.Add(_baseUrlTextBox);
            Grid.SetRow(baseUrlPanel, 2);
            root.Children.Add(baseUrlPanel);

            var siteLinkButton = CreateHyperlinkButton("Open radio-browser.info", OnOpenWebsiteClick);
            siteLinkButton.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
            Grid.SetRow(siteLinkButton, 4);
            root.Children.Add(siteLinkButton);

            var legalPanel = new StackPanel
            {
                Spacing = 6,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            };
            legalPanel.Children.Add(new TextBlock
            {
                Text = "Copyright (C) Traydio contributors. Data source: radio-browser.info",
                Foreground = Avalonia.Media.Brushes.Gray,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            });

            var disclaimerButton = new Button
            {
                Content = "View disclaimer information",
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            };
            disclaimerButton.Click += OnShowDisclaimerClick;
            legalPanel.Children.Add(disclaimerButton);

            Grid.SetRow(legalPanel, 5);
            root.Children.Add(legalPanel);

            Grid.SetRow(_statusText, 6);
            root.Children.Add(_statusText);

            Content = root;
        }

        private static Button CreateHyperlinkButton(string text, EventHandler<RoutedEventArgs> clickHandler)
        {
            var textBlock = new TextBlock
            {
                Text = text,
                Foreground = Avalonia.Media.Brushes.DodgerBlue,
                TextDecorations = Avalonia.Media.TextDecorations.Underline,
            };

            var button = new Button
            {
                Content = textBlock,
                Background = Avalonia.Media.Brushes.Transparent,
                BorderThickness = new Avalonia.Thickness(0),
                Padding = new Avalonia.Thickness(0),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            };
            button.Click += clickHandler;
            return button;
        }

        private void OnBaseUrlLostFocus(object? sender, RoutedEventArgs e)
        {
            var value = (_baseUrlTextBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(value) ||
                string.Equals(value, RadioBrowserPluginSettings.DEFAULT_API_BASE_URL, StringComparison.OrdinalIgnoreCase))
            {
                _settingsAccessor.SetValue(RadioBrowserPluginSettings.API_BASE_URL_KEY, null);
                _baseUrlTextBox.Text = RadioBrowserPluginSettings.DEFAULT_API_BASE_URL;
                return;
            }

            _settingsAccessor.SetValue(RadioBrowserPluginSettings.API_BASE_URL_KEY, value);
        }

        private void OnOpenWebsiteClick(object? sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(InstallDisclaimer.LinkUrl))
            {
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = InstallDisclaimer.LinkUrl,
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                _statusText.Text = "Could not open browser: " + ex.Message;
            }
        }

        private async void OnShowDisclaimerClick(object? sender, RoutedEventArgs e)
        {
            var shown = await _settingsAccessor.ShowInstallDisclaimerAsync(
                PLUGIN_ID,
                InstallDisclaimer,
                requireAcceptance: false);
            if (!shown)
            {
                _statusText.Text = "Could not display disclaimer dialog.";
            }
        }
    }
}

