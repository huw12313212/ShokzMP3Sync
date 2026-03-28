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

    // Add channel dialog fields
    [ObservableProperty] private bool _isAddDialogOpen;
    [ObservableProperty] private string _newChannelUrl = "";
    [ObservableProperty] private string _newChannelName = "";
    [ObservableProperty] private string _newChannelFolder = "";
    [ObservableProperty] private int _newChannelKeepCount = 10;
    [ObservableProperty] private bool _isResolvingChannel;
    [ObservableProperty] private int _editingIndex = -1;

    public ObservableCollection<ChannelViewModel> Channels { get; } = new();

    private AppConfig _config = new();

    public void Initialize()
    {
        // Load config
        _config = _configService.Load();
        _ytDlpService = new YtDlpService(_config.YtDlpPath);
        _syncService = new SyncService(_ytDlpService, _deviceService);

        foreach (var ch in _config.Channels)
        {
            var vm = ChannelViewModel.FromConfig(ch);
            UpdateChannelCurrentCount(vm);
            Channels.Add(vm);
        }

        // Check tools
        IsYtDlpAvailable = _ytDlpService.IsAvailable();
        ToolStatusText = IsYtDlpAvailable
            ? "yt-dlp: OK"
            : "yt-dlp: 未安裝 (請執行 brew install yt-dlp)";

        // Start device check timer
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

            // Update current counts
            foreach (var ch in Channels)
                UpdateChannelCurrentCount(ch);
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
        catch
        {
            // Ignore - user can set name manually
        }
        finally
        {
            IsResolvingChannel = false;
        }
    }

    [RelayCommand]
    private void ConfirmAddChannel()
    {
        if (string.IsNullOrWhiteSpace(NewChannelUrl) || string.IsNullOrWhiteSpace(NewChannelName))
            return;

        if (EditingIndex >= 0)
        {
            // Editing existing
            var ch = Channels[EditingIndex];
            ch.Url = NewChannelUrl;
            ch.Name = NewChannelName;
            ch.FolderName = string.IsNullOrWhiteSpace(NewChannelFolder) ? NewChannelName : NewChannelFolder;
            ch.KeepCount = NewChannelKeepCount;
        }
        else
        {
            // Adding new
            var ch = new ChannelViewModel
            {
                Url = NewChannelUrl,
                Name = NewChannelName,
                FolderName = string.IsNullOrWhiteSpace(NewChannelFolder) ? NewChannelName : NewChannelFolder,
                KeepCount = NewChannelKeepCount
            };
            Channels.Add(ch);
        }

        SaveConfig();
        IsAddDialogOpen = false;
    }

    [RelayCommand]
    private void CancelAddChannel()
    {
        IsAddDialogOpen = false;
    }

    [RelayCommand]
    private void RemoveChannel(ChannelViewModel channel)
    {
        Channels.Remove(channel);
        if (IsDeviceConnected)
        {
            _deviceService.DeleteFolderIfExists(_config.DeviceVolumeName, channel.FolderName);
        }
        SaveConfig();
    }

    [RelayCommand]
    private async Task SyncAllAsync()
    {
        if (!IsDeviceConnected || !IsYtDlpAvailable || IsSyncing) return;

        IsSyncing = true;
        _syncCts = new CancellationTokenSource();

        try
        {
            for (int i = 0; i < Channels.Count; i++)
            {
                var ch = Channels[i];
                ch.IsSyncing = true;

                var result = await _syncService.SyncChannelAsync(
                    ch.ToConfig(),
                    _config.DeviceVolumeName,
                    status =>
                    {
                        SyncStatusText = status;
                        ch.StatusText = status;
                    },
                    progress =>
                    {
                        SyncProgress = ((double)i + progress) / Channels.Count;
                    },
                    _syncCts.Token);

                ch.IsSyncing = false;
                ch.StatusText = $"下載 {result.Downloaded}, 刪除 {result.Deleted}, 略過 {result.Skipped}";
                if (result.Errors.Any())
                    ch.StatusText += $" (錯誤 {result.Errors.Count})";

                UpdateChannelCurrentCount(ch);
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
                channel.ToConfig(),
                _config.DeviceVolumeName,
                status => channel.StatusText = status,
                progress => SyncProgress = progress,
                _syncCts.Token);

            channel.StatusText = $"下載 {result.Downloaded}, 刪除 {result.Deleted}, 略過 {result.Skipped}";
            if (result.Errors.Any())
                channel.StatusText += $" (錯誤 {result.Errors.Count})";

            UpdateChannelCurrentCount(channel);
        }
        catch (OperationCanceledException)
        {
            channel.StatusText = "已取消";
        }
        finally
        {
            channel.IsSyncing = false;
            _syncCts = null;
        }
    }

    [RelayCommand]
    private void CancelSync()
    {
        _syncCts?.Cancel();
    }

    private void SaveConfig()
    {
        _config.Channels = Channels.Select(c => c.ToConfig()).ToList();
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
