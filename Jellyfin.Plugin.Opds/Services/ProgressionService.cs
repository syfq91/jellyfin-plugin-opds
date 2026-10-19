using System;
using System.Collections.Generic;
using System.Threading;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.Opds.Models;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.Opds.Services;

/// <summary>
/// Provides the reading progression of books, by mapping the OPDS progression
/// specification onto Jellyfin's per-user item data.
/// </summary>
public class ProgressionService : IProgressionService
{
    /// <summary>
    /// The maximum amount of references accepted in a progression document.
    /// </summary>
    private const int MaxReferences = 64;

    /// <summary>
    /// The maximum length of a single reference.
    /// </summary>
    private const int MaxReferenceLength = 4096;

    /// <summary>
    /// The maximum age difference between a stored report and the user data
    /// it describes for the report to still be considered its source; small
    /// enough to ignore positions written by other clients right after it.
    /// </summary>
    private static readonly TimeSpan FreshnessTolerance = TimeSpan.FromSeconds(1);

    private readonly IUserDataManager _userDataManager;
    private readonly IPageStreamService _pageStreamService;
    private readonly IProgressionStore _progressionStore;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProgressionService"/> class.
    /// </summary>
    /// <param name="userDataManager">Instance of the <see cref="IUserDataManager"/> interface.</param>
    /// <param name="pageStreamService">Instance of the <see cref="IPageStreamService"/> interface.</param>
    /// <param name="progressionStore">Instance of the <see cref="IProgressionStore"/> interface.</param>
    public ProgressionService(
        IUserDataManager userDataManager,
        IPageStreamService pageStreamService,
        IProgressionStore progressionStore)
    {
        _userDataManager = userDataManager;
        _pageStreamService = pageStreamService;
        _progressionStore = progressionStore;
    }

    /// <inheritdoc />
    public ProgressionDocument? GetProgression(Book book, User user)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(user);

        var data = _userDataManager.GetUserData(user, book);
        var totalTicks = book.RunTimeTicks.GetValueOrDefault();
        if (data is null || totalTicks <= 0)
        {
            return null;
        }

        var entry = GetFreshEntry(user.Id, book.Id, data);

        // An empty payload signals that no progression has been communicated yet.
        if (data.PlaybackPositionTicks <= 0 && entry is null)
        {
            return null;
        }

