using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace Traydio.Services.Implementations;

public sealed class PluginLoadContext : AssemblyLoadContext
{
    private readonly string _pluginDirectory;

    public PluginLoadContext(string pluginPath)
        : base($"plugin::{Path.GetFileNameWithoutExtension(pluginPath)}::{Guid.NewGuid():N}", isCollectible: true)
    {
        _pluginDirectory = Path.GetDirectoryName(pluginPath) ?? AppContext.BaseDirectory;
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        var candidate = Path.Combine(_pluginDirectory, assemblyName.Name + ".dll");
        if (File.Exists(candidate))
        {
            return LoadFromAssemblyPath(candidate);
        }

        return null;
    }
}

