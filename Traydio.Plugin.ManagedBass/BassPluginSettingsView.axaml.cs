using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using ManagedBass;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Traydio.Common;

namespace Traydio.Plugin.ManagedBass;

public partial class BassPluginSettingsView : UserControl
{
    private static readonly HttpClient _http = new();

    private readonly IPluginSettingsAccessor _settingsAccessor;
    private readonly ILogger<BassPluginSettingsView> _logger;
    private readonly TextBox _bassPathBox;
    private readonly TextBox _bassOpusPathBox;
    private readonly TextBox _tagsPathBox;
    private readonly Image _bassStatusIcon;
    private readonly Image _bassOpusStatusIcon;
    private readonly Image _tagsStatusIcon;
    private readonly ComboBox _outputDeviceComboBox;
    private readonly TextBlock _statusText;
    private readonly IBrush? _statusNormalForeground;

    private static readonly IImage _statusLoadedIcon = LoadIcon("avares://Traydio/Assets/Icons9x/play.ico");
    private static readonly IImage _statusWarningIcon = LoadIcon("avares://Traydio/Assets/Icons9x/warning.ico");
    private static readonly IImage _statusInvalidIcon = LoadIcon("avares://Traydio/Assets/Icons9x/stop.ico");

    public BassPluginSettingsView()
        : this(new NullPluginSettingsAccessor(), NullLogger<BassPluginSettingsView>.Instance)
    {
    }

    public BassPluginSettingsView(IPluginSettingsAccessor settingsAccessor)
        : this(settingsAccessor, NullLogger<BassPluginSettingsView>.Instance)
    {
    }

