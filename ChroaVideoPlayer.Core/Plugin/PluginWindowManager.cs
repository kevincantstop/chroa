using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Avalonia.Controls;
using ChroaVideoPlayer.Core.Plugin.UI;

namespace ChroaVideoPlayer.Core.Plugin;

public sealed class PluginWindowManager
{
    private readonly Window _host;

    private readonly Dictionary<IChroaPlugin, PluginWindow> _windows = new();

    public PluginWindowManager(Window host) => _host = host;

    public bool IsOpen(IChroaPlugin plugin) => _windows.ContainsKey(plugin);

    public void Toggle(IChroaPlugin plugin)
    {
        if (_windows.TryGetValue(plugin, out var existing))
        {
            existing.Close();

            return;
        }

        PluginWindow window;

        try
        {
            window = plugin.CreateWindow();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Plugin {plugin.Name} failed to create its window: {ex.Message}");

            return;
        }

        window.Host = _host;
        window.Closed += (_, _) => _windows.Remove(plugin);

        _windows[plugin] = window;

        window.Show();
    }

    public void CloseAll()
    {
        foreach (var window in _windows.Values.ToArray()) window.Close();
    }
}
