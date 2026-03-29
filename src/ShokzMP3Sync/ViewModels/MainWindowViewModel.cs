using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShokzMP3Sync.Models;
using ShokzMP3Sync.Services;

namespace ShokzMP3Sync.ViewModels;

public partial class ChannelViewModel : ObservableObject
{
    [ObservableProperty] private string _url = "";
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _folderName = "";
    [ObservableProperty] private int _keepCount = 10;
    [ObservableProperty] private bool _normalizeAudio;
    [ObservableProperty] private bool _includeLivestreams;
    [ObservableProperty] private int _currentCount;
    [ObservableProperty] private bool _isSyncing;
    [ObservableProperty] private string _statusText = "";

    public ChannelConfig ToConfig() => new()
    {
        Url = Url,
        Name = Name,
        FolderName = FolderName,
        KeepCount = KeepCount,
        NormalizeAudio = NormalizeAudio,
        IncludeLivestreams = IncludeLivestreams
    };

    public static ChannelViewModel FromConfig(ChannelConfig config) => new()
    {
        Url = config.Url,
        Name = config.Name,
        FolderName = config.FolderName,
        KeepCount = config.KeepCount,
        NormalizeAudio = config.NormalizeAudio,
        IncludeLivestreams = config.IncludeLivestreams
    };
}

public partial class PlaylistViewModel : ObservableObject
{
    [ObservableProperty] private string _url = "";
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _folderName = "";
    [ObservableProperty] private bool _normalizeAudio;
    [ObservableProperty] private int _currentCount;
    [ObservableProperty] private bool _isSyncing;
    [ObservableProperty] private string _statusText = "";

    public PlaylistConfig ToConfig() => new()
    {
        Url = Url,
        Name = Name,
        FolderName = FolderName,
        NormalizeAudio = NormalizeAudio
    };

    public static PlaylistViewModel FromConfig(PlaylistConfig config) => new()
    {
        Url = config.Url,
        Name = config.Name,
        FolderName = config.FolderName,
        NormalizeAudio = config.NormalizeAudio
    };
}

public partial class MainWindowViewModel : ObservableObject
{
    private readonly ConfigService _configService = new();
    private readonly DeviceService _deviceService = new();
    private YtDlpService _ytDlpService = null!;
    private SyncService _syncService = null!;
    private Timer? _deviceCheckTimer;
    private CancellationTokenSource? _syncCts;

    [ObservableProperty] private bool _isDeviceConnected;
    [ObservableProperty] private string _deviceStatusText = "未偵測到裝置";
    [ObservableProperty] private string _deviceSpaceText = "";
    [ObservableProperty] private bool _isSyncing;
    [ObservableProperty] private string _syncStatusText = "";
    [ObservableProperty] private double _syncProgress;
    [ObservableProperty] private bool _isYtDlpAvailable;
    [ObservableProperty] private string _toolStatusText = "";
    [ObservableProperty] private bool _hasMissingDependencies;
    [ObservableProperty] private string _missingDependenciesText = "";

    // Latest feed fields
    [ObservableProperty] private bool _latestFeedEnabled;
    [ObservableProperty] private string _latestFeedFolderName = "最新動態";
    [ObservableProperty] private double _latestFeedMinHours = 2.0;
    [ObservableProperty] private string _latestFeedStatusText = "";

    private bool _isInitializing;

    partial void OnLatestFeedEnabledChanged(bool value) { if (!_isInitializing) SaveConfig(); }
    partial void OnLatestFeedFolderNameChanged(string value) { if (!_isInitializing) SaveConfig(); }
    partial void OnLatestFeedMinHoursChanged(double value) { if (!_isInitializing) SaveConfig(); }

    // Add channel dialog fields
    [ObservableProperty] private bool _isAddDialogOpen;
    [ObservableProperty] private string _newChannelUrl = "";
    [ObservableProperty] private string _newChannelName = "";
    [ObservableProperty] private string _newChannelFolder = "";
    [ObservableProperty] private int _newChannelKeepCount = 10;
    [ObservableProperty] private bool _newChannelNormalizeAudio;
    [ObservableProperty] private bool _newChannelIncludeLivestreams;
    [ObservableProperty] private bool _isResolvingChannel;
    [ObservableProperty] private int _editingIndex = -1;

