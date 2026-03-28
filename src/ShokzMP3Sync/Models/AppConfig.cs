using System.Collections.Generic;

namespace ShokzMP3Sync.Models;

public class AppConfig
{
    public string DeviceVolumeName { get; set; } = "SWIM PRO";
    public string YtDlpPath { get; set; } = "yt-dlp";
    public List<ChannelConfig> Channels { get; set; } = new();
}
