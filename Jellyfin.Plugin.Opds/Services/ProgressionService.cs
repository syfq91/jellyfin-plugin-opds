using System;
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

    /// <summary>
    /// Initializes a new instance of the <see cref="ProgressionService"/> class.
    /// </summary>
    /// <param name="userDataManager">Instance of the <see cref="IUserDataManager"/> interface.</param>
    public ProgressionService(IUserDataManager userDataManager)
    {
        _userDataManager = userDataManager;
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
        var progression = Math.Clamp((double)data.PlaybackPositionTicks / totalTicks, 0d, 1d);
        return CreateDocument(progression, data.LastPlayedDate ?? DateTime.UtcNow, user);
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
            ?? CreateDocument(incoming.Progression!.Value, modified, user);
        return new ProgressionUpdateResult(ProgressionUpdateStatus.Success, document);
    }

    private static ProgressionDocument CreateDocument(double progression, DateTime modified, User user)
    {
        // Jellyfin does not persist the device of a progression report, so the
        // device is synthesized from the user the progression belongs to.
        return new ProgressionDocument
        {
            Progression = progression,
            Modified = modified.ToUniversalTime(),
            Device = new ProgressionDevice
            {
                Id = "urn:uuid:" + user.Id,
                Name = "Jellyfin (" + user.Username + ")"
            }
        };
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
