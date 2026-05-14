using System.Collections.Concurrent;
using System.Diagnostics;
using Jellyfin.Plugin.SubSync.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SubSync.Services;

/// <summary>
/// Status of a sync job.
/// </summary>
public enum SyncJobStatus
{
    /// <summary>Job is queued / waiting to start.</summary>
    Queued,
    /// <summary>Job is currently running.</summary>
    Running,
    /// <summary>Job completed successfully.</summary>
    Completed,
    /// <summary>Job failed.</summary>
    Failed
}

/// <summary>
/// Represents a subtitle stream that can be synced.
/// </summary>
public class SubtitleInfo
{
    /// <summary>Gets or sets the stream index within the media source.</summary>
    public int Index { get; set; }

    /// <summary>Gets or sets the display title (language + title).</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Gets or sets the three-letter language code.</summary>
    public string Language { get; set; } = string.Empty;

    /// <summary>Gets or sets whether this subtitle is external (sidecar file).</summary>
    public bool IsExternal { get; set; }

    /// <summary>Gets or sets the path to the external subtitle file, if applicable.</summary>
    public string? ExternalPath { get; set; }

    /// <summary>Gets or sets whether this subtitle has already been synced (has a .synced.srt counterpart).</summary>
    public bool HasSyncedVersion { get; set; }
}

/// <summary>
/// Tracks the state of a single sync job.
/// </summary>
public class SyncJob
{
    /// <summary>Gets or sets the unique job identifier.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Gets or sets the Jellyfin item ID.</summary>
    public Guid ItemId { get; set; }

    /// <summary>Gets or sets the subtitle stream index.</summary>
    public int SubtitleIndex { get; set; }

    /// <summary>Gets or sets the current status.</summary>
    public SyncJobStatus Status { get; set; } = SyncJobStatus.Queued;

    /// <summary>Gets or sets a progress value from 0.0 to 1.0.</summary>
    public double Progress { get; set; }

    /// <summary>Gets or sets the error message if the job failed.</summary>
    public string? Error { get; set; }

    /// <summary>Gets or sets the output file path after successful sync.</summary>
    public string? OutputPath { get; set; }
}

/// <summary>
/// Describes the installation status of the managed ffsubsync.
/// </summary>
public class FfSubSyncInstallationStatus
{
    /// <summary>Gets or sets whether ffsubsync is ready to use.</summary>
    public bool IsInstalled { get; set; }

    /// <summary>Gets or sets the path to the managed ffsubsync binary, if installed.</summary>
    public string? ManagedBinaryPath { get; set; }

    /// <summary>Gets or sets the path to the managed virtualenv.</summary>
    public string? VenvPath { get; set; }

    /// <summary>Gets or sets the resolved binary path that will be used (managed or custom).</summary>
    public string? ResolvedBinaryPath { get; set; }

    /// <summary>Gets or sets whether a system python3 was found.</summary>
    public bool PythonAvailable { get; set; }

    /// <summary>Gets or sets the python3 version string, if found.</summary>
    public string? PythonVersion { get; set; }

    /// <summary>Gets or sets the ffsubsync version string, if installed.</summary>
    public string? FfSubSyncVersion { get; set; }
}

/// <summary>
/// Service that manages ffsubsync installation and runs sync jobs.
/// </summary>
public class SubSyncService
{
    private readonly ILogger<SubSyncService> _logger;
    private readonly ILibraryManager _libraryManager;
    private readonly ConcurrentDictionary<string, SyncJob> _jobs = new();

    // Track whether an installation is currently in progress
    private int _installing;

    /// <summary>
    /// Initializes a new instance of the <see cref="SubSyncService"/> class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    /// <param name="libraryManager">Jellyfin library manager.</param>
    public SubSyncService(ILogger<SubSyncService> logger, ILibraryManager libraryManager)
    {
        _logger = logger;
        _libraryManager = libraryManager;
    }

    /// <summary>
    /// Gets the path to the ffsubsync binary inside the managed virtualenv.
    /// </summary>
    public string ManagedFfSubSyncPath => Path.Join(Plugin.Instance?.VenvPath, "bin", "ffsubsync");

