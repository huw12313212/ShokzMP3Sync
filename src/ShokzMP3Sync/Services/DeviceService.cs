using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace ShokzMP3Sync.Services;

public class DeviceService
{
    private static readonly Regex VideoIdRegex = new(@"\[([a-zA-Z0-9_-]{11})\]\.mp3$");

    public string GetDevicePath(string volumeName)
    {
        return Path.Combine("/Volumes", volumeName);
    }

    public bool IsDeviceConnected(string volumeName)
    {
        return Directory.Exists(GetDevicePath(volumeName));
    }

    public long GetFreeSpace(string volumeName)
    {
        var path = GetDevicePath(volumeName);
        if (!Directory.Exists(path)) return 0;
        var info = new DriveInfo(path);
        return info.AvailableFreeSpace;
    }

    public long GetTotalSpace(string volumeName)
    {
        var path = GetDevicePath(volumeName);
        if (!Directory.Exists(path)) return 0;
        var info = new DriveInfo(path);
        return info.TotalSize;
    }

    /// <summary>
    /// Gets the set of YouTube video IDs already on the device for a given channel folder.
    /// </summary>
    public HashSet<string> GetExistingVideoIds(string volumeName, string folderName)
    {
        var folderPath = Path.Combine(GetDevicePath(volumeName), folderName);
        if (!Directory.Exists(folderPath))
            return new HashSet<string>();

        return Directory.GetFiles(folderPath, "*.mp3")
            .Where(f => !Path.GetFileName(f).StartsWith("._"))
            .Select(f => VideoIdRegex.Match(Path.GetFileName(f)))
            .Where(m => m.Success)
            .Select(m => m.Groups[1].Value)
            .ToHashSet();
    }

    /// <summary>
    /// Gets all mp3 files in a channel folder, mapped by video ID.
    /// </summary>
    public Dictionary<string, string> GetExistingFiles(string volumeName, string folderName)
    {
        var folderPath = Path.Combine(GetDevicePath(volumeName), folderName);
        if (!Directory.Exists(folderPath))
            return new Dictionary<string, string>();

        return Directory.GetFiles(folderPath, "*.mp3")
            .Where(f => !Path.GetFileName(f).StartsWith("._"))
            .Select(f => new { Path = f, Match = VideoIdRegex.Match(Path.GetFileName(f)) })
            .Where(x => x.Match.Success)
            .ToDictionary(x => x.Match.Groups[1].Value, x => x.Path);
    }

    public void EnsureFolder(string volumeName, string folderName)
    {
        var folderPath = Path.Combine(GetDevicePath(volumeName), folderName);
        Directory.CreateDirectory(folderPath);
    }

    public void DeleteFile(string filePath)
    {
        if (File.Exists(filePath))
            File.Delete(filePath);
    }

    public void DeleteFolderIfExists(string volumeName, string folderName)
    {
        var folderPath = Path.Combine(GetDevicePath(volumeName), folderName);
        if (Directory.Exists(folderPath))
            Directory.Delete(folderPath, true);
    }
}
