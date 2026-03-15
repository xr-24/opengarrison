using System.Reflection;
using System.Runtime.Loader;
using GG2.Server.Plugins;

namespace GG2.Server;

internal static class PluginLoader
{
    public static IReadOnlyList<LoadedPlugin> LoadFromDirectory(
        string pluginsDirectory,
        Func<IGg2ServerPlugin, IGg2ServerPluginContext> contextFactory,
        Action<string> log)
    {
        Directory.CreateDirectory(pluginsDirectory);
        var assemblies = new List<Assembly>();
        foreach (var pluginPath in Directory.EnumerateFiles(pluginsDirectory, "*.dll", SearchOption.TopDirectoryOnly)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                assemblies.Add(AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(pluginPath)));
            }
            catch (Exception ex)
            {
                log($"[plugin] failed to load assembly \"{pluginPath}\": {ex.Message}");
            }
        }

        return LoadFromAssemblies(assemblies, contextFactory, log);
    }

    public static IReadOnlyList<LoadedPlugin> LoadFromAssemblies(
        IEnumerable<Assembly> assemblies,
        Func<IGg2ServerPlugin, IGg2ServerPluginContext> contextFactory,
        Action<string> log)
    {
        var loadedPlugins = new List<LoadedPlugin>();
        foreach (var assembly in assemblies)
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(type => type is not null).Cast<Type>().ToArray();
            }

            foreach (var type in types
                         .Where(type => typeof(IGg2ServerPlugin).IsAssignableFrom(type)
                             && type is { IsAbstract: false, IsInterface: false }))
            {
                try
                {
                    if (Activator.CreateInstance(type) is not IGg2ServerPlugin plugin)
                    {
                        continue;
                    }

                    var context = contextFactory(plugin);
                    plugin.Initialize(context);
                    loadedPlugins.Add(new LoadedPlugin(plugin, context));
                }
                catch (Exception ex)
                {
                    log($"[plugin] failed to initialize \"{type.FullName}\": {ex.Message}");
                }
            }
        }

        return loadedPlugins;
    }

    internal sealed record LoadedPlugin(IGg2ServerPlugin Plugin, IGg2ServerPluginContext Context);
}
