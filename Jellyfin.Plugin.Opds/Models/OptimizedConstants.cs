namespace Jellyfin.Plugin.Opds.Models;

/// <summary>
/// Constants for the optimized EPUB feed.
/// </summary>
public static class OptimizedConstants
{
    /// <summary>
    /// The media type of EPUB publications.
    /// </summary>
    public const string MediaType = "application/epub+zip";

    /// <summary>
    /// The OPDS acquisition link relation used for optimized downloads.
    /// </summary>
    public const string AcquisitionRel = "http://opds-spec.org/acquisition";

    /// <summary>
    /// The title of the optimized acquisition link.
    /// </summary>
    public const string LinkTitle = "Optimized EPUB (downscaled images)";

    /// <summary>
    /// The path of the optimized feed, relative to the server base URL.
    /// </summary>
    public const string FeedPath = "/opds/optimized";

    /// <summary>
    /// The prefix of the optimized download path, relative to the server base URL.
    /// </summary>
    public const string DownloadPath = "/opds/optimized/download/";

    /// <summary>
    /// The cache format version. Bump when the repacking algorithm changes.
    /// </summary>
    public const string CacheVersion = "v1";
}
