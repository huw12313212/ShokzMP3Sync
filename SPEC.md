# ShokzMP3Sync 規格書

## 概述

ShokzMP3Sync 是一款桌面應用程式，用於自動將 YouTube 頻道的最新影片下載、轉檔為 MP3，並同步至 SHOKZ OPEN SWIM PRO (S710) 耳機的內建儲存空間。

## 目標裝置

- **耳機型號**: SHOKZ OPEN SWIM PRO S710
- **連接方式**: USB 磁吸充電/傳輸線，掛載為 USB 隨身碟
- **掛載路徑**: `/Volumes/SWIM PRO/` (macOS)
- **儲存結構**: 每個頻道一個資料夾，MP3 檔案以 `影片標題 [影片ID].mp3` 命名
- **系統資料夾**: `/Volumes/SWIM PRO/SYSTEM/` (含 allsong.lst、order.lst，由耳機韌體管理)

## 功能需求

### 1. 頻道管理
- 新增 YouTube 頻道（輸入頻道 URL 或 @handle）
- 自動解析頻道名稱作為顯示名稱及裝置上的資料夾名稱
- 設定每個頻道要保留的最新影片數量（例: 股癌 10 部、曼報 5 部）
- 刪除頻道（同時清理裝置上對應資料夾）
- 編輯頻道設定（修改保留數量、資料夾名稱）

### 2. 下載與轉檔
- 使用 yt-dlp 取得頻道最新影片列表
- 只下載尚未存在於裝置上的影片（透過 YouTube 影片 ID 比對）
- 使用 yt-dlp + ffmpeg 直接下載為 MP3 格式（128kbps，適合語音內容）
- 檔案命名格式: `{影片標題} [{影片ID}].mp3`

### 3. 同步邏輯
- **新增**: 下載頻道最新 N 部影片中，裝置上尚未存在的
- **刪除**: 移除裝置上已不在「最新 N 部」範圍內的舊影片
- **跳過**: 已存在且仍在範圍內的影片不重複下載
- 同步完成後顯示摘要（新增幾部、刪除幾部、跳過幾部）

### 4. 裝置偵測
- 自動偵測 SWIM PRO 裝置是否已連接（監控 `/Volumes/SWIM PRO/`）
- 裝置狀態即時顯示於 GUI（已連接 / 未連接）
- 顯示裝置剩餘空間

### 5. GUI 介面
- **主畫面**:
  - 裝置連線狀態與剩餘空間
  - 已設定的頻道清單（頻道名稱、保留數量、目前已同步數量）
  - 「全部同步」按鈕
  - 各頻道獨立「同步」按鈕
- **新增/編輯頻道對話框**:
  - YouTube 頻道 URL 輸入
  - 頻道資料夾名稱（自動帶入，可修改）
  - 保留影片數量設定
- **同步進度**:
  - 整體進度條
  - 目前正在處理的項目名稱
  - 下載速度、剩餘時間估算
  - 可取消同步

## 技術架構

### 技術棧
- **框架**: .NET 9
- **GUI**: Avalonia UI 11（跨平台桌面 UI 框架）
- **架構模式**: MVVM（使用 CommunityToolkit.Mvvm）
- **YouTube 下載**: yt-dlp（外部命令列工具）
- **音訊轉檔**: ffmpeg（由 yt-dlp 呼叫）
- **設定儲存**: JSON 檔案（存放於應用程式目錄）

### 專案結構
```
ShokzMP3Sync/
├── ShokzMP3Sync.sln
├── src/
│   └── ShokzMP3Sync/
│       ├── ShokzMP3Sync.csproj
│       ├── App.axaml / App.axaml.cs
│       ├── Program.cs
│       ├── Models/
│       │   ├── ChannelConfig.cs        # 頻道設定模型
│       │   └── AppConfig.cs            # 應用程式設定
│       ├── Services/
│       │   ├── YtDlpService.cs         # yt-dlp 操作封裝
│       │   ├── DeviceService.cs        # 裝置偵測與檔案操作
│       │   ├── SyncService.cs          # 同步邏輯協調
│       │   └── ConfigService.cs        # 設定讀寫
│       ├── ViewModels/
│       │   ├── MainWindowViewModel.cs
│       │   └── ChannelEditViewModel.cs
│       ├── Views/
│       │   ├── MainWindow.axaml
│       │   └── ChannelEditDialog.axaml
│       └── Assets/
└── SPEC.md
```

### 設定檔格式 (config.json)
```json
{
  "deviceVolumeName": "SWIM PRO",
  "ytDlpPath": "yt-dlp",
  "channels": [
    {
      "url": "https://www.youtube.com/@Gooaye",
      "name": "股癌",
      "folderName": "股癌",
      "keepCount": 10
    },
    {
      "url": "https://www.youtube.com/@MannysNewsletter",
      "name": "曼報",
      "folderName": "曼報",
      "keepCount": 5
    }
  ]
}
```

### 同步流程
```
1. 偵測裝置是否連接
2. 讀取設定檔中的頻道清單
3. 對每個頻道:
   a. 呼叫 yt-dlp 取得最新 N 部影片的 ID 與標題
   b. 掃描裝置上該頻道資料夾的現有檔案，提取影片 ID
   c. 計算差異:
      - 需下載: 在最新清單中但不在裝置上
      - 需刪除: 在裝置上但不在最新清單中
      - 已存在: 兩邊都有，跳過
   d. 刪除過期檔案
   e. 下載並轉檔新影片，直接存到裝置上
4. 顯示同步摘要
```

### 外部依賴
- **yt-dlp**: `brew install yt-dlp`
- **ffmpeg**: 已安裝 (`/opt/homebrew/bin/ffmpeg`)

## 非功能需求
- 應用啟動時自動檢查 yt-dlp 與 ffmpeg 是否可用
- 下載失敗時跳過該影片並繼續處理其他影片
- 支援中文檔名（UTF-8）
- 同步過程中的臨時檔案放在系統暫存目錄，完成後才搬移到裝置
