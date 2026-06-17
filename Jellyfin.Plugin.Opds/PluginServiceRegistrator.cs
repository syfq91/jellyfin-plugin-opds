using System.IO;
using Jellyfin.Plugin.Opds.Data;
using Jellyfin.Plugin.Opds.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.EntityFrameworkCore;
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

        var dataPath = OpdsPlugin.Instance!.DataFolderPath;
        Directory.CreateDirectory(dataPath);
        var dbPath = Path.Combine(dataPath, "kosync.db");

        serviceCollection.AddDbContext<KosyncDbContext>(options =>
            options.UseSqlite($"Data Source={dbPath}"));

        serviceCollection.AddScoped<IMd5Computer, Md5Computer>();
        serviceCollection.AddScoped<IKosyncService, KosyncService>();
    }
}
