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
        // IOpdsFeedProvider is registered as Scoped because it constructs feeds per HTTP request.
        serviceCollection.AddScoped<IOpdsFeedProvider, OpdsFeedProvider>();

        // The following services are Singletons because they maintain cross-request state
        // (in-memory auth cache, page and PDF count caches, progression store lock and persistence).
        // Their injected Jellyfin server dependencies (ILibraryManager, IUserDataManager, IUserManager,
        // IImageEncoder, IApplicationPaths, IFileSystem) are all singletons in Jellyfin, so there are
        // no captive dependency issues. If Jellyfin server lifetimes ever change, re-evaluate these.
        serviceCollection.AddSingleton<IBasicAuthCache, BasicAuthCache>();
        serviceCollection.AddSingleton<IPageStreamService, PageStreamService>();
        serviceCollection.AddSingleton<IPdfPageSource, PdfPageSource>();
        serviceCollection.AddSingleton<IProgressionStore, ProgressionStore>();
        serviceCollection.AddSingleton<IProgressionService, ProgressionService>();
    }
}