    public BassPluginSettingsView(IPluginSettingsAccessor settingsAccessor, ILogger<BassPluginSettingsView> logger)
    {
        _settingsAccessor = settingsAccessor ?? throw new ArgumentNullException(nameof(settingsAccessor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _bassPathBox = new TextBox();
        _bassOpusPathBox = new TextBox();
        _tagsPathBox = new TextBox();
        _bassStatusIcon = new Image();
        _bassOpusStatusIcon = new Image();
        _tagsStatusIcon = new Image();
        _outputDeviceComboBox = new ComboBox();
        _statusText = new TextBlock();
        _statusNormalForeground = null;

        try
        {
            AvaloniaXamlLoader.Load(this);

            _bassPathBox = this.FindControl<TextBox>("BassPathBox")
                ?? throw new InvalidOperationException("ManagedBass settings view is missing BassPathBox.");
            _bassOpusPathBox = this.FindControl<TextBox>("BassOpusPathBox")
                ?? throw new InvalidOperationException("ManagedBass settings view is missing BassOpusPathBox.");
            _tagsPathBox = this.FindControl<TextBox>("TagsPathBox")
                ?? throw new InvalidOperationException("ManagedBass settings view is missing TagsPathBox.");
            _bassStatusIcon = this.FindControl<Image>("BassStatusIcon")
                ?? throw new InvalidOperationException("ManagedBass settings view is missing BassStatusIcon.");
            _bassOpusStatusIcon = this.FindControl<Image>("BassOpusStatusIcon")
                ?? throw new InvalidOperationException("ManagedBass settings view is missing BassOpusStatusIcon.");
            _tagsStatusIcon = this.FindControl<Image>("TagsStatusIcon")
                ?? throw new InvalidOperationException("ManagedBass settings view is missing TagsStatusIcon.");
            _outputDeviceComboBox = this.FindControl<ComboBox>("OutputDeviceComboBox")
                ?? throw new InvalidOperationException("ManagedBass settings view is missing OutputDeviceComboBox.");
            _statusText = this.FindControl<TextBlock>("StatusText")
                ?? throw new InvalidOperationException("ManagedBass settings view is missing StatusText.");
            _statusNormalForeground = _statusText.Foreground;

            LoadDependencyPaths();

            ConfigureNativeLibraryPath();
            LoadOutputDeviceOptions();
            RefreshDependencyStatuses();
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
            LogError("Initialization failed.", ex);
        }
    }

    private void OnBrowseBassClick(object? sender, RoutedEventArgs e)
    {
        RunSafelyAsync(OnBrowseBassClickAsync(), "Browse bass.dll");
    }

    private async System.Threading.Tasks.Task OnBrowseBassClickAsync()
    {
        await PickDependencyPathAsync(_bassPathBox, BassPluginSettings.BassDllPathKey, "bass.dll");
        ConfigureNativeLibraryPath();
        RefreshDependencyStatuses();
    }

    private void OnBrowseBassOpusClick(object? sender, RoutedEventArgs e)
    {
        RunSafelyAsync(OnBrowseBassOpusClickAsync(), "Browse bassopus.dll");
    }

    private async System.Threading.Tasks.Task OnBrowseBassOpusClickAsync()
    {
        await PickDependencyPathAsync(_bassOpusPathBox, BassPluginSettings.BassOpusDllPathKey, "bassopus.dll");
        RefreshDependencyStatuses();
    }

    private void OnBrowseTagsClick(object? sender, RoutedEventArgs e)
    {
        RunSafelyAsync(OnBrowseTagsClickAsync(), "Browse tags.dll");
    }

    private async System.Threading.Tasks.Task OnBrowseTagsClickAsync()
    {
        await PickDependencyPathAsync(_tagsPathBox, BassPluginSettings.TagsDllPathKey, "tags.dll");
        RefreshDependencyStatuses();
    }

    private void OnSaveOutputDeviceClick(object? sender, RoutedEventArgs e)
    {
        SaveOutputDeviceIndex();
        SetStatus("Saved output device setting.");
    }

    private void OnDownloadBassClick(object? sender, RoutedEventArgs e)
    {
        RunSafelyAsync(OnDownloadBassClickAsync(), "Download bass.dll");
    }

    private async System.Threading.Tasks.Task OnDownloadBassClickAsync()
    {
        await DownloadDependencyAsync(
            BassPluginSettings.BassDownloadUrl,
            "bass.dll",
            _bassPathBox,
            BassPluginSettings.BassDllPathKey,
            "Downloading bass24.zip...");
        ConfigureNativeLibraryPath();
        RefreshDependencyStatuses();
    }

    private void OnDownloadBassOpusClick(object? sender, RoutedEventArgs e)
    {
        RunSafelyAsync(OnDownloadBassOpusClickAsync(), "Download bassopus.dll");
    }

    private async System.Threading.Tasks.Task OnDownloadBassOpusClickAsync()
    {
        await DownloadDependencyAsync(
            BassPluginSettings.BassOpusDownloadUrl,
            "bassopus.dll",
            _bassOpusPathBox,
            BassPluginSettings.BassOpusDllPathKey,
            "Downloading bassopus24.zip...");
        RefreshDependencyStatuses();
    }

    private void OnDownloadTagsClick(object? sender, RoutedEventArgs e)
    {
        RunSafelyAsync(OnDownloadTagsClickAsync(), "Download tags.dll");
    }

    private async System.Threading.Tasks.Task OnDownloadTagsClickAsync()
    {
        await DownloadDependencyAsync(
            BassPluginSettings.BassTagsDownloadUrl,
            "tags.dll",
            _tagsPathBox,
            BassPluginSettings.TagsDllPathKey,
            "Downloading basstags.zip...");
        RefreshDependencyStatuses();
    }

    private void RunSafelyAsync(System.Threading.Tasks.Task task, string context)
    {
        _ = RunSafelyCoreAsync(task, context);
    }

    private async System.Threading.Tasks.Task RunSafelyCoreAsync(System.Threading.Tasks.Task task, string context)
    {
        try
        {
            await task.ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            SetErrorStatus(context + " failed: " + ex.Message);
            LogError(context + " failed.", ex);
        }
    }

    private async System.Threading.Tasks.Task PickDependencyPathAsync(TextBox targetBox, string settingsKey, string expectedDllName)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is null)
        {
            return;
        }

        var result = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            Title = $"Select {expectedDllName}",
            FileTypeFilter =
            [
                new FilePickerFileType("DLL")
                {
                    Patterns = ["*.dll"],
                },
            ],
        });

        var selected = result.FirstOrDefault();
        var selectedPath = selected?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            return;
        }

        targetBox.Text = selectedPath;
        SaveDependencyPath(settingsKey, selectedPath);
        if (settingsKey == BassPluginSettings.BassDllPathKey)
        {
            SaveLegacyNativeFolder(selectedPath);
        }
        SetStatus($"Saved {expectedDllName} path.");
    }

    private async System.Threading.Tasks.Task DownloadDependencyAsync(
        string archiveUrl,
        string dllName,
        TextBox targetPathBox,
        string settingsKey,
        string downloadingMessage)
    {
        var outputPath = ResolveOutputPath(targetPathBox.Text, dllName);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            SaveOutputDeviceIndex();

            SetStatus(downloadingMessage);
            var zipBytes = await DownloadArchiveBytesAsync(archiveUrl).ConfigureAwait(true);

            using var archiveStream = new MemoryStream(zipBytes);
            using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read);

            var entry = FindDependencyEntry(archive, dllName);

            if (entry is null)
            {
                SetErrorStatus($"Downloaded archive did not contain {dllName}.");
                return;
            }

            await using var entryStream = entry.Open();
            await using var outputStream = File.Create(outputPath);
            await entryStream.CopyToAsync(outputStream).ConfigureAwait(true);

            targetPathBox.Text = outputPath;
            SaveDependencyPath(settingsKey, outputPath);
            SaveLegacyNativeFolder(outputPath);

            SetStatus($"Downloaded {dllName} to {outputPath}");
        }
        catch (Exception ex)
        {
            SetErrorStatus("Download failed: " + ex.Message);
            LogError($"Download failed. url={archiveUrl} dll={dllName}", ex);
        }
    }

    private static string ResolveOutputPath(string? configuredPath, string dllName)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var trimmed = configuredPath.Trim();
            if (string.Equals(Path.GetExtension(trimmed), ".dll", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed;
            }

            if (Directory.Exists(trimmed))
            {
                return Path.Combine(trimmed, dllName);
            }
        }

        return Path.Combine(AppContext.BaseDirectory, "BASS", dllName);
    }

    private static ZipArchiveEntry? FindDependencyEntry(ZipArchive archive, string dllName)
    {
        var architectureFolder = Environment.Is64BitProcess ? "x64" : "x86";
        var architectureSuffix = $"{architectureFolder}/{dllName}";

        var preferred = archive.Entries.FirstOrDefault(item =>
            NormalizeArchivePath(item.FullName).EndsWith(architectureSuffix, StringComparison.OrdinalIgnoreCase));
        if (preferred is not null)
        {
            return preferred;
        }

        return archive.Entries.FirstOrDefault(item =>
            NormalizeArchivePath(item.FullName).EndsWith('/' + dllName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(Path.GetFileName(item.FullName), dllName, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeArchivePath(string path)
    {
        return path.Replace('\\', '/').TrimStart('/');
    }

    private static async System.Threading.Tasks.Task<byte[]> DownloadArchiveBytesAsync(string archiveUrl)
    {
        return await _http.GetByteArrayAsync(archiveUrl).ConfigureAwait(true);
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
            LogError("Failed to load output device setting.", ex);
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
            LogError("Failed to enumerate output devices.", ex);
        }

        _outputDeviceComboBox.ItemsSource = options;
        _outputDeviceComboBox.SelectedItem = options.FirstOrDefault(option => option.DeviceIndex == configuredIndex)
            ?? options.First();
    }

    private sealed record OutputDeviceOption(int? DeviceIndex, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }

    private void LoadDependencyPaths()
    {
        var fallbackFolder = Path.Combine(AppContext.BaseDirectory, "BASS");

        try
        {
            var savedFolder = _settingsAccessor.GetValue(BassPluginSettings.NativeLibraryFolderKey);
            if (!string.IsNullOrWhiteSpace(savedFolder))
            {
                fallbackFolder = savedFolder;
            }
        }
        catch (Exception ex)
        {
            LogError("Failed to load native folder fallback.", ex);
        }

        _bassPathBox.Text = LoadDependencyPath(BassPluginSettings.BassDllPathKey, fallbackFolder, "bass.dll");
        _bassOpusPathBox.Text = LoadDependencyPath(BassPluginSettings.BassOpusDllPathKey, fallbackFolder, "bassopus.dll");
        _tagsPathBox.Text = LoadDependencyPath(BassPluginSettings.TagsDllPathKey, fallbackFolder, "tags.dll");
    }

    private string LoadDependencyPath(string settingsKey, string fallbackFolder, string dllName)
    {
        try
        {
            var saved = _settingsAccessor.GetValue(settingsKey);
            if (!string.IsNullOrWhiteSpace(saved))
            {
                return saved;
            }
        }
        catch (Exception ex)
        {
            LogError("Failed to load path key=" + settingsKey, ex);
        }

        return Path.Combine(fallbackFolder, dllName);
    }

    private void SaveDependencyPath(string settingsKey, string? path)
    {
        _settingsAccessor.SetValue(settingsKey, path);
        _settingsAccessor.Save();
    }

    private void SaveLegacyNativeFolder(string dependencyPath)
    {
        var folder = Path.GetDirectoryName(dependencyPath);
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        _settingsAccessor.SetValue(BassPluginSettings.NativeLibraryFolderKey, folder);
        _settingsAccessor.Save();
    }

    private void RefreshDependencyStatuses()
    {
        UpdateDependencyStatus(_bassStatusIcon, "bass.dll", _bassPathBox.Text, false);
        UpdateDependencyStatus(_bassOpusStatusIcon, "bassopus.dll", _bassOpusPathBox.Text, true);
        UpdateDependencyStatus(_tagsStatusIcon, "tags.dll", _tagsPathBox.Text, true);
    }

    private static void UpdateDependencyStatus(Image icon, string displayName, string? path, bool mayLoadOnDemand)
    {
        if (!IsValidDllPath(path))
        {
            icon.Source = _statusInvalidIcon;
            ToolTip.SetTip(icon, $"{displayName}: invalid or missing path");
            return;
        }

        if (IsLibraryLoaded(path!))
        {
            icon.Source = _statusLoadedIcon;
            ToolTip.SetTip(icon, $"{displayName}: valid and loaded");
            return;
        }

        icon.Source = _statusWarningIcon;
        var warningText = mayLoadOnDemand
            ? $"{displayName}: valid path. This dependency may load only when first used, so this can remain pending until playback/metadata touches it."
            : $"{displayName}: valid path, restart app to load";
        ToolTip.SetTip(icon, warningText);
    }

    private static IImage LoadIcon(string uri)
    {
        using var stream = AssetLoader.Open(new Uri(uri));
        return new Bitmap(stream);
    }

    private static bool IsValidDllPath(string? path)
    {
        return !string.IsNullOrWhiteSpace(path)
               && string.Equals(Path.GetExtension(path), ".dll", StringComparison.OrdinalIgnoreCase)
               && File.Exists(path);
    }

    private static bool IsLibraryLoaded(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            var fullPath = Path.GetFullPath(path);
            using var process = Process.GetCurrentProcess();
            foreach (ProcessModule module in process.Modules)
            {
                if (!string.IsNullOrWhiteSpace(module.FileName)
                    && string.Equals(Path.GetFullPath(module.FileName), fullPath, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private void SetStatus(string message)
    {
        _statusText.Foreground = _statusNormalForeground;
        _statusText.Text = message;
    }

    private void SetErrorStatus(string message)
    {
        _statusText.Foreground = TryResolveBrushResource("StatusErrorBrush") ?? Brushes.IndianRed;
        _statusText.Text = message;
    }

    private IBrush? TryResolveBrushResource(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        if (this.TryFindResource(key, ActualThemeVariant, out var brush)
            && brush is IBrush typedBrush)
        {
            return typedBrush;
        }

        if (Avalonia.Application.Current?.TryFindResource(key, ActualThemeVariant, out var appBrush) == true
            && appBrush is IBrush appTypedBrush)
        {
            return appTypedBrush;
        }

        return null;
    }

    private void LogError(string message, Exception? ex = null)
    {
        _logger.LogError(ex, "{Message}", message);
    }

    private void ConfigureNativeLibraryPath()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var bassPath = _bassPathBox.Text?.Trim();
        if (!IsValidDllPath(bassPath))
        {
            return;
        }

        var folderPath = Path.GetDirectoryName(bassPath!);
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
        {
            return;
        }

        if (!SetDllDirectory(folderPath))
        {
            var errorCode = Marshal.GetLastWin32Error();
            LogError($"SetDllDirectory failed for path={folderPath} win32={errorCode}");
        }
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SetDllDirectory(string lpPathName);

    private sealed class NullPluginSettingsAccessor : IPluginSettingsAccessor
    {
        public string? GetValue(string key) => null;

        public void SetValue(string key, string? value)
        {
        }

        public void Save()
        {
        }
    }
}


