namespace Jellyfin.Plugin.Opds.Models;

/// <summary>
/// The outcome of an OPDS progression update.
/// </summary>
public enum ProgressionUpdateStatus
{
    /// <summary>
    /// The progression was stored.
    /// </summary>
    Success = 0,

    /// <summary>
    /// The provided progression document is invalid.
    /// </summary>
    InvalidPayload = 1,

    /// <summary>
    /// A more recent progression is already stored.
    /// </summary>
    StaleProgression = 2
}
