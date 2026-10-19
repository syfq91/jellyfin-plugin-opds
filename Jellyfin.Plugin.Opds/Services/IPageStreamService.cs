using System;
using Jellyfin.Plugin.Opds.Models;
using MediaBrowser.Controller.Entities;

namespace Jellyfin.Plugin.Opds.Services;

/// <summary>
/// Provides page by page access to books, as used by the OPDS page streaming extension.
/// </summary>
public interface IPageStreamService
{
    /// <summary>
    /// Gets the number of pages of a book.
    /// </summary>
    /// <param name="book">The book.</param>
    /// <returns>The page count, or 0 when the book cannot be streamed page by page.</returns>
    int GetPageCount(Book book);

    /// <summary>
    /// Gets a single page of a book as an image.
    /// </summary>
    /// <param name="bookId">The book id.</param>
    /// <param name="pageNumber">The zero-based page number.</param>
    /// <param name="maxWidth">The optional maximum width of the returned image.</param>
    /// <returns>The page image, or <c>null</c> when the page does not exist.</returns>
    PageStreamResult? GetPage(Guid bookId, int pageNumber, int? maxWidth);
}
