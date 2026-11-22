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
    /// The path of the regular OPDS catalog, relative to the server base URL.
    /// </summary>
    public const string CatalogPath = "/opds";

    /// <summary>
    /// The path of the optimized feed, relative to the server base URL.
    /// </summary>
    public const string FeedPath = "/opds/optimized";

    /// <summary>
    /// The path of the optimized alphabetical books feed.
    /// </summary>
    public const string BooksPath = FeedPath + "/books";

    /// <summary>
    /// The path of the optimized genres feed.
    /// </summary>
    public const string GenresPath = FeedPath + "/genres";

    /// <summary>
    /// The path of the optimized folders feed.
    /// </summary>
    public const string FoldersPath = FeedPath + "/folders";

    /// <summary>
    /// The path of the optimized search feed.
    /// </summary>
    public const string SearchPath = FeedPath + "/search";

    /// <summary>
    /// The path of the optimized OpenSearch description.
    /// </summary>
    public const string OsdPath = FeedPath + "/osd";

    /// <summary>
    /// The prefix of the optimized download path, relative to the server base URL.
    /// </summary>
    public const string DownloadPath = "/opds/optimized/download/";

    /// <summary>
    /// The cache format version. Bump when the repacking algorithm changes.
    /// </summary>
    public const string CacheVersion = "v1";
}
