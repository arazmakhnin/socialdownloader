using Microsoft.Extensions.Hosting;
using SocialDownloader.Configuration;
using SocialDownloader.Telegram;

namespace SocialDownloader;

public class DownloadService : IHostedService
{
    private readonly TelegramDownloader _telegramDownloader;
    private readonly IHostApplicationLifetime _applicationLifetime;
    private readonly TelegramConfiguration _telegramConfiguration;
    private readonly DownloadConfiguration _downloadConfiguration;

    public DownloadService(TelegramDownloader telegramDownloader, IHostApplicationLifetime applicationLifetime, 
        TelegramConfiguration telegramConfiguration, DownloadConfiguration downloadConfiguration)
    {
        _telegramDownloader = telegramDownloader;
        _applicationLifetime = applicationLifetime;
        _telegramConfiguration = telegramConfiguration;
        _downloadConfiguration = downloadConfiguration;
    }
    
    public Task StartAsync(CancellationToken cancellationToken)
    {
        return Task.Run(async () =>
        {
            await RunDownloadJobs(cancellationToken);
            _applicationLifetime.StopApplication();
        }, cancellationToken);
    }

    private async Task RunDownloadJobs(CancellationToken cancellationToken)
    {
        try
        {
            foreach (var channel in _telegramConfiguration.Channels)
            {
                await _telegramDownloader.Execute(channel, _downloadConfiguration.Directory, cancellationToken);   
            }
        }
        catch (TelegramDownloaderException e)
        {
            ConsoleHelper.WriteLineColor(e.Message, ConsoleColor.Red);
        }
        catch (Exception e)
        {
            ConsoleHelper.WriteLineColor(e.ToString(), ConsoleColor.Red);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}