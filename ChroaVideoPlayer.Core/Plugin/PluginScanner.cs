using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;

namespace ChroaVideoPlayer.Core.Plugin;

public static class PluginScanner
{
    private const string PluginFolderName = "Plugins";

    public static IReadOnlyList<IChroaPlugin> Load(string? directory = null)
    {
        var folder = directory ?? Path.Combine(AppContext.BaseDirectory, PluginFolderName);
        var assemblies = new List<Assembly>();

        var entry = Assembly.GetEntryAssembly();
        if (entry is not null) assemblies.Add(entry);

        if (Directory.Exists(folder))
        {
            foreach (var file in Directory.EnumerateFiles(folder, "*.dll").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    assemblies.Add(Assembly.LoadFrom(file));
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to load plugin assembly {file}: {ex.Message}");
                }
            }
        }

        return assemblies.SelectMany(CreatePlugins).ToList();
    }

    private static IEnumerable<IChroaPlugin> CreatePlugins(Assembly assembly)
    {
        Type[] types;

        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            types = ex.Types.Where(t => t is not null).Cast<Type>().ToArray();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to enumerate types in {assembly.GetName().Name}: {ex.Message}");

            return Array.Empty<IChroaPlugin>();
        }

        var plugins = new List<IChroaPlugin>();

        foreach (var type in types)
        {
            if (type.IsAbstract || type.IsInterface) continue;
            if (!typeof(IChroaPlugin).IsAssignableFrom(type)) continue;
            if (type.GetConstructor(Type.EmptyTypes) is null) continue;

            try
            {
                if (Activator.CreateInstance(type) is IChroaPlugin plugin) plugins.Add(plugin);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to create plugin {type.FullName}: {ex.Message}");
            }
        }

        return plugins;
    }
}
