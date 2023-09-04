using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace SocialDownloader.Configuration;

public static class ConfigurationExtension
{
    public static void Configure<T>(this IServiceCollection services, string sectionName) where T : class
    {
        services.AddOptions<T>()
            .BindConfiguration(sectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();
            
        services.AddSingleton(s => s.GetRequiredService<IOptions<T>>().Value);
    }
}