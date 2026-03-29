namespace ShokzMP3Sync.Models;

public class VideoInfo
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string UploadDate { get; set; } = "";  // YYYYMMDD format
    public int DurationSeconds { get; set; }
    public string? SourceFolder { get; set; }
}
