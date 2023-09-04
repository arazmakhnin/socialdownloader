using System.IO.Abstractions.TestingHelpers;
using System.Text;
using Moq;
using Newtonsoft.Json;
using NodaTime.Text;
using NUnit.Framework;
using Shouldly;
using SocialDownloader.Telegram;
using TL;

namespace SocialDownloader.Tests;

[TestFixture]
public class TelegramTests
{
    private Mock<ITelegramClient> _mockTelegramClient;
    private List<ChatBase> _allChats;
    private List<MessageBase> _allMessages;
    private MockFileSystem _fileSystem;
    private CancellationTokenSource _cancellationTokenSource;
    private int _limit;

    [SetUp]
    public void Setup()
    {
        _limit = int.MaxValue;
        
        _allChats = new List<ChatBase>();
        _allMessages = new List<MessageBase>();

        _mockTelegramClient = new Mock<ITelegramClient>();
        _mockTelegramClient.Setup(m => m.Login())
            .ReturnsAsync(new User { id = 1 });

        _mockTelegramClient.Setup(m => m.GetAllChats())
            .ReturnsAsync(() => _allChats);

        _mockTelegramClient.Setup(m => m.GetHistory(It.IsAny<Channel>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns<Channel, int, int>(GetMockMessages);

        _mockTelegramClient.Setup(m => m.GetSenderName(It.IsAny<Messages_MessagesBase>(), It.IsAny<Peer>()))
            .Returns<Messages_MessagesBase, Peer>((_, _) => "Test");
        
        _fileSystem = new MockFileSystem();

        CreateNewDownloader();
    }

    private Task<Messages_MessagesBase> GetMockMessages(Channel channel, int offsetId, int limit)
    {
        IEnumerable<MessageBase> query = _allMessages;
        if (offsetId > 0)
        {
            query = query.Where(m => m.ID < offsetId);
        }
        
        var messages = query.Take(Math.Min(limit, _limit)).ToArray();

        Messages_MessagesBase result = new Messages_Messages
        {
            messages = messages
        };
        return Task.FromResult(result);
    }

    [Test]
    public void Create_WithEmptyChannelName_ShouldThrowException()
    {
        // Arrange
        var channel = string.Empty;
        var downloader = new TelegramDownloader(_mockTelegramClient.Object, new TelegramDownloadConfiguration(), _fileSystem.Directory, _fileSystem.File);

        // Act
        var exception = Should.Throw<TelegramDownloaderException>(
            async () => await downloader.Execute(channel, "", default));

        // Assert
        exception.Message.ShouldBe("Channel name or Id is not set");
    }

    [Test]
    public void Execute_WithNoChannelFound_ShouldThrowException()
    {
        // Arrange
        const string channel = "asd";
        var downloader = new TelegramDownloader(_mockTelegramClient.Object, new TelegramDownloadConfiguration(), _fileSystem.Directory, _fileSystem.File);

        // Act
        var exception = Should.Throw<TelegramDownloaderException>(
            async () => await downloader.Execute(channel, "", default));

        // Assert
        exception.Message.ShouldBe("No channel with given name or id found");
    }

    [Test]
    public void Execute_WithMultipleChannelFound_ShouldThrowException()
    {
        // Arrange
        _allChats.AddRange(new List<ChatBase>
        {
            new Channel
            {
                id = 123,
                title = "qwe"
            },
            new Channel
            {
                id = 234,
                title = "123"
            },
            new Channel
            {
                id = 1,
                title = "Another channel"
            }
        });
        
        var downloader = new TelegramDownloader(_mockTelegramClient.Object, new TelegramDownloadConfiguration(), _fileSystem.Directory, _fileSystem.File);

        const string channel = "123";

        // Act
        var exception = Should.Throw<TelegramDownloaderException>(
            async () => await downloader.Execute(channel, "", default));

        // Assert
        exception.Message.ShouldStartWith("Found 2 channels with given name or id");
    }

    [Test]
    [TestCase("asd", "/home/telegram/123-asd")]
    [TestCase("asd/asd", "/home/telegram/123-asd_asd")]
    public async Task Execute_WithOneChannelFound_ShouldCreateFolderAndEmptyJsonFile(string channelName, string expectedDir)
    {
        // Arrange
        _allChats.Add(new Channel { id = 123, title = channelName });
        var downloader = new TelegramDownloader(_mockTelegramClient.Object, new TelegramDownloadConfiguration(), _fileSystem.Directory, _fileSystem.File);
        _fileSystem.Directory.Exists(expectedDir).ShouldBeFalse();

        // Act
        await downloader.Execute(channelName, "/home", default);

        // Assert
        _fileSystem.Directory.Exists(expectedDir).ShouldBeTrue();
    }

    [Test]
    public async Task Execute_WithNoMessages_ShouldSaveEmptyJsonFile()
    {
        // Arrange
        // Act
        var folder = await Execute();

        // Assert
        var messages = await ParseResultMessages(folder);
        messages.ShouldBeEmpty();
    }

    [Test]
    public async Task Execute_WithOneTextMessage_ShouldSaveTextMessage()
    {
//         var d = new DateTime(2022, 5, 1, 7, 8, 9, DateTimeKind.Utc);
//         var z = new DateTimeOffset(d).ToZonedDateTime();
//         var z2 = z.WithZone(DateTimeZoneProviders.Tzdb["Europe/Samara"]);
// new ZonedDateTime()
//         var q = z2.ToString(OffsetDateTimePattern.Rfc3339.PatternText, new CultureInfo("ru-RU"));

        // Arrange
        AddMessages(new Message
        {
            id = 5,
            date = new DateTime(2022, 5, 1, 7, 8, 9),
            message = "test text"
        });

        // Act
        var folder = await Execute();

        // Assert
        var messages = await ParseResultMessages(folder);
        messages.Count.ShouldBe(1);
        messages[0].ShouldSatisfyAllConditions(
            m => m.Id.ShouldBe(5),
            m => m.From.ShouldBe("Test"),
            m => m.Text.ShouldBe("test text"),
            // m => DateTime.ParseExact(m.DateTime, "yyyy.MM.dd HH:mm:ss", new CultureInfo("ru-RU")).ToUniversalTime().ShouldBe(messageTime),
            m => VerifyUtcTime(m.DateTime, new DateTime(2022, 5, 1, 7, 8, 9)),
            m => m.Files.ShouldBeEmpty());
    }

    [Test]
    public async Task Execute_WithTwoTextMessagesInTwoRequests_ShouldSaveTextMessages()
    {
        // Arrange
        AddMessages(new Message
            {
                id = 5,
                date = new DateTime(2022, 5, 1, 7, 8, 9),
                message = "test text"
            },
            new Message
            {
                id = 4,
                date = new DateTime(2022, 6, 1, 7, 8, 9),
                message = "test text 2"
            });

        _limit = 1;

        // Act
        var folder = await Execute();

        // Assert
        var messages = await ParseResultMessages(folder);
        messages.Count.ShouldBe(2);
        messages[0].ShouldSatisfyAllConditions(
            m => m.Id.ShouldBe(5),
            m => m.Text.ShouldBe("test text"),
            m => VerifyUtcTime(m.DateTime, new DateTime(2022, 5, 1, 7, 8, 9)),
            m => m.Files.ShouldBeEmpty());

        messages[1].ShouldSatisfyAllConditions(
            m => m.Id.ShouldBe(4),
            m => m.Text.ShouldBe("test text 2"),
            m => VerifyUtcTime(m.DateTime, new DateTime(2022, 6, 1, 7, 8, 9)),
            m => m.Files.ShouldBeEmpty());
    }

    [Test]
    public async Task Execute_WithOnePhotoMessage_ShouldSaveMessageAndPhoto()
    {
        // Arrange
        AddMessages(new Message
        {
            id = 5,
            date = new DateTime(2022, 5, 1, 7, 8, 9),
            message = "test text",
            media = AddPhoto(1, "Photo content")
        });

        // Act
        var folder = await Execute();

        // Assert
        var messages = await ParseResultMessages(folder);
        messages.Count.ShouldBe(1);
        messages[0].ShouldSatisfyAllConditions(
            m => m.Id.ShouldBe(5),
            m => m.Text.ShouldBe("test text"),
            m => VerifyUtcTime(m.DateTime, new DateTime(2022, 5, 1, 7, 8, 9)),
            m => m.Files.Count.ShouldBe(1));

        messages[0].Files[0].ShouldSatisfyAllConditions(
            f => f.Id.ShouldBe(1),
            f => f.OriginalFileName.ShouldBeEmpty(),
            f => f.Downloaded.ShouldBeTrue(),
            f => f.DownloadedFileName.ShouldBe("20220501-070809-1.jpg")); // todo : What about PNG?

        var downloadedFileName = Path.Combine(folder, messages[0].Files[0].DownloadedFileName);
        _fileSystem.File.Exists(downloadedFileName).ShouldBeTrue();
        (await _fileSystem.File.ReadAllTextAsync(downloadedFileName)).ShouldBe("Photo content");
    }

    [Test]
    public async Task Execute_WithOneVideoMessage_ShouldSaveMessageAndVideo()
    {
        // Arrange
        AddMessages(new Message
        {
            id = 5,
            date = new DateTime(2022, 5, 1, 7, 8, 9),
            message = "test text",
            media = AddVideo(1, "OriginalName.mp4", "Video content")
        });

        // Act
        var folder = await Execute(downloadVideo: true);

        // Assert
        var messages = await ParseResultMessages(folder);
        messages.Count.ShouldBe(1);
        messages[0].ShouldSatisfyAllConditions(
            m => m.Id.ShouldBe(5),
            m => m.Text.ShouldBe("test text"),
            m => VerifyUtcTime(m.DateTime, new DateTime(2022, 5, 1, 7, 8, 9)),
            m => m.Files.Count.ShouldBe(1));

        messages[0].Files[0].ShouldSatisfyAllConditions(
            f => f.Id.ShouldBe(1),
            f => f.OriginalFileName.ShouldBe("OriginalName.mp4"),
            f => f.Downloaded.ShouldBeTrue(),
            f => f.DownloadedFileName.ShouldBe("20220501-070809-1.mp4")); 

        var downloadedFileName = Path.Combine(folder, messages[0].Files[0].DownloadedFileName);
        _fileSystem.File.Exists(downloadedFileName).ShouldBeTrue();
        (await _fileSystem.File.ReadAllTextAsync(downloadedFileName)).ShouldBe("Video content");
    }

    [Test]
    public async Task Execute_WithOneVideoMessage_AndNotDownloadingVideoOption_ShouldSaveMessageWithoutVideo()
    {
        // Arrange
        AddMessages(new Message
        {
            id = 5,
            date = new DateTime(2022, 5, 1, 7, 8, 9),
            message = "test text",
            media = AddVideo(1, "OriginalName.mp4", "Video content")
        });

        // Act
        var folder = await Execute(downloadVideo: false);

        // Assert
        var messages = await ParseResultMessages(folder);
        messages.Count.ShouldBe(1);
        messages[0].ShouldSatisfyAllConditions(
            m => m.Id.ShouldBe(5),
            m => m.Text.ShouldBe("test text"),
            m => VerifyUtcTime(m.DateTime, new DateTime(2022, 5, 1, 7, 8, 9)),
            m => m.Files.Count.ShouldBe(1));

        messages[0].Files[0].ShouldSatisfyAllConditions(
            f => f.Id.ShouldBe(1),
            f => f.OriginalFileName.ShouldBe("OriginalName.mp4"),
            f => f.Downloaded.ShouldBeFalse(),
            f => f.DownloadedFileName.ShouldBeEmpty());

        _fileSystem.Directory.GetFiles(folder).Length.ShouldBe(1); // It's a _messages.json
        
        _mockTelegramClient.Verify(m => m.DownloadVideo(It.IsAny<Document>(), It.IsAny<Stream>()), Times.Never);
    }

    [Test]
    public async Task Execute_WithMultiplePhotosAndVideosInOneMessage_ShouldSaveMessageAndAllFiles()
    {
        // Arrange
        AddMessages(
            new Message
            {
                id = 5,
                date = new DateTime(2022, 5, 1, 7, 8, 9),
                message = "test text",
                media = AddPhoto(10, "Photo content"),
                grouped_id = 100
            },
            new Message
            {
                id = 6,
                date = new DateTime(2022, 5, 1, 7, 8, 9),
                media = AddPhoto(20, "Second photo content"),
                grouped_id = 100
            },
            new Message
            {
                id = 7,
                date = new DateTime(2022, 5, 1, 7, 8, 9),
                media = AddVideo(30, "original.mp4", "Video content"),
                grouped_id = 100
            });

        // Act
        var folder = await Execute(downloadVideo: true);

        // Assert
        var messages = await ParseResultMessages(folder);
        messages.Count.ShouldBe(1);
        messages[0].ShouldSatisfyAllConditions(
            m => m.Id.ShouldBe(5),
            m => m.Text.ShouldBe("test text"),
            m => VerifyUtcTime(m.DateTime, new DateTime(2022, 5, 1, 7, 8, 9)),
            m => m.Files.Count.ShouldBe(3));

        messages[0].Files[0].ShouldSatisfyAllConditions(
            f => f.Id.ShouldBe(10),
            f => f.OriginalFileName.ShouldBeEmpty(),
            f => f.Downloaded.ShouldBeTrue(),
            f => f.DownloadedFileName.ShouldBe("20220501-070809-1.jpg"));

        messages[0].Files[1].ShouldSatisfyAllConditions(
            f => f.Id.ShouldBe(20),
            f => f.OriginalFileName.ShouldBeEmpty(),
            f => f.Downloaded.ShouldBeTrue(),
            f => f.DownloadedFileName.ShouldBe("20220501-070809-2.jpg"));

        messages[0].Files[2].ShouldSatisfyAllConditions(
            f => f.Id.ShouldBe(30),
            f => f.OriginalFileName.ShouldBe("original.mp4"),
            f => f.Downloaded.ShouldBeTrue(),
            f => f.DownloadedFileName.ShouldBe("20220501-070809-3.mp4"));

        (await _fileSystem.File.ReadAllTextAsync(Path.Combine(folder, messages[0].Files[0].DownloadedFileName)))
            .ShouldBe("Photo content");
        (await _fileSystem.File.ReadAllTextAsync(Path.Combine(folder, messages[0].Files[1].DownloadedFileName)))
            .ShouldBe("Second photo content");
        (await _fileSystem.File.ReadAllTextAsync(Path.Combine(folder, messages[0].Files[2].DownloadedFileName)))
            .ShouldBe("Video content");
    }

    [Test]
    public async Task Execute_WithCancellation_ShouldSaveOnlyOneMessage()
    {
        // Arrange
        AddMessages(new Message
        {
            id = 5,
            date = new DateTime(2022, 5, 1, 7, 8, 9),
            message = "test text"
        });

        AddMessages(new Message
        {
            id = 4,
            date = new DateTime(2022, 4, 1, 7, 8, 9),
            message = "test text 2"
        });
        
        //_telegramClient.SetMessageLimit(1);
        _limit = 1;

        // Act
        _cancellationTokenSource.Cancel();
        var folder = await Execute();

        // Assert
        var messages = await ParseResultMessages(folder);
        messages.Count.ShouldBe(1);
        messages[0].Id.ShouldBe(5);
    }

    [Test]
    public async Task Execute_WithCancellation_ShouldSaveBothMessagesAfterSecondExecution()
    {
        // Arrange
        AddMessages(new Message
        {
            id = 5,
            date = new DateTime(2022, 5, 1, 7, 8, 9),
            message = "test text"
        });

        AddMessages(new Message
        {
            id = 4,
            date = new DateTime(2022, 4, 1, 7, 8, 9),
            message = "test text 2"
        });
        
        _limit = 1;

        // Act
        _cancellationTokenSource.Cancel();
        var folder = await Execute();

        CreateNewDownloader();
        _mockTelegramClient.Invocations.Clear();

        await Execute();

        // Assert
        var messages = await ParseResultMessages(folder);
        messages.Count.ShouldBe(2);
        messages[0].Id.ShouldBe(5);
        messages[1].Id.ShouldBe(4);

        VerifyHistoryInvocations(5, 4);
    }

    private void VerifyHistoryInvocations(params int[] offsetIds)
    {
        foreach (var offsetId in offsetIds)
        {
            _mockTelegramClient.Verify(m => m.GetHistory(It.IsAny<Channel>(), offsetId, It.IsAny<int>()), Times.Once);            
        }
        
        _mockTelegramClient.Verify(m => m.GetHistory(It.IsAny<Channel>(), It.IsNotIn(offsetIds), It.IsAny<int>()), Times.Never);
    }

    [Test]
    public async Task Execute_WithReloadFromNewest_ShouldLoadNewestMessage()
    {
        // Arrange
        var message = new Message
        {
            id = 4,
            date = new DateTime(2022, 4, 1, 7, 8, 9),
            message = "test text 2"
        };
        AddMessages(message);

        var folder = await Execute();

        CreateNewDownloader();
        _mockTelegramClient.Invocations.Clear();

        AddMessages(new Message
        {
            id = 5,
            date = new DateTime(2022, 5, 1, 7, 8, 9),
            message = "test text"
        }, message);

        // Act
        await Execute(reloadFromNewest: true);

        // Assert
        var messages = await ParseResultMessages(folder);
        messages.Count.ShouldBe(2);
        messages[0].Id.ShouldBe(5);
        messages[1].Id.ShouldBe(4);

        VerifyHistoryInvocations(0, 4);
    }
    
    [Test]
    public async Task Execute_WithReloadFromNewest_ShouldUpdateMessageTextIfItWasEdited()
    {
        // Arrange
        var message = new Message
        {
            id = 4,
            date = new DateTime(2022, 4, 1, 7, 8, 9),
            message = "test text 2"
        };
        AddMessages(message);

        var folder = await Execute();

        CreateNewDownloader();
        _mockTelegramClient.Invocations.Clear();

        // Act
        message.message = "text 3";
        
        await Execute(reloadFromNewest: true);

        // Assert
        var messages = await ParseResultMessages(folder);
        messages.Count.ShouldBe(1);
        messages[0].ShouldSatisfyAllConditions(
            m => m.Id.ShouldBe(4),
            m => m.Text.ShouldBe("text 3"));

        VerifyHistoryInvocations(0, 4);
    }
    
    [Test]
    public async Task Execute_WithReloadFromNewest_ShouldNotReloadMedia()
    {
        // Arrange
        AddMessages(new Message
            {
                id = 5,
                date = new DateTime(2022, 5, 1, 7, 8, 9),
                message = "test text",
                media = AddPhoto(10, "Photo content"),
                grouped_id = 100
            },
            new Message
            {
                id = 6,
                date = new DateTime(2022, 5, 1, 7, 8, 9),
                media = AddPhoto(20, "Second photo content"),
                grouped_id = 100
            },
            new Message
            {
                id = 7,
                date = new DateTime(2022, 5, 1, 7, 8, 9),
                media = AddVideo(30, "original.mp4", "Video content"),
                grouped_id = 100
            });

        var folder = await Execute();

        CreateNewDownloader();
        _mockTelegramClient.Invocations.Clear();
        
        AddMessages(new Message
        {
            id = 8,
            date = new DateTime(2022, 6, 1, 7, 8, 9),
            message = "test text 2"
        });

        // Act
        await Execute(reloadFromNewest: true);

        // Assert
        var messages = await ParseResultMessages(folder);
        messages.Count.ShouldBe(2);
        messages[0].Id.ShouldBe(8);
        messages[1].Id.ShouldBe(5);

        VerifyHistoryInvocations(0, 5);
        _mockTelegramClient.Verify(m => m.DownloadPhoto(It.IsAny<Photo>(), It.IsAny<Stream>()), Times.Never);
        _mockTelegramClient.Verify(m => m.DownloadVideo(It.IsAny<Document>(), It.IsAny<Stream>()), Times.Never);
    }
    
    [Test]
    public async Task Execute_WithReloadFromNewest_AndDownloadVideo_ShouldReloadVideo()
    {
        // Arrange
        AddMessages(new Message
            {
                id = 5,
                date = new DateTime(2022, 5, 1, 7, 8, 9),
                message = "test text",
                media = AddPhoto(10, "Photo content"),
                grouped_id = 100
            },
            new Message
            {
                id = 6,
                date = new DateTime(2022, 5, 1, 7, 8, 9),
                media = AddPhoto(20, "Second photo content"),
                grouped_id = 100
            },
            new Message
            {
                id = 7,
                date = new DateTime(2022, 5, 1, 7, 8, 9),
                media = AddVideo(30, "original.mp4", "Video content"),
                grouped_id = 100
            });

        var folder = await Execute();
        
        var messages1 = await ParseResultMessages(folder);
        messages1[0].Files[2].Downloaded.ShouldBeFalse(); // Video should not be downloaded

        CreateNewDownloader();
        _mockTelegramClient.Invocations.Clear();
        
        AddMessages(new Message
        {
            id = 8,
            date = new DateTime(2022, 6, 1, 7, 8, 9),
            message = "test text 2"
        });

        // Act
        await Execute(downloadVideo: true, reloadFromNewest: true);

        // Assert
        var messages2 = await ParseResultMessages(folder);
        messages2.Count.ShouldBe(2);
        messages2[0].Id.ShouldBe(8);
        messages2[1].Id.ShouldBe(5);
        
        messages2[1].Files[2].Downloaded.ShouldBeTrue();
        
        var downloadedFileName = Path.Combine(folder, messages2[1].Files[2].DownloadedFileName);
        _fileSystem.File.Exists(downloadedFileName).ShouldBeTrue();

        VerifyHistoryInvocations(0, 5);
        _mockTelegramClient.Verify(m => m.DownloadPhoto(It.IsAny<Photo>(), It.IsAny<Stream>()), Times.Never);
    }
    
    [Test]
    public async Task Execute_WithMultiplePhotosInOneMessage_AndMainMessageWasNotGotBecauseOfLimit_ShouldReloadToGetMainMessage()
    {
        // Arrange
        AddMessages(
            new Message
            {
                id = 5,
                date = new DateTime(2022, 5, 1, 7, 8, 9),
                message = "test text",
                media = AddPhoto(10, "Photo content"),
                grouped_id = 100
            },
            new Message
            {
                id = 6,
                date = new DateTime(2022, 5, 1, 7, 8, 9),
                media = AddPhoto(20, "Second photo content"),
                grouped_id = 100
            },
            new Message
            {
                id = 7,
                date = new DateTime(2022, 5, 1, 7, 8, 9),
                message = "test text"
            });

        _limit = 2;

        // Act
        var folder = await Execute(downloadVideo: true);

        // Assert
        var messages = await ParseResultMessages(folder);
        messages.Count.ShouldBe(2);
        messages[0].Id.ShouldBe(7);
        messages[1].ShouldSatisfyAllConditions(
            m => m.Id.ShouldBe(5),
            m => m.Text.ShouldBe("test text"),
            m => VerifyUtcTime(m.DateTime, new DateTime(2022, 5, 1, 7, 8, 9)),
            m => m.Files.Count.ShouldBe(2));

        messages[1].Files[0].ShouldSatisfyAllConditions(
            f => f.Id.ShouldBe(10),
            f => f.OriginalFileName.ShouldBeEmpty(),
            f => f.Downloaded.ShouldBeTrue(),
            f => f.DownloadedFileName.ShouldBe("20220501-070809-1.jpg"));

        messages[1].Files[1].ShouldSatisfyAllConditions(
            f => f.Id.ShouldBe(20),
            f => f.OriginalFileName.ShouldBeEmpty(),
            f => f.Downloaded.ShouldBeTrue(),
            f => f.DownloadedFileName.ShouldBe("20220501-070809-2.jpg"));

        (await _fileSystem.File.ReadAllTextAsync(Path.Combine(folder, messages[1].Files[0].DownloadedFileName)))
            .ShouldBe("Photo content");
        (await _fileSystem.File.ReadAllTextAsync(Path.Combine(folder, messages[1].Files[1].DownloadedFileName)))
            .ShouldBe("Second photo content");
        
        VerifyHistoryInvocations(0, 7, 5);
    }

    private MessageMediaPhoto AddPhoto(int id, string content)
    {
        var photo = new Photo
        {
            id = id
        };

        _mockTelegramClient.Setup(m => m.DownloadPhoto(photo, It.IsAny<Stream>()))
            .Callback<Photo, Stream>((_, s) => s.Write(Encoding.UTF8.GetBytes(content)));
        
        return new MessageMediaPhoto
        {
            photo = photo
        };
            
        // return _telegramClient.AddPhoto(id, content);
    }

    private MessageMediaDocument AddVideo(int id, string originalFileName, string content)
    {
        var doc = new Document
        {
            id = id,
            attributes = new DocumentAttribute[]
            {
                new DocumentAttributeFilename
                {
                    file_name = originalFileName
                }
            }
        };

        _mockTelegramClient.Setup(m => m.DownloadVideo(doc, It.IsAny<Stream>()))
            .Callback<Document, Stream>((_, s) => s.Write(Encoding.UTF8.GetBytes(content)));

        return new MessageMediaDocument
        {
            document = doc
        };
        
        // return _telegramClient.AddVideo(id, originalFileName, content);
    }

    private async Task<string> Execute(bool downloadVideo = false, bool reloadFromNewest = false)
    {
        _allChats.Clear();
        _allChats.Add(new Channel { id = 123, title = "Any" });

        var configuration = new TelegramDownloadConfiguration
        {
            DownloadVideo = downloadVideo,
            ReloadFromNewest = reloadFromNewest
        };
        var downloader = new TelegramDownloader(_mockTelegramClient.Object, configuration, _fileSystem.Directory, _fileSystem.File);
        await downloader.Execute("Any", "/home", _cancellationTokenSource.Token);

        return "/home/telegram/123-Any";
    }

    private void AddMessages(params MessageBase[] messages)
    {
        _allMessages.AddRange(messages);
        _allMessages.Sort((m1, m2) => m2.ID - m1.ID);
    }
    
    private static void VerifyUtcTime(string dateTime, DateTime expected)
    {
        // DateTime.ParseExact(dateTime, "yyyy.MM.dd HH:mm:ss", new CultureInfo("ru-RU"))
        //     .ToUniversalTime()
        //     .ShouldBe(expected);

        OffsetDateTimePattern.Rfc3339.Parse(dateTime).Value.InFixedZone().ShouldSatisfyAllConditions(
            d => d.ToDateTimeUtc().ShouldBe(expected),
            d => d.Zone.GetUtcOffset(d.ToInstant()).ToTimeSpan().TotalHours.ShouldBe(4));
    }

    private async Task<List<SavedMessage>> ParseResultMessages(string folder)
    {
        var messagesFile = Path.Combine(folder, "_messages.json");
        _fileSystem.File.Exists(messagesFile).ShouldBeTrue();
        return JsonConvert.DeserializeObject<List<SavedMessage>>(await _fileSystem.File.ReadAllTextAsync(messagesFile));
    }

    private void CreateNewDownloader()
    {
        _cancellationTokenSource = new CancellationTokenSource();
    }
}