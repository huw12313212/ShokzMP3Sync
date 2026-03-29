namespace ShokzMP3Sync.Models;

public class PlaylistConfig
{
    public string Url { get; set; } = "";
    public string Name { get; set; } = "";
    public string FolderName { get; set; } = "";
    public bool NormalizeAudio { get; set; }
}
