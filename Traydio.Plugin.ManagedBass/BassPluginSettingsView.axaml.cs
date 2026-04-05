using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using ManagedBass;
using Traydio.Common;

namespace Traydio.Plugin.ManagedBass;

public partial class BassPluginSettingsView : UserControl
{
    private static readonly HttpClient _http = new();

    private readonly IPluginSettingsAccessor _settingsAccessor;
    private readonly TextBox _folderPathBox;
    private readonly ComboBox _outputDeviceComboBox;
    private readonly TextBlock _statusText;
    private readonly IBrush? _statusNormalForeground;

    public BassPluginSettingsView(IPluginSettingsAccessor settingsAccessor)
    {
        _settingsAccessor = settingsAccessor ?? throw new ArgumentNullException(nameof(settingsAccessor));
        _folderPathBox = new TextBox();
        _outputDeviceComboBox = new ComboBox();
        _statusText = new TextBlock();
        _statusNormalForeground = null;

        try
        {
            AvaloniaXamlLoader.Load(this);

            _folderPathBox = this.FindControl<TextBox>("FolderPathBox")
                ?? throw new InvalidOperationException("ManagedBass settings view is missing FolderPathBox.");
            _outputDeviceComboBox = this.FindControl<ComboBox>("OutputDeviceComboBox")
                ?? throw new InvalidOperationException("ManagedBass settings view is missing OutputDeviceComboBox.");
            _statusText = this.FindControl<TextBlock>("StatusText")
                ?? throw new InvalidOperationException("ManagedBass settings view is missing StatusText.");
            _statusNormalForeground = _statusText.Foreground;

            _folderPathBox.Text = Path.Combine(AppContext.BaseDirectory, "BASS");

            try
            {
                var configuredPath = _settingsAccessor.GetValue(BassPluginSettings.NativeLibraryFolderKey);
                if (!string.IsNullOrWhiteSpace(configuredPath))
                {
                    _folderPathBox.Text = configuredPath;
                }
            }
            catch (Exception ex)
            {
                SetErrorStatus("Failed to load saved path: " + ex.Message);
                LogError($"[Traydio][ManagedBassSettings] Failed to load saved path: {ex}");
            }

            ConfigureNativeLibraryPath();
            LoadOutputDeviceOptions();
        }
        catch (Exception ex)
        {
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(12),
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = "ManagedBass settings could not be loaded.",
                        FontWeight = FontWeight.SemiBold,
                    },
                    new TextBlock
                    {
                        Text = ex.GetType().Name + ": " + ex.Message,
                        TextWrapping = TextWrapping.Wrap,
                    },
                },
            };
            SetErrorStatus("Initialization failed.");
            LogError($"[Traydio][ManagedBassSettings] Initialization failed: {ex}");
        }
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

        _folderPathBox.Text = selected.TryGetLocalPath() ?? selected.Name;
        SaveFolderPath();
        ConfigureNativeLibraryPath();
        SetStatus("Saved native folder path.");
    }

    private void OnSavePathClick(object? sender, RoutedEventArgs e)
    {
        SaveFolderPath();
        ConfigureNativeLibraryPath();
        SetStatus("Saved native folder path.");
    }

    private void OnSaveOutputDeviceClick(object? sender, RoutedEventArgs e)
    {
        SaveOutputDeviceIndex();
        SetStatus("Saved output device setting.");
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
        var folderPath = _folderPathBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            SetErrorStatus("Choose a folder first.");
            return;
        }

        try
        {
            Directory.CreateDirectory(folderPath);
            SaveFolderPath();
            SaveOutputDeviceIndex();

            SetStatus(downloadingMessage);
            var zipBytes = await DownloadArchiveBytesWithFallbackAsync(archiveUrl).ConfigureAwait(true);

            using var archiveStream = new MemoryStream(zipBytes);
            using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read);

            var architectureFolder = Environment.Is64BitProcess ? "x64" : "x86";
            var expectedEntryPath = $"{architectureFolder}/{dllName}";
            var entry = archive.Entries.FirstOrDefault(item =>
                NormalizeArchivePath(item.FullName).EndsWith(expectedEntryPath, StringComparison.OrdinalIgnoreCase));

            if (entry is null)
            {
                var relevantEntries = string.Join(", ", archive.Entries
                    .Select(item => NormalizeArchivePath(item.FullName))
                    .Where(path => path.Contains(architectureFolder, StringComparison.OrdinalIgnoreCase)
                                   || path.EndsWith(dllName, StringComparison.OrdinalIgnoreCase))
                    .Take(20));

                LogError($"[Traydio][ManagedBassSettings] Missing native entry. url={archiveUrl} expected={expectedEntryPath} entries={archive.Entries.Count} relevant=[{relevantEntries}]");
                SetErrorStatus($"Downloaded archive did not contain {architectureFolder}/{dllName}.");
                return;
            }

            var outputPath = Path.Combine(folderPath, dllName);
            await using var entryStream = entry.Open();
            await using var outputStream = File.Create(outputPath);
            await entryStream.CopyToAsync(outputStream).ConfigureAwait(true);

            SetStatus($"Downloaded {dllName} to {outputPath}");
        }
        catch (Exception ex)
        {
            SetErrorStatus("Download failed: " + ex.Message);
            LogError($"[Traydio][ManagedBassSettings] Download failed. url={archiveUrl} dll={dllName}: {ex}");
        }
    }

    private static string NormalizeArchivePath(string path)
    {
        return path.Replace('\\', '/').TrimStart('/');
    }

    private static async System.Threading.Tasks.Task<byte[]> DownloadArchiveBytesWithFallbackAsync(string archiveUrl)
    {
        var candidates = new System.Collections.Generic.List<string> { archiveUrl };
        if (archiveUrl.Contains("/files/z/0/", StringComparison.OrdinalIgnoreCase))
        {
            candidates.Add(archiveUrl.Replace("/files/z/0/", "/files/", StringComparison.OrdinalIgnoreCase));
        }
        else if (archiveUrl.Contains("/files/", StringComparison.OrdinalIgnoreCase))
        {
            candidates.Add(archiveUrl.Replace("/files/", "/files/z/0/", StringComparison.OrdinalIgnoreCase));
        }

        Exception? lastError = null;
        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                return await _http.GetByteArrayAsync(candidate).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                lastError = ex;
                LogError($"[Traydio][ManagedBassSettings] Archive download failed url={candidate}: {ex.Message}");
            }
        }

        throw lastError ?? new InvalidOperationException("Failed to download archive.");
    }

    private void SaveFolderPath()
    {
        _settingsAccessor.SetValue(BassPluginSettings.NativeLibraryFolderKey, _folderPathBox.Text);
        _settingsAccessor.Save();
    }

    private void SaveOutputDeviceIndex()
    {
        if (_outputDeviceComboBox.SelectedItem is OutputDeviceOption { DeviceIndex: { } selectedIndex })
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
        int? configuredIndex = 1;

        try
        {
            var configuredValue = _settingsAccessor.GetValue(BassPluginSettings.OutputDeviceIndexKey);
            configuredIndex = int.TryParse(configuredValue, out var parsedIndex)
                ? parsedIndex
                : null;
        }
        catch (Exception ex)
        {
            SetErrorStatus("Failed to load saved output device: " + ex.Message);
            LogError($"[Traydio][ManagedBassSettings] Failed to load output device setting: {ex}");
        }

        var options = new System.Collections.Generic.List<OutputDeviceOption>
        {
            new(1, "Default (1)"),
        };

        try
        {
            ConfigureNativeLibraryPath();
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
        }
        catch (Exception ex)
        {
            SetErrorStatus("Failed to list output devices: " + ex.Message);
            LogError($"[Traydio][ManagedBassSettings] Failed to enumerate output devices: {ex}");
        }

        _outputDeviceComboBox.ItemsSource = options;
        _outputDeviceComboBox.SelectedItem = options.FirstOrDefault(option => option.DeviceIndex == configuredIndex)
            ?? options.First();
    }

    private sealed record OutputDeviceOption(int? DeviceIndex, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }

    private void SetStatus(string message)
    {
        _statusText.Foreground = _statusNormalForeground;
        _statusText.Text = message;
    }

    private void SetErrorStatus(string message)
    {
        _statusText.Foreground = Brushes.IndianRed;
        _statusText.Text = message;
    }

    private static void LogError(string message)
    {
        Console.Error.WriteLine(message);
        System.Diagnostics.Trace.WriteLine(message);
    }

    private void ConfigureNativeLibraryPath()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var folderPath = _folderPathBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
        {
            return;
        }

        var bassPath = Path.Combine(folderPath, "bass.dll");
        if (!File.Exists(bassPath))
        {
            LogError($"[Traydio][ManagedBassSettings] bass.dll not found at configured path: {bassPath}");
            return;
        }

        if (!SetDllDirectory(folderPath))
        {
            var errorCode = Marshal.GetLastWin32Error();
            LogError($"[Traydio][ManagedBassSettings] SetDllDirectory failed for path={folderPath} win32={errorCode}");
        }
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SetDllDirectory(string lpPathName);
}