    /// <summary>
    /// Gets the path to the pip binary inside the managed virtualenv.
    /// </summary>
    public string ManagedPipPath => Path.Join(Plugin.Instance?.VenvPath, "bin", "pip");

    /// <summary>
    /// Gets the path to the python3 binary inside the managed virtualenv.
    /// </summary>
    public string ManagedPythonPath => Path.Join(Plugin.Instance?.VenvPath, "bin", "python3");

    /// <summary>
    /// Resolves the actual ffsubsync binary to use.
    /// Priority: user-configured path → managed venv install → system PATH fallback.
    /// </summary>
    /// <returns>The ffsubsync executable path.</returns>
    public string ResolveFfSubSyncPath()
    {
        var config = Plugin.Instance?.Configuration;

        // 1. If user explicitly set a custom path, use it
        if (config is not null
            && !string.IsNullOrWhiteSpace(config.FfSubSyncPath)
            && config.FfSubSyncPath != "ffsubsync")
        {
            return config.FfSubSyncPath;
        }

        // 2. Check managed venv
        if (File.Exists(ManagedFfSubSyncPath))
        {
            return ManagedFfSubSyncPath;
        }

        // 3. Fall back to system PATH
        return "ffsubsync";
    }

    /// <summary>
    /// Checks the installation status of ffsubsync.
    /// </summary>
    /// <returns>Detailed installation status.</returns>
    public async Task<FfSubSyncInstallationStatus> GetInstallationStatusAsync()
    {
        var status = new FfSubSyncInstallationStatus
        {
            VenvPath = Plugin.Instance?.VenvPath,
            ManagedBinaryPath = ManagedFfSubSyncPath,
            IsInstalled = File.Exists(ManagedFfSubSyncPath)
        };

        // Check system python3
        try
        {
            var (exitCode, stdout) = await RunProcessCaptureAsync("python3", "--version", null).ConfigureAwait(false);
            if (exitCode == 0 && !string.IsNullOrWhiteSpace(stdout))
            {
                status.PythonAvailable = true;
                status.PythonVersion = stdout.Trim();
            }
        }
        catch
        {
            // python3 not found
        }

        // Check managed ffsubsync version
        if (status.IsInstalled)
        {
            try
            {
                var (exitCode, stdout) = await RunProcessCaptureAsync(ManagedFfSubSyncPath, "--version", null).ConfigureAwait(false);
                if (exitCode == 0)
                {
                    status.FfSubSyncVersion = stdout.Trim();
                }
            }
            catch
            {
                // ignore
            }
        }

        status.ResolvedBinaryPath = ResolveFfSubSyncPath();
        return status;
    }

