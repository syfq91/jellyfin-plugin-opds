namespace Jellyfin.Plugin.Opds.Models;

/// <summary>
/// The outcome of an OPDS progression update.
/// </summary>
/// <param name="Status">The status of the update.</param>
/// <param name="Document">The resulting progression document, when the update succeeded.</param>
public sealed record ProgressionUpdateResult(ProgressionUpdateStatus Status, ProgressionDocument? Document);
