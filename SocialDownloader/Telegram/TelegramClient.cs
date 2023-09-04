using TL;
using WTelegram;

namespace SocialDownloader.Telegram;

public interface ITelegramClient
{
    Task<User> Login();
    Task<IReadOnlyCollection<ChatBase>> GetAllChats();
    Task<Messages_MessagesBase> GetHistory(ChatBase channel, int offsetId, int limit = int.MaxValue);
    Task DownloadPhoto(Photo photo, Stream stream);
    Task DownloadVideo(Document document, Stream stream);
    string GetSenderName(Messages_MessagesBase messagesMessagesBase, Peer sender);
}

public class TelegramClient : ITelegramClient, IDisposable
{
    private readonly TelegramConfiguration _telegramConfiguration;
    private Client? _client;

    public TelegramClient(TelegramConfiguration telegramConfiguration)
    {
        _telegramConfiguration = telegramConfiguration;
    }

    private Client? CreateClient()
    {
        try
        {
            Helpers.Log = (_, _) => { };

            var client = new Client(f => f switch
            {
                "api_id" => HardcodedSettings.AppId,
                "api_hash" => HardcodedSettings.ApiHash,
                "phone_number" => _telegramConfiguration.Phone,
                "verification_code" => ReadVerificationCode(),
                "password" => _telegramConfiguration.Password,
                "session_pathname" => Path.Combine(Directory.GetCurrentDirectory(), "telegram.session"),
                _ => null
            });
            
            return client;
        }
        catch (TelegramDownloaderException e)
        {
            ConsoleHelper.WriteLineColor(e.Message, ConsoleColor.Red);
        }
        catch (Exception e)
        {
            ConsoleHelper.WriteLineColor(e.ToString(), ConsoleColor.Red);
        }

        return null;
    }
    
    private static string ReadVerificationCode()
    {
        Console.Write("Verification code: ");
        return Console.ReadLine() ?? string.Empty;
    }

    public async Task<User> Login()
    {
        _client ??= CreateClient();
        return await _client.LoginUserIfNeeded();
    }

    public async Task<IReadOnlyCollection<ChatBase>> GetAllChats()
    {
        return (await _client.Messages_GetAllChats()).chats.Values;
    }

    public async Task<Messages_MessagesBase> GetHistory(ChatBase channel, int offsetId, int limit = int.MaxValue)
    {
        return await _client.Messages_GetHistory(channel, offsetId, limit: limit);
    }

    public async Task DownloadPhoto(Photo photo, Stream stream)
    {
        await _client.DownloadFileAsync(photo, stream);
    }

    public async Task DownloadVideo(Document document, Stream stream)
    {
        await _client.DownloadFileAsync(document, stream);
    }

    public string GetSenderName(Messages_MessagesBase history, Peer sender)
    {
        var name = history.UserOrChat(sender);
        return name?.ToString() ?? string.Empty;
    }

    public void Dispose()
    {
        _client?.Dispose();
    }
}