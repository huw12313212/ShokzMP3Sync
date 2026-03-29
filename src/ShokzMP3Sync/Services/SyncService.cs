using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ShokzMP3Sync.Models;

namespace ShokzMP3Sync.Services;

public class SyncResult
{
    public string ChannelName { get; set; } = "";
    public int Downloaded { get; set; }
    public int Deleted { get; set; }
    public int Skipped { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<VideoInfo> KeptVideos { get; set; } = new();
}

public class SyncService
{
    private readonly YtDlpService _ytDlp;
    private readonly DeviceService _device;

    private static readonly string LogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ShokzMP3Sync");

    private static readonly string LogPath = Path.Combine(LogDir, "sync.log");

    public SyncService(YtDlpService ytDlp, DeviceService device)
    {
        _ytDlp = ytDlp;
        _device = device;
    }

    private static void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(LogDir);
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n";
            File.AppendAllText(LogPath, line);
        }
        catch { /* ignore logging failures */ }
    }

    public async Task<SyncResult> SyncChannelAsync(
        ChannelConfig channel,
        string volumeName,
        Action<string>? onStatus = null,
        Action<double>? onProgress = null,
        CancellationToken ct = default)
    {
        var result = new SyncResult { ChannelName = channel.Name };
        Log($"[{channel.Name}] 開始同步頻道 (KeepCount={channel.KeepCount}, Normalize={channel.NormalizeAudio}, IncludeLive={channel.IncludeLivestreams})");

        // 1. Fetch extra videos to compensate for members-only
        onStatus?.Invoke($"正在取得 {channel.Name} 的最新影片清單...");
        var fetchCount = channel.KeepCount * 3;
        List<VideoInfo> allVideos;
        try
        {
            allVideos = await _ytDlp.GetLatestVideosAsync(channel.Url, fetchCount, ct,
                includeLivestreams: channel.IncludeLivestreams);
        }
        catch (Exception ex)
        {
            var err = $"無法取得影片清單: {ex.Message}";
            result.Errors.Add(err);
            Log($"[{channel.Name}] {err}");
            return result;
        }

        // 2. Get existing files on device
        var existingFiles = _device.GetExistingFiles(volumeName, channel.FolderName);

        // 3. Walk through videos in order, build target set of KeepCount videos
        //    (already on device = kept, not on device = need download, members-only = skip)
        _device.EnsureFolder(volumeName, channel.FolderName);
        var outputDir = Path.Combine(_device.GetDevicePath(volumeName), channel.FolderName);
        var tempDir = Path.Combine(Path.GetTempPath(), "ShokzMP3Sync", channel.FolderName);
        Directory.CreateDirectory(tempDir);

        var keptIds = new HashSet<string>();
        int downloadAttempt = 0;

        foreach (var video in allVideos)
        {
            ct.ThrowIfCancellationRequested();
            if (keptIds.Count >= channel.KeepCount) break;

            // Already on device
            if (existingFiles.ContainsKey(video.Id))
            {
                keptIds.Add(video.Id);
                video.SourceFolder = channel.FolderName;
                result.KeptVideos.Add(video);
                result.Skipped++;
                continue;
            }

            // Need to download
            downloadAttempt++;
            onStatus?.Invoke($"下載中 ({downloadAttempt}): {video.Title}");
            onProgress?.Invoke((double)keptIds.Count / channel.KeepCount);

            try
            {
                await _ytDlp.DownloadAsMp3Async(video.Id, tempDir,
                    line =>
                    {
                        if (line.Contains('%'))
                            onStatus?.Invoke($"下載中: {video.Title} - {line.Trim()}");
                    }, channel.NormalizeAudio, ct);

                var downloadedFile = Directory.GetFiles(tempDir, $"*[{video.Id}].mp3").FirstOrDefault();
                if (downloadedFile != null)
                {
                    var destFile = Path.Combine(outputDir, Path.GetFileName(downloadedFile));
                    File.Move(downloadedFile, destFile, overwrite: true);
                    result.Downloaded++;
                    keptIds.Add(video.Id);
                    video.SourceFolder = channel.FolderName;
                    result.KeptVideos.Add(video);
                }
                else
                {
                    var err = $"找不到下載的檔案: {video.Title}";
                    result.Errors.Add(err);
                    Log($"[{channel.Name}] {err}");
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex.Message.Contains("members-only") || ex.Message.Contains("members on level"))
            {
                Log($"[{channel.Name}] 跳過會員專屬: {video.Title}");
                // Don't count toward kept — continue to next video
            }
            catch (Exception ex)
            {
                var err = $"{video.Title}: {ex.Message}";
                result.Errors.Add(err);
                Log($"[{channel.Name}] {err}");
            }
        }

        // 4. Delete files on device that are NOT in our kept set
        foreach (var (videoId, filePath) in existingFiles)
        {
            if (!keptIds.Contains(videoId))
            {
                onStatus?.Invoke($"刪除過期: {Path.GetFileName(filePath)}");
                _device.DeleteFile(filePath);
                var dir = Path.GetDirectoryName(filePath)!;
                var resourceFork = Path.Combine(dir, "._" + Path.GetFileName(filePath));
                _device.DeleteFile(resourceFork);
                result.Deleted++;
            }
        }

        // Cleanup temp
        try { Directory.Delete(tempDir, true); } catch { /* ignore */ }

        onProgress?.Invoke(1.0);
        Log($"[{channel.Name}] 同步完成: 下載 {result.Downloaded}, 刪除 {result.Deleted}, 略過 {result.Skipped}, 裝置上共 {keptIds.Count} 首");
        onStatus?.Invoke($"{channel.Name} 同步完成: 下載 {result.Downloaded}, 刪除 {result.Deleted}, 略過 {result.Skipped}");

        return result;
    }

    public async Task<SyncResult> SyncPlaylistAsync(
        PlaylistConfig playlist,
        string volumeName,
        Action<string>? onStatus = null,
        Action<double>? onProgress = null,
        CancellationToken ct = default)
    {
        var result = new SyncResult { ChannelName = playlist.Name };
        Log($"[{playlist.Name}] 開始同步播放清單 (Normalize={playlist.NormalizeAudio})");

        // 1. Get all videos from playlist
        onStatus?.Invoke($"正在取得播放清單 {playlist.Name} 的影片...");
        List<VideoInfo> playlistVideos;
        try
        {
            playlistVideos = await _ytDlp.GetPlaylistVideosAsync(playlist.Url, ct);
        }
        catch (Exception ex)
        {
            var err = $"無法取得播放清單: {ex.Message}";
            result.Errors.Add(err);
            Log($"[{playlist.Name}] {err}");
            return result;
        }

        var playlistIds = playlistVideos.Select(v => v.Id).ToHashSet();

        // 2. Get existing files on device
        var existingFiles = _device.GetExistingFiles(volumeName, playlist.FolderName);

        // 3. Delete files not in playlist
        var toDelete = existingFiles.Where(kv => !playlistIds.Contains(kv.Key)).ToList();
        foreach (var (videoId, filePath) in toDelete)
        {
            onStatus?.Invoke($"刪除: {Path.GetFileName(filePath)}");
            _device.DeleteFile(filePath);
            var dir = Path.GetDirectoryName(filePath)!;
            var resourceFork = Path.Combine(dir, "._" + Path.GetFileName(filePath));
            _device.DeleteFile(resourceFork);
            result.Deleted++;
        }

        // 4. Download new files
        var toDownload = playlistVideos.Where(v => !existingFiles.ContainsKey(v.Id)).ToList();
        _device.EnsureFolder(volumeName, playlist.FolderName);

        var tempDir = Path.Combine(Path.GetTempPath(), "ShokzMP3Sync", playlist.FolderName);
        Directory.CreateDirectory(tempDir);
        var outputDir = Path.Combine(_device.GetDevicePath(volumeName), playlist.FolderName);

        for (int i = 0; i < toDownload.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var video = toDownload[i];

            onStatus?.Invoke($"下載中 ({i + 1}/{toDownload.Count}): {video.Title}");
            onProgress?.Invoke((double)i / toDownload.Count);

            try
            {
                await _ytDlp.DownloadAsMp3Async(video.Id, tempDir,
                    line =>
                    {
                        if (line.Contains('%'))
                            onStatus?.Invoke($"下載中 ({i + 1}/{toDownload.Count}): {video.Title} - {line.Trim()}");
                    }, playlist.NormalizeAudio, ct);

                var downloadedFile = Directory.GetFiles(tempDir, $"*[{video.Id}].mp3").FirstOrDefault();
                if (downloadedFile != null)
                {
                    var destFile = Path.Combine(outputDir, Path.GetFileName(downloadedFile));
                    File.Move(downloadedFile, destFile, overwrite: true);
                    result.Downloaded++;
                }
                else
                {
                    var err = $"找不到下載的檔案: {video.Title}";
                    result.Errors.Add(err);
                    Log($"[{playlist.Name}] {err}");
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex.Message.Contains("members-only") || ex.Message.Contains("members on level"))
            {
                Log($"[{playlist.Name}] 跳過會員專屬: {video.Title}");
                result.Skipped++;
            }
            catch (Exception ex)
            {
                var err = $"{video.Title}: {ex.Message}";
                result.Errors.Add(err);
                Log($"[{playlist.Name}] {err}");
            }
        }

        result.Skipped = existingFiles.Count(kv => playlistIds.Contains(kv.Key));

        try { Directory.Delete(tempDir, true); } catch { }

        onProgress?.Invoke(1.0);
        onStatus?.Invoke($"{playlist.Name} 同步完成: 下載 {result.Downloaded}, 刪除 {result.Deleted}, 略過 {result.Skipped}");

        return result;
    }

    public async Task<SyncResult> SyncLatestFeedAsync(
        LatestFeedConfig config,
        string volumeName,
        List<ChannelConfig> channels,
        Action<string>? onStatus = null,
        Action<double>? onProgress = null,
        CancellationToken ct = default)
    {
        var result = new SyncResult { ChannelName = config.FolderName };
        Log($"[{config.FolderName}] 開始建立最新動態 (MinHours={config.MinHours})");

        // 1. Fetch videos with dates from each channel
        onStatus?.Invoke("正在取得各頻道影片日期資訊...");
        var allVideos = new List<VideoInfo>();

        for (int c = 0; c < channels.Count; c++)
        {
            ct.ThrowIfCancellationRequested();
            var ch = channels[c];
            onStatus?.Invoke($"取得日期資訊: {ch.Name} ({c + 1}/{channels.Count})");
            onProgress?.Invoke((double)c / channels.Count * 0.3); // First 30% for metadata

            try
            {
                var videos = await _ytDlp.GetLatestVideosWithDateAsync(
                    ch.Url, ch.KeepCount * 3, ct, ch.IncludeLivestreams);
                foreach (var v in videos)
                    v.SourceFolder = ch.FolderName;
                allVideos.AddRange(videos);
            }
            catch (Exception ex)
            {
                Log($"[{config.FolderName}] 無法取得 {ch.Name} 的日期: {ex.Message}");
            }
        }

        // 2. Sort by upload date descending (newest first)
        allVideos = allVideos
            .Where(v => !string.IsNullOrEmpty(v.UploadDate))
            .OrderByDescending(v => v.UploadDate)
            .ToList();

        // 3. Match with actual files on device, accumulate duration
        var selectedFiles = new List<(VideoInfo video, string sourcePath)>();
        double totalSeconds = 0;
        var targetSeconds = config.MinHours * 3600;
        var seenIds = new HashSet<string>();

        foreach (var video in allVideos)
        {
            if (totalSeconds >= targetSeconds) break;
            if (seenIds.Contains(video.Id)) continue;
            seenIds.Add(video.Id);

            // Find the actual file on device
            var channelDir = Path.Combine(_device.GetDevicePath(volumeName), video.SourceFolder!);
            if (!Directory.Exists(channelDir)) continue;

            var files = Directory.GetFiles(channelDir, $"*[{video.Id}].mp3");
            if (files.Length == 0) continue;

            selectedFiles.Add((video, files[0]));
            totalSeconds += video.DurationSeconds > 0 ? video.DurationSeconds : 600; // default 10min if unknown
        }

        Log($"[{config.FolderName}] 選取 {selectedFiles.Count} 個影片, 總時長 {totalSeconds / 3600:F1} 小時");

        // 4. Clear and rebuild the latest feed folder
        onStatus?.Invoke($"正在建立 {config.FolderName} 資料夾...");
        onProgress?.Invoke(0.4);
        _device.DeleteFolderIfExists(volumeName, config.FolderName);
        _device.EnsureFolder(volumeName, config.FolderName);
        var feedDir = Path.Combine(_device.GetDevicePath(volumeName), config.FolderName);

        // 5. Copy files with numbered prefix (001_ = newest)
        for (int i = 0; i < selectedFiles.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var (video, sourcePath) = selectedFiles[i];
            var prefix = $"{i + 1:D3}_";
            var destName = prefix + Path.GetFileName(sourcePath);
            var destPath = Path.Combine(feedDir, destName);

            onStatus?.Invoke($"複製 ({i + 1}/{selectedFiles.Count}): {video.Title}");
            onProgress?.Invoke(0.4 + 0.6 * ((double)i / selectedFiles.Count));

            try
            {
                File.Copy(sourcePath, destPath, overwrite: true);
                result.Downloaded++;
            }
            catch (Exception ex)
            {
                var err = $"複製失敗 {video.Title}: {ex.Message}";
                result.Errors.Add(err);
                Log($"[{config.FolderName}] {err}");
            }
        }

        onProgress?.Invoke(1.0);
        Log($"[{config.FolderName}] 完成: 複製 {result.Downloaded} 個檔案");
        onStatus?.Invoke($"{config.FolderName} 完成: {result.Downloaded} 首, {totalSeconds / 3600:F1} 小時");

        return result;
    }
}
