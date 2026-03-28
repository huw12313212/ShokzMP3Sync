using System;
using System.Collections.ObjectModel;
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
    [ObservableProperty] private int _currentCount;
    [ObservableProperty] private bool _isSyncing;
    [ObservableProperty] private string _statusText = "";

    public ChannelConfig ToConfig() => new()
    {
        Url = Url,
        Name = Name,
        FolderName = FolderName,
        KeepCount = KeepCount
    };

    public static ChannelViewModel FromConfig(ChannelConfig config) => new()
    {
        Url = config.Url,
        Name = config.Name,
        FolderName = config.FolderName,
        KeepCount = config.KeepCount
    };
}

public partial class PlaylistViewModel : ObservableObject
{
    [ObservableProperty] private string _url = "";
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _folderName = "";
    [ObservableProperty] private int _currentCount;
    [ObservableProperty] private bool _isSyncing;
    [ObservableProperty] private string _statusText = "";

    public PlaylistConfig ToConfig() => new()
    {
        Url = Url,
        Name = Name,
        FolderName = FolderName
    };

    public static PlaylistViewModel FromConfig(PlaylistConfig config) => new()
    {
        Url = config.Url,
        Name = config.Name,
        FolderName = config.FolderName
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

    // Add channel dialog fields
    [ObservableProperty] private bool _isAddDialogOpen;
    [ObservableProperty] private string _newChannelUrl = "";
    [ObservableProperty] private string _newChannelName = "";
    [ObservableProperty] private string _newChannelFolder = "";
    [ObservableProperty] private int _newChannelKeepCount = 10;
    [ObservableProperty] private bool _isResolvingChannel;
    [ObservableProperty] private int _editingIndex = -1;

    // Add playlist dialog fields
    [ObservableProperty] private bool _isAddPlaylistDialogOpen;
    [ObservableProperty] private string _newPlaylistUrl = "";
    [ObservableProperty] private string _newPlaylistName = "";
    [ObservableProperty] private string _newPlaylistFolder = "";
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
        }
        else
        {
            Channels.Add(new ChannelViewModel
            {
                Url = NewChannelUrl, Name = NewChannelName,
                FolderName = folder, KeepCount = NewChannelKeepCount
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
        IsAddPlaylistDialogOpen = true;
    }

    [RelayCommand]
    private void OpenEditPlaylistDialog(PlaylistViewModel playlist)
    {
        EditingPlaylistIndex = Playlists.IndexOf(playlist);
        NewPlaylistUrl = playlist.Url;
        NewPlaylistName = playlist.Name;
        NewPlaylistFolder = playlist.FolderName;
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
        }
        else
        {
            Playlists.Add(new PlaylistViewModel
            {
                Url = NewPlaylistUrl, Name = NewPlaylistName, FolderName = folder
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
        var totalItems = Channels.Count + Playlists.Count;

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
                ch.StatusText = $"下載 {result.Downloaded}, 刪除 {result.Deleted}, 略過 {result.Skipped}";
                if (result.Errors.Any()) ch.StatusText += $" (錯誤 {result.Errors.Count})";
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
                pl.StatusText = $"下載 {result.Downloaded}, 刪除 {result.Deleted}, 略過 {result.Skipped}";
                if (result.Errors.Any()) pl.StatusText += $" (錯誤 {result.Errors.Count})";
                UpdatePlaylistCurrentCount(pl);
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

            channel.StatusText = $"下載 {result.Downloaded}, 刪除 {result.Deleted}, 略過 {result.Skipped}";
            if (result.Errors.Any()) channel.StatusText += $" (錯誤 {result.Errors.Count})";
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

            playlist.StatusText = $"下載 {result.Downloaded}, 刪除 {result.Deleted}, 略過 {result.Skipped}";
            if (result.Errors.Any()) playlist.StatusText += $" (錯誤 {result.Errors.Count})";
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
        _configService.Save(_config);
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
