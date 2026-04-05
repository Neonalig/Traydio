using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Traydio.Common;
using Traydio.Services;
using Traydio.ViewModels;

namespace Traydio.Views;

[ViewFor(typeof(StationManagerPageViewModel))]
public partial class StationManagerPage : UserControl
{
    // ReSharper disable once InconsistentNaming
    private const string _M3U_EXTINF_PREFIX = "#EXTINF:";

    public StationManagerPage()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public StationManagerPage(StationManagerPageViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = HasSupportedDrop(e) ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        OnDropAsync(e).ForgetWithErrorHandling("Station drop import", showDialog: true);
    }

    private async Task OnDropAsync(DragEventArgs e)
    {
        if (DataContext is not StationManagerPageViewModel viewModel)
        {
            return;
        }

        var files = e.DataTransfer.TryGetFiles()
            ?.OfType<IStorageFile>()
            .Select(f => f.TryGetLocalPath())
            .Where(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p))
            .Select(p => p!)
            .Where(IsSupportedPlaylistPath)
            .ToArray() ?? [];

        if (files.Length > 0)
        {
            var stations = new List<(string Name, string Url)>();
            foreach (var file in files)
            {
                stations.AddRange(await ParsePlaylistFileAsync(file).ConfigureAwait(true));
            }

            var distinctStations = stations
                .Where(s => !string.IsNullOrWhiteSpace(s.Url))
                .DistinctBy(s => s.Url, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (distinctStations.Length == 0)
            {
                return;
            }

            if (files.Length == 1 && distinctStations.Length == 1)
            {
                viewModel.PrefillNewStation(distinctStations[0].Name, distinctStations[0].Url);
                return;
            }

            viewModel.AddStationsFromDrop(distinctStations);
            return;
        }

        if (e.DataTransfer.Contains(DataFormat.Text))
        {
            var text = e.DataTransfer.TryGetText();
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            var links = text
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => Uri.TryCreate(line, UriKind.Absolute, out _))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (links.Length == 0)
            {
                return;
            }

            if (links.Length == 1)
            {
                viewModel.PrefillNewStation("Dropped stream", links[0]);
                return;
            }

            viewModel.AddStationsFromDrop(links.Select((url, idx) => ($"Dropped stream {idx + 1}", url)));
        }
    }

    private void OnStationsListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.C && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            CopySelectedStationIdAsync().ForgetWithErrorHandling("Copy selected station id", showDialog: true);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Delete)
        {
            DeleteSelectedStations();
            e.Handled = true;
        }
    }

    private bool HasSupportedDrop(DragEventArgs e)
    {
        if (e.DataTransfer.Contains(DataFormat.Text))
        {
            return true;
        }

        if (!e.DataTransfer.Contains(DataFormat.File))
        {
            return false;
        }

        var paths = e.DataTransfer.TryGetFiles()
            ?.OfType<IStorageFile>()
            .Select(f => f.TryGetLocalPath())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!)
            .ToArray();

        return paths is { Length: > 0 } && paths.Any(IsSupportedPlaylistPath);
    }

    private async Task<List<(string Name, string Url)>> ParsePlaylistFileAsync(string path)
    {
        var ext = Path.GetExtension(path);
        if (string.Equals(ext, ".pls", StringComparison.OrdinalIgnoreCase))
        {
            return await ParsePlsAsync(path).ConfigureAwait(true);
        }

        return await ParseM3UOrTextAsync(path).ConfigureAwait(true);
    }

    private async Task<List<(string Name, string Url)>> ParseM3UOrTextAsync(string path)
    {
        var lines = await File.ReadAllLinesAsync(path).ConfigureAwait(true);
        var fallbackName = Path.GetFileNameWithoutExtension(path);
        var results = new List<(string Name, string Url)>();

        string? pendingTitle = null;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (line.StartsWith(_M3U_EXTINF_PREFIX, StringComparison.OrdinalIgnoreCase))
            {
                var commaIndex = line.IndexOf(',');
                if (commaIndex >= 0 && commaIndex + 1 < line.Length)
                {
                    pendingTitle = line[(commaIndex + 1)..].Trim();
                }

                continue;
            }

            if (line.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            if (!Uri.TryCreate(line, UriKind.Absolute, out _))
            {
                continue;
            }

            var name = string.IsNullOrWhiteSpace(pendingTitle)
                ? fallbackName
                : pendingTitle;

            results.Add((name, line));
            pendingTitle = null;
        }

        return results;
    }

    private async Task<List<(string Name, string Url)>> ParsePlsAsync(string path)
    {
        var lines = await File.ReadAllLinesAsync(path).ConfigureAwait(true);
        var fallbackName = Path.GetFileNameWithoutExtension(path);
        var urlsByIndex = new Dictionary<int, string>();
        var titlesByIndex = new Dictionary<int, string>();

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (string.IsNullOrWhiteSpace(line) || !line.Contains('='))
            {
                continue;
            }

            var separator = line.IndexOf('=');
            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();

            if (key.StartsWith("File", StringComparison.OrdinalIgnoreCase) && int.TryParse(key[4..], out var fileIndex))
            {
                if (Uri.TryCreate(value, UriKind.Absolute, out _))
                {
                    urlsByIndex[fileIndex] = value;
                }

                continue;
            }

            if (key.StartsWith("Title", StringComparison.OrdinalIgnoreCase) && int.TryParse(key[5..], out var titleIndex))
            {
                titlesByIndex[titleIndex] = value;
            }
        }

        var results = new List<(string Name, string Url)>();
        foreach (var pair in urlsByIndex.OrderBy(p => p.Key))
        {
            var name = titlesByIndex.TryGetValue(pair.Key, out var title) && !string.IsNullOrWhiteSpace(title)
                ? title
                : fallbackName;

            results.Add((name, pair.Value));
        }

        return results;
    }

    private static bool IsSupportedPlaylistPath(string path)
    {
        var ext = Path.GetExtension(path);
        return string.Equals(ext, ".m3u", StringComparison.OrdinalIgnoreCase)
               || string.Equals(ext, ".m3u8", StringComparison.OrdinalIgnoreCase)
               || string.Equals(ext, ".pls", StringComparison.OrdinalIgnoreCase)
               || string.Equals(ext, ".txt", StringComparison.OrdinalIgnoreCase);
    }

    private void OnStationsListDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not StationManagerPageViewModel viewModel)
        {
            return;
        }

        if (sender is not ListBox { SelectedItem: StationManagerPageViewModel.StationItem station })
        {
            return;
        }

        viewModel.PlayStationCommand.Execute(station);
    }

    private void OnPlayStationClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not StationManagerPageViewModel viewModel)
        {
            return;
        }

        if (sender is not Control { DataContext: StationManagerPageViewModel.StationItem station })
        {
            return;
        }

        viewModel.PlayStationCommand.Execute(station);
    }

    private void OnRemoveStationClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not StationManagerPageViewModel viewModel)
        {
            return;
        }

        if (sender is not Control { DataContext: StationManagerPageViewModel.StationItem station })
        {
            return;
        }

        viewModel.RemoveStationCommand.Execute(station);
    }

    private void OnCopyStationNameClick(object? sender, RoutedEventArgs e)
        => CopyStationFieldAsync(sender, station => station.Name).ForgetWithErrorHandling("Copy station name", showDialog: true);

    private void OnCopyStationLinkClick(object? sender, RoutedEventArgs e)
        => CopyStationFieldAsync(sender, station => station.StreamUrl).ForgetWithErrorHandling("Copy station link", showDialog: true);

    private void OnCopyStationIdClick(object? sender, RoutedEventArgs e)
        => CopyStationFieldAsync(sender, station => station.Station.Id).ForgetWithErrorHandling("Copy station id", showDialog: true);

    private void OnCopyPlayCommandClick(object? sender, RoutedEventArgs e)
        => CopyStationFieldAsync(sender, station => "station " + station.Station.Id).ForgetWithErrorHandling("Copy station play command", showDialog: true);

    private void OnOpenStationLinkClick(object? sender, RoutedEventArgs e)
        => OpenStationLinkAsync(sender).ForgetWithErrorHandling("Open station link", showDialog: true);

    private void OnExportM3UClick(object? sender, RoutedEventArgs e)
    {
        ExportM3UAsync(sender).ForgetWithErrorHandling("Export m3u playlist", showDialog: true);
    }

    private void OnStationContextMenuOpened(object? sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu contextMenu)
        {
            return;
        }

        var exportMenuItem = contextMenu.FindControl<MenuItem>("ExportM3UMenuItem");
        if (exportMenuItem is not null)
        {
            var exportCount = GetStationsForExport(exportMenuItem).Length;
            exportMenuItem.Header = exportCount > 1
                ? "Export Stations as M3U Playlist..."
                : "Export Station as M3U Playlist...";
        }

        if (DataContext is not StationManagerPageViewModel viewModel)
        {
            return;
        }

        var station = TryGetStationItem(contextMenu);
        if (station is null)
        {
            return;
        }

        var stations = viewModel.Stations;
        var index = stations
            .Select((item, idx) => (item, idx))
            .FirstOrDefault(entry => string.Equals(entry.item.Station.Id, station.Station.Id, StringComparison.Ordinal))
            .idx;

        if (index < 0)
        {
            return;
        }

        var canMoveUp = index > 0;
        var canMoveDown = index < stations.Count - 1;

        var moveToTopMenuItem = contextMenu.FindControl<MenuItem>("MoveToTopMenuItem");
        if (moveToTopMenuItem is not null)
        {
            moveToTopMenuItem.IsEnabled = canMoveUp;
        }

        var moveUpMenuItem = contextMenu.FindControl<MenuItem>("MoveUpMenuItem");
        if (moveUpMenuItem is not null)
        {
            moveUpMenuItem.IsEnabled = canMoveUp;
        }

        var moveDownMenuItem = contextMenu.FindControl<MenuItem>("MoveDownMenuItem");
        if (moveDownMenuItem is not null)
        {
            moveDownMenuItem.IsEnabled = canMoveDown;
        }

        var moveToBottomMenuItem = contextMenu.FindControl<MenuItem>("MoveToBottomMenuItem");
        if (moveToBottomMenuItem is not null)
        {
            moveToBottomMenuItem.IsEnabled = canMoveDown;
        }
    }

    private void OnMoveStationUpClick(object? sender, RoutedEventArgs e)
    {
        MoveStationAndSelect(sender, static (viewModel, station) => viewModel.MoveStationUp(station));
    }

    private void OnMoveStationDownClick(object? sender, RoutedEventArgs e)
    {
        MoveStationAndSelect(sender, static (viewModel, station) => viewModel.MoveStationDown(station));
    }

    private void OnMoveStationToTopClick(object? sender, RoutedEventArgs e)
    {
        MoveStationAndSelect(sender, static (viewModel, station) => viewModel.MoveStationToTop(station));
    }

    private void OnMoveStationToBottomClick(object? sender, RoutedEventArgs e)
    {
        MoveStationAndSelect(sender, static (viewModel, station) => viewModel.MoveStationToBottom(station));
    }

    private void MoveStationAndSelect(
        object? sender,
        Action<StationManagerPageViewModel, StationManagerPageViewModel.StationItem?> moveAction)
    {
        if (DataContext is not StationManagerPageViewModel viewModel)
        {
            return;
        }

        var station = TryGetStationItem(sender);
        if (station is not null)
        {
            viewModel.SelectedStation = station;
        }

        moveAction(viewModel, station);
    }

    private StationManagerPageViewModel.StationItem[] GetStationsForExport(object? sender)
    {
        var list = this.FindControl<ListBox>("StationsList");
        var selectedStations = list?.SelectedItems?
            .OfType<StationManagerPageViewModel.StationItem>()
            .ToArray() ?? [];

        var contextStation = TryGetStationItem(sender);
        if (selectedStations.Length > 1)
        {
            if (contextStation is null)
            {
                return selectedStations;
            }

            var isContextStationSelected = selectedStations.Any(item =>
                ReferenceEquals(item, contextStation)
                || string.Equals(item.Station.Id, contextStation.Station.Id, StringComparison.OrdinalIgnoreCase));

            if (isContextStationSelected)
            {
                return selectedStations;
            }
        }

        if (contextStation is not null)
        {
            return [contextStation];
        }

        return selectedStations;
    }

    private static string BuildSuggestedPlaylistName(StationManagerPageViewModel.StationItem? station, int count)
    {
        if (count > 1)
        {
            return "stations.m3u";
        }

        var baseName = station?.Name;
        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = "station";
        }

        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            baseName = baseName.Replace(invalid, '_');
        }

        return baseName + ".m3u";
    }

    private static string BuildM3UPlaylist(IEnumerable<StationManagerPageViewModel.StationItem> stations)
    {
        var builder = new StringBuilder();
        builder.AppendLine("#EXTM3U");

        foreach (var station in stations)
        {
            var streamUrl = station.StreamUrl.Trim();
            if (string.IsNullOrWhiteSpace(streamUrl) || !Uri.TryCreate(streamUrl, UriKind.Absolute, out _))
            {
                continue;
            }

            var displayName = station.Name;
            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = station.Station.Id;
            }

            displayName = displayName.Replace('\r', ' ').Replace('\n', ' ');
            builder.AppendLine("#EXTINF:-1," + displayName);
            builder.AppendLine(streamUrl);
        }

        return builder.ToString();
    }

    private StationManagerPageViewModel.StationItem? TryGetStationItem(object? sender)
    {
        if (sender is Control { DataContext: StationManagerPageViewModel.StationItem station })
        {
            return station;
        }

        if (sender is ContextMenu { PlacementTarget.DataContext: StationManagerPageViewModel.StationItem placementStation })
        {
            return placementStation;
        }

        if (DataContext is StationManagerPageViewModel { SelectedStation: not null } viewModel)
        {
            return viewModel.SelectedStation;
        }

        return null;
    }

    private async Task CopyToClipboardAsync(string text)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.Clipboard is null || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        await topLevel.Clipboard.SetTextAsync(text).ConfigureAwait(true);
    }

    private static void PublishCopyStatus(string message)
    {
        RibbonStatusHub.SetTemporaryOverride(
            id: "station.copy",
            text: message,
            priority: 100,
            duration: TimeSpan.FromSeconds(3));
    }

    private void DeleteSelectedStations()
    {
        if (DataContext is not StationManagerPageViewModel viewModel)
        {
            return;
        }

        var list = this.FindControl<ListBox>("StationsList");
        if (list?.SelectedItems is null || list.SelectedItems.Count == 0)
        {
            return;
        }

        var selectedStations = list.SelectedItems
            .OfType<StationManagerPageViewModel.StationItem>()
            .ToArray();
        if (selectedStations.Length == 0)
        {
            return;
        }

        viewModel.RemoveStations(selectedStations);
    }

    private void OnSetStationIconClick(object? sender, RoutedEventArgs e)
        => SetStationIconAsync(sender).ForgetWithErrorHandling("Set station icon", showDialog: true);

    private void OnClearStationIconClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not StationManagerPageViewModel viewModel)
        {
            return;
        }

        var station = TryGetStationItem(sender);
        if (station is null)
        {
            return;
        }

        viewModel.SetStationIconPath(station, null);
    }

    private async Task CopySelectedStationIdAsync()
    {
        if (DataContext is not StationManagerPageViewModel viewModel || viewModel.SelectedStation is null)
        {
            return;
        }

        var stationId = viewModel.SelectedStation.Station.Id;
        await CopyToClipboardAsync(stationId).ConfigureAwait(true);
        PublishCopyStatus("Copied station id: " + stationId);
    }

    private async Task CopyStationFieldAsync(object? sender, Func<StationManagerPageViewModel.StationItem, string> selector)
    {
        var station = TryGetStationItem(sender);
        if (station is null)
        {
            return;
        }

        var copiedText = selector(station);
        await CopyToClipboardAsync(copiedText).ConfigureAwait(true);
        PublishCopyStatus("Copied: " + copiedText);
    }

    private Task OpenStationLinkAsync(object? sender)
    {
        var station = TryGetStationItem(sender);
        if (station is null)
        {
            return Task.CompletedTask;
        }

        var url = station.StreamUrl;
        if (!Uri.TryCreate(url, UriKind.Absolute, out _))
        {
            return Task.CompletedTask;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true,
        });

        return Task.CompletedTask;
    }

    private async Task SetStationIconAsync(object? sender)
    {
        if (DataContext is not StationManagerPageViewModel viewModel)
        {
            return;
        }

        var station = TryGetStationItem(sender);
        if (station is null)
        {
            return;
        }

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is null)
        {
            return;
        }

        var result = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            Title = "Select station icon",
            FileTypeFilter =
            [
                new FilePickerFileType("Image files")
                {
                    Patterns = ["*.png", "*.jpg", "*.jpeg", "*.ico", "*.bmp"],
                },
            ],
        }).ConfigureAwait(true);

        var selectedPath = result.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            return;
        }

        viewModel.SetStationIconPath(station, selectedPath);
    }

    private async Task ExportM3UAsync(object? sender)
    {
        var stations = GetStationsForExport(sender);
        if (stations.Length == 0)
        {
            return;
        }

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is null)
        {
            return;
        }

        var selectedStation = TryGetStationItem(sender);
        var suggestedName = BuildSuggestedPlaylistName(selectedStation, stations.Length);

        var targetFile = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export M3U playlist",
            SuggestedFileName = suggestedName,
            DefaultExtension = "m3u",
            ShowOverwritePrompt = true,
            FileTypeChoices =
            [
                new FilePickerFileType("M3U playlist")
                {
                    Patterns = ["*.m3u"],
                },
                new FilePickerFileType("M3U8 playlist")
                {
                    Patterns = ["*.m3u8"],
                },
            ],
        }).ConfigureAwait(true);

        if (targetFile is null)
        {
            return;
        }

        var localPath = targetFile.TryGetLocalPath();
        var extension = localPath is null
            ? ".m3u"
            : Path.GetExtension(localPath);
        if (!string.Equals(extension, ".m3u8", StringComparison.OrdinalIgnoreCase))
        {
            extension = ".m3u";
        }

        var playlistText = BuildM3UPlaylist(stations);
        var encoding = string.Equals(extension, ".m3u8", StringComparison.OrdinalIgnoreCase)
            ? new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
            : new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

        await using var stream = await targetFile.OpenWriteAsync().ConfigureAwait(true);
        if (stream.CanSeek)
        {
            stream.Position = 0;
            stream.SetLength(0);
        }

        await using var writer = new StreamWriter(stream, encoding, leaveOpen: false);
        await writer.WriteAsync(playlistText).ConfigureAwait(true);
        await writer.FlushAsync().ConfigureAwait(true);
    }
}
