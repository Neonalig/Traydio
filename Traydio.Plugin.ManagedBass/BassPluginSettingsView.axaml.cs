using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using ManagedBass;
using Traydio.Common;

namespace Traydio.Plugin.ManagedBass;

public partial class BassPluginSettingsView : UserControl
{
    private static readonly HttpClient _http = new();

    private readonly IPluginSettingsAccessor _settingsAccessor;

    public BassPluginSettingsView(IPluginSettingsAccessor settingsAccessor)
    {
        _settingsAccessor = settingsAccessor;
        AvaloniaXamlLoader.Load(this);

        var configuredPath = _settingsAccessor.GetValue(BassPluginSettings.NativeLibraryFolderKey);
        FolderPathBox.Text = string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(AppContext.BaseDirectory, "BASS")
            : configuredPath;

        LoadOutputDeviceOptions();
    }

    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is null)
        {
            return;
        }

        var result = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            AllowMultiple = false,
            Title = "Select BASS native folder",
        });

        var selected = result.FirstOrDefault();
        if (selected is null)
        {
            return;
        }

        FolderPathBox.Text = selected.TryGetLocalPath() ?? selected.Name;
        SaveFolderPath();
        StatusText.Text = "Saved native folder path.";
    }

    private void OnSavePathClick(object? sender, RoutedEventArgs e)
    {
        SaveFolderPath();
        StatusText.Text = "Saved native folder path.";
    }

    private void OnSaveOutputDeviceClick(object? sender, RoutedEventArgs e)
    {
        SaveOutputDeviceIndex();
        StatusText.Text = "Saved output device setting.";
    }

    private async void OnDownloadClick(object? sender, RoutedEventArgs e)
    {
        await DownloadNativeFileAsync(
            BassPluginSettings.BassDownloadUrl,
            "bass.dll",
            "Downloading bass24.zip...");
    }

    private async void OnDownloadBassOpusClick(object? sender, RoutedEventArgs e)
    {
        await DownloadNativeFileAsync(
            BassPluginSettings.BassOpusDownloadUrl,
            "bassopus.dll",
            "Downloading bassopus24.zip...");
    }

    private async System.Threading.Tasks.Task DownloadNativeFileAsync(string archiveUrl, string dllName, string downloadingMessage)
    {
        var folderPath = FolderPathBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            StatusText.Text = "Choose a folder first.";
            return;
        }

        try
        {
            Directory.CreateDirectory(folderPath);
            SaveFolderPath();
            SaveOutputDeviceIndex();

            StatusText.Text = downloadingMessage;
            var zipBytes = await _http.GetByteArrayAsync(archiveUrl).ConfigureAwait(true);

            using var archiveStream = new MemoryStream(zipBytes);
            using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read);

            var architectureFolder = Environment.Is64BitProcess ? "x64" : "x86";
            var entry = archive.Entries.FirstOrDefault(item =>
                item.FullName.Replace('\\', '/').EndsWith($"/{architectureFolder}/{dllName}", StringComparison.OrdinalIgnoreCase));

            if (entry is null)
            {
                StatusText.Text = $"Downloaded archive did not contain {architectureFolder}/{dllName}.";
                return;
            }

            var outputPath = Path.Combine(folderPath, dllName);
            await using var entryStream = entry.Open();
            await using var outputStream = File.Create(outputPath);
            await entryStream.CopyToAsync(outputStream).ConfigureAwait(true);

            StatusText.Text = $"Downloaded {dllName} to {outputPath}";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Download failed: " + ex.Message;
        }
    }

    private void SaveFolderPath()
    {
        _settingsAccessor.SetValue(BassPluginSettings.NativeLibraryFolderKey, FolderPathBox.Text);
        _settingsAccessor.Save();
    }

    private void SaveOutputDeviceIndex()
    {
        if (OutputDeviceComboBox.SelectedItem is OutputDeviceOption { DeviceIndex: int selectedIndex })
        {
            _settingsAccessor.SetValue(BassPluginSettings.OutputDeviceIndexKey, selectedIndex.ToString());
        }
        else
        {
            _settingsAccessor.SetValue(BassPluginSettings.OutputDeviceIndexKey, null);
        }

        _settingsAccessor.Save();
    }

    private void LoadOutputDeviceOptions()
    {
        var configuredValue = _settingsAccessor.GetValue(BassPluginSettings.OutputDeviceIndexKey);
        var configuredIndex = int.TryParse(configuredValue, out var parsedIndex)
            ? parsedIndex
            : (int?)null;

        var options = new System.Collections.Generic.List<OutputDeviceOption>
        {
            new(null, "System default"),
        };

        for (var index = 0; index < Bass.DeviceCount; index++)
        {
            if (!Bass.GetDeviceInfo(index, out var info) || !info.IsEnabled)
            {
                continue;
            }

            var name = string.IsNullOrWhiteSpace(info.Name)
                ? $"Device {index}"
                : info.Name;
            options.Add(new OutputDeviceOption(index, $"{name} ({index})"));
        }

        OutputDeviceComboBox.ItemsSource = options;
        OutputDeviceComboBox.SelectedItem = options.FirstOrDefault(option => option.DeviceIndex == configuredIndex)
            ?? options.First();
    }

    private sealed record OutputDeviceOption(int? DeviceIndex, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }
}


