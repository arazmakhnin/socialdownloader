namespace SocialDownloader.Telegram;

public class TelegramDownloaderException : Exception
{
    public TelegramDownloaderException(string message) : base(message)
    {
    }

    public TelegramDownloaderException(string message, Exception innerException) : base(message, innerException)
    {
        
    }
}