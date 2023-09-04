using System.Diagnostics;
using System.Globalization;
using System.IO.Abstractions;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using NodaTime;
using NodaTime.Extensions;
using NodaTime.Text;
using TL;

namespace SocialDownloader.Telegram;

public class TelegramDownloader
{
    private IDirectory Directory { get; }
    private IFile File { get; }
    private readonly ITelegramClient _client;
    private readonly TelegramDownloadConfiguration _telegramDownloadConfiguration;
    private CancellationToken _cancellationToken;

    public TelegramDownloader(ITelegramClient client, TelegramDownloadConfiguration telegramDownloadConfiguration, IDirectory directory, IFile file)
    {
        Directory = directory;
        File = file;
        _client = client;
        _telegramDownloadConfiguration = telegramDownloadConfiguration;
    }

    public async Task Execute(string channel, string rootDownloadDirectory, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(channel))
        {
            throw new TelegramDownloaderException("Channel name or Id is not set");
        }

        _cancellationToken = cancellationToken;

        var myself = await _client.Login();
        Console.WriteLine($"We are logged-in as {myself} (id {myself.id})");

        var chats = await _client.GetAllChats();
        var channels = chats
            .Where(c => c.ID.ToString() == channel ||
                        c.Title?.Equals(channel, StringComparison.InvariantCultureIgnoreCase) == true)
            .ToArray();

        if (channels.Length == 0)
        {
            throw new TelegramDownloaderException("No channel with given name or id found");
        }

        if (channels.Length > 1)
        {
            throw new TelegramDownloaderException($"Found {channels.Length} channels with given name or id: \r\n" +
                                                  string.Join("\r\n", channels.Select(c => $"{c.ID} -- {c.Title}")));
        }

        var telegramChannel = channels.Single();

