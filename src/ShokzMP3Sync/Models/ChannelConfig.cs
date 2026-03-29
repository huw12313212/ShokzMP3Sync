namespace ShokzMP3Sync.Models;

public class ChannelConfig
{
    public string Url { get; set; } = "";
    public string Name { get; set; } = "";
    public string FolderName { get; set; } = "";
    public int KeepCount { get; set; } = 10;
    public bool NormalizeAudio { get; set; }
    public bool IncludeLivestreams { get; set; }
}
