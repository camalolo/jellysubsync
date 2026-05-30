# AGENTS.md

## Overview

Jellyfin plugin (C# / .NET 9) that adds "Sync Subtitles" to video detail pages. Wraps [ffsubsync](https://github.com/smacke/ffsubsync) as the sync backend, managed in an isolated Python virtualenv. No unit test project exists yet.

Detailed project knowledge is in knowledge/ — consult it for architecture, APIs, data models, and session discoveries.

## Build & Run

```bash
dotnet build -c Release
# Output: Jellyfin.Plugin.SubSync/bin/Release/net9.0/Jellyfin.Plugin.SubSync.dll
```

There is no test runner. `AssemblyInfo.cs` declares `InternalsVisibleTo("Jellyfin.Plugin.SubSync.Tests")` but that test project does not exist yet.

The plugin runs inside Jellyfin 10.11+. There is no standalone runner — deploy the DLL + `meta.json` to `/var/lib/jellyfin/plugins/SubSync_1.0.0.0/` and restart Jellyfin.

## Architecture

```
Plugin.cs                        — Entry point (BasePlugin<PluginConfiguration>, IHasWebPages).
                                   On construction: sets static Instance, injects <script> tag into
                                   Jellyfin's index.html (idempotent). On uninstall: removes injection.
SubSyncServiceRegistrator.cs     — DI registration (IServerServiceRegistrator). Registers SubSyncService as singleton.
Configuration/PluginConfiguration.cs — Settings model (XML-serialized by Jellyfin).
Api/SubSyncController.cs         — REST API at /SubSync/* (ApiController, [Authorize]).
                                   Also serves the client JS via GET /SubSync/ClientScript from embedded resource.
Services/SubSyncService.cs       — Core logic: ffsubsync/ffmpeg process execution, job tracking,
                                   venv management, atomic file replacement with rollback.
Web/configPage.html              — Dashboard config UI (Jellyfin plugin page).
Web/subsync.js                   — Client injection script: hooks the "More" button on video pages,
                                   injects "Sync Subtitles" into the actionSheet, polls job status.
```

## Key Patterns & Gotchas

- **Plugin GUID** `c7d8e9f0-a1b2-4c3d-e5f6-a7b8c9d0e1f2` must stay consistent across `meta.json`, `build.yaml`, `Plugin.cs`, and `configPage.html` (the `PluginId` JS variable).

- **Web resources are embedded** via `<EmbeddedResource Include="Web\**" />` in the csproj. The config page is accessed through Jellyfin's `IHasWebPages` mechanism (resource path: `Jellyfin.Plugin.SubSync.Web.configPage.html`). The client JS is served manually by `SubSyncController.GetClientScript()` reading the embedded resource stream.

- **Script injection into index.html** is done at plugin startup by modifying Jellyfin's `index.html` on disk (`Plugin.InjectScript()`). The injection uses sentinel comments (`<!-- SubSync Client Script -->` / `<!-- End SubSync Client Script -->`) for idempotency and clean removal on uninstall.

- **Client-side "More" button hook** (`subsync.js`) uses a DOM click event listener with retries to find the `.actionSheetContent` element after the user clicks the More button. This is fragile — it polls up to 20 times with 100ms delays.

- **Sync job concurrency** is capped at 2 via `SemaphoreSlim(2, 2)`. Jobs are tracked in-memory (`ConcurrentDictionary<string, SyncJob>`) with a 30-minute cleanup timer. Jobs are not persisted across restarts.

- **Atomic file replacement pattern**: backup → copy → verify → cleanup. On any failure after backup creation, rollback restores the original. If rollback itself fails, the `.bak.subsync` file is preserved for manual recovery.

- **External vs embedded subtitles** follow different code paths: external `.srt` files are replaced in-place; embedded subtitles require ffmpeg remux (extract → sync → remux with `-map 0 -map 1:0 -c copy`). Original embedded streams are preserved in the container.

- **Argument escaping** is hand-rolled (`EscapeArg` in `SubSyncService.cs`) — only handles spaces, double-quotes, and single-quotes. Not using `System.Diagnostics.ProcessStartInfo.ArgumentList`.

- **ffsubsync progress parsing** reads stderr line-by-line in real-time, matching tqdm percentage output with a regex and phase log messages. Progress mapping: speech extraction 10–55%, subtitle extraction 55–60%, alignment 60–75%.

- **Config validation** for VAD method and output encoding uses hardcoded `HashSet<string>` allow-lists in `SubSyncService` to prevent argument injection — the HTML form offers options not in the C# allow-list (e.g., `silero`, `subs_then_auditok`), which would be silently defaulted.

- **`TreatWarningsAsErrors`** and **`GenerateDocumentationFile`** are enabled in the csproj — all public members require XML doc comments, and all warnings are build errors.

- **Jellyfin NuGet packages** (`Jellyfin.Controller`, `Jellyfin.Model` v10.11.6) use `<ExcludeAssets>runtime</ExcludeAssets>` — they're compile-time only; at runtime the plugin resolves against Jellyfin's own assemblies.

## Naming & Style

- C# files use PascalCase for all public members.
- XML doc comments (`///`) on every public member (enforced by `GenerateDocumentationFile` + `TreatWarningsAsErrors`).
- JavaScript uses ES5-style IIFE with `var`, no modules or transpilation.
- Logger messages use structured logging with `{Placeholder}` syntax.
- Process invocations consistently use `ConfigureAwait(false)` on all awaited calls.