        var downloadDirectory = CreateDownloadDirectory(rootDownloadDirectory, telegramChannel);
        await DownloadMessages(telegramChannel, downloadDirectory);
    }

    private string CreateDownloadDirectory(string rootDownloadDirectory, ChatBase channel)
    {
        var safeDirName = Path.GetInvalidFileNameChars()
            .Aggregate(channel.Title, (current, invalidChar) => current.Replace(invalidChar, '_'));

        var downloadDirectory = Path.Combine(rootDownloadDirectory, "telegram", $"{channel.ID}-{safeDirName}");

        Directory.CreateDirectory(downloadDirectory);
        return downloadDirectory;
    }

    private async Task DownloadMessages(ChatBase channel, string downloadDirectory)
    {
        List<SavedMessage> savedMessages;
        var messagesFileName = Path.Combine(downloadDirectory, "_messages.json");
        if (File.Exists(messagesFileName))
        {
            var content = await File.ReadAllTextAsync(messagesFileName); // todo : replace with stream parsing
            savedMessages = JsonSerializer.Deserialize<List<SavedMessage>>(content) ?? new List<SavedMessage>();
        }
        else
        {
            savedMessages = new List<SavedMessage>();
        }

        var existedMessages = savedMessages.ToDictionary(m => m.Id);

        var jsonSerializerOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.BasicLatin, UnicodeRanges.Cyrillic),
        };

        var offsetId = savedMessages.Any() && !_telegramDownloadConfiguration.ReloadFromNewest ? savedMessages.Min(m => m.Id) : 0;
        while (true)
        {
            Console.Write("Downloading messages... ");
            var history = await _client.GetHistory(channel, offsetId);
            Console.WriteLine("done");
            if (history.Messages.Length == 0)
            {
                ConsoleHelper.WriteLineColor("Looks like everything is downloaded", ConsoleColor.Green);
                break;
            }

            var messageGroups = history.Messages
                .OfType<Message>()
                .GroupBy(m => m.grouped_id != 0 ? m.grouped_id : m.id)
                .OrderByDescending(m => m.First().date);

            var justLoaded = true;
            
            var offsetAlreadySet = false;
            foreach (var messageGroup in messageGroups)
            {
                var mainMessage = messageGroup.FirstOrDefault(m => !string.IsNullOrWhiteSpace(m.message));
                if (mainMessage == null)
                {
                    if (!justLoaded)
                    {
                        offsetId = messageGroup.Max(m => m.ID) + 1;
                        offsetAlreadySet = true;
                        break;
                    }
                    else
                    {
                        mainMessage = messageGroup.First();
                    }
                }
                
                justLoaded = false;

                Console.Write($"[{mainMessage.id}] {mainMessage.date.ToString("yyyy.MM.dd HH:mm:ss... ")}");

                var files = new List<MessageFile>();

                var i = 0;
                foreach (var message in messageGroup.OrderBy(m => m.ID))
                {
                    i++;

                    await TryDownloadFile(i, message, downloadDirectory, files);
                }

                var from = _client.GetSenderName(history, mainMessage.From ?? mainMessage.Peer);
                
                var savedMessage = new SavedMessage
                {
                    Id = mainMessage.ID,
                    DateTime = new DateTimeOffset(mainMessage.Date, TimeSpan.Zero)
                        .ToZonedDateTime()
                        .WithZone(DateTimeZone.ForOffset(Offset.FromHours(4)))
                        .ToString(OffsetDateTimePattern.Rfc3339.PatternText, CultureInfo.GetCultureInfo("ru-RU")),
                    From = from,
                    Text = mainMessage.message,
                    Files = files
                };
                
                if (existedMessages.TryGetValue(mainMessage.ID, out var oldMessage))
                {
                    oldMessage.DateTime = savedMessage.DateTime;
                    oldMessage.Text = savedMessage.Text;
                    oldMessage.Files = savedMessage.Files;
                }
                else
                {
                    savedMessages.Add(savedMessage);   
                }
                
                var w = Stopwatch.StartNew();
                var json = JsonSerializer.SerializeToUtf8Bytes(savedMessages.OrderByDescending(m => m.Id), jsonSerializerOptions);
                await File.WriteAllBytesAsync(messagesFileName, json);
                w.Stop();
            
                Console.WriteLine($"saved ({w.ElapsedMilliseconds}ms)");
            }

            if (!offsetAlreadySet)
            {
                offsetId = history.Messages.Last().ID;
            }

            if (_cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }
        
        await File.WriteAllBytesAsync(messagesFileName, JsonSerializer.SerializeToUtf8Bytes(savedMessages.OrderByDescending(m => m.Id), jsonSerializerOptions));
    }

    private async Task TryDownloadFile(int i, Message message, string downloadDirectory, ICollection<MessageFile> files)
    {
        switch (message.media)
        {
            case MessageMediaPhoto { photo: Photo photo }:
            {
                var downloadedFileName = $"{message.date:yyyyMMdd-HHmmss}-{i}.jpg";
                var messageFile = await DownloadPhoto(downloadDirectory, downloadedFileName, photo);
                files.Add(messageFile);
                break;
            }
            case MessageMediaDocument { document: Document doc }:
            {
                var downloadedFileName = $"{message.date:yyyyMMdd-HHmmss}-{i}";
                var messageFile = await DownloadVideo(downloadDirectory, downloadedFileName, doc);
                files.Add(messageFile);
                break;
            }
        }
    }

    private async Task<MessageFile> DownloadVideo(string downloadDirectory, string downloadedFileName, Document doc)
    {
        if (!_telegramDownloadConfiguration.DownloadVideo)
        {
            return new MessageFile
            {
                Id = doc.ID,
                OriginalFileName = doc.Filename,
                MimeType = doc.mime_type,
                Downloaded = false,
                DownloadedFileName = string.Empty
            };
        }

        var extension = Path.GetExtension(doc.Filename);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = doc.mime_type switch
            {
                "video/mp4" => ".mp4",
                _ => string.Empty
            };
        }

        downloadedFileName = downloadedFileName + extension;
        var fullPath = Path.Combine(downloadDirectory, downloadedFileName);
        if (!File.Exists(fullPath))
        {
            await using var stream = File.Create(fullPath);
            await _client.DownloadVideo(doc, stream);
        }

        return new MessageFile
        {
            Id = doc.ID,
            OriginalFileName = doc.Filename,
            MimeType = doc.mime_type,
            Downloaded = true,
            DownloadedFileName = downloadedFileName
        };
    }

    private async Task<MessageFile> DownloadPhoto(string downloadDirectory, string downloadedFileName, Photo photo)
    {
        var fullPath = Path.Combine(downloadDirectory, downloadedFileName);
        if (!File.Exists(fullPath))
        {
            await using var stream = File.Create(fullPath);
            await _client.DownloadPhoto(photo, stream);
        }

        return new MessageFile
        {
            Id = photo.ID,
            OriginalFileName = string.Empty,
            Downloaded = true,
            DownloadedFileName = downloadedFileName
        };
    }
}
