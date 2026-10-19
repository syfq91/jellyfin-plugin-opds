using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.Opds.Models;
using MediaBrowser.Controller.Entities;

namespace Jellyfin.Plugin.Opds.Services;

/// <summary>
/// Provides the reading progression of books, as used by the OPDS progression specification.
/// </summary>
public interface IProgressionService
{
    /// <summary>
    /// Gets the reading progression of a book for a user.
    /// </summary>
    /// <param name="book">The book.</param>
    /// <param name="user">The user.</param>
    /// <returns>The progression document, or <c>null</c> when no progression is known yet.</returns>
    ProgressionDocument? GetProgression(Book book, User user);

    /// <summary>
    /// Updates the reading progression of a book for a user.
    /// </summary>
    /// <param name="book">The book.</param>
    /// <param name="user">The user.</param>
    /// <param name="incoming">The progression document reported by the client.</param>
    /// <returns>The outcome of the update.</returns>
    ProgressionUpdateResult UpdateProgression(Book book, User user, ProgressionDocument? incoming);
}
