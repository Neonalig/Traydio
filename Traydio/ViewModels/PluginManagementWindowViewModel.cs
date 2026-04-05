using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Traydio.Common;
using Traydio.Models;
using Traydio.Services;
using Traydio.Views;

namespace Traydio.ViewModels;

[ViewModelFor(typeof(PluginManagementPage))]
public partial class PluginManagementWindowViewModel : ViewModelBase
{
    private readonly IPluginManager _pluginManager;
    private readonly IStationRepository _stationRepository;
    private readonly IWindowManager _windowManager;

    public ObservableCollection<InstalledPluginItem> InstalledPlugins { get; } = [];

    public ObservableCollection<PluginCandidateItem> PluginCandidates { get; } = [];

    [ObservableProperty]
    private InstalledPluginItem? _selectedInstalledPlugin;

    [ObservableProperty]
    private PluginCandidateItem? _selectedPluginCandidate;

    [ObservableProperty]
    private string _pluginDllPath = string.Empty;

    [ObservableProperty]
    private string _pluginDirectory = string.Empty;

    [ObservableProperty]
    private string _status = string.Empty;

    public PluginManagementWindowViewModel(IPluginManager pluginManager, IStationRepository stationRepository, IWindowManager windowManager)
    {
        _pluginManager = pluginManager;
        _stationRepository = stationRepository;
        _windowManager = windowManager;

        _pluginManager.PluginsChanged += (_, _) => Refresh();
        Refresh();
    }

    [RelayCommand]
    private void Refresh()
    {
        void Update()
        {
            var installedByAssemblyName = new Dictionary<string, Version>(StringComparer.OrdinalIgnoreCase);

            InstalledPlugins.Clear();
            foreach (var plugin in _pluginManager.GetPlugins().OrderBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase))
            {
                var version = plugin.GetType().Assembly.GetName().Version ?? new Version(0, 0, 0, 0);
                var assemblyName = plugin.GetType().Assembly.GetName().Name ?? plugin.Id;
                installedByAssemblyName[assemblyName] = version;

                InstalledPlugins.Add(new InstalledPluginItem(
                    plugin.Id,
                    plugin.DisplayName,
                    plugin.Capabilities.OfType<IPluginSettingsCapability>().Any(),
                    assemblyName,
                    version));
            }

            PluginDirectory = _stationRepository.StationDiscoveryPlugins.PluginDirectory;

            var discovered = DiscoverEligiblePluginDlls(installedByAssemblyName)
                .OrderBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            PluginCandidates.Clear();
            foreach (var candidate in discovered)
            {
                PluginCandidates.Add(candidate);
            }

            SelectedInstalledPlugin ??= InstalledPlugins.FirstOrDefault();
            SelectedPluginCandidate ??= PluginCandidates.FirstOrDefault();
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            Update();
            return;
        }

