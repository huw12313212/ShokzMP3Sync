# ShokzMP3Sync

自動將 YouTube 頻道 / 播放清單下載為 MP3，同步至 SHOKZ OPEN SWIM PRO (S710) 耳機。

插上耳機 → 打開程式 → 按下同步 → 拔掉耳機去游泳。

## 故事

買了 SHOKZ OPEN SWIM PRO 游泳用水下耳機，結果才發現水裡根本不能用藍牙，什麼都聽不了。

打開說明書，居然寫要「手動把 MP3 放進耳機裡」才能在水裡播放。什麼上個世紀的使用情境。

還好有 [Claude Code](https://claude.ai/code)。嘴了半小時，做了一個工具，讓耳機接上電腦充電時自動抓 YouTube 指定頻道的最新內容跟播放清單，轉成 MP3 塞進耳機。

問題解決，好舒壓。

## 功能

- **頻道同步** — 設定 YouTube 頻道，自動保留最新 N 部影片
- **播放清單同步** — 以線上清單為準完整同步，清單移除的自動刪除
- **包含直播影片** — 頻道可選擇是否包含直播錄影，不只抓一般上傳
- **音量正規化** — 可對頻道/播放清單啟用 EBU R128 loudnorm，自動平衡音量大小
- **會員影片自動跳過** — 遇到會員專屬影片自動跳過，繼續往下抓直到湊滿指定數量
- **最新動態資料夾** — 從所有頻道收集最新上傳，按時間排序放入一個資料夾（依最少時數填滿）
- **智慧差異比對** — 透過影片 ID 辨識，不重複下載已存在的檔案
- **自動清理** — 過期影片、不在清單中的影片自動從裝置刪除
- **裝置偵測** — 自動偵測 SWIM PRO 連線狀態與剩餘空間
- **啟動檢查** — 自動偵測 yt-dlp / ffmpeg 是否安裝，缺少時顯示安裝提示
- **同步 Log** — 錯誤記錄至 `~/.config/ShokzMP3Sync/sync.log`，方便除錯
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
│ │ 游泳         5 首 / 完整同步          │ │
│ │              / 音量正規化     [同步]   │ │
│ └──────────────────────────────────────┘ │
│                                          │
│ ☑ 最新動態資料夾  18 首                    │
│   資料夾名稱: 最新動態                      │
│   最少時數: 8.0 小時                       │
│                                          │
│ [全部同步]                                │
└──────────────────────────────────────────┘
```

## 系統需求

> **目前僅測試過 macOS (Apple Silicon / arm64)。**
> Windows 因裝置掛載路徑不同 (`/Volumes/` vs 磁碟機代號) **尚未支援**。
> Linux 理論上可行但未經驗證。

### 必要工具

- macOS (Apple Silicon)
- [yt-dlp](https://github.com/yt-dlp/yt-dlp) — YouTube 下載
- [ffmpeg](https://ffmpeg.org/) — 音訊轉檔

```bash
brew install yt-dlp ffmpeg
```

應用程式啟動時會自動檢查以上工具，若未安裝會顯示提示並阻止操作。

### 開發環境（僅從原始碼建置時需要）

- [.NET 7 SDK](https://dotnet.microsoft.com/download/dotnet/7.0)

## 安裝

### 使用預建 .app（推薦）

從 [Releases](../../releases) 下載 `ShokzMP3Sync.app`，拖入 `/Applications/` 即可使用。
不需要安裝 .NET runtime。

### 從原始碼建置

```bash
git clone https://github.com/huw12313212/ShokzMP3Sync.git
cd ShokzMP3Sync

# 直接執行
dotnet run --project src/ShokzMP3Sync

# 或打包成 .app
bash build-app.sh
cp -r build/ShokzMP3Sync.app /Applications/
```

## 執行測試

插上 SWIM PRO 耳機後執行：

```bash
dotnet run --project tests/ShokzMP3Sync.IntegrationTests
```

測試涵蓋 43 項：裝置偵測、檔案讀寫、yt-dlp 整合、頻道同步流程、播放清單同步流程、設定檔存取、音量正規化 (LUFS 驗證)、直播影片擷取、最新動態資料夾排序。

## 專案結構

```
src/ShokzMP3Sync/
├── Models/          # ChannelConfig, PlaylistConfig, LatestFeedConfig, AppConfig, VideoInfo
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

儲存於 `~/.config/ShokzMP3Sync/config.json`：

```json
{
  "deviceVolumeName": "SWIM PRO",
  "channels": [
    { "url": "https://www.youtube.com/@Gooaye", "name": "股癌", "folderName": "股癌", "keepCount": 10, "normalizeAudio": false, "includeLivestreams": false },
    { "url": "https://www.youtube.com/@yutinghaofinance", "name": "財經皓角", "folderName": "財經皓角", "keepCount": 5, "normalizeAudio": false, "includeLivestreams": true }
  ],
  "playlists": [
    { "url": "https://www.youtube.com/...list=PL...", "name": "包子音樂", "folderName": "包子音樂", "normalizeAudio": false },
    { "url": "https://www.youtube.com/...list=PL...", "name": "游泳", "folderName": "游泳", "normalizeAudio": true }
  ],
  "latestFeed": { "enabled": true, "folderName": "最新動態", "minHours": 8.0 }
}
```

## 同步邏輯

| 類型 | 新增 | 刪除 | 跳過 |
|------|------|------|------|
| 頻道 | 最新 N 部中裝置上沒有的 | 不在最新 N 部的舊檔案 | 已存在且仍在範圍內 |
| 播放清單 | 清單中裝置上沒有的 | 裝置上有但清單中已移除的 | 已存在且仍在清單中 |
| 最新動態 | 從各頻道複製到動態資料夾 | 每次全部同步時重建 | — |

- 頻道同步多抓 KeepCount×3 的影片，會員專屬影片自動跳過並繼續往下，直到湊滿指定數量
- 最新動態資料夾的檔案以 `001_`、`002_` 數字前綴命名，讓播放器的字母排序 = 時間排序（001 = 最新）

## 技術棧

- .NET 7 + Avalonia UI 11
- CommunityToolkit.Mvvm (MVVM)
- yt-dlp + ffmpeg (下載轉檔)
- 本專案由 [Claude Code](https://claude.ai/code) 協助開發

## 授權

本專案採用 [CC BY-NC 4.0](https://creativecommons.org/licenses/by-nc/4.0/) 授權。

你可以自由分享、修改本專案，但 **不得用於商業用途**。詳見 [LICENSE](LICENSE)。
