using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Traydio.Common;
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

    private async void OnDrop(object? sender, DragEventArgs e)
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
                OpenAddFlyout();
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
                OpenAddFlyout();
                return;
            }

            viewModel.AddStationsFromDrop(links.Select((url, idx) => ($"Dropped stream {idx + 1}", url)));
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

    private void OpenAddFlyout()
    {
        var addButton = this.FindControl<Button>("AddStationButton");
        addButton?.Flyout?.ShowAt(addButton);
    }
}
