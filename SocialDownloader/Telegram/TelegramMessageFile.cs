namespace SocialDownloader.Telegram;

public class MessageFile
{
    public long Id { get; set; }
    public string OriginalFileName { get; set; }
    public string MimeType { get; set; }
    public bool Downloaded { get; set; }
    public string DownloadedFileName { get; set; }
}