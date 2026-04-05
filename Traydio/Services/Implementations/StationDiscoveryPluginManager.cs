using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Avalonia.Threading;
using Traydio.Common;

namespace Traydio.Services.Implementations;

public sealed class PluginManager(IStationRepository stationRepository) : IPluginManager
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
                item.CanUninstall))
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
        Console.Error.WriteLine($"[Traydio][PluginManager] Remove requested pluginId={pluginId}");

        var descriptor = _inventory.FirstOrDefault(item =>
            string.Equals(item.Id, pluginId, StringComparison.OrdinalIgnoreCase));
        if (descriptor is null)
        {
            error = "Plugin not found.";
            Console.Error.WriteLine($"[Traydio][PluginManager] Remove failed pluginId={pluginId}: {error}");
            return false;
        }

        if (!descriptor.CanUninstall || string.IsNullOrWhiteSpace(descriptor.SourcePath))
        {
            // Built-in/referenced plugins cannot be deleted from disk, so remove means disable.
            Console.Error.WriteLine($"[Traydio][PluginManager] Remove fallback-to-disable pluginId={pluginId}");
            return SetPluginEnabled(pluginId, enabled: false, out error);
        }

        try
        {
            var settings = stationRepository.StationDiscoveryPlugins;
            settings.DisabledPluginIds.RemoveAll(id => string.Equals(id, pluginId, StringComparison.OrdinalIgnoreCase));
            stationRepository.SaveStationDiscoveryPluginSettings(settings);

            File.Delete(descriptor.SourcePath);
            ReloadPlugins();
            Console.Error.WriteLine($"[Traydio][PluginManager] Remove succeeded pluginId={pluginId} path={descriptor.SourcePath}");
            return true;
        }
        catch (IOException ex)
        {
            error = "Plugin file is currently locked and could not be removed.";
            Console.Error.WriteLine($"[Traydio][PluginManager] Remove IO failure pluginId={pluginId} path={descriptor.SourcePath}: {ex}");
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            error = "Access denied while removing plugin file.";
            Console.Error.WriteLine($"[Traydio][PluginManager] Remove access denied pluginId={pluginId} path={descriptor.SourcePath}: {ex}");
            return false;
        }
    }

    public bool SetPluginEnabled(string pluginId, bool enabled, out string? error)
    {
        error = null;
        Console.Error.WriteLine($"[Traydio][PluginManager] SetPluginEnabled pluginId={pluginId} enabled={enabled}");

        if (_inventory.All(p => !string.Equals(p.Id, pluginId, StringComparison.OrdinalIgnoreCase)))
        {
            error = "Plugin not found.";
            Console.Error.WriteLine($"[Traydio][PluginManager] SetPluginEnabled failed pluginId={pluginId}: {error}");
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
            Console.Error.WriteLine($"[Traydio][PluginManager] SetPluginEnabled no-op pluginId={pluginId} enabled={enabled}");
            return true;
        }

        stationRepository.SaveStationDiscoveryPluginSettings(settings);
        ReloadPlugins();
        Console.Error.WriteLine($"[Traydio][PluginManager] SetPluginEnabled succeeded pluginId={pluginId} enabled={enabled}");
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
        foreach (var item in _inventory)
        {
            item.IsEnabled = !disabled.Contains(item.Id, StringComparer.OrdinalIgnoreCase);
            if (item.IsEnabled)
            {
                _plugins.Add(item.Plugin);
            }
        }

        PluginsChanged?.Invoke(this, EventArgs.Empty);
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
            var plugins = CreatePlugins(assembly).ToArray();

            if (plugins.Length == 0)
            {
                loadContext.Unload();
                return;
            }

            _loadedPlugins[pluginPath] = new LoadedPlugin(loadContext, plugins);
            var canUninstall = IsPathUnderConfiguredPluginDirectory(pluginPath);
            foreach (var plugin in plugins)
            {
                AddIfNotExists(plugin, assembly, pluginPath, canUninstall);
            }
        }
        catch
        {
            // Ignore malformed or incompatible plugin assemblies.
        }
    }

    private static IEnumerable<ITraydioPlugin> CreatePlugins(Assembly assembly)
    {
        var pluginTypes = assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(ITraydioPlugin).IsAssignableFrom(t))
            .Where(t => t.GetConstructor(Type.EmptyTypes) is not null)
            .ToArray();

        foreach (var type in pluginTypes)
        {
            if (Activator.CreateInstance(type) is ITraydioPlugin plugin)
            {
                yield return plugin;
            }
        }

        var legacyProviderTypes = assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(IRadioStationProviderPlugin).IsAssignableFrom(t))
            .Where(t => t.GetConstructor(Type.EmptyTypes) is not null)
            .ToArray();

        foreach (var type in legacyProviderTypes)
        {
            if (Activator.CreateInstance(type) is IRadioStationProviderPlugin provider)
            {
                yield return new LegacyStationProviderPluginAdapter(provider);
            }
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
        if (_inventory.Any(p => string.Equals(p.Id, plugin.Id, StringComparison.OrdinalIgnoreCase)))
        {
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

    private sealed class PluginDescriptor
    {
        public required ITraydioPlugin Plugin { get; init; }

        public required string Id { get; init; }

        public required string DisplayName { get; init; }

        public required string AssemblyName { get; init; }

        public required Version Version { get; init; }

        public required bool HasSettings { get; init; }

        public bool IsEnabled { get; set; }

        public string? SourcePath { get; init; }

        public bool CanUninstall { get; init; }
    }

    private sealed class LoadedPlugin(PluginLoadContext context, IReadOnlyList<ITraydioPlugin> plugins)
    {
        public PluginLoadContext Context { get; } = context;

        public IReadOnlyList<ITraydioPlugin> Plugins { get; } = plugins;
    }

    private sealed class LegacyStationProviderPluginAdapter(IRadioStationProviderPlugin provider) : ITraydioPlugin
    {
        public string Id => "legacy." + provider.Id;

        public string DisplayName => provider.DisplayName;

        public IReadOnlyList<IPluginCapability> Capabilities { get; } = [new LegacyStationDiscoveryCapability(provider)];
    }

    private sealed class LegacyStationDiscoveryCapability(IRadioStationProviderPlugin provider) : IStationDiscoveryCapability
    {
        public string CapabilityId => "station-discovery";

        public string ProviderId => provider.Id;

        public string DisplayName => provider.DisplayName;

        public IAsyncEnumerable<DiscoveredStation> SearchAsync(StationSearchRequest request, CancellationToken cancellationToken)
        {
            return provider.SearchAsync(request, cancellationToken);
        }
    }
}