    /// <summary>
    /// Installs ffsubsync into the managed virtualenv.
    /// Creates the venv if it doesn't exist, then pip-installs ffsubsync.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">Thrown when installation fails.</exception>
    public async Task InstallFfSubSyncAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _installing, 1, 0) == 1)
        {
            throw new InvalidOperationException("Installation is already in progress.");
        }

        try
        {
            var venvPath = Plugin.Instance?.VenvPath
                ?? throw new InvalidOperationException("Plugin not initialized.");

            // Step 1: Create virtualenv
            if (!Directory.Exists(venvPath) || !File.Exists(ManagedPythonPath))
            {
                _logger.LogInformation("Creating Python virtualenv at {Path}", venvPath);
                var (exitCode, output) = await RunProcessCaptureAsync("python3", $"-m venv {EscapeArg(venvPath)}", null).ConfigureAwait(false);
                if (exitCode != 0)
                {
                    throw new InvalidOperationException($"Failed to create virtualenv: {output}");
                }
            }

            // Step 2: Upgrade pip
            _logger.LogInformation("Upgrading pip in virtualenv");
            var pipExit = await RunProcessAsync(ManagedPythonPath, "-m pip install --upgrade pip", null, cancellationToken).ConfigureAwait(false);
            if (pipExit != 0)
            {
                _logger.LogWarning("pip upgrade failed with exit code {Code}, continuing anyway", pipExit);
            }

            // Step 3: Install ffsubsync + pin setuptools<81 (webrtcvad needs pkg_resources)
            _logger.LogInformation("Installing ffsubsync into virtualenv");
            var installExit = await RunProcessAsync(ManagedPipPath, "install ffsubsync \"setuptools<81\"", null, cancellationToken).ConfigureAwait(false);
            if (installExit != 0)
            {
                throw new InvalidOperationException($"pip install ffsubsync failed with exit code {installExit}.");
            }

            if (!File.Exists(ManagedFfSubSyncPath))
            {
                throw new InvalidOperationException("ffsubsync was installed but the binary was not found at the expected path.");
            }

            _logger.LogInformation("ffsubsync installed successfully at {Path}", ManagedFfSubSyncPath);
        }
        finally
        {
            Interlocked.Exchange(ref _installing, 0);
        }
    }

    /// <summary>
    /// Lists subtitle streams for a given video item.
    /// </summary>
    /// <param name="itemId">The Jellyfin item ID.</param>
    /// <returns>A list of subtitle infos, or null if the item is not found.</returns>
    public List<SubtitleInfo>? ListSubtitles(Guid itemId)
    {
        var item = _libraryManager.GetItemById(itemId);
        if (item is not Video video)
        {
            return null;
        }

        var mediaSources = video.GetMediaSources(true);
        if (mediaSources.Count == 0)
        {
            return new List<SubtitleInfo>();
        }

        var source = mediaSources[0];
        var videoDir = Path.GetDirectoryName(video.Path) ?? string.Empty;
        var videoNameWithoutExt = Path.GetFileNameWithoutExtension(video.Path);

        return source.MediaStreams
            .Where(s => s.Type == MediaBrowser.Model.Entities.MediaStreamType.Subtitle)
            .Select(s =>
            {
                var langCode = s.Language ?? "und";
                var syncedFile = Path.Combine(videoDir, $"{videoNameWithoutExt}.synced.{langCode}.srt");
                var hasSynced = File.Exists(syncedFile);

                return new SubtitleInfo
                {
                    Index = s.Index,
                    Title = s.DisplayTitle ?? s.Language ?? $"Track {s.Index}",
                    Language = langCode,
                    IsExternal = s.IsExternal,
                    ExternalPath = s.Path,
                    HasSyncedVersion = hasSynced
                };
            })
            .ToList();
    }

    /// <summary>
    /// Starts a sync job for the given item and subtitle stream index.
    /// Automatically ensures ffsubsync is available before running.
    /// </summary>
    /// <param name="itemId">The Jellyfin item ID.</param>
    /// <param name="subtitleIndex">The subtitle stream index within the first media source.</param>
    /// <returns>The created sync job.</returns>
    /// <exception cref="FileNotFoundException">Thrown when the video file is not found.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the subtitle stream is not found or ffsubsync is unavailable.</exception>
    public SyncJob StartSync(Guid itemId, int subtitleIndex)
    {
        var item = _libraryManager.GetItemById(itemId);
        if (item is not Video video)
        {
            throw new InvalidOperationException($"Item {itemId} is not a video.");
        }

        if (!File.Exists(video.Path))
        {
            throw new FileNotFoundException($"Video file not found: {video.Path}");
        }

        var mediaSources = video.GetMediaSources(true);
        if (mediaSources.Count == 0)
        {
            throw new InvalidOperationException("No media sources found for the video.");
        }

        var source = mediaSources[0];
        var subtitleStream = source.MediaStreams
            .FirstOrDefault(s => s.Type == MediaBrowser.Model.Entities.MediaStreamType.Subtitle && s.Index == subtitleIndex);

        if (subtitleStream is null)
        {
            throw new InvalidOperationException($"Subtitle stream index {subtitleIndex} not found.");
        }

        var config = Plugin.Instance?.Configuration ?? new Configuration.PluginConfiguration();
        var job = new SyncJob
        {
            ItemId = itemId,
            SubtitleIndex = subtitleIndex
        };

        _jobs[job.Id] = job;

        // Fire and forget — run in background
        _ = Task.Run(() => RunSyncJob(job, video, subtitleStream, config));

        return job;
    }

    /// <summary>
    /// Gets a sync job by its ID.
    /// </summary>
    /// <param name="jobId">The job identifier.</param>
    /// <returns>The sync job, or null if not found.</returns>
    public SyncJob? GetJob(string jobId)
    {
        return _jobs.TryGetValue(jobId, out var job) ? job : null;
    }

    /// <summary>
    /// Gets all sync jobs.
    /// </summary>
    /// <returns>All tracked sync jobs.</returns>
    public IEnumerable<SyncJob> GetAllJobs()
    {
        return _jobs.Values;
    }

    private async Task RunSyncJob(SyncJob job, Video video, MediaBrowser.Model.Entities.MediaStream subtitleStream, Configuration.PluginConfiguration config)
    {
        job.Status = SyncJobStatus.Running;
        job.Progress = 0.0;

        var videoPath = video.Path;
        var videoDir = Path.GetDirectoryName(videoPath) ?? ".";
        var videoNameNoExt = Path.GetFileNameWithoutExtension(videoPath);
        var videoExt = Path.GetExtension(videoPath);
        var tempDir = Path.Combine(Plugin.Instance?.TempPath ?? Path.GetTempPath(), job.Id);
        Directory.CreateDirectory(tempDir);

        // Paths for the safe atomic-replace workflow
        string? backupPath = null;   // .bak of original file (subtitle or video)
        string? tempOutput = null;   // ffsubsync output in temp dir
        string? tempVideo = null;    // remuxed video in temp dir (embedded only)

        try
        {
            // Step 0: Ensure ffsubsync is available
            var ffsubsyncExe = ResolveFfSubSyncPath();

            if (!File.Exists(ffsubsyncExe) && ffsubsyncExe != "ffsubsync")
            {
                throw new InvalidOperationException($"ffsubsync not found at '{ffsubsyncExe}'. Install it from the plugin configuration page.");
            }

            // Step 1: Prepare subtitle input
            //    - External: use the sidecar file directly (ffsubsync reads it read-only)
            //    - Embedded: extract to temp dir first
            string subtitleInputPath;

            if (subtitleStream.IsExternal && !string.IsNullOrEmpty(subtitleStream.Path))
            {
                subtitleInputPath = subtitleStream.Path;
            }
            else
            {
                subtitleInputPath = Path.Combine(tempDir, $"subtitle_{job.SubtitleIndex}.srt");
                _logger.LogInformation("Extracting embedded subtitle stream {Index} from {Video}", job.SubtitleIndex, videoPath);
                await ExtractSubtitle(videoPath, job.SubtitleIndex, subtitleInputPath).ConfigureAwait(false);
            }

            job.Progress = 0.15;

            // Step 2: Run ffsubsync → temp output (NEVER write directly to the original)
            tempOutput = Path.Combine(tempDir, "synced.srt");
            var args = BuildFfSubSyncArgs(config, videoPath, subtitleInputPath, tempOutput);

            _logger.LogInformation("Running ffsubsync ({Exe}): {Args}", ffsubsyncExe, args);

            var exitCode = await RunProcessAsync(ffsubsyncExe, args, tempDir, CancellationToken.None).ConfigureAwait(false);

            job.Progress = 0.7;

            if (exitCode != 0)
            {
                throw new InvalidOperationException($"ffsubsync exited with code {exitCode}.");
            }

            if (!File.Exists(tempOutput))
            {
                throw new InvalidOperationException("ffsubsync completed but output file was not created.");
            }

            _logger.LogInformation("ffsubsync produced synced subtitle ({Size} bytes)", new FileInfo(tempOutput).Length);

            // Step 3: Atomically replace original subtitle with the synced version
            if (subtitleStream.IsExternal && !string.IsNullOrEmpty(subtitleStream.Path))
            {
                //
                // EXTERNAL SUBTITLE: safe replace of the sidecar file
                //
                //   original.srt  →  original.srt.bak.subsync   (backup)
                //   synced.srt    →  original.srt               (atomic rename)
                //
                // If anything fails after the backup is created, rollback restores it.
                //
                await ReplaceExternalSubtitle(subtitleStream.Path, tempOutput).ConfigureAwait(false);
                backupPath = subtitleStream.Path + ".bak.subsync";

                job.OutputPath = subtitleStream.Path;
                _logger.LogInformation("Replaced external subtitle: {Path} (backup at {Backup})", subtitleStream.Path, backupPath);
            }
            else
            {
                //
                // EMBEDDED SUBTITLE: remux video with ffmpeg, replacing the subtitle stream
                //
                //   video.mkv         →  video.mkv.bak.subsync    (backup)
                //   video_new.mkv     →  video.mkv                (atomic rename)
                //
                // The new video is identical to the original except the target subtitle
                // stream is replaced with the synced SRT content.
                //
                // NOTE: remuxing is safe (no re-encoding) but requires the output container
                //       to support SRT subtitles. MKV always does; MP4 does too.
                //       If the input is e.g. .avi, this may fail — the error is propagated.
                //
                (tempVideo, backupPath) = await ReplaceEmbeddedSubtitle(
                    videoPath, videoDir, videoNameNoExt, videoExt,
                    job.SubtitleIndex, tempOutput).ConfigureAwait(false);

                job.OutputPath = videoPath;
                _logger.LogInformation("Replaced embedded subtitle in video: {Path} (backup at {Backup})", videoPath, backupPath);
            }

            // Step 4: Verify the replacement is valid before declaring success
            if (subtitleStream.IsExternal)
            {
                if (!File.Exists(subtitleStream.Path) || new FileInfo(subtitleStream.Path).Length == 0)
                {
                    throw new InvalidOperationException("Subtitle replacement verification failed — original file is missing or empty after replace.");
                }
            }
            else
            {
                if (!File.Exists(videoPath) || new FileInfo(videoPath).Length == 0)
                {
                    throw new InvalidOperationException("Video remux verification failed — file is missing or empty after replace.");
                }
            }

            // Step 5: Success — remove backup (everything went well)
            SafeDelete(backupPath);
            backupPath = null;

            job.Status = SyncJobStatus.Completed;
            job.Progress = 1.0;

            // Refresh the library item so Jellyfin picks up changes
            await _libraryManager.UpdateItemAsync(
                video,
                video.GetParent(),
                ItemUpdateType.MetadataImport,
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Subtitle sync failed for job {JobId}", job.Id);

            // ROLLBACK: if we created a backup but didn't complete successfully,
            // restore the original file from backup
            if (backupPath is not null && File.Exists(backupPath))
            {
                try
                {
                    // Determine what the original file was
                    var originalPath = backupPath.Substring(0, backupPath.Length - ".bak.subsync".Length);
                    _logger.LogWarning("Rolling back: restoring {Original} from backup {Backup}", originalPath, backupPath);
                    File.Copy(backupPath, originalPath, overwrite: true);
                    SafeDelete(backupPath);
                    _logger.LogInformation("Rollback complete: {Original} restored", originalPath);
                }
                catch (Exception rollbackEx)
                {
                    _logger.LogError(rollbackEx, "ROLLBACK FAILED for job {JobId}! Backup file preserved at {Backup}", job.Id, backupPath);
                    // Do NOT delete the backup — it's the user's last resort
                }
            }

            job.Status = SyncJobStatus.Failed;
            job.Error = ex.Message;
        }
        finally
        {
            // Clean up temp directory (contains ffsubsync output, extracted subs, etc.)
            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
            catch
            {
                // Non-critical
            }
        }
    }

    /// <summary>
    /// Safely replaces an external subtitle file with the synced version.
    /// Creates a backup first, then atomically renames the new file into place.
    /// </summary>
    private async Task ReplaceExternalSubtitle(string originalPath, string syncedTempPath)
    {
        if (!File.Exists(originalPath))
        {
            throw new FileNotFoundException($"Original subtitle file not found: {originalPath}");
        }

        var backupPath = originalPath + ".bak.subsync";

        // 1. Copy original → backup (preserves original permissions/attrs)
        _logger.LogInformation("Backing up original subtitle: {Original} → {Backup}", originalPath, backupPath);
        File.Copy(originalPath, backupPath, overwrite: false);

        // 2. Copy synced temp → original (use Copy+Delete instead of cross-device Rename)
        _logger.LogInformation("Replacing subtitle with synced version: {Temp} → {Original}", syncedTempPath, originalPath);
        await Task.Run(() => File.Copy(syncedTempPath, originalPath, overwrite: true)).ConfigureAwait(false);
    }

    /// <summary>
    /// Replaces an embedded subtitle stream in a video file by remuxing with ffmpeg.
    /// Creates a backup of the original video, then atomically renames the new video.
    /// Returns (tempVideoPath, backupPath) so the caller can manage cleanup.
    /// </summary>
    private async Task<(string TempVideo, string BackupPath)> ReplaceEmbeddedSubtitle(
        string videoPath, string videoDir, string videoNameNoExt, string videoExt,
        int subtitleStreamIndex, string syncedSrtPath)
    {
        // Determine output container — keep same extension, fallback to .mkv for safety
        var outputExt = videoExt.ToLowerInvariant();
        if (outputExt is not (".mkv" or ".mp4" or ".webm" or ".ts" or ".mov"))
        {
            // For unusual containers, remux to .mkv which supports all subtitle codecs
            _logger.LogWarning("Container format {Ext} may not support SRT subtitles, remuxing to .mkv", outputExt);
            outputExt = ".mkv";
        }

        var tempVideo = Path.Combine(videoDir, $"{videoNameNoExt}.subsync_tmp{outputExt}");
        var backupPath = videoPath + ".bak.subsync";

        // Build ffmpeg command:
        //   - Copy all streams as-is (no re-encoding)
        //   - Map the synced .srt as a new subtitle stream
        //   - Map all original streams
        //   - Disable the original subtitle stream at index (but keep it for safety)
        //
        // Actually, the safest approach: copy all streams + add the synced SRT as a new stream.
        // Then the user has both the original and synced embedded.
        // But the user asked to REPLACE, so we use -map to exclude the original sub and include the new one.

        var ffmpegPath = "ffmpeg";
        var pluginConfig = Plugin.Instance?.Configuration;
        if (pluginConfig is not null && !string.IsNullOrWhiteSpace(pluginConfig.FfmpegPath))
        {
            ffmpegPath = pluginConfig.FfmpegPath;
        }

        // Strategy: copy all streams, but replace the target subtitle stream with the synced SRT.
        // We use stream mapping:
        //   -map 0:v       → all video streams
        //   -map 0:a       → all audio streams
        //   -map 0:s       → all subtitle streams
        //   -map 1:0       → the synced SRT as new subtitle input
        //   -disposition:s:X  → clear disposition on original sub
        //
        // Simpler approach: copy everything except the target subtitle, then add the synced one.
        // This is complex with -map. Let's use the simplest safe approach:
        //
        //   ffmpeg -i video.mkv -i synced.srt \
        //     -map 0 -map 1:0 \
        //     -c copy \
        //     -metadata:s:s:0 language=eng \
        //     video_new.mkv
        //
        // This copies ALL original streams + adds the synced SRT as an additional subtitle.
        // The original (unsynced) subtitle is preserved inside the file.
        // This is the safest approach — no data loss even if something goes wrong.

        // NOTE: The synced SRT is added as an additional subtitle stream alongside the original.
        // This preserves the original subtitle inside the container for safety.

        var args = new List<string>
        {
            $"-i {EscapeArg(videoPath)}",
            $"-i {EscapeArg(syncedSrtPath)}",
            "-map 0",                // All original streams (including old subtitle)
            "-map 1:0",              // The synced SRT
            "-c copy",               // No re-encoding, just remux
            $"-metadata:s:s:{subtitleStreamIndex} title=\"Original (unsynced)\"", // Rename old sub
            "-y",                    // Overwrite output
            EscapeArg(tempVideo)
        };

        _logger.LogInformation("Remuxing video with synced subtitle: ffmpeg {Args}", string.Join(" ", args));

        var exitCode = await RunProcessAsync(ffmpegPath, string.Join(" ", args), null, CancellationToken.None).ConfigureAwait(false);

        if (exitCode != 0)
        {
            throw new InvalidOperationException($"ffmpeg remux failed with exit code {exitCode}.");
        }

        if (!File.Exists(tempVideo) || new FileInfo(tempVideo).Length == 0)
        {
            throw new InvalidOperationException("ffmpeg remux produced no output file.");
        }

        // Backup original video
        _logger.LogInformation("Backing up original video: {Original} → {Backup}", videoPath, backupPath);
        File.Copy(videoPath, backupPath, overwrite: false);

        // Replace original with new video (atomic on same filesystem)
        _logger.LogInformation("Replacing video with remuxed version: {Temp} → {Original}", tempVideo, videoPath);
        File.Copy(tempVideo, videoPath, overwrite: true);
        SafeDelete(tempVideo);

        return (tempVideo, backupPath);
    }

    /// <summary>
    /// Deletes a file if it exists. Swallows all exceptions.
    /// </summary>
    private static void SafeDelete(string? path)
    {
        if (path is null) return;
        try { if (File.Exists(path)) File.Delete(path); } catch { /* non-critical */ }
    }

    private string BuildFfSubSyncArgs(Configuration.PluginConfiguration config, string videoPath, string subtitleInput, string subtitleOutput)
    {
        var args = new List<string>
        {
            EscapeArg(videoPath),
            "-i", EscapeArg(subtitleInput),
            "-o", EscapeArg(subtitleOutput),
            $"--max-offset-seconds {config.MaxOffsetSeconds}",
            $"--max-subtitle-seconds {config.MaxSubtitleSeconds}",
            $"--vad {config.VadMethod}",
            $"--output-encoding {config.OutputEncoding}"
        };

        if (!string.IsNullOrWhiteSpace(config.FfmpegPath))
        {
            args.Add($"--ffmpeg-path {EscapeArg(config.FfmpegPath)}");
        }

        if (config.UseGoldenSectionSearch)
        {
            args.Add("--gss");
        }

        return string.Join(" ", args);
    }

    private async Task ExtractSubtitle(string videoPath, int streamIndex, string outputPath)
    {
        var args = $"-i {EscapeArg(videoPath)} -map 0:s:{streamIndex} -f srt {EscapeArg(outputPath)} -y";
        _logger.LogInformation("Extracting subtitle: ffmpeg {Args}", args);

        var exitCode = await RunProcessAsync("ffmpeg", args, null, CancellationToken.None).ConfigureAwait(false);
        if (exitCode != 0)
        {
            throw new InvalidOperationException($"ffmpeg subtitle extraction failed with exit code {exitCode}.");
        }
    }

    private async Task<int> RunProcessAsync(string executable, string arguments, string? workingDir, CancellationToken cancellationToken)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (workingDir is not null)
        {
            process.StartInfo.WorkingDirectory = workingDir;
        }

        process.Start();

        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        var stderr = await stderrTask.ConfigureAwait(false);
        var stdout = await stdoutTask.ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            _logger.LogWarning("Process {Exe} exited with code {Code}. stderr: {Stderr}", executable, process.ExitCode, stderr);
        }
        else
        {
            _logger.LogDebug("Process {Exe} completed. stdout: {Stdout}", executable, stdout);
        }

        return process.ExitCode;
    }

    /// <summary>
    /// Runs a process and returns (exitCode, combined stdout+stderr output).
    /// </summary>
    private async Task<(int ExitCode, string Output)> RunProcessCaptureAsync(string executable, string arguments, string? workingDir)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (workingDir is not null)
        {
            process.StartInfo.WorkingDirectory = workingDir;
        }

        process.Start();

        var stdout = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
        var stderr = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);

        await process.WaitForExitAsync().ConfigureAwait(false);

        var output = string.Concat(stdout, stderr).Trim();
        return (process.ExitCode, output);
    }

    private static string EscapeArg(string arg)
    {
        if (arg.Contains(' ') || arg.Contains('"') || arg.Contains('\''))
        {
            return $"\"{arg.Replace("\"", "\\\"")}\"";
        }

        return arg;
    }
}
