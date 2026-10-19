namespace Jellyfin.Plugin.Opds.Services;

/// <summary>
/// Provides access to the pages of PDF documents.
/// </summary>
public interface IPdfPageSource
{
    /// <summary>
    /// Gets the number of pages of the given PDF document.
    /// </summary>
    /// <param name="path">The path of the PDF document.</param>
    /// <returns>The number of pages, or <c>null</c> when the document cannot be read.</returns>
    int? GetPageCount(string path);

    /// <summary>
    /// Renders a single page of the given PDF document as a jpeg image.
    /// </summary>
    /// <param name="path">The path of the PDF document.</param>
    /// <param name="pageNumber">The zero-based number of the page.</param>
    /// <param name="maxWidth">The optional maximum width of the rendered page.</param>
    /// <returns>The jpeg image bytes, or <c>null</c> when the page cannot be rendered.</returns>
    byte[]? RenderPage(string path, int pageNumber, int? maxWidth);
}
