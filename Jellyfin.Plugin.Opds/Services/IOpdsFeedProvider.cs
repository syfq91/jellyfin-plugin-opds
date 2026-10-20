using System;
using System.Threading.Tasks;
using Jellyfin.Plugin.Opds.Models;

namespace Jellyfin.Plugin.Opds.Services;

/// <summary>
/// Opds feed provider.
/// </summary>
public interface IOpdsFeedProvider
{
    /// <summary>
    /// Get the root feeds list.
    /// </summary>
    /// <param name="baseUrl">The request path base.</param>
    /// <returns>The root feed.</returns>
    FeedDto GetFeeds(string baseUrl);

    /// <summary>
    /// Get the alphabetical books feed.
    /// </summary>
    /// <param name="baseUrl">The request path base.</param>
    /// <returns>The alphabetical books feed.</returns>
    FeedDto GetAlphabeticalFeed(string baseUrl);

    /// <summary>
    /// Gets the list of book genres.
    /// </summary>
    /// <param name="baseUrl">The request path base.</param>
    /// <param name="userId">The user id to filter by.</param>
    /// <returns>The list of genres.</returns>
    FeedDto GetBookGenres(string baseUrl, Guid userId);

    /// <summary>
    /// Gets the list of root folders / book libraries.
    /// </summary>
    /// <param name="baseUrl">The request path base.</param>
    /// <param name="userId">The user id to filter by.</param>
    /// <param name="startIndex">The 0-based start index for pagination.</param>
    /// <param name="limit">The maximum number of items to return.</param>
    /// <returns>The root folders feed.</returns>
    FeedDto GetRootFolders(string baseUrl, Guid userId, int? startIndex = null, int? limit = null);

    /// <summary>
    /// Gets the contents of a specific folder.
    /// </summary>
    /// <param name="baseUrl">The request path base.</param>
    /// <param name="userId">The user id to filter by.</param>
    /// <param name="folderId">The folder id.</param>
    /// <param name="startIndex">The 0-based start index for pagination.</param>
    /// <param name="limit">The maximum number of items to return.</param>
    /// <returns>The folder feed, or null if not found.</returns>
    FeedDto? GetFolder(string baseUrl, Guid userId, Guid folderId, int? startIndex = null, int? limit = null);

    /// <summary>
    /// Gets the list of recently added books.
    /// </summary>
    /// <param name="baseUrl">The request path base.</param>
    /// <param name="userId">The user id to filter by.</param>
    /// <param name="startIndex">The 0-based start index for pagination.</param>
    /// <param name="limit">The maximum number of items to return.</param>
    /// <returns>The list of recently added books.</returns>
    FeedDto GetRecentlyAdded(string baseUrl, Guid userId, int? startIndex = null, int? limit = null);

    /// <summary>
    /// Gets the list of favorite books.
    /// </summary>
    /// <param name="baseUrl">The request path base.</param>
    /// <param name="userId">The user id to filter by.</param>
    /// <param name="startIndex">The 0-based start index for pagination.</param>
    /// <param name="limit">The maximum number of items to return.</param>
    /// <returns>The list of favorite books.</returns>
    FeedDto GetFavoriteBooks(string baseUrl, Guid userId, int? startIndex = null, int? limit = null);

    /// <summary>
    /// Get the list of books matching the filter.
    /// </summary>
    /// <param name="baseUrl">The request path base.</param>
    /// <param name="userId">The user id to filter by.</param>
    /// <param name="filterStart">The filter start.</param>
    /// <param name="startIndex">The 0-based start index for pagination.</param>
    /// <param name="limit">The maximum number of items to return.</param>
    /// <returns>The list of books.</returns>
    FeedDto GetAllBooks(string baseUrl, Guid userId, string filterStart, int? startIndex = null, int? limit = null);

    /// <summary>
    /// Gets the list of books within a genre.
    /// </summary>
    /// <param name="baseUrl">The request path base.</param>
    /// <param name="userId">The user id to filter by.</param>
    /// <param name="genreId">The genre id.</param>
    /// <param name="startIndex">The 0-based start index for pagination.</param>
    /// <param name="limit">The maximum number of items to return.</param>
    /// <returns>The books in the genre.</returns>
    FeedDto GetBooksByGenre(string baseUrl, Guid userId, Guid genreId, int? startIndex = null, int? limit = null);

    /// <summary>
    /// Get the book image path.
    /// </summary>
    /// <param name="bookId">The book id.</param>
    /// <returns>The book image path.</returns>
    string? GetBookImage(Guid bookId);

    /// <summary>
    /// Gets the last modified timestamp of the book image.
    /// </summary>
    /// <param name="bookId">The book id.</param>
    /// <returns>The last modified timestamp, or null.</returns>
    DateTime? GetBookImageLastModified(Guid bookId);

    /// <summary>
    /// Gets the downscaled thumbnail bytes of a book cover.
    /// </summary>
    /// <param name="bookId">The book id.</param>
    /// <param name="maxWidth">The maximum width in pixels.</param>
    /// <returns>The jpeg bytes of the thumbnail, or null.</returns>
    byte[]? GetBookThumbnail(Guid bookId, int maxWidth);

    /// <summary>
    /// Get the book path.
    /// </summary>
    /// <param name="bookId">The book id.</param>
    /// <returns>The book path.</returns>
    string? GetBook(Guid bookId);

    /// <summary>
    /// Searches for a book.
    /// </summary>
    /// <param name="baseUrl">The request path base.</param>
    /// <param name="userId">The user id to filter by.</param>
    /// <param name="searchTerm">the search term.</param>
    /// <param name="startIndex">The 0-based start index for pagination.</param>
    /// <param name="limit">The maximum number of items to return.</param>
    /// <returns>The search result.</returns>
    Task<FeedDto> SearchBooks(string baseUrl, Guid userId, string searchTerm, int? startIndex = null, int? limit = null);

    /// <summary>
    /// Gets the search description.
    /// </summary>
    /// <param name="baseUrl">The request path base.</param>
    /// <returns>The search description.</returns>
    OpenSearchDescriptionDto GetSearchDescription(string baseUrl);
}
