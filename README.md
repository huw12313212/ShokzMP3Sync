# ShokzMP3Sync

自動將 YouTube 頻道 / 播放清單下載為 MP3，同步至 SHOKZ OPEN SWIM PRO (S710) 耳機。

插上耳機 → 打開程式 → 按下同步 → 拔掉耳機去游泳。

## 功能

- **頻道同步** — 設定 YouTube 頻道，自動保留最新 N 部影片
- **播放清單同步** — 以線上清單為準完整同步，清單移除的自動刪除
- **智慧差異比對** — 透過影片 ID 辨識，不重複下載已存在的檔案
- **自動清理** — 過期影片、不在清單中的影片自動從裝置刪除
- **裝置偵測** — 自動偵測 SWIM PRO 連線狀態與剩餘空間
- **GUI 介面** — Avalonia UI，支援新增/編輯/刪除頻道與播放清單

## 螢幕截圖概念

```
┌─ ShokzMP3Sync ──────────────────────────┐
│ ● SWIM PRO 已連接    可用: 28.9 / 29.1 GB │
│                                          │
│ YouTube 頻道                  [+ 新增頻道] │
│ ┌──────────────────────────────────────┐ │
│ │ 股癌         8 首 / 保留 10  [同步]   │ │
│ │ 曼報         3 首 / 保留 5   [同步]   │ │
│ └──────────────────────────────────────┘ │
│                                          │
│ YouTube 播放清單            [+ 新增播放清單] │
│ ┌──────────────────────────────────────┐ │
│ │ 包子音樂     32 首 / 完整同步 [同步]   │ │
│ └──────────────────────────────────────┘ │
│                                          │
│ [全部同步]                                │
└──────────────────────────────────────────┘
```

## 環境需求

- macOS
- [.NET 7 SDK](https://dotnet.microsoft.com/download/dotnet/7.0)
- [yt-dlp](https://github.com/yt-dlp/yt-dlp)
- [ffmpeg](https://ffmpeg.org/)

```bash
brew install yt-dlp ffmpeg
```

## 快速開始

```bash
git clone https://redcandlegames2025.myqnapcloud.com/huw12313212/shokzmp3sync.git
cd shokzmp3sync
dotnet run --project src/ShokzMP3Sync
```

## 執行測試

插上 SWIM PRO 耳機後執行：

```bash
dotnet run --project tests/ShokzMP3Sync.IntegrationTests
```

測試涵蓋 29 項：裝置偵測、檔案讀寫、yt-dlp 整合、頻道同步流程、播放清單同步流程、設定檔存取。

## 專案結構

```
src/ShokzMP3Sync/
├── Models/          # ChannelConfig, PlaylistConfig, AppConfig, VideoInfo
├── Services/
│   ├── YtDlpService.cs    # yt-dlp 封裝 (頻道/播放清單/下載)
│   ├── DeviceService.cs   # 裝置偵測、檔案操作
│   ├── SyncService.cs     # 同步邏輯 (頻道 + 播放清單)
│   └── ConfigService.cs   # JSON 設定讀寫
├── ViewModels/      # MVVM ViewModel
├── Views/           # Avalonia AXAML
└── Program.cs
```

## 設定檔

儲存於 `~/Library/Application Support/ShokzMP3Sync/config.json`：

```json
{
  "deviceVolumeName": "SWIM PRO",
  "channels": [
    { "url": "https://www.youtube.com/@Gooaye", "name": "股癌", "folderName": "股癌", "keepCount": 10 },
    { "url": "https://www.youtube.com/@MannysNewsletter", "name": "曼報", "folderName": "曼報", "keepCount": 5 }
  ],
  "playlists": [
    { "url": "https://www.youtube.com/...list=PL...", "name": "包子音樂", "folderName": "包子音樂" }
  ]
}
```

## 同步邏輯

| 類型 | 新增 | 刪除 | 跳過 |
|------|------|------|------|
| 頻道 | 最新 N 部中裝置上沒有的 | 不在最新 N 部的舊檔案 | 已存在且仍在範圍內 |
| 播放清單 | 清單中裝置上沒有的 | 裝置上有但清單中已移除的 | 已存在且仍在清單中 |

## 技術棧

- .NET 7 + Avalonia UI 11
- CommunityToolkit.Mvvm (MVVM)
- yt-dlp + ffmpeg (下載轉檔)
