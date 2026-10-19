using System;

namespace Jellyfin.Plugin.Opds.Models;

/// <summary>
/// Constants of the OPDS page streaming extension.
/// </summary>
internal static class PseConstants
{
    /// <summary>
    /// The OPDS page streaming extension namespace.
    /// </summary>
    public const string Namespace = "http://vaemendis.net/opds-pse/ns";

    /// <summary>
    /// The rel value of a page streaming acquisition link.
    /// </summary>
    public const string StreamRel = "http://vaemendis.net/opds-pse/stream";

    /// <summary>
    /// The media type of pages served by the page streaming endpoint.
    /// </summary>
    public const string JpegMediaType = "image/jpeg";

    /// <summary>
    /// The amount of ticks Jellyfin stores per page of book progress.
    /// </summary>
    public const long TicksPerPage = 10000;

    /// <summary>
    /// Gets the last read page of a document from Jellyfin's book progress value.
    /// </summary>
    /// <param name="playbackPositionTicks">The stored progress position, as page * 10000 ticks.</param>
    /// <param name="pageCount">The total number of pages of the document.</param>
    /// <returns>The 1-based last read page, or <c>null</c> when the document was never read.</returns>
    public static long? GetLastRead(long playbackPositionTicks, int pageCount)
    {
        if (playbackPositionTicks <= 0 || pageCount <= 0)
        {
            return null;
        }

        // The page streaming extension numbers pages from 1.
        return Math.Clamp(playbackPositionTicks / TicksPerPage, 1, pageCount);
    }
}
