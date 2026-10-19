#pragma warning disable CA2227
#pragma warning disable CA1002

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Opds.Models;

/// <summary>
/// The fidelity of a reading progression report: the device, title and
/// references a client sent, which Jellyfin's own user data cannot hold.
/// </summary>
public sealed class ProgressionStoreEntry
{
    /// <summary>
    /// Gets or sets the timestamp of the report, as accepted by the service.
    /// </summary>
    [JsonPropertyName("modified")]
    public DateTime Modified { get; set; }

    /// <summary>
    /// Gets or sets the reading position (in ticks) that was stored in
    /// Jellyfin's user data for the report.
    /// </summary>
    [JsonPropertyName("positionTicks")]
    public long PositionTicks { get; set; }

    /// <summary>
    /// Gets or sets the device that reported the progression.
    /// </summary>
    [JsonPropertyName("device")]
    public ProgressionDevice? Device { get; set; }

    /// <summary>
    /// Gets or sets the title reported alongside the progression.
    /// </summary>
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>
    /// Gets or sets the references reported alongside the progression.
    /// </summary>
    [JsonPropertyName("references")]
    public List<string>? References { get; set; }
}
