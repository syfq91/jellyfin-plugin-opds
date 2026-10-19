using System;
using Jellyfin.Plugin.Opds.Models;

namespace Jellyfin.Plugin.Opds.Services;

/// <summary>
/// Stores the fidelity of reading progression reports (device, title and
/// references), which Jellyfin's own user data cannot hold.
/// </summary>
public interface IProgressionStore
{
    /// <summary>
    /// Stores the last reported progression of a user for a book.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <param name="bookId">The book id.</param>
    /// <param name="entry">The progression entry.</param>
    void Save(Guid userId, Guid bookId, ProgressionStoreEntry entry);

    /// <summary>
    /// Gets the last reported progression of a user for a book.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <param name="bookId">The book id.</param>
    /// <returns>The stored entry, or <c>null</c> when none is known.</returns>
    ProgressionStoreEntry? Get(Guid userId, Guid bookId);
}
