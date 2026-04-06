using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Traydio.Common;

namespace Traydio.Services.Implementations;

public sealed class PluginManager(
    IStationRepository stationRepository,
    IServiceProvider serviceProvider,
    ILogger<PluginManager> logger) : IPluginManager
{
    private readonly List<ITraydioPlugin> _plugins = [];
    private readonly List<PluginDescriptor> _inventory = [];
    private readonly Dictionary<string, LoadedPlugin> _loadedPlugins = new(StringComparer.OrdinalIgnoreCase);

    private FileSystemWatcher? _watcher;
    private string? _pluginDirectory;
    private bool _isStarted;

    public event EventHandler? PluginsChanged;

    public IReadOnlyList<ITraydioPlugin> GetPlugins()
    {
        return _plugins.ToArray();
    }

    public IReadOnlyList<PluginInventoryItem> GetPluginInventory()
    {
        return _inventory
            .OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(item => new PluginInventoryItem(
                item.Id,
                item.DisplayName,
                item.AssemblyName,
                item.Version,
                item.HasSettings,
                item.IsEnabled,
                item.CanUninstall,
                item.IsPendingDelete))
            .ToArray();
    }

    public bool AddPlugin(string sourceDllPath, out string? error)
    {
        error = null;
        if (!File.Exists(sourceDllPath))
        {
            error = "Plugin file does not exist.";
            return false;
        }

        EnsurePluginDirectory();
        var targetPath = Path.Combine(_pluginDirectory!, Path.GetFileName(sourceDllPath));

        try
        {
            var sourceFullPath = Path.GetFullPath(sourceDllPath);
            var targetFullPath = Path.GetFullPath(targetPath);

            string? installedAssemblyName = null;
            try
            {
                installedAssemblyName = AssemblyName.GetAssemblyName(sourceFullPath).Name;
            }
            catch
            {
                // Best-effort metadata read.
            }

            if (string.Equals(sourceFullPath, targetFullPath, StringComparison.OrdinalIgnoreCase))
            {
                // Already in plugin folder; treat this as a refresh request.
                ReloadPlugins();
                ReEnableInstalledAssembly(installedAssemblyName);
                return true;
            }

            File.Copy(sourceFullPath, targetFullPath, overwrite: true);
            RemovePendingDeletePath(targetFullPath);
            ReloadPlugins();
            ReEnableInstalledAssembly(installedAssemblyName);
            return true;
        }
        catch (IOException)
        {
            error = "Plugin file is currently locked. If this plugin is already installed, use Refresh; otherwise close the process using the DLL and try again.";
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            error = "Access denied while copying plugin DLL.";
            return false;
        }
    }

    private void ReEnableInstalledAssembly(string? assemblyName)
    {
        if (string.IsNullOrWhiteSpace(assemblyName))
        {
            return;
        }

        var candidate = _inventory.FirstOrDefault(item =>
            string.Equals(item.AssemblyName, assemblyName, StringComparison.OrdinalIgnoreCase));
        if (candidate is null || candidate.IsEnabled)
        {
            return;
        }

        SetPluginEnabled(candidate.Id, enabled: true, out _);
    }

    public bool RemovePlugin(string pluginId, out string? error)
    {
        error = null;
        logger.LogInformation("Remove requested pluginId={PluginId}", pluginId);

        var descriptor = _inventory.FirstOrDefault(item =>
            string.Equals(item.Id, pluginId, StringComparison.OrdinalIgnoreCase));
        if (descriptor is null)
        {
            error = "Plugin not found.";
            logger.LogWarning("Remove failed pluginId={PluginId}: {Error}", pluginId, error);
            return false;
        }

        if (string.IsNullOrWhiteSpace(descriptor.SourcePath))
        {
            error = "Plugin cannot be uninstalled because no plugin file path is available.";
            logger.LogWarning("Remove failed pluginId={PluginId}: {Error}", pluginId, error);
            return false;
        }

        var pluginPath = descriptor.SourcePath;

        try
        {
            TryDeletePluginFileNow(pluginPath);
            RemovePendingDeletePath(pluginPath);
            RemoveDisabledPluginId(pluginId);
            ReloadPlugins();
            logger.LogInformation("Remove succeeded pluginId={PluginId} path={PluginPath}", pluginId, pluginPath);
            return true;
        }
        catch (IOException ex)
        {
            QueuePluginForDeleteOnRestart(pluginId, pluginPath);
            ReloadPlugins();
            logger.LogWarning(ex, "Remove deferred (IO) pluginId={PluginId} path={PluginPath}", pluginId, pluginPath);
            return true;
        }
        catch (UnauthorizedAccessException ex)
        {
            QueuePluginForDeleteOnRestart(pluginId, pluginPath);
            ReloadPlugins();
            logger.LogWarning(ex, "Remove deferred (access denied) pluginId={PluginId} path={PluginPath}", pluginId, pluginPath);
            return true;
        }
    }

    public bool SetPluginEnabled(string pluginId, bool enabled, out string? error)
    {
        error = null;
        logger.LogInformation("SetPluginEnabled pluginId={PluginId} enabled={Enabled}", pluginId, enabled);

        if (_inventory.All(p => !string.Equals(p.Id, pluginId, StringComparison.OrdinalIgnoreCase)))
        {
            error = "Plugin not found.";
            logger.LogWarning("SetPluginEnabled failed pluginId={PluginId}: {Error}", pluginId, error);
            return false;
        }

        var settings = stationRepository.StationDiscoveryPlugins;
        var changed = false;

        if (enabled)
        {
            changed = settings.DisabledPluginIds.RemoveAll(id => string.Equals(id, pluginId, StringComparison.OrdinalIgnoreCase)) > 0;
        }
        else if (!settings.DisabledPluginIds.Contains(pluginId, StringComparer.OrdinalIgnoreCase))
        {
            settings.DisabledPluginIds.Add(pluginId);
            changed = true;
        }

        if (!changed)
        {
            logger.LogDebug("SetPluginEnabled no-op pluginId={PluginId} enabled={Enabled}", pluginId, enabled);
            return true;
        }

        stationRepository.SaveStationDiscoveryPluginSettings(settings);
        ReloadPlugins();
        logger.LogInformation("SetPluginEnabled succeeded pluginId={PluginId} enabled={Enabled}", pluginId, enabled);
        return true;
    }

    public void Start()
    {
        if (_isStarted)
        {
            return;
        }

        _isStarted = true;
        EnsurePluginDirectory();
        ProcessPendingDeletesBeforeLoad();
        ReloadPlugins();

        _watcher = new FileSystemWatcher(_pluginDirectory!, "*.dll")
        {
            EnableRaisingEvents = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
        };

        _watcher.Created += (_, _) => ReloadPluginsOnUiThread();
        _watcher.Deleted += (_, _) => ReloadPluginsOnUiThread();
        _watcher.Renamed += (_, _) => ReloadPluginsOnUiThread();
        _watcher.Changed += (_, _) => ReloadPluginsOnUiThread();
    }

    public void Stop()
    {
        if (!_isStarted)
        {
            return;
        }

        _isStarted = false;

        if (_watcher is not null)
        {
            _watcher.Dispose();
            _watcher = null;
        }

        foreach (var loadedPlugin in _loadedPlugins.Values)
        {
            loadedPlugin.Context.Unload();
        }

        _loadedPlugins.Clear();
        _plugins.Clear();
    }

    private void ReloadPluginsOnUiThread()
    {
        Dispatcher.UIThread.Post(ReloadPlugins);
    }

    private void ReloadPlugins()
    {
        logger.LogDebug("Reloading plugins.");
        EnsurePluginDirectory();
        _plugins.Clear();
        _inventory.Clear();

        foreach (var loadedPlugin in _loadedPlugins.Values)
        {
            loadedPlugin.Context.Unload();
        }

        _loadedPlugins.Clear();

        // Prefer referenced plugin assemblies so locally-built plugins win over stale copied DLLs.
        LoadPluginsFromReferencedAssemblies();
        LoadPluginsFromFolder(_pluginDirectory!);
        LoadPluginsFromBaseDirectory();

        var disabled = stationRepository.StationDiscoveryPlugins.DisabledPluginIds;
        var pendingDeletePaths = stationRepository.StationDiscoveryPlugins.PendingDeletePluginPaths;
        foreach (var item in _inventory)
        {
            item.IsPendingDelete = IsPendingDelete(item, pendingDeletePaths);
            item.IsEnabled = !disabled.Contains(item.Id, StringComparer.OrdinalIgnoreCase);
            if (item.IsEnabled)
            {
                _plugins.Add(item.Plugin);
            }
        }

        PluginsChanged?.Invoke(this, EventArgs.Empty);
        logger.LogInformation("Plugin reload completed. loaded={LoadedCount}, inventory={InventoryCount}", _plugins.Count, _inventory.Count);
    }

    private void EnsurePluginDirectory()
    {
        var settingsDirectory = stationRepository.StationDiscoveryPlugins.PluginDirectory;
        _pluginDirectory = Path.IsPathRooted(settingsDirectory)
            ? settingsDirectory
            : Path.Combine(AppContext.BaseDirectory, settingsDirectory);

        Directory.CreateDirectory(_pluginDirectory);
    }

    private void LoadPluginsFromFolder(string pluginDirectory)
    {
        foreach (var pluginPath in Directory.GetFiles(pluginDirectory, "*.dll"))
        {
            TryLoadPluginsFromAssembly(pluginPath);
        }
    }

    private void LoadPluginsFromReferencedAssemblies()
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic)
            .Where(a => a.GetName().Name?.StartsWith("Traydio.Plugin.", StringComparison.OrdinalIgnoreCase) == true)
            .ToArray();

        foreach (var assembly in assemblies)
        {
            foreach (var plugin in CreatePlugins(assembly))
            {
                AddIfNotExists(plugin, assembly, sourcePath: null, canUninstall: false);
            }
        }
    }

    private void LoadPluginsFromBaseDirectory()
    {
        foreach (var pluginPath in Directory.GetFiles(AppContext.BaseDirectory, "Traydio.Plugin.*.dll"))
        {
            TryLoadPluginsFromAssembly(pluginPath);
        }
    }

    private void TryLoadPluginsFromAssembly(string pluginPath)
    {
        try
        {
            var loadContext = new PluginLoadContext(pluginPath);
            var assembly = loadContext.LoadFromAssemblyPath(pluginPath);
            var plugins = CreatePlugins(assembly, pluginPath).ToArray();

            if (plugins.Length == 0)
            {
                loadContext.Unload();
                return;
            }

            _loadedPlugins[pluginPath] = new LoadedPlugin(loadContext, plugins);
            var canUninstall = true;
            foreach (var plugin in plugins)
            {
                AddIfNotExists(plugin, assembly, pluginPath, canUninstall);
            }
        }
        catch (Exception ex)
        {
            // Ignore malformed or incompatible plugin assemblies.
            logger.LogWarning(ex, "Failed to load plugin assembly: {PluginPath}", pluginPath);
        }
    }

    private IEnumerable<ITraydioPlugin> CreatePlugins(Assembly assembly, string? sourcePath = null)
    {
        Type[] allTypes;
        try
        {
            allTypes = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            allTypes = ex.Types.Where(static t => t is not null).Cast<Type>().ToArray();
            logger.LogWarning(ex, "Partial type-load failure for plugin assembly {Assembly}", assembly.FullName);
        }

        var pluginTypes = allTypes
            .Where(t => !t.IsAbstract && typeof(ITraydioPlugin).IsAssignableFrom(t))
            .ToArray();

        foreach (var type in pluginTypes)
        {
            if (TryCreatePlugin(type, sourcePath, out var plugin) && plugin is not null)
            {
                yield return plugin;
            }
        }
    }

    private bool TryCreatePlugin(Type pluginType, string? sourcePath, out ITraydioPlugin? plugin)
    {
        plugin = null;
        try
        {
            plugin = ActivatorUtilities.CreateInstance(serviceProvider, pluginType) as ITraydioPlugin;
            if (plugin is not null)
            {
                return true;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "DI activation failed for plugin type {PluginType} from {SourcePath}", pluginType.FullName, sourcePath ?? "<referenced>");
        }

        if (pluginType.GetConstructor(Type.EmptyTypes) is null)
        {
            return false;
        }

        try
        {
            plugin = Activator.CreateInstance(pluginType) as ITraydioPlugin;
            return plugin is not null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Fallback activation failed for plugin type {PluginType} from {SourcePath}", pluginType.FullName, sourcePath ?? "<referenced>");
            return false;
        }
    }

    private bool IsPathUnderConfiguredPluginDirectory(string pluginPath)
    {
        if (string.IsNullOrWhiteSpace(_pluginDirectory))
        {
            return false;
        }

        try
        {
            var pluginFullPath = Path.GetFullPath(pluginPath);
            var pluginDirectoryFullPath = Path.GetFullPath(_pluginDirectory);

            return string.Equals(Path.GetDirectoryName(pluginFullPath), pluginDirectoryFullPath, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private void AddIfNotExists(ITraydioPlugin plugin, Assembly sourceAssembly, string? sourcePath, bool canUninstall)
    {
        var existing = _inventory.FirstOrDefault(p => string.Equals(p.Id, plugin.Id, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            if (string.IsNullOrWhiteSpace(existing.SourcePath) && !string.IsNullOrWhiteSpace(sourcePath))
            {
                // Keep existing plugin type preference but retain uninstall path for this ID.
                existing.SourcePath = sourcePath;
                existing.CanUninstall = canUninstall;
            }

            return;
        }

        var assemblyName = sourceAssembly.GetName();
        _inventory.Add(new PluginDescriptor
        {
            Plugin = plugin,
            Id = plugin.Id,
            DisplayName = plugin.DisplayName,
            AssemblyName = assemblyName.Name ?? plugin.Id,
            Version = assemblyName.Version ?? new Version(0, 0, 0, 0),
            HasSettings = plugin.Capabilities.OfType<IPluginSettingsCapability>().Any(),
            SourcePath = sourcePath,
            CanUninstall = canUninstall,
        });
    }

    private void TryDeletePluginFileNow(string pluginPath)
    {
        var normalized = Path.GetFullPath(pluginPath);
        if (!File.Exists(normalized))
        {
            return;
        }

        File.Delete(normalized);
    }

    private void QueuePluginForDeleteOnRestart(string pluginId, string pluginPath)
    {
        var settings = stationRepository.StationDiscoveryPlugins;
        var normalized = Path.GetFullPath(pluginPath);

        if (!settings.PendingDeletePluginPaths.Contains(normalized, StringComparer.OrdinalIgnoreCase))
        {
            settings.PendingDeletePluginPaths.Add(normalized);
        }

        if (!settings.DisabledPluginIds.Contains(pluginId, StringComparer.OrdinalIgnoreCase))
        {
            settings.DisabledPluginIds.Add(pluginId);
        }

        stationRepository.SaveStationDiscoveryPluginSettings(settings);
        logger.LogInformation("Remove queued for restart pluginId={PluginId} path={PluginPath}", pluginId, normalized);
    }

    private void RemovePendingDeletePath(string pluginPath)
    {
        var settings = stationRepository.StationDiscoveryPlugins;
        var normalized = Path.GetFullPath(pluginPath);
        if (settings.PendingDeletePluginPaths.RemoveAll(path => string.Equals(path, normalized, StringComparison.OrdinalIgnoreCase)) > 0)
        {
            stationRepository.SaveStationDiscoveryPluginSettings(settings);
        }
    }

    private void RemoveDisabledPluginId(string pluginId)
    {
        var settings = stationRepository.StationDiscoveryPlugins;
        if (settings.DisabledPluginIds.RemoveAll(id => string.Equals(id, pluginId, StringComparison.OrdinalIgnoreCase)) > 0)
        {
            stationRepository.SaveStationDiscoveryPluginSettings(settings);
        }
    }

    private void ProcessPendingDeletesBeforeLoad()
    {
        var settings = stationRepository.StationDiscoveryPlugins;
        if (settings.PendingDeletePluginPaths.Count == 0)
        {
            return;
        }

        var remaining = new List<string>();
        foreach (var path in settings.PendingDeletePluginPaths)
        {
            try
            {
                var normalized = Path.GetFullPath(path);
                if (File.Exists(normalized))
                {
                    File.Delete(normalized);
                    logger.LogInformation("Pending delete succeeded path={PluginPath}", normalized);
                }
                else
                {
                    logger.LogDebug("Pending delete skipped (missing file) path={PluginPath}", normalized);
                }
            }
            catch (Exception ex)
            {
                remaining.Add(path);
                logger.LogWarning(ex, "Pending delete failed path={PluginPath}", path);
            }
        }

        settings.PendingDeletePluginPaths = remaining;
        stationRepository.SaveStationDiscoveryPluginSettings(settings);
    }

    private static bool IsPendingDelete(PluginDescriptor descriptor, IReadOnlyList<string> pendingDeletePaths)
    {
        if (!string.IsNullOrWhiteSpace(descriptor.SourcePath) &&
            pendingDeletePaths.Contains(descriptor.SourcePath, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        return pendingDeletePaths.Any(path =>
            string.Equals(Path.GetFileNameWithoutExtension(path), descriptor.AssemblyName, StringComparison.OrdinalIgnoreCase));
    }

    private sealed class PluginDescriptor
    {
        public required ITraydioPlugin Plugin { get; init; }

        public required string Id { get; init; }

        public required string DisplayName { get; init; }

        public required string AssemblyName { get; init; }

        public required Version Version { get; init; }

        public required bool HasSettings { get; init; }

        public bool IsEnabled { get; set; }

        public string? SourcePath { get; set; }

        public bool CanUninstall { get; set; }

        public bool IsPendingDelete { get; set; }
    }

    private sealed class LoadedPlugin(PluginLoadContext context, IReadOnlyList<ITraydioPlugin> plugins)
    {
        public PluginLoadContext Context { get; } = context;

        public IReadOnlyList<ITraydioPlugin> Plugins { get; } = plugins;
    }

}
