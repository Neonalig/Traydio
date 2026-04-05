using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Traydio.Common;

namespace Traydio.Services.Implementations;

public sealed class PluginManager : IPluginManager
{
    private readonly IStationRepository _stationRepository;
    private readonly List<ITraydioPlugin> _plugins = new();
    private readonly Dictionary<string, LoadedPlugin> _loadedPlugins = new(StringComparer.OrdinalIgnoreCase);

    private FileSystemWatcher? _watcher;
    private string? _pluginDirectory;

    public event EventHandler? PluginsChanged;

    public PluginManager(IStationRepository stationRepository)
    {
        _stationRepository = stationRepository;
    }

    public IReadOnlyList<ITraydioPlugin> GetPlugins()
    {
        return _plugins.ToArray();
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
        File.Copy(sourceDllPath, targetPath, overwrite: true);
        ReloadPlugins();
        return true;
    }

    public bool RemovePlugin(string pluginId, out string? error)
    {
        error = null;
        var settings = _stationRepository.StationDiscoveryPlugins;

        if (_plugins.Any(p => string.Equals(p.Id, pluginId, StringComparison.OrdinalIgnoreCase)))
        {
            if (!settings.DisabledPluginIds.Contains(pluginId, StringComparer.OrdinalIgnoreCase))
            {
                settings.DisabledPluginIds.Add(pluginId);
                _stationRepository.SaveStationDiscoveryPluginSettings(settings);
            }

            ReloadPlugins();
            return true;
        }

        error = "Plugin not found.";
        return false;
    }

    public void Start()
    {
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

        foreach (var loadedPlugin in _loadedPlugins.Values)
        {
            loadedPlugin.Context.Unload();
        }

        _loadedPlugins.Clear();

        LoadPluginsFromFolder(_pluginDirectory!);
        LoadPluginsFromReferencedAssemblies();
        LoadPluginsFromBaseDirectory();

        var disabled = _stationRepository.StationDiscoveryPlugins.DisabledPluginIds;
        _plugins.RemoveAll(plugin => disabled.Contains(plugin.Id, StringComparer.OrdinalIgnoreCase));

        PluginsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void EnsurePluginDirectory()
    {
        var settingsDirectory = _stationRepository.StationDiscoveryPlugins.PluginDirectory;
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
                AddIfNotExists(plugin);
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
            foreach (var plugin in plugins)
            {
                AddIfNotExists(plugin);
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

    private void AddIfNotExists(ITraydioPlugin plugin)
    {
        if (_plugins.Any(p => string.Equals(p.Id, plugin.Id, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        _plugins.Add(plugin);
    }

    private sealed class LoadedPlugin
    {
        public LoadedPlugin(PluginLoadContext context, IReadOnlyList<ITraydioPlugin> plugins)
        {
            Context = context;
            Plugins = plugins;
        }

        public PluginLoadContext Context { get; }

        public IReadOnlyList<ITraydioPlugin> Plugins { get; }
    }

    private sealed class LegacyStationProviderPluginAdapter : ITraydioPlugin
    {
        private readonly IRadioStationProviderPlugin _provider;

        public LegacyStationProviderPluginAdapter(IRadioStationProviderPlugin provider)
        {
            _provider = provider;
            Capabilities = [new LegacyStationDiscoveryCapability(provider)];
        }

        public string Id => "legacy." + _provider.Id;

        public string DisplayName => _provider.DisplayName;

        public IReadOnlyList<IPluginCapability> Capabilities { get; }
    }

    private sealed class LegacyStationDiscoveryCapability : IStationDiscoveryCapability
    {
        private readonly IRadioStationProviderPlugin _provider;

        public LegacyStationDiscoveryCapability(IRadioStationProviderPlugin provider)
        {
            _provider = provider;
        }

        public string CapabilityId => "station-discovery";

        public string ProviderId => _provider.Id;

        public string DisplayName => _provider.DisplayName;

        public Task<IReadOnlyList<DiscoveredStation>> SearchAsync(StationSearchRequest request, CancellationToken cancellationToken)
        {
            return _provider.SearchAsync(request, cancellationToken);
        }
    }
}
