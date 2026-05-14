using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.SubSync.Configuration;

/// <summary>
/// Plugin configuration for SubSync.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets the path to the ffsubsync executable.
    /// </summary>
    public string FfSubSyncPath { get; set; } = "ffsubsync";

    /// <summary>
    /// Gets or sets the path to the ffmpeg executable (passed via --ffmpeg-path).
    /// Leave empty to use system PATH.
    /// </summary>
    public string FfmpegPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the voice activity detector to use.
    /// </summary>
    public string VadMethod { get; set; } = "subs_then_webrtc";

    /// <summary>
    /// Gets or sets the maximum allowed offset in seconds for any subtitle segment.
    /// </summary>
    public int MaxOffsetSeconds { get; set; } = 60;

    /// <summary>
    /// Gets or sets the output encoding for synced subtitles.
    /// </summary>
    public string OutputEncoding { get; set; } = "utf-8";

    /// <summary>
    /// Gets or sets the maximum duration in seconds for a subtitle to appear on-screen.
    /// </summary>
    public double MaxSubtitleSeconds { get; set; } = 10.0;

    /// <summary>
    /// Gets or sets whether to use golden-section search for optimal framerate ratio.
    /// </summary>
    public bool UseGoldenSectionSearch { get; set; } = false;

    /// <summary>
    /// Gets or sets whether to overwrite existing external subtitles when a sync output
    /// would collide with an existing file.
    /// </summary>
    public bool OverwriteExisting { get; set; } = true;
}
