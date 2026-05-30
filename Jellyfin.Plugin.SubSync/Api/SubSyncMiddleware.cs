using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

namespace Jellyfin.Plugin.SubSync.Api;

/// <summary>
/// Middleware that injects the SubSync client script into the Jellyfin index.html response.
/// This replaces the disk-based injection approach which fails when the web root is read-only.
/// </summary>
public class SubSyncMiddleware
{
    private const string ScriptStartComment = "<!-- SubSync Client Script -->";
    private const string ScriptEndComment = "<!-- End SubSync Client Script -->";
    private const string ScriptTag = "<script src=\"/SubSync/ClientScript\"></script>";
    private const string InjectionBlock = ScriptStartComment + "\n" + ScriptTag + "\n" + ScriptEndComment;

    private readonly RequestDelegate _next;
    private readonly ILogger<SubSyncMiddleware> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SubSyncMiddleware"/> class.
    /// </summary>
    /// <param name="next">The next middleware in the pipeline.</param>
    /// <param name="logger">The logger.</param>
    public SubSyncMiddleware(RequestDelegate next, ILogger<SubSyncMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    /// <summary>
    /// Handles the HTTP request, injecting the script tag into index.html responses.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        // Quick path check — only intercept index page requests
        var path = context.Request.Path.Value ?? string.Empty;
        var isIndexPage = path == "/"
            || path.Equals("/index.html", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/web/index.html", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/web/", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/web", StringComparison.OrdinalIgnoreCase);

        if (!isIndexPage)
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        // Disable response compression so we get raw HTML we can modify
        context.Request.Headers[HeaderNames.AcceptEncoding] = "identity";

        // Capture the response body
        var originalBodyStream = context.Response.Body;
        using var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        await _next(context).ConfigureAwait(false);

        // Only modify HTML responses
        var contentType = context.Response.ContentType ?? string.Empty;
        if (!contentType.Contains("text/html", StringComparison.OrdinalIgnoreCase))
        {
            responseBody.Position = 0;
            await responseBody.CopyToAsync(originalBodyStream).ConfigureAwait(false);
            return;
        }

        responseBody.Position = 0;
        using var reader = new StreamReader(responseBody, Encoding.UTF8, leaveOpen: true);
        var html = await reader.ReadToEndAsync().ConfigureAwait(false);

        // Already injected — pass through as-is
        if (html.Contains(ScriptStartComment, StringComparison.Ordinal))
        {
            responseBody.Position = 0;
            await responseBody.CopyToAsync(originalBodyStream).ConfigureAwait(false);
            return;
        }

        // Inject before </body>
        var closingBody = "</body>";
        if (html.Contains(closingBody, StringComparison.OrdinalIgnoreCase))
        {
            html = html.Replace(closingBody, InjectionBlock + "\n" + closingBody, StringComparison.OrdinalIgnoreCase);
            _logger.LogDebug("SubSync: Injected client script into index.html response");
        }

        // Write modified response
        context.Response.Headers.Remove(HeaderNames.ContentEncoding);
        var bytes = Encoding.UTF8.GetBytes(html);
        context.Response.ContentLength = bytes.Length;
        context.Response.Body = originalBodyStream;
        await context.Response.Body.WriteAsync(bytes).ConfigureAwait(false);
    }
}
