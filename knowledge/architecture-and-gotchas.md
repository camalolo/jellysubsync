# Architecture & Gotchas

## Component Architecture

```
Plugin.cs                     — Entry point: BasePlugin<PluginConfiguration>, IHasWebPages
                                • Sets static Instance singleton on construction
                                • Injects <script> into Jellyfin's index.html on startup
                                • Removes injection on uninstall via OnUninstalling()

SubSyncServiceRegistrator.cs  — DI: IServerServiceRegistrator
                                • Registers SubSyncService as singleton in DI container

SubSyncController.cs          — REST API: ApiController, [Authorize], [Route("SubSync")]
                                • Serves client JS from embedded resource

SubSyncService.cs             — Core logic: ffsubsync/ffmpeg process execution, job tracking,
                                venv management, atomic file replacement with rollback

Web/configPage.html           — Dashboard config page (Jellyfin plugin page)
Web/subsync.js                — Client injection script (ES5 IIFE)
```

Source: project directory listing, `SubSyncServiceRegistrator.cs:14-17`, `Plugin.cs:14`

## Plugin GUID

The GUID `c7d8e9f0-a1b2-4c3d-e5f6-a7b8c9d0e1f2` must stay consistent across:
- `meta.json` (`guid` field)
- `build.yaml` (`guid` field)
- `Plugin.cs:16` (`_id` field)
- `configPage.html:113` (`PluginId` JS variable)

## Script Injection into index.html

Source: `Plugin.cs:81-120`

- On startup, `InjectScript()` reads Jellyfin's `{WebPath}/index.html`
- Injects a `<script src="/SubSync/ClientScript">` block before `</body>`
- Idempotent: checks for sentinel `<!-- SubSync Client Script -->` before injecting
- On uninstall, `OnUninstalling()` removes the injection block via regex
- If index.html doesn't exist or lacks `</body>`, logs a warning and skips

## Sync Job Execution Pipeline

Source: `SubSyncService.cs:469-648` (`RunSyncJob`)

1. **Prepare** (progress 0.0) — resolve ffsubsync binary, validate it exists
2. **Extract subtitle** (progress 0.05) — only for embedded: `ffmpeg -map 0:s:{index} -f srt`
3. **Run ffsubsync** (progress 0.1–0.75) — with real-time stderr parsing for progress
4. **Replace subtitle** — two paths:
   - **External `.srt`**: backup → copy synced over original (progress 0.85)
   - **Embedded**: ffmpeg remux: `-map 0 -map 1:0 -c copy` to add synced SRT (progress 0.75)
5. **Verify** (progress 0.95) — check file exists and non-empty
6. **Cleanup** — delete backup on success; rollback on failure
7. **Refresh library** — `UpdateItemAsync` with `ItemUpdateType.MetadataImport`

### Atomic File Replacement Pattern

Source: `SubSyncService.cs:654-741`

- **Backup first**: `File.Copy(original, original + ".bak.subsync", overwrite: false)`
- **Copy synced into place**: `File.Copy(synced, original, overwrite: true)`
- **Rollback on failure**: if backup exists after exception, `File.Copy(backup, original, overwrite: true)`
- **Rollback failure**: if rollback itself fails, backup `.bak.subsync` file is preserved for manual recovery
- **Temp dir cleanup**: always in `finally` block (line 635-647)

### Progress Parsing from ffsubsync stderr

Source: `SubSyncService.cs:757-807`

ffsubsync stderr is read line-by-line in real-time. Two patterns are detected:

| Pattern | Progress Range | Phase Label |
|---------|---------------|-------------|
| tqdm `NN%\|...` regex | 0.10–0.55 (linear map from 0-100%) | "Analyzing speech" |
| `"extracting speech segments from subtitle"` | 0.55 | "Extracting subtitle speech" |
| `"computing alignments"` | 0.60 | "Computing alignment" |
| `"got score" ... "for ratio"` | +0.01 per line, max 0.74 | "Computing alignment" |
| `"writing output"` | 0.75 | "Writing output" |

## Concurrency Model

Source: `SubSyncService.cs:124`

- Max **2 concurrent sync jobs** via `SemaphoreSlim(2, 2)` (`_concurrencyLimiter`)
- Semaphore is acquired in `StartSync()` at line 376 via `Wait(0)` (non-blocking)
- If semaphore is full, throws `InvalidOperationException` immediately — **no queueing**
- Semaphore release is manual in every early-return path (lines 384, 390, 397, 407, 416) and in `Task.Run`'s finally block (line 443)
- ⚠️ **Gotcha**: Any new early-return path that forgets `Release()` permanently reduces concurrency capacity

## Job Storage

Source: `SubSyncService.cs:118`

- Jobs are stored in-memory: `ConcurrentDictionary<string, SyncJob>`
- **Not persisted** across restarts
- Cleanup timer fires every 30 minutes (see [config-and-validation.md](config-and-validation.md) for eviction rules)

## Process Execution

Source: `SubSyncService.cs:903-1048`

Three process runner methods:

| Method | Purpose | Stderr Handling |
|--------|---------|----------------|
| `RunProcessAsync` | General-purpose (install, extract, remux) | Read all at end, log on non-zero exit |
| `RunProcessWithStderrCallbackAsync` | ffsubsync only | Line-by-line real-time via callback |
| `RunProcessCaptureAsync` | Status checks (python3 --version, etc.) | Returns `(exitCode, combinedStdout+Stderr)` |

All methods use `ProcessStartInfo` with `UseShellExecute=false`, `CreateNoWindow=true`, and support cancellation via `Kill(entireProcessTree: true)`.

## Missing IDisposable on SubSyncService

Source: `SubSyncService.cs:114-153`

`SubSyncService` holds `Timer _cleanupTimer` (line 127) and `SemaphoreSlim _concurrencyLimiter` (line 124), both `IDisposable`. The class does not implement `IDisposable`. These resources are never deterministically disposed.

## Test Project Gap

Source: `Properties/AssemblyInfo.cs:3`

```csharp
[assembly: InternalsVisibleTo("Jellyfin.Plugin.SubSync.Tests")]
```

The test project `Jellyfin.Plugin.SubSync.Tests` is declared as an internals-visible target but does not exist. The solution file contains only the main plugin project.

## Build Constraints

Source: `Jellyfin.Plugin.SubSync.csproj:5-6`, `Directory.Build.props`

- `TreatWarningsAsErrors=true` — all compiler warnings are build errors
- `GenerateDocumentationFile=true` — all public members require `///` XML doc comments
- `Nullable=enable` — enabled via `Directory.Build.props`, nullable reference types enforced
- `ImplicitUsings=enable` — enabled via `Directory.Build.props`, common `using`s auto-imported

## Installation Security

Source: `SubSyncService.cs:265-279`

- Install concurrency guarded by `Interlocked.CompareExchange` (`_installing` flag) — prevents double-install
- Path traversal check on venv path: rejects paths containing `..` (`SubSyncService.cs:277-279`)
- ⚠️ The path traversal check uses `Contains("..")` on the full path rather than checking if the resolved path is within the expected parent directory — this is a heuristic, not a robust path sandbox

See also:
- [api-reference.md](api-reference.md) for REST endpoint details
- [config-and-validation.md](config-and-validation.md) for configuration fields and validation rules
