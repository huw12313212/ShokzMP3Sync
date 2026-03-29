namespace ShokzMP3Sync.Models;

public class LatestFeedConfig
{
    public bool Enabled { get; set; }
    public string FolderName { get; set; } = "最新動態";
    public double MinHours { get; set; } = 2.0;
}
