namespace SocialDownloader.Telegram;

public class SavedMessage
{
    public int Id { get; set; }
    public string From { get; set; }
    public string Text { get; set; }
    public string DateTime { get; set; }
    public List<MessageFile> Files { get; set; }
}