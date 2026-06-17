using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Opds.Configuration;

/// <summary>
/// Opds plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
    /// </summary>
    public PluginConfiguration()
    {
        AllowAnonymousAccess = false;
        EnableSyncServer = true;
        EnableUserRegistration = false;
    }

    /// <summary>
    /// Gets or sets a value indicating whether the api should allow anonymous access.
    /// </summary>
    public bool AllowAnonymousAccess { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the KoSync server is enabled.
    /// </summary>
    public bool EnableSyncServer { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether user registration via KoSync is enabled.
    /// </summary>
    public bool EnableUserRegistration { get; set; }

    /// <summary>
    /// Gets the current plugin configuration instance.
    /// </summary>
    public static PluginConfiguration? Instance { get; internal set; }
}
