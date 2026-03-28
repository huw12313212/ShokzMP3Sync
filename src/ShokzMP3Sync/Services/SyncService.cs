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
}

public class SyncService
{
    private readonly YtDlpService _ytDlp;
    private readonly DeviceService _device;

    public SyncService(YtDlpService ytDlp, DeviceService device)
    {
        _ytDlp = ytDlp;
        _device = device;
    }

    public async Task<SyncResult> SyncChannelAsync(
        ChannelConfig channel,
        string volumeName,
        Action<string>? onStatus = null,
        Action<double>? onProgress = null,
        CancellationToken ct = default)
    {
        var result = new SyncResult { ChannelName = channel.Name };

        // 1. Get latest videos from YouTube
        onStatus?.Invoke($"正在取得 {channel.Name} 的最新影片清單...");
        List<VideoInfo> latestVideos;
        try
        {
            latestVideos = await _ytDlp.GetLatestVideosAsync(channel.Url, channel.KeepCount, ct);
        }
        catch (Exception ex)
        {
            result.Errors.Add($"無法取得影片清單: {ex.Message}");
            return result;
        }

        var latestIds = latestVideos.Select(v => v.Id).ToHashSet();

        // 2. Get existing files on device
        var existingFiles = _device.GetExistingFiles(volumeName, channel.FolderName);

        // 3. Delete expired files
        var toDelete = existingFiles.Where(kv => !latestIds.Contains(kv.Key)).ToList();
        foreach (var (videoId, filePath) in toDelete)
        {
            onStatus?.Invoke($"刪除過期: {Path.GetFileName(filePath)}");
            _device.DeleteFile(filePath);
            // Also delete macOS resource fork file
            var dir = Path.GetDirectoryName(filePath)!;
            var resourceFork = Path.Combine(dir, "._" + Path.GetFileName(filePath));
            _device.DeleteFile(resourceFork);
            result.Deleted++;
        }

        // 4. Download new files
        var toDownload = latestVideos.Where(v => !existingFiles.ContainsKey(v.Id)).ToList();
        _device.EnsureFolder(volumeName, channel.FolderName);
        var outputDir = Path.Combine(_device.GetDevicePath(volumeName), channel.FolderName);

        // Use temp directory for downloads, then move to device
        var tempDir = Path.Combine(Path.GetTempPath(), "ShokzMP3Sync", channel.FolderName);
        Directory.CreateDirectory(tempDir);

        for (int i = 0; i < toDownload.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var video = toDownload[i];
            var progressBase = (double)i / toDownload.Count;
            var progressStep = 1.0 / toDownload.Count;

            onStatus?.Invoke($"下載中 ({i + 1}/{toDownload.Count}): {video.Title}");
            onProgress?.Invoke(progressBase);

            try
            {
                await _ytDlp.DownloadAsMp3Async(video.Id, tempDir,
                    line =>
                    {
                        if (line.Contains('%'))
                            onStatus?.Invoke($"下載中 ({i + 1}/{toDownload.Count}): {video.Title} - {line.Trim()}");
                    }, ct);

                // Move downloaded file to device
                var downloadedFile = Directory.GetFiles(tempDir, $"*[{video.Id}].mp3").FirstOrDefault();
                if (downloadedFile != null)
                {
                    var destFile = Path.Combine(outputDir, Path.GetFileName(downloadedFile));
                    File.Move(downloadedFile, destFile, overwrite: true);
                    result.Downloaded++;
                }
                else
                {
                    result.Errors.Add($"找不到下載的檔案: {video.Title}");
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                result.Errors.Add($"{video.Title}: {ex.Message}");
            }
        }

        result.Skipped = latestVideos.Count - toDownload.Count - result.Errors.Count(e => true) + result.Errors.Count;
        result.Skipped = existingFiles.Count(kv => latestIds.Contains(kv.Key));

        // Cleanup temp
        try { Directory.Delete(tempDir, true); } catch { /* ignore */ }

        onProgress?.Invoke(1.0);
        onStatus?.Invoke($"{channel.Name} 同步完成: 下載 {result.Downloaded}, 刪除 {result.Deleted}, 略過 {result.Skipped}");

        return result;
    }
}