    // Add playlist dialog fields
    [ObservableProperty] private bool _isAddPlaylistDialogOpen;
    [ObservableProperty] private string _newPlaylistUrl = "";
    [ObservableProperty] private string _newPlaylistName = "";
    [ObservableProperty] private string _newPlaylistFolder = "";
    [ObservableProperty] private bool _newPlaylistNormalizeAudio;
    [ObservableProperty] private bool _isResolvingPlaylist;
    [ObservableProperty] private int _editingPlaylistIndex = -1;

    public ObservableCollection<ChannelViewModel> Channels { get; } = new();
    public ObservableCollection<PlaylistViewModel> Playlists { get; } = new();

    private AppConfig _config = new();

    public void Initialize()
    {
        _config = _configService.Load();
        _ytDlpService = new YtDlpService(_config.YtDlpPath);
        _syncService = new SyncService(_ytDlpService, _deviceService);

        foreach (var ch in _config.Channels)
        {
            var vm = ChannelViewModel.FromConfig(ch);
            UpdateChannelCurrentCount(vm);
            Channels.Add(vm);
        }

        foreach (var pl in _config.Playlists)
        {
            var vm = PlaylistViewModel.FromConfig(pl);
            UpdatePlaylistCurrentCount(vm);
            Playlists.Add(vm);
        }

        _isInitializing = true;
        if (_config.LatestFeed != null)
        {
            LatestFeedEnabled = _config.LatestFeed.Enabled;
            LatestFeedFolderName = _config.LatestFeed.FolderName;
            LatestFeedMinHours = _config.LatestFeed.MinHours;
        }
        _isInitializing = false;

        IsYtDlpAvailable = _ytDlpService.IsAvailable();
        var isFfmpegAvailable = YtDlpService.IsFfmpegAvailable();

        // Build dependency check overlay
        var missing = new System.Collections.Generic.List<string>();
        if (!IsYtDlpAvailable) missing.Add("yt-dlp");
        if (!isFfmpegAvailable) missing.Add("ffmpeg");

        if (missing.Count > 0)
        {
            HasMissingDependencies = true;
            MissingDependenciesText = $"缺少必要工具：{string.Join("、", missing)}\n\n"
                + "請在終端機執行以下指令安裝：\n"
                + $"brew install {string.Join(" ", missing)}\n\n"
                + "安裝完成後請重新啟動應用程式。";
        }

        ToolStatusText = IsYtDlpAvailable && isFfmpegAvailable
            ? "yt-dlp: OK | ffmpeg: OK"
            : $"yt-dlp: {(IsYtDlpAvailable ? "OK" : "缺少")} | ffmpeg: {(isFfmpegAvailable ? "OK" : "缺少")}";

        CheckDevice();
        _deviceCheckTimer = new Timer(_ => CheckDevice(), null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
    }

    private void CheckDevice()
    {
        var connected = _deviceService.IsDeviceConnected(_config.DeviceVolumeName);
        IsDeviceConnected = connected;

        if (connected)
        {
            var free = _deviceService.GetFreeSpace(_config.DeviceVolumeName);
            var total = _deviceService.GetTotalSpace(_config.DeviceVolumeName);
            DeviceStatusText = "SWIM PRO 已連接";
            DeviceSpaceText = $"可用空間: {FormatSize(free)} / {FormatSize(total)}";

            foreach (var ch in Channels)
                UpdateChannelCurrentCount(ch);
            foreach (var pl in Playlists)
                UpdatePlaylistCurrentCount(pl);

            if (_config.LatestFeed?.Enabled == true)
            {
                var feedDir = Path.Combine(_deviceService.GetDevicePath(_config.DeviceVolumeName), _config.LatestFeed.FolderName);
                if (Directory.Exists(feedDir))
                {
                    var count = Directory.GetFiles(feedDir, "*.mp3")
                        .Count(f => !Path.GetFileName(f).StartsWith("._"));
                    LatestFeedStatusText = $"{count} 首";
                }
                else
                {
                    LatestFeedStatusText = "";
                }
            }
        }
        else
        {
            DeviceStatusText = "未偵測到 SWIM PRO 裝置";
            DeviceSpaceText = "";
        }
    }

    private void UpdateChannelCurrentCount(ChannelViewModel ch)
    {
        if (IsDeviceConnected)
        {
            var ids = _deviceService.GetExistingVideoIds(_config.DeviceVolumeName, ch.FolderName);
            ch.CurrentCount = ids.Count;
        }
    }

    private void UpdatePlaylistCurrentCount(PlaylistViewModel pl)
    {
        if (IsDeviceConnected)
        {
            var ids = _deviceService.GetExistingVideoIds(_config.DeviceVolumeName, pl.FolderName);
            pl.CurrentCount = ids.Count;
        }
    }

    // ===== Channel commands =====

    [RelayCommand]
    private void OpenAddDialog()
    {
        EditingIndex = -1;
        NewChannelUrl = "";
        NewChannelName = "";
        NewChannelFolder = "";
        NewChannelKeepCount = 10;
        NewChannelNormalizeAudio = false;
        NewChannelIncludeLivestreams = false;
        IsAddDialogOpen = true;
    }

    [RelayCommand]
    private void OpenEditDialog(ChannelViewModel channel)
    {
        EditingIndex = Channels.IndexOf(channel);
        NewChannelUrl = channel.Url;
        NewChannelName = channel.Name;
        NewChannelFolder = channel.FolderName;
        NewChannelKeepCount = channel.KeepCount;
        NewChannelNormalizeAudio = channel.NormalizeAudio;
        NewChannelIncludeLivestreams = channel.IncludeLivestreams;
        IsAddDialogOpen = true;
    }

    [RelayCommand]
    private async Task ResolveChannelNameAsync()
    {
        if (string.IsNullOrWhiteSpace(NewChannelUrl)) return;

        IsResolvingChannel = true;
        try
        {
            var name = await _ytDlpService.GetChannelNameAsync(NewChannelUrl);
            if (name != null)
            {
                NewChannelName = name;
                if (string.IsNullOrEmpty(NewChannelFolder))
                    NewChannelFolder = name;
            }
        }
        catch { }
        finally { IsResolvingChannel = false; }
    }

    [RelayCommand]
    private void ConfirmAddChannel()
    {
        if (string.IsNullOrWhiteSpace(NewChannelUrl) || string.IsNullOrWhiteSpace(NewChannelName))
            return;

        var folder = string.IsNullOrWhiteSpace(NewChannelFolder) ? NewChannelName : NewChannelFolder;

        if (EditingIndex >= 0)
        {
            var ch = Channels[EditingIndex];
            ch.Url = NewChannelUrl;
            ch.Name = NewChannelName;
            ch.FolderName = folder;
            ch.KeepCount = NewChannelKeepCount;
            ch.NormalizeAudio = NewChannelNormalizeAudio;
            ch.IncludeLivestreams = NewChannelIncludeLivestreams;
        }
        else
        {
            Channels.Add(new ChannelViewModel
            {
                Url = NewChannelUrl, Name = NewChannelName,
                FolderName = folder, KeepCount = NewChannelKeepCount,
                NormalizeAudio = NewChannelNormalizeAudio,
                IncludeLivestreams = NewChannelIncludeLivestreams
            });
        }

        SaveConfig();
        IsAddDialogOpen = false;
    }

    [RelayCommand]
    private void CancelAddChannel() => IsAddDialogOpen = false;

    [RelayCommand]
    private void RemoveChannel(ChannelViewModel channel)
    {
        Channels.Remove(channel);
        if (IsDeviceConnected)
            _deviceService.DeleteFolderIfExists(_config.DeviceVolumeName, channel.FolderName);
        SaveConfig();
    }

    // ===== Playlist commands =====

    [RelayCommand]
    private void OpenAddPlaylistDialog()
    {
        EditingPlaylistIndex = -1;
        NewPlaylistUrl = "";
        NewPlaylistName = "";
        NewPlaylistFolder = "";
        NewPlaylistNormalizeAudio = false;
        IsAddPlaylistDialogOpen = true;
    }

    [RelayCommand]
    private void OpenEditPlaylistDialog(PlaylistViewModel playlist)
    {
        EditingPlaylistIndex = Playlists.IndexOf(playlist);
        NewPlaylistUrl = playlist.Url;
        NewPlaylistName = playlist.Name;
        NewPlaylistFolder = playlist.FolderName;
        NewPlaylistNormalizeAudio = playlist.NormalizeAudio;
        IsAddPlaylistDialogOpen = true;
    }

    [RelayCommand]
    private async Task ResolvePlaylistNameAsync()
    {
        if (string.IsNullOrWhiteSpace(NewPlaylistUrl)) return;

        IsResolvingPlaylist = true;
        try
        {
            var name = await _ytDlpService.GetPlaylistNameAsync(NewPlaylistUrl);
            if (name != null)
            {
                NewPlaylistName = name;
                if (string.IsNullOrEmpty(NewPlaylistFolder))
                    NewPlaylistFolder = name;
            }
        }
        catch { }
        finally { IsResolvingPlaylist = false; }
    }

    [RelayCommand]
    private void ConfirmAddPlaylist()
    {
        if (string.IsNullOrWhiteSpace(NewPlaylistUrl) || string.IsNullOrWhiteSpace(NewPlaylistName))
            return;

        var folder = string.IsNullOrWhiteSpace(NewPlaylistFolder) ? NewPlaylistName : NewPlaylistFolder;

        if (EditingPlaylistIndex >= 0)
        {
            var pl = Playlists[EditingPlaylistIndex];
            pl.Url = NewPlaylistUrl;
            pl.Name = NewPlaylistName;
            pl.FolderName = folder;
            pl.NormalizeAudio = NewPlaylistNormalizeAudio;
        }
        else
        {
            Playlists.Add(new PlaylistViewModel
            {
                Url = NewPlaylistUrl, Name = NewPlaylistName, FolderName = folder,
                NormalizeAudio = NewPlaylistNormalizeAudio
            });
        }

        SaveConfig();
        IsAddPlaylistDialogOpen = false;
    }

    [RelayCommand]
    private void CancelAddPlaylist() => IsAddPlaylistDialogOpen = false;

    [RelayCommand]
    private void RemovePlaylist(PlaylistViewModel playlist)
    {
        Playlists.Remove(playlist);
        if (IsDeviceConnected)
            _deviceService.DeleteFolderIfExists(_config.DeviceVolumeName, playlist.FolderName);
        SaveConfig();
    }

    // ===== Sync commands =====

    [RelayCommand]
    private async Task SyncAllAsync()
    {
        if (!IsDeviceConnected || !IsYtDlpAvailable || IsSyncing) return;

        IsSyncing = true;
        _syncCts = new CancellationTokenSource();
        var hasLatestFeed = _config.LatestFeed?.Enabled == true;
        var totalItems = Channels.Count + Playlists.Count + (hasLatestFeed ? 1 : 0);

        try
        {
            int idx = 0;

            // Sync channels
            for (int i = 0; i < Channels.Count; i++, idx++)
            {
                var ch = Channels[i];
                ch.IsSyncing = true;

                var result = await _syncService.SyncChannelAsync(
                    ch.ToConfig(), _config.DeviceVolumeName,
                    status => { SyncStatusText = status; ch.StatusText = status; },
                    progress => { SyncProgress = (idx + progress) / totalItems; },
                    _syncCts.Token);

                ch.IsSyncing = false;
                ch.StatusText = FormatSyncResult(result);
                UpdateChannelCurrentCount(ch);
            }

            // Sync playlists
            for (int i = 0; i < Playlists.Count; i++, idx++)
            {
                var pl = Playlists[i];
                pl.IsSyncing = true;

                var result = await _syncService.SyncPlaylistAsync(
                    pl.ToConfig(), _config.DeviceVolumeName,
                    status => { SyncStatusText = status; pl.StatusText = status; },
                    progress => { SyncProgress = (idx + progress) / totalItems; },
                    _syncCts.Token);

                pl.IsSyncing = false;
                pl.StatusText = FormatSyncResult(result);
                UpdatePlaylistCurrentCount(pl);
            }

            // Sync latest feed
            if (hasLatestFeed)
            {
                SyncStatusText = "正在建立最新動態...";
                var feedResult = await _syncService.SyncLatestFeedAsync(
                    _config.LatestFeed!, _config.DeviceVolumeName,
                    _config.Channels,
                    status => SyncStatusText = status,
                    progress => SyncProgress = (idx + progress) / totalItems,
                    _syncCts.Token);

                LatestFeedStatusText = $"{feedResult.Downloaded} 首";
                if (feedResult.Errors.Any())
                    LatestFeedStatusText += $" (錯誤 {feedResult.Errors.Count})";
            }

            SyncStatusText = "同步完成!";
            SyncProgress = 1.0;
        }
        catch (OperationCanceledException)
        {
            SyncStatusText = "同步已取消";
        }
        finally
        {
            IsSyncing = false;
            _syncCts = null;
        }
    }

    [RelayCommand]
    private async Task SyncChannelAsync(ChannelViewModel channel)
    {
        if (!IsDeviceConnected || !IsYtDlpAvailable || channel.IsSyncing) return;

        channel.IsSyncing = true;
        _syncCts = new CancellationTokenSource();

        try
        {
            var result = await _syncService.SyncChannelAsync(
                channel.ToConfig(), _config.DeviceVolumeName,
                status => channel.StatusText = status,
                progress => SyncProgress = progress,
                _syncCts.Token);

            channel.StatusText = FormatSyncResult(result);
            UpdateChannelCurrentCount(channel);
        }
        catch (OperationCanceledException) { channel.StatusText = "已取消"; }
        finally { channel.IsSyncing = false; _syncCts = null; }
    }

    [RelayCommand]
    private async Task SyncPlaylistAsync(PlaylistViewModel playlist)
    {
        if (!IsDeviceConnected || !IsYtDlpAvailable || playlist.IsSyncing) return;

        playlist.IsSyncing = true;
        _syncCts = new CancellationTokenSource();

        try
        {
            var result = await _syncService.SyncPlaylistAsync(
                playlist.ToConfig(), _config.DeviceVolumeName,
                status => playlist.StatusText = status,
                progress => SyncProgress = progress,
                _syncCts.Token);

            playlist.StatusText = FormatSyncResult(result);
            UpdatePlaylistCurrentCount(playlist);
        }
        catch (OperationCanceledException) { playlist.StatusText = "已取消"; }
        finally { playlist.IsSyncing = false; _syncCts = null; }
    }

    [RelayCommand]
    private void CancelSync()
    {
        _syncCts?.Cancel();
    }

    private void SaveConfig()
    {
        _config.Channels = Channels.Select(c => c.ToConfig()).ToList();
        _config.Playlists = Playlists.Select(p => p.ToConfig()).ToList();
        _config.LatestFeed = new LatestFeedConfig
        {
            Enabled = LatestFeedEnabled,
            FolderName = LatestFeedFolderName,
            MinHours = LatestFeedMinHours
        };
        _configService.Save(_config);
    }

    private static string FormatSyncResult(SyncResult result)
    {
        var text = $"下載 {result.Downloaded}, 刪除 {result.Deleted}, 略過 {result.Skipped}";
        if (result.Errors.Any())
        {
            text += $" (錯誤 {result.Errors.Count}: {result.Errors.First()}";
            if (result.Errors.Count > 1) text += $" ...等";
            text += ")";
        }
        return text;
    }

    private static string FormatSize(long bytes)
    {
        double gb = bytes / (1024.0 * 1024.0 * 1024.0);
        if (gb >= 1) return $"{gb:F1} GB";
        double mb = bytes / (1024.0 * 1024.0);
        return $"{mb:F0} MB";
    }

    public void Cleanup()
    {
        _deviceCheckTimer?.Dispose();
        _syncCts?.Cancel();
    }
}
