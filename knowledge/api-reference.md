# API Reference

All endpoints are under the `SubSync` route prefix, require `[Authorize]`, and live in
`SubSyncController.cs` (`Jellyfin.Plugin.SubSync/Api/SubSyncController.cs`).

## Endpoints

| Method | Path | Handler | Request | Response | Notes |
|--------|------|---------|---------|----------|-------|
| GET | `/SubSync/Subtitles/{itemId}` | `GetSubtitles` | `itemId: Guid` (route) | `List<SubtitleInfo>` | 404 if item not found or not video |
| POST | `/SubSync/Sync` | `SyncSubtitle` | `SyncRequest` (JSON body) | `SyncJob` | 404 for FileNotFoundException, 400 for InvalidOperationException |
| GET | `/SubSync/Jobs/{jobId}` | `GetJobStatus` | `jobId: string` (route) | `SyncJob` | 404 if job not found |
| GET | `/SubSync/Jobs` | `GetAllJobs` | — | `IEnumerable<SyncJob>` | Returns all tracked jobs |
| GET | `/SubSync/InstallationStatus` | `GetInstallationStatus` | — | `FfSubSyncInstallationStatus` | Checks python3, managed venv, ffsubsync version |
| POST | `/SubSync/Install` | `InstallFfSubSync` | — | `{ message: string }` | 503 if install already in progress, 400 on failure |
| GET | `/SubSync/ClientScript` | `GetClientScript` | — | `application/javascript` | Serves embedded `subsync.js` |

## Request/Response Types

### SyncRequest

| Field | Type | Constraints |
|-------|------|-------------|
| `ItemId` | `Guid` | Required |
| `SubtitleIndex` | `int` | `[Range(0, 999)]` |

Source: `SubSyncController.cs:175-183`

### SubtitleInfo

| Field | Type | Notes |
|-------|------|-------|
| `Index` | `int` | Stream index within media source |
| `Title` | `string` | Display title (language + title) |
| `Language` | `string` | Three-letter code |
| `IsExternal` | `bool` | Whether sidecar file |
| `ExternalPath` | `string?` | `[JsonIgnore]` — not in API responses |
| `HasSyncedVersion` | `bool` | Whether a completed sync exists for this stream |

Source: `SubSyncService.cs:29-49`

### SyncJob

| Field | Type | Default | Notes |
|-------|------|---------|-------|
| `Id` | `string` | `Guid.NewGuid().ToString("N")` | 32-char hex, no hyphens |
| `ItemId` | `Guid` | — | Jellyfin item ID |
| `SubtitleIndex` | `int` | — | Stream index |
| `Status` | `SyncJobStatus` | `Queued` | Queued/Running/Completed/Failed |
| `Progress` | `double` | `0.0` | 0.0 to 1.0 |
| `Phase` | `string` | `"Preparing"` | Human-readable phase label |
| `Error` | `string?` | `null` | Error message if Failed |
| `OutputPath` | `string?` | `null` | Output file path after success |

Source: `SubSyncService.cs:54-82`

### FfSubSyncInstallationStatus

| Field | Type | Notes |
|-------|------|-------|
| `IsInstalled` | `bool` | Managed ffsubsync binary exists |
| `ManagedBinaryPath` | `string?` | Path to venv ffsubsync |
| `VenvPath` | `string?` | Path to managed virtualenv |
| `ResolvedBinaryPath` | `string?` | Actual binary that will be used |
| `PythonAvailable` | `bool` | System python3 found |
| `PythonVersion` | `string?` | Python version string |
| `FfSubSyncVersion` | `string?` | ffsubsync version string |

Source: `SubSyncService.cs:87-109`

### SyncJobStatus Enum

| Value | Meaning |
|-------|---------|
| `Queued` | Waiting to start |
| `Running` | Currently executing |
| `Completed` | Finished successfully |
| `Failed` | Errored out |

Source: `SubSyncService.cs:14-24`

## Client-Side Integration

The client JS (`subsync.js`) is an ES5 IIFE that:
- Hooks `document` click events looking for `button.btnMoreCommands` (the "More" button)
- Polls for `.actionSheetContent` with up to 20 retries at 100ms intervals
- Injects a "Sync Subtitles" menu item with `data-id="subsync"`
- Shows a modal dialog for subtitle selection and sync progress
- Polls `GET /SubSync/Jobs/{jobId}` every 2000ms for progress updates
- Authenticates all requests via `X-Emby-Token` header from `ApiClient.accessToken()`
