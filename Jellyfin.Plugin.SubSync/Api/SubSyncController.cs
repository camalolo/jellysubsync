using Jellyfin.Plugin.SubSync.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.SubSync.Api;

/// <summary>
/// API controller for the SubSync plugin.
/// </summary>
[ApiController]
[Route("SubSync")]
public class SubSyncController : ControllerBase
{
    private readonly SubSyncService _syncService;

    /// <summary>
    /// Initializes a new instance of the <see cref="SubSyncController"/> class.
    /// </summary>
    /// <param name="syncService">The SubSync service.</param>
    public SubSyncController(SubSyncService syncService)
    {
        _syncService = syncService;
    }

    /// <summary>
    /// Lists subtitle tracks for a given video item.
    /// </summary>
    /// <param name="itemId">The Jellyfin item ID.</param>
    /// <returns>List of available subtitle tracks.</returns>
    [HttpGet("Subtitles/{itemId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<List<SubtitleInfo>> GetSubtitles(Guid itemId)
    {
        var subtitles = _syncService.ListSubtitles(itemId);
        if (subtitles is null)
        {
            return NotFound("Item not found or is not a video.");
        }

        return Ok(subtitles);
    }

    /// <summary>
    /// Starts a subtitle sync job.
    /// </summary>
    /// <param name="request">The sync request containing item ID and subtitle index.</param>
    /// <returns>The created sync job.</returns>
    [HttpPost("Sync")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<SyncJob> SyncSubtitle([FromBody] SyncRequest request)
    {
        try
        {
            var job = _syncService.StartSync(request.ItemId, request.SubtitleIndex);
            return Ok(job);
        }
        catch (FileNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Gets the status of a sync job.
    /// </summary>
    /// <param name="jobId">The sync job ID.</param>
    /// <returns>The sync job status.</returns>
    [HttpGet("Jobs/{jobId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<SyncJob> GetJobStatus(string jobId)
    {
        var job = _syncService.GetJob(jobId);
        if (job is null)
        {
            return NotFound("Job not found.");
        }

        return Ok(job);
    }

    /// <summary>
    /// Lists all sync jobs.
    /// </summary>
    /// <returns>All sync jobs.</returns>
    [HttpGet("Jobs")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<IEnumerable<SyncJob>> GetAllJobs()
    {
        return Ok(_syncService.GetAllJobs());
    }

    /// <summary>
    /// Gets the ffsubsync installation status.
    /// </summary>
    /// <returns>Detailed installation status.</returns>
    [HttpGet("InstallationStatus")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<FfSubSyncInstallationStatus>> GetInstallationStatus()
    {
        var status = await _syncService.GetInstallationStatusAsync().ConfigureAwait(false);
        return Ok(status);
    }

    /// <summary>
    /// Installs ffsubsync into the managed virtualenv.
    /// </summary>
    /// <returns>Installation result.</returns>
    [HttpPost("Install")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> InstallFfSubSync()
    {
        try
        {
            await _syncService.InstallFfSubSyncAsync(HttpContext.RequestAborted).ConfigureAwait(false);
            return Ok(new { message = "ffsubsync installed successfully." });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already in progress"))
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, ex.Message);
        }
        catch (Exception ex)
        {
            return BadRequest($"Installation failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Serves the client-side injection script that adds "Sync Subtitles" to video pages.
    /// </summary>
    /// <returns>The JavaScript content.</returns>
    [HttpGet("ClientScript")]
    [Produces("application/javascript")]
    public IActionResult GetClientScript()
    {
        var js = GetEmbeddedResource("Jellyfin.Plugin.SubSync.Web.subsync.js");
        if (js is null)
        {
            return NotFound();
        }

        return Content(js, "application/javascript");
    }

    private string? GetEmbeddedResource(string resourceName)
    {
        var assembly = typeof(SubSyncController).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return null;
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

/// <summary>
/// Request body for starting a subtitle sync.
/// </summary>
public class SyncRequest
{
    /// <summary>Gets or sets the Jellyfin item ID.</summary>
    public Guid ItemId { get; set; }

    /// <summary>Gets or sets the subtitle stream index.</summary>
    public int SubtitleIndex { get; set; }
}
