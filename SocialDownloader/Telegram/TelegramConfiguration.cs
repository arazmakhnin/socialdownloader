namespace SocialDownloader.Telegram;

public class TelegramConfiguration
{
    public string Phone { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string[] Channels { get; set; } = Array.Empty<string>();
}

public class TelegramDownloadConfiguration
{
    public bool DownloadVideo { get; set; }
    public bool ReloadFromNewest { get; set; }
} 