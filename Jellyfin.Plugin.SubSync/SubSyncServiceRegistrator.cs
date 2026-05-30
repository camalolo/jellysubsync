using Jellyfin.Plugin.SubSync.Api;
using Jellyfin.Plugin.SubSync.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
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
        serviceCollection.AddSingleton<IStartupFilter, SubSyncStartupFilter>();
    }
}

/// <summary>
/// Registers the SubSync middleware in the ASP.NET pipeline.
/// </summary>
public class SubSyncStartupFilter : IStartupFilter
{
    /// <inheritdoc />
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            app.UseMiddleware<SubSyncMiddleware>();
            next(app);
        };
    }
}
