using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ShokzMP3Sync.Models;

namespace ShokzMP3Sync.Services;

public class YtDlpService
{
    private readonly string _ytDlpPath;

    // macOS .app bundles have a minimal PATH that excludes Homebrew.
    // We prepend common Homebrew paths so yt-dlp and ffmpeg can be found.
    private static readonly string[] ExtraPaths = new[]
    {
        "/opt/homebrew/bin",
        "/usr/local/bin",
        "/opt/homebrew/sbin",
        "/usr/local/sbin"
    };

    public YtDlpService(string ytDlpPath = "yt-dlp")
    {
        _ytDlpPath = ResolveExecutable(ytDlpPath);
    }

    private static string ResolveExecutable(string name)
    {
        // If already an absolute path, use as-is
        if (Path.IsPathRooted(name))
            return name;

        // Try to find the executable in extra paths (covers .app bundle case)
        foreach (var dir in ExtraPaths)
        {
            var candidate = Path.Combine(dir, name);
            if (File.Exists(candidate))
                return candidate;
        }

        // Fall back to bare name (relies on PATH)
        return name;
    }

    private static void EnrichPath(ProcessStartInfo psi)
    {
        var current = psi.Environment.TryGetValue("PATH", out var p) ? p ?? "" : "";
        var missing = ExtraPaths.Where(d => !current.Contains(d));
        if (missing.Any())
            psi.Environment["PATH"] = string.Join(":", missing) + ":" + current;
    }

    public bool IsAvailable()
    {
        return IsCommandAvailable(_ytDlpPath);
    }

    public static bool IsFfmpegAvailable()
    {
        var ffmpegPath = ResolveExecutable("ffmpeg");
        return IsCommandAvailable(ffmpegPath);
    }

    private static bool IsCommandAvailable(string command)
    {
        try
        {
            var psi = new ProcessStartInfo(command, "-version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            EnrichPath(psi);
            using var process = Process.Start(psi);
            process?.WaitForExit(5000);
            // Some tools (e.g. ffmpeg) return non-zero for --version;
            // if the process started at all, the tool is installed.
            return process != null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Gets the channel name from a YouTube channel URL.
    /// </summary>
    public async Task<string?> GetChannelNameAsync(string channelUrl, CancellationToken ct = default)
    {
        var tabUrl = channelUrl.TrimEnd('/') + "/videos";
        var args = $"--playlist-items 1 --print channel \"{tabUrl}\"";
        var result = await RunAsync(args, ct);
        var name = result.Trim();
        return string.IsNullOrEmpty(name) ? null : name;
    }

    /// <summary>
    /// Gets the latest N video IDs and titles from a channel.
    /// </summary>
    public async Task<List<VideoInfo>> GetLatestVideosAsync(string channelUrl, int count,
        CancellationToken ct = default)
    {
        // Use /videos tab to get only regular uploads (not shorts/live)
        var tabUrl = channelUrl.TrimEnd('/') + "/videos";
        var args = $"--flat-playlist --playlist-items 1:{count} --print id --print title \"{tabUrl}\"";
        var result = await RunAsync(args, ct);

        var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var videos = new List<VideoInfo>();

        for (int i = 0; i + 1 < lines.Length; i += 2)
        {
            videos.Add(new VideoInfo
            {
                Id = lines[i].Trim(),
                Title = lines[i + 1].Trim()
            });
        }

        return videos;
    }

    /// <summary>
    /// Gets all video IDs and titles from a YouTube playlist.
    /// </summary>
    public async Task<List<VideoInfo>> GetPlaylistVideosAsync(string playlistUrl, CancellationToken ct = default)
    {
        var args = $"--flat-playlist --print id --print title \"{playlistUrl}\"";
        var result = await RunAsync(args, ct);

        var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var videos = new List<VideoInfo>();

        for (int i = 0; i + 1 < lines.Length; i += 2)
        {
            videos.Add(new VideoInfo
            {
                Id = lines[i].Trim(),
                Title = lines[i + 1].Trim()
            });
        }

        return videos;
    }

    /// <summary>
    /// Gets the playlist title from a YouTube playlist URL.
    /// </summary>
    public async Task<string?> GetPlaylistNameAsync(string playlistUrl, CancellationToken ct = default)
    {
        var args = $"--flat-playlist --playlist-items 1 --print playlist_title \"{playlistUrl}\"";
        var result = await RunAsync(args, ct);
        var name = result.Trim();
        return string.IsNullOrEmpty(name) ? null : name;
    }

    /// <summary>
    /// Downloads a video as MP3 to the specified output directory.
    /// </summary>
    public async Task DownloadAsMp3Async(string videoId, string outputDir,
        Action<string>? onProgress = null, CancellationToken ct = default)
    {
        var outputTemplate = Path.Combine(outputDir, "%(title)s [%(id)s].%(ext)s");
        var args = $"-x --audio-format mp3 --audio-quality 128K " +
                   $"--no-playlist " +
                   $"-o \"{outputTemplate}\" " +
                   $"\"https://www.youtube.com/watch?v={videoId}\"";

        await RunWithProgressAsync(args, onProgress, ct);
    }

    private async Task<string> RunAsync(string args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(_ytDlpPath, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        EnrichPath(psi);

        using var process = Process.Start(psi)
                            ?? throw new InvalidOperationException("Failed to start yt-dlp");

        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync();
            throw new InvalidOperationException($"yt-dlp failed (exit {process.ExitCode}): {error}");
        }

        return output;
    }

    private async Task RunWithProgressAsync(string args, Action<string>? onProgress, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(_ytDlpPath, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        EnrichPath(psi);

        using var process = Process.Start(psi)
                            ?? throw new InvalidOperationException("Failed to start yt-dlp");

        // Read output line by line for progress
        var outputTask = Task.Run(async () =>
        {
            while (await process.StandardOutput.ReadLineAsync() is { } line)
            {
                ct.ThrowIfCancellationRequested();
                onProgress?.Invoke(line);
            }
        }, ct);

        var errorTask = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync(ct);
        await outputTask;

        if (process.ExitCode != 0)
        {
            var error = await errorTask;
            throw new InvalidOperationException($"yt-dlp failed (exit {process.ExitCode}): {error}");
        }
    }
}
