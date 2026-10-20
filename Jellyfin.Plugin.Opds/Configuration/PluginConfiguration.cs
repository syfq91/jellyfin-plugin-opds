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
        EnablePageStreaming = false;
        EnableProgression = false;
        DefaultPageSize = 50;
        ThumbnailWidth = 240;
        AuthCacheDurationMinutes = 10;
    }

    /// <summary>
    /// Gets or sets a value indicating whether the api should allow anonymous access.
    /// </summary>
    public bool AllowAnonymousAccess { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether comic book archives should be
    /// exposed page by page via the OPDS page streaming extension.
    /// </summary>
    public bool EnablePageStreaming { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether reading progression should be
    /// exposed via the OPDS progression specification.
    /// </summary>
    public bool EnableProgression { get; set; }

    /// <summary>
    /// Gets or sets the default number of items per page in OPDS feeds.
    /// </summary>
    public int DefaultPageSize { get; set; }

    /// <summary>
    /// Gets or sets the maximum width in pixels for generated book cover thumbnails.
    /// </summary>
    public int ThumbnailWidth { get; set; }

    /// <summary>
    /// Gets or sets the duration in minutes to cache basic authentication credentials.
    /// </summary>
    public int AuthCacheDurationMinutes { get; set; }
}
