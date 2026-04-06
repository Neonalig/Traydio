using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Interactivity;
using JetBrains.Annotations;
using Microsoft.Extensions.Logging;
using Traydio.Common;

namespace Traydio.Plugin.StreamUrlLink;

[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public sealed partial class StreamUrlLinkPlugin : ITraydioPlugin
{
    public const string PLUGIN_ID = "plugin.streamurl.link";

    public static PluginInstallDisclaimer SettingsDisclaimer { get; } = new()
    {
        Version = "2026-04-06",
        Title = "StreamUrl.link provider information",
        Message =
            "This provider scrapes streamurl.link search results and may return inaccurate or stale links.\n\n" +
            "Please verify stream safety and legality before use.",
        LinkText = "Open streamurl.link",
        LinkUrl = "https://streamurl.link/",
        AcceptButtonText = "OK",
        RejectButtonText = "Close",
    };

    private static readonly HttpClient _httpClient = new();
    private static readonly StationSearchProviderFeatures _features = new()
    {
        SupportsPagination = false,
        SupportsModes = false,
        SupportsCountryFilter = false,
        SupportsGenreFilter = false,
        SupportsLanguageFilter = false,
        SupportsHighQualityPreference = false,
        SupportsOrderFilter = false,
        DefaultPageSize = 100,
        SupportedModes = [StationSearchMode.Query],
    };

    private readonly ILogger<StreamUrlLinkPlugin> _logger;

    public StreamUrlLinkPlugin(ILogger<StreamUrlLinkPlugin> logger)
    {
        _logger = logger;
        Capabilities = [new StationDiscoveryCapability(this), new SettingsCapability()];
    }

    public string Id => PLUGIN_ID;

    public string DisplayName => "StreamUrl.link";

    public IReadOnlyList<IPluginCapability> Capabilities { get; }

    private static string _providerId => "streamurl.link";

    private async IAsyncEnumerable<DiscoveredStation> SearchStationsAsync(
        StationSearchRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var query = Uri.EscapeDataString(request.Query);
        var url = $"https://streamurl.link/search?q={query}";

        DiscoveredStation[] stations;
        try
        {
            var html = await _httpClient.GetStringAsync(url, cancellationToken).ConfigureAwait(false);

            var linkPattern = LinkPattern();

            var results = new List<DiscoveredStation>();
            foreach (Match match in linkPattern.Matches(html))
            {
                var href = match.Groups["href"].Value;
                var title = WebUtility.HtmlDecode(match.Groups["title"].Value.Trim());

                if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(href))
                {
                    continue;
                }

                if (!href.Contains("stream", StringComparison.OrdinalIgnoreCase) &&
                    !href.EndsWith(".m3u", StringComparison.OrdinalIgnoreCase) &&
                    !href.EndsWith(".pls", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                results.Add(new DiscoveredStation
                {
                    Name = title,
                    StreamUrl = href,
                    Description = "Discovered via streamurl.link search",
                });
            }

            stations = results
                .GroupBy(r => r.StreamUrl, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .Take(Math.Max(1, request.Limit))
                .ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "streamurl search failed for query={Query}", query);
            yield break;
        }

        foreach (var station in stations)
        {
            yield return station;
        }
    }

    private sealed class StationDiscoveryCapability(StreamUrlLinkPlugin plugin) : IStationDiscoveryCapability
    {
        public string CapabilityId => "station-discovery";

        public StationSearchProviderFeatures Features => _features;

        public string ProviderId => _providerId;

        public string DisplayName => plugin.DisplayName;

        public IAsyncEnumerable<DiscoveredStation> SearchAsync(StationSearchRequest request, CancellationToken cancellationToken)
        {
            return plugin.SearchStationsAsync(request, cancellationToken);
        }
    }

    private sealed class SettingsCapability : IPluginSettingsCapability
    {
        public string CapabilityId => "plugin-settings";

        public string DisplayName => "StreamUrl.link";

        public object CreateSettingsView(IPluginSettingsAccessor settingsAccessor)
        {
            return new SettingsView(settingsAccessor);
        }
    }

    private sealed class SettingsView : UserControl
    {
        private readonly IPluginSettingsAccessor _settingsAccessor;
        private readonly TextBlock _statusText;

        public SettingsView(IPluginSettingsAccessor settingsAccessor)
        {
            _settingsAccessor = settingsAccessor;
            _statusText = new TextBlock();

            var root = new Grid
            {
                Margin = new Avalonia.Thickness(12),
                RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto,Auto,Auto"),
                RowSpacing = 10,
            };

            root.Children.Add(new TextBlock
            {
                Text = "StreamUrl.link Provider Settings",
                FontSize = 18,
                FontWeight = Avalonia.Media.FontWeight.SemiBold,
            });

            var infoText = new TextBlock
            {
                Text = "This provider currently has no configurable runtime options. Use the links below for source/disclaimer information.",
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            };
            Grid.SetRow(infoText, 1);
            root.Children.Add(infoText);

            var siteLinkButton = CreateHyperlinkButton("Open streamurl.link", OnOpenWebsiteClick);
            siteLinkButton.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
            Grid.SetRow(siteLinkButton, 3);
            root.Children.Add(siteLinkButton);

            var legalPanel = new StackPanel
            {
                Spacing = 6,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            };
            legalPanel.Children.Add(new TextBlock
            {
                Text = "Copyright (C) Traydio contributors. Data source: streamurl.link",
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

            Grid.SetRow(legalPanel, 4);
            root.Children.Add(legalPanel);

            _statusText.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
            Grid.SetRow(_statusText, 5);
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

        private void OnOpenWebsiteClick(object? sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(SettingsDisclaimer.LinkUrl))
            {
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = SettingsDisclaimer.LinkUrl,
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
                SettingsDisclaimer,
                requireAcceptance: false);
            if (!shown)
            {
                _statusText.Text = "Could not display disclaimer dialog.";
            }
        }
    }

    [GeneratedRegex("<a[^>]+href=[\"'](?<href>https?://[^\"']+)[\"'][^>]*>(?<title>[^<]+)</a>", RegexOptions.IgnoreCase)]
    private static partial Regex LinkPattern();
}