        Dispatcher.UIThread.Post(Update);
    }

    [RelayCommand]
    private void InstallSelectedCandidate()
    {
        if (SelectedPluginCandidate is null)
        {
            Status = "Select an eligible plugin candidate first.";
            return;
        }

        InstallPluginFromPath(SelectedPluginCandidate.Path);
    }

    [RelayCommand]
    private void InstallFromPath()
    {
        if (string.IsNullOrWhiteSpace(PluginDllPath))
        {
            Status = "Select a plugin DLL path first.";
            return;
        }

        InstallPluginFromPath(PluginDllPath);
    }

    [RelayCommand]
    private void DisableSelectedPlugin()
    {
        var selectedPlugin = SelectedInstalledPlugin;
        if (selectedPlugin is null)
        {
            Status = "Select an installed plugin first.";
            return;
        }

        if (_pluginManager.RemovePlugin(selectedPlugin.Id, out var error))
        {
            Status = $"Plugin '{selectedPlugin.DisplayName}' disabled.";
            Refresh();
            return;
        }

        Status = "Could not disable plugin: " + (error ?? "Unknown error.");
    }

    [RelayCommand]
    private void SavePluginDirectory()
    {
        var settings = _stationRepository.StationDiscoveryPlugins;
        _stationRepository.SaveStationDiscoveryPluginSettings(new StationDiscoveryPluginSettings
        {
            PluginDirectory = string.IsNullOrWhiteSpace(PluginDirectory) ? settings.PluginDirectory : PluginDirectory.Trim(),
            DisabledPluginIds = settings.DisabledPluginIds,
        });

        Status = "Plugin directory saved.";
        Refresh();
    }

    [RelayCommand]
    private void OpenPluginSettings(InstalledPluginItem? pluginItem)
    {
        if (pluginItem is null)
        {
            Status = "Select an installed plugin first.";
            return;
        }

        if (!pluginItem.HasSettings)
        {
            Status = $"Plugin '{pluginItem.DisplayName}' does not expose settings.";
            return;
        }

        if (_windowManager.ShowPluginSettings(pluginItem.Id, out var error))
        {
            Status = $"Opened settings for '{pluginItem.DisplayName}'.";
            return;
        }

        Status = "Could not open plugin settings: " + (error ?? "Unknown error.");
    }

    private void InstallPluginFromPath(string path)
    {
        if (_pluginManager.AddPlugin(path, out var error))
        {
            PluginDllPath = string.Empty;
            Status = "Plugin installed and loaded.";
            Refresh();
            return;
        }

        Status = "Could not install plugin: " + (error ?? "Unknown error.");
    }

    [RelayCommand]
    private void UpgradeCandidate(PluginCandidateItem? candidate)
    {
        if (candidate is null)
        {
            Status = "Select an eligible plugin candidate first.";
            return;
        }

        InstallPluginFromPath(candidate.Path);
    }

    public string? GetDowngradeWarningForPath(string path)
    {
        if (!TryGetPluginAssemblyMetadata(path, out var assemblyName, out var candidateVersion))
        {
            return null;
        }

        var installed = InstalledPlugins.FirstOrDefault(plugin =>
            string.Equals(plugin.AssemblyName, assemblyName, StringComparison.OrdinalIgnoreCase));

        if (installed is null || candidateVersion is null)
        {
            return null;
        }

        if (candidateVersion < installed.Version)
        {
            return $"The selected file is older than the installed plugin. Installed: {installed.VersionText}, selected: {FormatVersion(candidateVersion)}. The plugin will still be replaced.";
        }

        return null;
    }

    public void InstallPluginFromFilePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            Status = "Select a plugin DLL path first.";
            return;
        }

        InstallPluginFromPath(path);
    }

    private IEnumerable<PluginCandidateItem> DiscoverEligiblePluginDlls(IReadOnlyDictionary<string, Version> installedByAssemblyName)
    {
        var knownFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var configuredDir = _stationRepository.StationDiscoveryPlugins.PluginDirectory;
        var pluginDir = Path.IsPathRooted(configuredDir)
            ? configuredDir
            : Path.Combine(AppContext.BaseDirectory, configuredDir);

        foreach (var path in EnumerateMatches(pluginDir))
        {
            var fileName = Path.GetFileName(path);
            if (knownFileNames.Add(fileName))
            {
                yield return CreateCandidate(path, installedByAssemblyName);
            }
        }

        foreach (var path in EnumerateMatches(AppContext.BaseDirectory))
        {
            var fileName = Path.GetFileName(path);
            if (knownFileNames.Add(fileName))
            {
                yield return CreateCandidate(path, installedByAssemblyName);
            }
        }
    }

    private static PluginCandidateItem CreateCandidate(string path, IReadOnlyDictionary<string, Version> installedByAssemblyName)
    {
        if (!TryGetPluginAssemblyMetadata(path, out var assemblyName, out var version))
        {
            return new PluginCandidateItem(
                path,
                Path.GetFileNameWithoutExtension(path),
                Path.GetFileNameWithoutExtension(path),
                null,
                null,
                false);
        }

        installedByAssemblyName.TryGetValue(assemblyName, out var installedVersion);
        var isUpgrade = version is not null && installedVersion is not null && version > installedVersion;

        return new PluginCandidateItem(
            path,
            Path.GetFileNameWithoutExtension(path),
            assemblyName,
            version,
            installedVersion,
            isUpgrade);
    }

    private static bool TryGetPluginAssemblyMetadata(string path, out string assemblyName, out Version? version)
    {
        assemblyName = Path.GetFileNameWithoutExtension(path);
        version = null;

        try
        {
            var assembly = AssemblyName.GetAssemblyName(path);
            assemblyName = assembly.Name ?? assemblyName;
            version = assembly.Version;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string FormatVersion(Version? version)
    {
        if (version is null)
        {
            return "unknown";
        }

        var parts = new[] { version.Major, version.Minor, version.Build, version.Revision }
            .TakeWhile((value, index) => value >= 0 && (index < 2 || value > 0 || partsBeforeHadValue(index, version)))
            .ToArray();

        return parts.Length == 0
            ? $"{version.Major}.{version.Minor}"
            : string.Join('.', parts);

        static bool partsBeforeHadValue(int index, Version v)
        {
            return index switch
            {
                2 => v.Build > 0 || v.Revision > 0,
                3 => v.Revision > 0,
                _ => false,
            };
        }
    }

    private static IEnumerable<string> EnumerateMatches(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return [];
        }

        return Directory.GetFiles(directory, "*.dll", SearchOption.TopDirectoryOnly)
            .Where(path => Path.GetFileName(path).Contains("Plugin", StringComparison.OrdinalIgnoreCase));
    }

    public sealed class InstalledPluginItem(string id, string displayName, bool hasSettings, string assemblyName, Version version)
    {
        public string Id { get; } = id;

        public string DisplayName { get; } = displayName;

        public bool HasSettings { get; } = hasSettings;

        public string AssemblyName { get; } = assemblyName;

        public Version Version { get; } = version;

        public string VersionText { get; } = FormatVersion(version);
    }

    public sealed class PluginCandidateItem(
        string path,
        string displayName,
        string assemblyName,
        Version? version,
        Version? installedVersion,
        bool isUpgrade)
    {
        public string Path { get; } = path;

        public string DisplayName { get; } = displayName;

        public string AssemblyName { get; } = assemblyName;

        public Version? Version { get; } = version;

        public string VersionText { get; } = FormatVersion(version);

        public Version? InstalledVersion { get; } = installedVersion;

        public string InstalledVersionText { get; } = FormatVersion(installedVersion);

        public bool IsUpgrade { get; } = isUpgrade;
    }
}

