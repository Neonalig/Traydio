using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace Traydio.Services.Implementations;

public sealed class PluginLoadContext(string pluginPath) : AssemblyLoadContext($"plugin::{Path.GetFileNameWithoutExtension(pluginPath)}::{Guid.NewGuid():N}", isCollectible: true)
{
    private readonly string _pluginDirectory = Path.GetDirectoryName(pluginPath) ?? AppContext.BaseDirectory;

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

