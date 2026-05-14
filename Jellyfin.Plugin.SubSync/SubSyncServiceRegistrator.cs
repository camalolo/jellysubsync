using Jellyfin.Plugin.SubSync.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.SubSync;

/// <summary>
/// Registers SubSync services with the Jellyfin DI container.
/// </summary>
public class SubSyncServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<SubSyncService>();
    }
}
