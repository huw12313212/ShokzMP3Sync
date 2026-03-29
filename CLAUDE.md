# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run

```bash
# Build entire solution
dotnet build

# Run GUI app
dotnet run --project src/ShokzMP3Sync

# Run integration tests (requires SWIM PRO device connected via USB)
dotnet run --project tests/ShokzMP3Sync.IntegrationTests

# Build macOS .app bundle (self-contained, 不需安裝 .NET runtime)
bash build-app.sh
# 產出位置: build/ShokzMP3Sync.app
# 安裝: cp -r build/ShokzMP3Sync.app /Applications/
```

External tools required: `yt-dlp` and `ffmpeg` (install via `brew install yt-dlp ffmpeg`).

## Architecture

.NET 7 + Avalonia UI 11 desktop app using MVVM pattern (CommunityToolkit.Mvvm).

**Two sync modes with different deletion logic:**
- **Channel sync** (`SyncChannelAsync`): keeps latest N videos per channel; deletes anything older than N.
- **Playlist sync** (`SyncPlaylistAsync`): syncs all videos in playlist; deletes anything removed from the online playlist.

Both modes identify existing files by extracting YouTube video IDs from filenames via regex `\[([a-zA-Z0-9_-]{11})\]\.mp3$`.

**Service layer:**
- `YtDlpService` — wraps yt-dlp CLI for channel info, playlist listing, and MP3 download
- `DeviceService` — detects SWIM PRO at `/Volumes/SWIM PRO/`, manages files on device
- `SyncService` — orchestrates download-to-temp → move-to-device flow
- `ConfigService` — persists `AppConfig` as JSON at `~/.config/ShokzMP3Sync/config.json`

**Device specifics:** SWIM PRO mounts as USB storage. Each channel/playlist gets its own folder. Files named `{title} [{videoId}].mp3`. Must skip macOS `._` resource fork files when scanning.

## Key Conventions

- All user-facing strings are in Traditional Chinese (繁體中文)
- Downloads go to temp directory first, then move to device (avoids corruption on disconnect)
- Integration tests are a standalone console app (not xUnit/NUnit), run sequentially with real device + real YouTube API calls
- The test project creates and cleans up `_test_sync` / `_test_playlist` folders on the device
- **每個新功能都必須撰寫對應的整合測試來驗證功能正常**
- Config 相關測試必須先備份再還原使用者的 config，不可覆蓋使用者設定
