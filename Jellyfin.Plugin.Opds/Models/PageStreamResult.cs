namespace Jellyfin.Plugin.Opds.Models;

/// <summary>
/// A single page image extracted from a book, as used by the OPDS page streaming extension.
/// </summary>
public class PageStreamResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PageStreamResult"/> class.
    /// </summary>
    /// <param name="content">The page image bytes.</param>
    /// <param name="contentType">The media type of the page image.</param>
    public PageStreamResult(byte[] content, string contentType)
    {
        Content = content;
        ContentType = contentType;
    }

    /// <summary>
    /// Gets the page image bytes.
    /// </summary>
    public byte[] Content { get; }

    /// <summary>
    /// Gets the media type of the page image.
    /// </summary>
    public string ContentType { get; }
}
