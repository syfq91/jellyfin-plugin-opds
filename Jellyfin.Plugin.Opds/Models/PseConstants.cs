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
}
