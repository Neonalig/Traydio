using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
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

    public PluginManagementWindowViewModel(IPluginManager pluginManager, IStationRepository stationRepository)
    {
        _pluginManager = pluginManager;
        _stationRepository = stationRepository;

        _pluginManager.PluginsChanged += (_, _) => Refresh();
        Refresh();
    }

    [RelayCommand]
    private void Refresh()
    {
        void Update()
        {
            InstalledPlugins.Clear();
            foreach (var plugin in _pluginManager.GetPlugins().OrderBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase))
            {
                InstalledPlugins.Add(new InstalledPluginItem(plugin.Id, plugin.DisplayName));
            }

            PluginDirectory = _stationRepository.StationDiscoveryPlugins.PluginDirectory;

            var discovered = DiscoverEligiblePluginDlls()
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
        if (SelectedInstalledPlugin is null)
        {
            Status = "Select an installed plugin first.";
            return;
        }

        if (_pluginManager.RemovePlugin(SelectedInstalledPlugin.Id, out var error))
        {
            Status = $"Plugin '{SelectedInstalledPlugin.DisplayName}' disabled.";
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

    private IEnumerable<PluginCandidateItem> DiscoverEligiblePluginDlls()
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
                yield return new PluginCandidateItem(path, Path.GetFileNameWithoutExtension(path));
            }
        }

        foreach (var path in EnumerateMatches(AppContext.BaseDirectory))
        {
            var fileName = Path.GetFileName(path);
            if (knownFileNames.Add(fileName))
            {
                yield return new PluginCandidateItem(path, Path.GetFileNameWithoutExtension(path));
            }
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

    public sealed class InstalledPluginItem(string id, string displayName)
    {
        public string Id { get; } = id;

        public string DisplayName { get; } = displayName;
    }

    public sealed class PluginCandidateItem(string path, string displayName)
    {
        public string Path { get; } = path;

        public string DisplayName { get; } = displayName;
    }
}

