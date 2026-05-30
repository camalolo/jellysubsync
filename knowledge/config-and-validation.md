# Configuration & Validation

## Plugin Configuration Fields

Source: `Jellyfin.Plugin.SubSync/Configuration/PluginConfiguration.cs`

| Field | Type | Default | ffsubsync Flag | Notes |
|-------|------|---------|-----------------|-------|
| `FfSubSyncPath` | `string` | `"ffsubsync"` | — | If `"ffsubsync"` or empty, uses managed venv or system PATH |
| `FfmpegPath` | `string` | `""` | `--ffmpeg-path` | Empty = system PATH |
| `VadMethod` | `string` | `"subs_then_webrtc"` | `--vad` | Validated against C# allow-list |
| `MaxOffsetSeconds` | `int` | `60` | `--max-offset-seconds` | HTML input: min=1, max=600 |
| `MaxSubtitleSeconds` | `double` | `10.0` | `--max-subtitle-seconds` | HTML input: min=1, max=60, step=0.5 |
| `OutputEncoding` | `string` | `"utf-8"` | `--output-encoding` | Validated against C# allow-list |
| `UseGoldenSectionSearch` | `bool` | `false` | `--gss` | |
| `OverwriteExisting` | `bool` | `true` | — | If false, rejects re-sync when completed job exists |

## C# Allow-Lists (Argument Injection Prevention)

Source: `SubSyncService.cs:130-139`

**Allowed VAD Methods:**

| Value | In HTML form? |
|-------|---------------|
| `subs` | No |
| `webrtc` | Yes |
| `subs_then_webrtc` | Yes |
| `auditok` | Yes |

**Allowed Output Encodings:**

| Value |
|-------|
| `utf-8` |
| `ascii` |
| `latin-1` |
| `utf-8-sig` |
| `utf-16` |

## Validation Gap: HTML vs C# Allow-Lists

The HTML configuration form (`configPage.html:54-61`) offers these VAD methods that the
C# allow-list does **not** include:

| HTML Option | In C# Allow-List? | Behavior |
|-------------|-------------------|----------|
| `subs_then_auditok` | No | Silently defaulted to `subs_then_webrtc` |
| `subs_then_silero` | No | Silently defaulted to `subs_then_webrtc` |
| `silero` | No | Silently defaulted to `subs_then_webrtc` |

And the C# allow-list includes `subs` which is **not** offered in the HTML form.

The fallback logic is in `BuildFfSubSyncArgs` (`SubSyncService.cs:854-856`):
if the configured VAD method is not in `AllowedVadMethods`, it silently uses
`"subs_then_webrtc"`.

## ffsubsync Binary Resolution Order

Source: `SubSyncService.cs:175-195`

1. User-configured custom path (if not empty and not `"ffsubsync"`)
2. Managed venv binary: `{VenvPath}/bin/ffsubsync`
3. System PATH fallback: `"ffsubsync"`

## Managed Virtualenv Paths

Source: `SubSyncService.cs:158-168`, `Plugin.cs:54-59`

| Component | Path |
|-----------|------|
| Venv root | `{DataPath}/subsync/venv` |
| ffsubsync binary | `{VenvPath}/bin/ffsubsync` |
| pip binary | `{VenvPath}/bin/pip` |
| python3 binary | `{VenvPath}/bin/python3` |
| Temp working dir | `{CachePath}/subsync/{jobId}` |

## Argument Escaping

Source: `SubSyncService.cs:1040-1048`

The `EscapeArg(string)` method is hand-rolled and only handles:
- Spaces → wraps in double-quotes
- Double-quotes → escapes with backslash and wraps in double-quotes
- Single-quotes → wraps in double-quotes

**Not handled**: trailing backslashes (can break trailing-quote), newlines, tabs,
shell metacharacters (`$`, `` ` ``, `&`, `|`, `;`, etc.).

The project does not use `ProcessStartInfo.ArgumentList` (which handles escaping
correctly) — arguments are built as a single `Arguments` string via `string.Join(" ", args)`
at `SubSyncService.cs:887`.

## Build Constraints

Source: `Jellyfin.Plugin.SubSync.csproj:5-6`, `Directory.Build.props`

- `TreatWarningsAsErrors=true` — all compiler warnings are build errors
- `GenerateDocumentationFile=true` — all public members require `///` XML doc comments
- `Nullable=enable` and `ImplicitUsings=enable` set via `Directory.Build.props`

This means any PR adding undocumented public/internal members will fail to build.

See also: [architecture-and-gotchas.md](architecture-and-gotchas.md) for build constraints context.

## Job Cleanup Logic

Source: `SubSyncService.cs:823-849`

The cleanup timer fires every 30 minutes. Actual eviction behavior:

- **Failed jobs**: always evicted on next cleanup run, regardless of age
- **Completed jobs**: evicted only when `Progress >= 1.0` AND total job count exceeds 10
- **Queued/Running jobs**: never evicted

Note: the `cutoff` variable (`DateTime.UtcNow.AddHours(-1)`) is computed at line 827
but never referenced in the filter logic — it is dead code. The comment at line 151
says "older than 1 hour" but the code does not use timestamp-based eviction.
