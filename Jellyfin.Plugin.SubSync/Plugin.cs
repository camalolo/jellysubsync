using System.Text.RegularExpressions;
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

    private const string ScriptStartComment = "<!-- SubSync Client Script -->";
    private const string ScriptEndComment = "<!-- End SubSync Client Script -->";
    private const string ScriptTag = "<script src=\"/SubSync/ClientScript\"></script>";
    private const string InjectionBlock = ScriptStartComment + "\n" + ScriptTag + "\n" + ScriptEndComment;

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
        _logger.LogInformation("SubSync: WebPath is {WebPath}", applicationPaths.WebPath);
        InjectScript();
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

    /// <summary>
    /// Injects the SubSync client script into the Jellyfin web client's index.html.
    /// Idempotent — safe to call multiple times.
    /// </summary>
    private void InjectScript()
    {
        var indexPath = Path.Combine(ApplicationPaths.WebPath, "index.html");
        _logger.LogInformation("SubSync: Attempting to inject into {Path}", indexPath);

        if (!File.Exists(indexPath))
        {
            _logger.LogWarning("SubSync: Could not find index.html at {Path}. Client script not injected.", indexPath);
            return;
        }

        try
        {
            var content = File.ReadAllText(indexPath);

            // Already injected — nothing to do
            if (content.Contains(ScriptStartComment, StringComparison.Ordinal))
            {
                _logger.LogInformation("SubSync: Client script already injected in index.html.");
                return;
            }

            // Inject before </body>
            var closingBody = "</body>";
            if (content.Contains(closingBody, StringComparison.OrdinalIgnoreCase))
            {
                content = content.Replace(closingBody, InjectionBlock + "\n" + closingBody, StringComparison.OrdinalIgnoreCase);
                File.WriteAllText(indexPath, content);
                _logger.LogInformation("SubSync: Successfully injected client script into index.html.");
            }
            else
            {
                _logger.LogWarning("SubSync: Could not find </body> in index.html. Script not injected.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SubSync: Error injecting client script into index.html.");
        }
    }

    /// <inheritdoc />
    public override void OnUninstalling()
    {
        var indexPath = Path.Combine(ApplicationPaths.WebPath, "index.html");
        if (File.Exists(indexPath))
        {
            try
            {
                var content = File.ReadAllText(indexPath);
                var regex = new Regex(Regex.Escape(ScriptStartComment) + @"[\s\S]*?" + Regex.Escape(ScriptEndComment) + @"\s*", RegexOptions.Multiline);
                if (regex.IsMatch(content))
                {
                    content = regex.Replace(content, string.Empty);
                    File.WriteAllText(indexPath, content);
                }
            }
            catch
            {
                // Non-critical
            }
        }

        base.OnUninstalling();
    }
}
