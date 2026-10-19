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

    private readonly IUserDataManager _userDataManager;
    private readonly IPageStreamService _pageStreamService;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProgressionService"/> class.
    /// </summary>
    /// <param name="userDataManager">Instance of the <see cref="IUserDataManager"/> interface.</param>
    /// <param name="pageStreamService">Instance of the <see cref="IPageStreamService"/> interface.</param>
    public ProgressionService(IUserDataManager userDataManager, IPageStreamService pageStreamService)
    {
        _userDataManager = userDataManager;
        _pageStreamService = pageStreamService;
    }

    /// <inheritdoc />
    public ProgressionDocument? GetProgression(Book book, User user)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(user);

        var data = _userDataManager.GetUserData(user, book);
        var totalTicks = book.RunTimeTicks.GetValueOrDefault();

        // An empty payload signals that no progression has been communicated yet.
        if (data is null || data.PlaybackPositionTicks <= 0 || totalTicks <= 0)
        {
            return null;
        }

        // Both book conventions (page ticks and the EPUB percentage convention)
        // map onto the same ratio, because the server scales the total to match.
        return CreateDocument(book, data.PlaybackPositionTicks, totalTicks, data.LastPlayedDate ?? DateTime.UtcNow, user);
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

        // Answer with the stored state, so that clients see the effect of their
        // report; when the total length is unknown the accepted values are echoed.
        var document = GetProgression(book, user)
            ?? new ProgressionDocument
            {
                Progression = incoming.Progression!.Value,
                Modified = modified,
                Device = CreateDevice(user)
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

    private ProgressionDocument CreateDocument(Book book, long positionTicks, long totalTicks, DateTime modified, User user)
    {
        var document = new ProgressionDocument
        {
            Progression = Math.Clamp((double)positionTicks / totalTicks, 0d, 1d),
            Modified = modified.ToUniversalTime(),
            Device = CreateDevice(user)
        };

        var references = GetReferences(book, positionTicks, totalTicks);
        if (references is not null)
        {
            document.References = references;
        }

        return document;
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
