using Jellyfin.Plugin.SubSync.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SubSync;

/// <summary>
/// The SubSync plugin for Jellyfin — synchronizes subtitles with video audio using ffsubsync.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    private readonly Guid _id = new("c7d8e9f0-a1b2-4c3d-e5f6-a7b8c9d0e1f2");

    private readonly ILogger<Plugin> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">The application paths.</param>
    /// <param name="xmlSerializer">The XML serializer.</param>
    /// <param name="loggerFactory">Logger factory.</param>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer, ILoggerFactory loggerFactory)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
        _logger = loggerFactory.CreateLogger<Plugin>();
    }

    /// <inheritdoc />
    public override string Name => "SubSync";

    /// <inheritdoc />
    public override Guid Id => _id;

    /// <summary>
    /// Gets the current plugin instance.
    /// </summary>
    public static Plugin? Instance { get; private set; }

    /// <summary>
    /// Gets the temp working directory for SubSync operations.
    /// </summary>
    public string TempPath => Path.Join(ApplicationPaths.CachePath, "subsync");

    /// <summary>
    /// Gets the path to the managed Python virtualenv directory.
    /// </summary>
    public string VenvPath => Path.Join(ApplicationPaths.DataPath, "subsync", "venv");

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return new[]
        {
            new PluginPageInfo
            {
                Name = "SubSync",
                DisplayName = "SubSync Configuration",
                EmbeddedResourcePath = GetType().Namespace + ".Web.configPage.html",
                EnableInMainMenu = false,
                MenuSection = "server"
            }
        };
    }
}
