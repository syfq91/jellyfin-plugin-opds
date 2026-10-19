using Jellyfin.Plugin.Opds.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Opds;

/// <summary>
/// Register OPDS services.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddScoped<IOpdsFeedProvider, OpdsFeedProvider>();
        serviceCollection.AddSingleton<IPageStreamService, PageStreamService>();
        serviceCollection.AddSingleton<IPdfPageSource, PdfPageSource>();
        serviceCollection.AddSingleton<IProgressionStore, ProgressionStore>();
        serviceCollection.AddSingleton<IProgressionService, ProgressionService>();
    }
}