        // Both book conventions (page ticks and the EPUB percentage convention)
        // map onto the same ratio, because the server scales the total to match.
        return CreateDocument(book, user, data, totalTicks, entry);
    }

    /// <inheritdoc />
    public ProgressionUpdateResult UpdateProgression(Book book, User user, ProgressionDocument? incoming)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(user);

        if (!IsValid(incoming))
        {
            return new ProgressionUpdateResult(ProgressionUpdateStatus.InvalidPayload, null);
        }

        var modified = incoming!.Modified!.Value.ToUniversalTime();
        var data = _userDataManager.GetUserData(user, book) ?? new UserItemData { Key = string.Empty };

        // Reject regressions: a client reporting an older timestamp than the
        // stored one is told a more recent progression point is available.
        if (data.LastPlayedDate is not null && modified < data.LastPlayedDate.Value)
        {
            return new ProgressionUpdateResult(ProgressionUpdateStatus.StaleProgression, null);
        }

        var totalTicks = book.RunTimeTicks.GetValueOrDefault();
        if (totalTicks > 0)
        {
            data.PlaybackPositionTicks = Math.Min(
                (long)Math.Round(incoming.Progression!.Value * totalTicks),
                totalTicks);
        }

        data.LastPlayedDate = modified;
        _userDataManager.SaveUserData(user, book, data, UserDataSaveReason.PlaybackProgress, CancellationToken.None);

        // Remember the fidelity of the report, which Jellyfin's user data
        // has no fields for.
        var entry = new ProgressionStoreEntry
        {
            Modified = modified,
            PositionTicks = data.PlaybackPositionTicks,
            Device = incoming.Device,
            Title = incoming.Title,
            References = incoming.References is { Count: > 0 } ? new List<string>(incoming.References) : null
        };
        _progressionStore.Save(user.Id, book.Id, entry);

        // Answer with the stored state, so that clients see the effect of their
        // report; when the total length is unknown the accepted values are echoed.
        var document = GetProgression(book, user)
            ?? new ProgressionDocument
            {
                Progression = incoming.Progression!.Value,
                Modified = modified,
                Device = entry.Device ?? CreateDevice(user),
                Title = entry.Title,
                References = entry.References is { Count: > 0 } ? new List<string>(entry.References) : null
            };

        return new ProgressionUpdateResult(ProgressionUpdateStatus.Success, document);
    }

    private static ProgressionDevice CreateDevice(User user)
    {
        // Jellyfin does not persist the device of a progression report, so the
        // device is synthesized from the user the progression belongs to.
        return new ProgressionDevice
        {
            Id = "urn:uuid:" + user.Id,
            Name = "Jellyfin (" + user.Username + ")"
        };
    }

    /// <summary>
    /// Builds a progression document from the stored user data, preferring
    /// the reported fidelity of a fresh store entry over the synthesized
    /// device and the derived reference.
    /// </summary>
    /// <param name="book">The book.</param>
    /// <param name="user">The user the progression belongs to.</param>
    /// <param name="data">The stored user data.</param>
    /// <param name="totalTicks">The total length of the book.</param>
    /// <param name="entry">The fresh store entry, when there is one.</param>
    /// <returns>The progression document.</returns>
    private ProgressionDocument CreateDocument(
        Book book,
        User user,
        UserItemData data,
        long totalTicks,
        ProgressionStoreEntry? entry)
    {
        var document = new ProgressionDocument
        {
            Progression = Math.Clamp((double)data.PlaybackPositionTicks / totalTicks, 0d, 1d),
            Modified = (data.LastPlayedDate ?? DateTime.UtcNow).ToUniversalTime()
        };

        if (entry is not null)
        {
            // Report what the OPDS client that wrote this progression sent.
            document.Device = entry.Device;
            document.Title = entry.Title;
            if (entry.References is { Count: > 0 })
            {
                document.References = new List<string>(entry.References);
            }
        }
        else
        {
            // Jellyfin does not persist the device of a progression report,
            // so without a stored report it is synthesized from the user.
            document.Device = CreateDevice(user);
        }

        if (document.References is null)
        {
            // No references were reported (or the entry is gone): derive the
            // reference of the page the position points at.
            var references = GetReferences(book, data.PlaybackPositionTicks, totalTicks);
            if (references is not null)
            {
                document.References = references;
            }
        }

        return document;
    }

    /// <summary>
    /// Gets the stored report that describes the current user data, if any.
    /// The entry only counts when it is the report that produced the stored
    /// position and timestamp; positions written by other clients since then
    /// make it stale, and the fallbacks take over.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <param name="bookId">The book id.</param>
    /// <param name="data">The stored user data.</param>
    /// <returns>The fresh entry, or <c>null</c> when there is none.</returns>
    private ProgressionStoreEntry? GetFreshEntry(Guid userId, Guid bookId, UserItemData data)
    {
        if (data.LastPlayedDate is null)
        {
            return null;
        }

        var entry = _progressionStore.Get(userId, bookId);
        if (entry is null
            || entry.Device is null
            || entry.PositionTicks != data.PlaybackPositionTicks
            || Math.Abs(entry.Modified.Ticks - data.LastPlayedDate.Value.Ticks) > FreshnessTolerance.Ticks)
        {
            return null;
        }

        return entry;
    }

    /// <summary>
    /// Derives the reference of the page the position points at, from the
    /// natural page order of the book (an archive entry path, or a PDF page
    /// fragment). Only available for books with a page model.
    /// </summary>
    /// <param name="book">The book.</param>
    /// <param name="positionTicks">The stored position.</param>
    /// <param name="totalTicks">The total length of the book.</param>
    /// <returns>The references, or <c>null</c> when the book has no page model.</returns>
    private List<string>? GetReferences(Book book, long positionTicks, long totalTicks)
    {
        var pageCount = _pageStreamService.GetPageCount(book);
        if (pageCount <= 0 || totalTicks <= 0)
        {
            return null;
        }

        var pageIndex = (int)((double)positionTicks * pageCount / totalTicks);
        var reference = _pageStreamService.GetPageReference(book, Math.Clamp(pageIndex, 0, pageCount - 1));
        return reference is null ? null : new List<string> { reference };
    }

    private static bool IsValid(ProgressionDocument? document)
    {
        if (document is null
            || document.Progression is not (>= 0d and <= 1d)
            || document.Modified is null
            || document.Device is null
            || string.IsNullOrEmpty(document.Device.Id)
            || string.IsNullOrEmpty(document.Device.Name))
        {
            return false;
        }

        if (document.References is not null
            && (document.References.Count > MaxReferences
                || document.References.Exists(reference => reference is null || reference.Length > MaxReferenceLength)))
        {
            return false;
        }

        return true;
    }
}
