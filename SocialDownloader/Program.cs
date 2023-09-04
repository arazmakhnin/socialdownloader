using System.IO.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SocialDownloader;
using SocialDownloader.Configuration;
using SocialDownloader.Telegram;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(ConfigureServices)
    .Build();
await host.RunAsync();

static void ConfigureServices(IServiceCollection services)
{
    services.AddHostedService<DownloadService>();

    var fileSystem = new FileSystem();
    services.AddSingleton<IFileSystem>(fileSystem);
    services.AddSingleton(fileSystem.Directory);
    services.AddSingleton(fileSystem.File);

    services.AddSingleton<ITelegramClient, TelegramClient>();
    services.AddSingleton<TelegramDownloader>();
    
	// todo : Add user secrets
    services.Configure<DownloadConfiguration>("Download");
    services.Configure<TelegramConfiguration>("Telegram");
    services.Configure<TelegramDownloadConfiguration>("Telegram:Configuration");
}
