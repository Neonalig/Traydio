using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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
    private const string _FEATURED_ASSEMBLY_HASH_METADATA_KEY = "Traydio.FeaturedPluginAssemblyNameHash";
    private const string _OFFICIAL_ASSEMBLY_HASH_METADATA_KEY = "Traydio.OfficialPluginAssemblyNameHash";

    private static readonly HashSet<string> _featuredAssemblyNameHashes = LoadPluginHashes(_FEATURED_ASSEMBLY_HASH_METADATA_KEY);
    private static readonly HashSet<string> _officialAssemblyNameHashes = LoadPluginHashes(_OFFICIAL_ASSEMBLY_HASH_METADATA_KEY);

    private readonly IPluginManager _pluginManager;
    private readonly IPluginInstallDisclaimerService _pluginInstallDisclaimerService;
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

    [ObservableProperty]
    private bool _hasPendingDeletes;

    public PluginManagementWindowViewModel(
        IPluginManager pluginManager,
        IPluginInstallDisclaimerService pluginInstallDisclaimerService,
        IStationRepository stationRepository,
        IWindowManager windowManager)
    {
        _pluginManager = pluginManager;
        _pluginInstallDisclaimerService = pluginInstallDisclaimerService;
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
            var previousInstalledId = SelectedInstalledPlugin?.Id;
            var previousCandidatePath = SelectedPluginCandidate?.Path;

            InstalledPlugins.Clear();
            foreach (var plugin in _pluginManager.GetPluginInventory().OrderBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase))
            {
                installedByAssemblyName[plugin.AssemblyName] = plugin.Version;

                InstalledPlugins.Add(new InstalledPluginItem(
                    plugin.Id,
                    plugin.DisplayName,
                    plugin.HasSettings,
                    plugin.AssemblyName,
                    plugin.Version,
                    plugin.IsEnabled,
                    plugin.CanUninstall,
                    plugin.IsPendingDelete));
            }

            HasPendingDeletes = InstalledPlugins.Any(item => item.IsPendingDelete);

            PluginDirectory = _stationRepository.StationDiscoveryPlugins.PluginDirectory;

            var discovered = DiscoverEligiblePluginDlls(installedByAssemblyName)
                .Where(candidate => !candidate.IsSameVersionInstalled)
                .OrderByDescending(candidate => candidate.IsFeatured)
                .ThenByDescending(candidate => candidate.IsOfficial)
                .ThenBy(candidate => candidate.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            PluginCandidates.Clear();
            foreach (var candidate in discovered)
            {
                PluginCandidates.Add(candidate);
            }

            SelectedInstalledPlugin = InstalledPlugins.FirstOrDefault(item =>
                string.Equals(item.Id, previousInstalledId, StringComparison.OrdinalIgnoreCase))
                ?? InstalledPlugins.FirstOrDefault();

            SelectedPluginCandidate = PluginCandidates.FirstOrDefault(item =>
                string.Equals(item.Path, previousCandidatePath, StringComparison.OrdinalIgnoreCase))
                ?? PluginCandidates.FirstOrDefault();
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            Update();
            return;
        }

        Dispatcher.UIThread.Post(Update);
    }

    [RelayCommand]
    private async Task InstallSelectedCandidate()
    {
        if (SelectedPluginCandidate is null)
        {
            Status = "Select an eligible plugin candidate first.";
            return;
        }

        await InstallPluginFromPathAsync(SelectedPluginCandidate.Path, CancellationToken.None).ConfigureAwait(false);
    }

    [RelayCommand]
    private async Task InstallFromPath()
    {
        if (string.IsNullOrWhiteSpace(PluginDllPath))
        {
            Status = "Select a plugin DLL path first.";
            return;
        }

        await InstallPluginFromPathAsync(PluginDllPath, CancellationToken.None).ConfigureAwait(false);
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

        if (_pluginManager.SetPluginEnabled(selectedPlugin.Id, enabled: false, out var error))
        {
            Status = $"Plugin '{selectedPlugin.DisplayName}' disabled.";
            Refresh();
            return;
        }

        Status = "Could not disable plugin: " + (error ?? "Unknown error.");
    }

    [RelayCommand]
    private void RemoveSelectedPlugin()
    {
        RemoveInstalledPlugin(SelectedInstalledPlugin);
    }

    [RelayCommand]
    private void RemovePlugin(InstalledPluginItem? pluginItem)
    {
        RemoveInstalledPlugin(pluginItem);
    }

    [RelayCommand]
    private void SavePluginDirectory()
    {
        var settings = _stationRepository.StationDiscoveryPlugins;
        _stationRepository.SaveStationDiscoveryPluginSettings(new StationDiscoveryPluginSettings
        {
            PluginDirectory = string.IsNullOrWhiteSpace(PluginDirectory) ? settings.PluginDirectory : PluginDirectory.Trim(),
            DisabledPluginIds = settings.DisabledPluginIds,
            PendingDeletePluginPaths = settings.PendingDeletePluginPaths,
            HasShownPluginSafetyWarning = settings.HasShownPluginSafetyWarning,
        });

        Status = "Plugin directory saved.";
        Refresh();
    }

    [RelayCommand]
    private void OpenPluginSettings(InstalledPluginItem? pluginItem)
    {
        if (TryOpenPluginSettings(pluginItem, out var error))
        {
            return;
        }

        Status = "Could not open plugin settings: " + (error ?? "Unknown error.");
    }

    [RelayCommand]
    private async Task InstallCandidateItem(PluginCandidateItem? candidate)
    {
        await InstallCandidate(candidate, CancellationToken.None).ConfigureAwait(false);
    }

    [RelayCommand]
    private async Task UpgradeCandidateItem(PluginCandidateItem? candidate)
    {
        await InstallCandidate(candidate, CancellationToken.None).ConfigureAwait(false);
    }

    public bool TryOpenPluginSettings(InstalledPluginItem? pluginItem, out string? error)
    {
        error = null;
        if (pluginItem is null)
        {
            error = "Select an installed plugin first.";
            return false;
        }

        if (!pluginItem.HasSettings)
        {
            error = $"Plugin '{pluginItem.DisplayName}' does not expose settings.";
            return false;
        }

        if (_windowManager.ShowPluginSettings(pluginItem.Id, out error))
        {
            Status = $"Opened settings for '{pluginItem.DisplayName}'.";
            return true;
        }

        error ??= "Unknown error.";
        return false;
    }

    private async Task<bool> InstallPluginFromPathAsync(string path, CancellationToken cancellationToken)
    {
        var installedBefore = _pluginManager.GetPluginInventory()
            .Select(item => item.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (_pluginManager.AddPlugin(path, out var error))
        {
            if (!await ConfirmInstallDisclaimersAsync(installedBefore, cancellationToken).ConfigureAwait(false))
            {
                Refresh();
                return false;
            }

            PluginDllPath = string.Empty;
            Status = "Plugin installed and loaded.";
            Refresh();
            return true;
        }

        Status = "Could not install plugin: " + (error ?? "Unknown error.");
        return false;
    }

    public async Task InstallPluginsFromDroppedPathsAsync(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
        {
            return;
        }

        var successes = 0;
        string? lastError = null;

        foreach (var path in paths)
        {
            if (await InstallPluginFromPathAsync(path, CancellationToken.None).ConfigureAwait(false))
            {
                successes++;
            }
            else
            {
                lastError = Status;
            }
        }

        Refresh();
        Status = successes > 0
            ? $"Installed {successes} plugin file(s)."
            : "Could not install dropped plugins: " + (lastError ?? "Unknown error.");
    }

    public bool SetInstalledPluginEnabled(InstalledPluginItem? pluginItem, bool enabled)
    {
        if (pluginItem is null)
        {
            Status = "Select an installed plugin first.";
            return false;
        }

        if (_pluginManager.SetPluginEnabled(pluginItem.Id, enabled, out var error))
        {
            Status = enabled
                ? $"Plugin '{pluginItem.DisplayName}' enabled."
                : $"Plugin '{pluginItem.DisplayName}' disabled.";
            Refresh();
            return true;
        }

        Status = "Could not update plugin state: " + (error ?? "Unknown error.");
        return false;
    }

    public void ToggleInstalledPlugin(InstalledPluginItem? pluginItem)
    {
        if (pluginItem is null)
        {
            return;
        }

        SetInstalledPluginEnabled(pluginItem, !pluginItem.IsEnabled);
    }

    public bool RemoveInstalledPlugin(InstalledPluginItem? pluginItem)
    {
        if (pluginItem is null)
        {
            Status = "Select an installed plugin first.";
            return false;
        }

        if (_pluginManager.RemovePlugin(pluginItem.Id, out var error))
        {
            Refresh();

            var refreshed = InstalledPlugins.FirstOrDefault(item =>
                string.Equals(item.Id, pluginItem.Id, StringComparison.OrdinalIgnoreCase));
            Status = refreshed?.IsPendingDelete == true
                ? $"Plugin '{pluginItem.DisplayName}' is marked for delete on restart and has been disabled."
                : $"Plugin '{pluginItem.DisplayName}' uninstalled.";
            return true;
        }

        Status = "Could not remove plugin: " + (error ?? "Unknown error.");
        return false;
    }

    public async Task InstallCandidate(PluginCandidateItem? candidate, CancellationToken cancellationToken)
    {
        if (candidate is null)
        {
            return;
        }

        await InstallPluginFromPathAsync(candidate.Path, cancellationToken).ConfigureAwait(false);
    }

    public bool ChangePluginDirectory(string newDirectory, bool migrateExistingPlugins, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(newDirectory))
        {
            error = "Plugin directory path is empty.";
            return false;
        }

        try
        {
            var currentConfigured = _stationRepository.StationDiscoveryPlugins.PluginDirectory;
            var oldPath = Path.IsPathRooted(currentConfigured)
                ? currentConfigured
                : Path.Combine(AppContext.BaseDirectory, currentConfigured);

            var targetPath = Path.GetFullPath(newDirectory.Trim());
            Directory.CreateDirectory(targetPath);

            if (migrateExistingPlugins && Directory.Exists(oldPath) && !string.Equals(oldPath, targetPath, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var file in Directory.GetFiles(oldPath, "*.dll", SearchOption.TopDirectoryOnly))
                {
                    var destination = Path.Combine(targetPath, Path.GetFileName(file));
                    File.Copy(file, destination, overwrite: true);
                }
            }

            var settings = _stationRepository.StationDiscoveryPlugins;
            _stationRepository.SaveStationDiscoveryPluginSettings(new StationDiscoveryPluginSettings
            {
                PluginDirectory = targetPath,
                DisabledPluginIds = settings.DisabledPluginIds,
                PendingDeletePluginPaths = settings.PendingDeletePluginPaths,
                HasShownPluginSafetyWarning = settings.HasShownPluginSafetyWarning,
            });

            PluginDirectory = targetPath;
            Refresh();
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    [RelayCommand]
    private async Task UpgradeCandidate(PluginCandidateItem? candidate)
    {
        if (candidate is null)
        {
            Status = "Select an eligible plugin candidate first.";
            return;
        }

        await InstallPluginFromPathAsync(candidate.Path, CancellationToken.None).ConfigureAwait(false);
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

    public async Task InstallPluginFromFilePathAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            Status = "Select a plugin DLL path first.";
            return;
        }

        await InstallPluginFromPathAsync(path, CancellationToken.None).ConfigureAwait(false);
    }

    private async Task<bool> ConfirmInstallDisclaimersAsync(HashSet<string> installedBefore, CancellationToken cancellationToken)
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
                .EnsureAcceptedAsync(plugin.Id, disclaimerCapability.Disclaimer, cancellationToken)
                .ConfigureAwait(false);
            if (accepted)
            {
                continue;
            }

            _pluginManager.RemovePlugin(plugin.Id, out _);
            Status = $"Installation canceled: '{plugin.DisplayName}' conditions were rejected.";
            return false;
        }

        return true;
    }

    private IEnumerable<PluginCandidateItem> DiscoverEligiblePluginDlls(
        IReadOnlyDictionary<string, Version> installedByAssemblyName)
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
                yield return CreateCandidate(path, installedByAssemblyName, _featuredAssemblyNameHashes, _officialAssemblyNameHashes);
            }
        }

        foreach (var path in EnumerateMatches(AppContext.BaseDirectory))
        {
            var fileName = Path.GetFileName(path);
            if (knownFileNames.Add(fileName))
            {
                yield return CreateCandidate(path, installedByAssemblyName, _featuredAssemblyNameHashes, _officialAssemblyNameHashes);
            }
        }
    }

    private static PluginCandidateItem CreateCandidate(
        string path,
        IReadOnlyDictionary<string, Version> installedByAssemblyName,
        ISet<string> featuredAssemblyNameHashes,
        ISet<string> officialAssemblyNameHashes)
    {
        if (!TryGetPluginAssemblyMetadata(path, out var assemblyName, out var version))
        {
            return new PluginCandidateItem(
                path,
                Path.GetFileNameWithoutExtension(path),
                Path.GetFileNameWithoutExtension(path),
                null,
                null,
                false,
                false,
                false,
                false,
                false,
                true);
        }

        installedByAssemblyName.TryGetValue(assemblyName, out var installedVersion);
        var hasInstalledVersion = installedVersion is not null;
        var isSameVersionInstalled = version is not null && installedVersion is not null && version == installedVersion;
        var isUpgrade = version is not null && installedVersion is not null && version > installedVersion;

        var isFeatured = IsFeaturedCandidate(assemblyName, featuredAssemblyNameHashes);
        var isOfficial = IsOfficialCandidate(assemblyName, officialAssemblyNameHashes);

        return new PluginCandidateItem(
            path,
            Path.GetFileNameWithoutExtension(path),
            assemblyName,
            version,
            installedVersion,
            isUpgrade,
            hasInstalledVersion,
            isSameVersionInstalled,
            isFeatured,
            isOfficial,
            !isOfficial);
    }

    private static bool IsFeaturedCandidate(string assemblyName, ISet<string> featuredAssemblyNameHashes)
    {
        if (featuredAssemblyNameHashes.Count == 0)
        {
            return false;
        }

        var normalizedAssemblyName = assemblyName.Trim().ToLowerInvariant();
        return featuredAssemblyNameHashes.Contains(ComputeSha256Hex(normalizedAssemblyName));
    }

    private static bool IsOfficialCandidate(string assemblyName, ISet<string> officialAssemblyNameHashes)
    {
        if (officialAssemblyNameHashes.Count == 0)
        {
            return false;
        }

        var normalizedAssemblyName = assemblyName.Trim().ToLowerInvariant();
        return officialAssemblyNameHashes.Contains(ComputeSha256Hex(normalizedAssemblyName));
    }

    private static HashSet<string> LoadPluginHashes(string metadataKey)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var metadata = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>();
            foreach (var item in metadata)
            {
                if (!string.Equals(item.Key, metadataKey, StringComparison.Ordinal))
                {
                    continue;
                }

                var normalized = NormalizeHash(item.Value);
                if (normalized is not null)
                {
                    result.Add(normalized);
                }
            }
        }
        catch
        {
            // Failing closed keeps plugin candidate rendering stable.
        }

        return result;
    }

    private static string? NormalizeHash(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim().ToLowerInvariant();
        if (trimmed.Length != 64)
        {
            return null;
        }

        return trimmed.All(static c => char.IsAsciiHexDigit(c))
            ? trimmed
            : null;
    }

    private static string ComputeSha256Hex(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
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
            .TakeWhile((value, index) => value >= 0 && (index < 2 || value > 0 || PartsBeforeHadValue(index, version)))
            .ToArray();

        return parts.Length == 0
            ? $"{version.Major}.{version.Minor}"
            : string.Join('.', parts);

        static bool PartsBeforeHadValue(int index, Version v)
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

    public sealed class InstalledPluginItem(string id, string displayName, bool hasSettings, string assemblyName, Version version, bool isEnabled, bool canUninstall, bool isPendingDelete)
    {
        public string Id { get; } = id;

        public string DisplayName { get; } = displayName;

        public bool HasSettings { get; } = hasSettings;

        public string AssemblyName { get; } = assemblyName;

        public Version Version { get; } = version;

        public string VersionText { get; } = FormatVersion(version);

        public bool IsEnabled { get; } = isEnabled;

        public bool CanUninstall { get; } = canUninstall;

        public bool IsPendingDelete { get; } = isPendingDelete;
    }

    public sealed class PluginCandidateItem(
        string path,
        string displayName,
        string assemblyName,
        Version? version,
        Version? installedVersion,
        bool isUpgrade,
        bool hasInstalledVersion,
        bool isSameVersionInstalled,
        bool isFeatured,
        bool isOfficial,
        bool isExternal)
    {
        public string Path { get; } = path;

        public string DisplayName { get; } = displayName;

        public string AssemblyName { get; } = assemblyName;

        public Version? Version { get; } = version;

        public string VersionText { get; } = FormatVersion(version);

        public Version? InstalledVersion { get; } = installedVersion;

        public string InstalledVersionText { get; } = FormatVersion(installedVersion);

        public bool HasInstalledVersion { get; } = hasInstalledVersion;

        public bool IsUpgrade { get; } = isUpgrade;

        public bool IsSameVersionInstalled { get; } = isSameVersionInstalled;

        public bool IsFeatured { get; } = isFeatured;

        public bool IsOfficial { get; } = isOfficial;

        public bool IsExternal { get; } = isExternal;
    }
}

