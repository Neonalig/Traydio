using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Avalonia.Threading;
using Traydio.Common;

namespace Traydio.Services.Implementations;

public sealed class StationDiscoveryPluginManager : IStationDiscoveryPluginManager
{
    private readonly IStationRepository _stationRepository;
    private readonly List<IRadioStationProviderPlugin> _providers = new();
    private readonly Dictionary<string, LoadedPlugin> _loadedPlugins = new(StringComparer.OrdinalIgnoreCase);

    private FileSystemWatcher? _watcher;
    private string? _pluginDirectory;

    public event EventHandler? ProvidersChanged;

    public StationDiscoveryPluginManager(IStationRepository stationRepository)
    {
        _stationRepository = stationRepository;
    }

    public IReadOnlyList<IRadioStationProviderPlugin> GetProviders()
    {
        return _providers.ToArray();
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
        ReloadProviders();
        return true;
    }

    public bool RemovePlugin(string pluginId, out string? error)
    {
        error = null;
        var settings = _stationRepository.StationDiscoveryPlugins;

        if (_providers.Any(p => string.Equals(p.Id, pluginId, StringComparison.OrdinalIgnoreCase)))
        {
            if (!settings.DisabledPluginIds.Contains(pluginId, StringComparer.OrdinalIgnoreCase))
            {
                settings.DisabledPluginIds.Add(pluginId);
                _stationRepository.SaveStationDiscoveryPluginSettings(settings);
            }

            ReloadProviders();
            return true;
        }

        error = "Plugin not found.";
        return false;
    }

    public void Start()
    {
        EnsurePluginDirectory();
        ReloadProviders();

        _watcher = new FileSystemWatcher(_pluginDirectory!, "*.dll")
        {
            EnableRaisingEvents = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
        };

        _watcher.Created += (_, _) => ReloadProvidersOnUiThread();
        _watcher.Deleted += (_, _) => ReloadProvidersOnUiThread();
        _watcher.Renamed += (_, _) => ReloadProvidersOnUiThread();
        _watcher.Changed += (_, _) => ReloadProvidersOnUiThread();
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
        _providers.Clear();
    }

    private void ReloadProvidersOnUiThread()
    {
        Dispatcher.UIThread.Post(ReloadProviders);
    }

    private void ReloadProviders()
    {
        EnsurePluginDirectory();
        _providers.Clear();

        foreach (var loadedPlugin in _loadedPlugins.Values)
        {
            loadedPlugin.Context.Unload();
        }

        _loadedPlugins.Clear();

        LoadProvidersFromFolder(_pluginDirectory!);
        LoadProvidersFromReferencedAssemblies();
        LoadProvidersFromBaseDirectory();

        var disabled = _stationRepository.StationDiscoveryPlugins.DisabledPluginIds;
        _providers.RemoveAll(provider => disabled.Contains(provider.Id, StringComparer.OrdinalIgnoreCase));

        ProvidersChanged?.Invoke(this, EventArgs.Empty);
    }

    private void EnsurePluginDirectory()
    {
        var settingsDirectory = _stationRepository.StationDiscoveryPlugins.PluginDirectory;
        _pluginDirectory = Path.IsPathRooted(settingsDirectory)
            ? settingsDirectory
            : Path.Combine(AppContext.BaseDirectory, settingsDirectory);

        Directory.CreateDirectory(_pluginDirectory);
    }

    private void LoadProvidersFromFolder(string pluginDirectory)
    {
        foreach (var pluginPath in Directory.GetFiles(pluginDirectory, "*.dll"))
        {
            TryLoadProvidersFromAssembly(pluginPath);
        }
    }

    private void LoadProvidersFromReferencedAssemblies()
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic)
            .Where(a => a.GetName().Name?.StartsWith("Traydio.Plugin.", StringComparison.OrdinalIgnoreCase) == true)
            .ToArray();

        foreach (var assembly in assemblies)
        {
            foreach (var plugin in CreateProviders(assembly))
            {
                AddIfNotExists(plugin);
            }
        }
    }

    private void LoadProvidersFromBaseDirectory()
    {
        foreach (var pluginPath in Directory.GetFiles(AppContext.BaseDirectory, "Traydio.Plugin.*.dll"))
        {
            TryLoadProvidersFromAssembly(pluginPath);
        }
    }

    private void TryLoadProvidersFromAssembly(string pluginPath)
    {
        try
        {
            var loadContext = new PluginLoadContext(pluginPath);
            var assembly = loadContext.LoadFromAssemblyPath(pluginPath);
            var providers = CreateProviders(assembly).ToArray();

            if (providers.Length == 0)
            {
                loadContext.Unload();
                return;
            }

            _loadedPlugins[pluginPath] = new LoadedPlugin(loadContext, providers);
            foreach (var provider in providers)
            {
                AddIfNotExists(provider);
            }
        }
        catch
        {
            // Ignore malformed or incompatible plugin assemblies.
        }
    }

    private static IEnumerable<IRadioStationProviderPlugin> CreateProviders(Assembly assembly)
    {
        var types = assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(IRadioStationProviderPlugin).IsAssignableFrom(t))
            .Where(t => t.GetConstructor(Type.EmptyTypes) is not null)
            .ToArray();

        foreach (var type in types)
        {
            if (Activator.CreateInstance(type) is IRadioStationProviderPlugin provider)
            {
                yield return provider;
            }
        }
    }

    private void AddIfNotExists(IRadioStationProviderPlugin provider)
    {
        if (_providers.Any(p => string.Equals(p.Id, provider.Id, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        _providers.Add(provider);
    }

    private sealed class LoadedPlugin
    {
        public LoadedPlugin(PluginLoadContext context, IReadOnlyList<IRadioStationProviderPlugin> providers)
        {
            Context = context;
            Providers = providers;
        }

        public PluginLoadContext Context { get; }

        public IReadOnlyList<IRadioStationProviderPlugin> Providers { get; }
    }
}

