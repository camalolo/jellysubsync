# Jellyfin SubSync Plugin

Automatically synchronize subtitles with video audio using [ffsubsync](https://github.com/smacke/ffsubsync).

Adds a **"Sync Subtitles"** option to the video detail page's **More** dropdown menu in Jellyfin.

## How It Works

1. Select a subtitle track from any video in your library
2. The plugin runs ffsubsync to analyze the video's audio track and align the subtitle timings
3. The original subtitle is safely replaced with the synced version (with automatic rollback on failure)

## Features

- **External subtitle support** — syncs `.srt` sidecar files, replaces in-place with backup
- **Embedded subtitle support** — extracts embedded subtitles, syncs, and remuxes into the video container (original streams preserved)
- **Real-time progress** — live progress bar with phase labels, elapsed timer, and animated spinner
- **Safe file handling** — atomic file replacement with backup and automatic rollback on failure
- **Managed ffsubsync** — installs ffsubsync into an isolated Python virtualenv (no system-wide pip needed)
- **Configurable** — VAD method, offset limits, encoding, ffmpeg path, golden-section search

## Requirements

- Jellyfin 10.11+
- Python 3 (for the managed ffsubsync virtualenv)
- ffmpeg (usually bundled with Jellyfin)

## Installation

### From Release

1. Download the plugin DLL from the [Releases](../../releases) page
2. Create a directory at `/var/lib/jellyfin/plugins/SubSync_1.0.0.0/`
3. Copy the DLL into that directory
4. Restart Jellyfin

### From Source

```bash
git clone https://github.com/YOUR_USERNAME/jellysubsync.git
cd jellysubsync
dotnet build -c Release
```

The built DLL is at `Jellyfin.Plugin.SubSync/bin/Release/net9.0/Jellyfin.Plugin.SubSync.dll`.

## Setup

After installing:

1. Go to **Dashboard → Plugins → SubSync**
2. Click **"Install/Update ffsubsync"** — this creates a Python virtualenv and installs ffsubsync automatically
3. Navigate to any video with subtitles, click **More → Sync Subtitles**

## Usage

1. Open any video details page in Jellyfin
2. Click the **"More"** (⋯) button
3. Select **"Sync Subtitles"**
4. Choose a subtitle track from the list
5. Click **Sync** and watch the real-time progress

The sync process has these phases:
- **Analyzing speech** — ffsubsync decodes the audio and runs voice activity detection
- **Computing alignment** — finds the optimal timing offset between speech and subtitles
- **Replacing subtitle** / **Remuxing video** — safely writes the synced subtitle
- **Verifying** — confirms the output file is valid

A typical movie takes 60–90 seconds to sync.

## Configuration

| Option | Default | Description |
|--------|---------|-------------|
| **FfSubSync Path** | (managed) | Path to ffsubsync binary. Leave default to use the managed virtualenv. |
| **FFmpeg Path** | (system) | Path to ffmpeg. Leave empty to use system PATH. |
| **VAD Method** | `subs_then_webrtc` | Voice activity detection: `subs` (fast), `webrtc` (accurate), `subs_then_webrtc` (adaptive) |
| **Max Offset Seconds** | `60` | Maximum allowed subtitle offset in seconds |
| **Max Subtitle Seconds** | `10` | Maximum on-screen duration for a subtitle |
| **Output Encoding** | `utf-8` | Character encoding for synced subtitles |
| **Golden Section Search** | `false` | Use golden-section search for framerate ratio optimization |
| **Overwrite Existing** | `true` | Allow re-syncing subtitles that have already been synced |

## Architecture

```
Plugin.cs                  — Entry point, DI registration, index.html script injection
├── Services/
│   └── SubSyncService.cs  — ffsubsync runner, job tracker, venv installer, file operations
├── Api/
│   └── SubSyncController.cs — REST API endpoints
├── Configuration/
│   └── PluginConfiguration.cs — Settings model
└── Web/
    ├── subsync.js         — Client-side script (injected into Jellyfin pages)
    └── configPage.html    — Dashboard configuration UI
```

## Safety

- Subtitle replacements are **atomic**: backup → copy → verify → cleanup
- If anything fails after the backup is created, the original file is **automatically restored**
- If the rollback itself fails, the backup file (`.bak.subsync`) is preserved for manual recovery
- Embedded subtitle remuxing preserves all original streams (video, audio, existing subtitles)

## Building a Release

```bash
dotnet build -c Release
```

The output DLL is self-contained and references Jellyfin assemblies at runtime.

## License

GPL-2.0-only — see [LICENSE](LICENSE).
