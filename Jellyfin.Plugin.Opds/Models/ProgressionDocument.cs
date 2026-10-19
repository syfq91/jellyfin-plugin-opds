#pragma warning disable CA2227
#pragma warning disable CA1002

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Opds.Models;

/// <summary>
/// An OPDS progression document, as defined by the OPDS progression specification.
/// </summary>
public sealed class ProgressionDocument
{
    /// <summary>
    /// Gets or sets text that identifies or contextualizes the progression,
    /// such as a chapter title.
    /// </summary>
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>
    /// Gets or sets the timestamp of the last known progression.
    /// </summary>
    [JsonPropertyName("modified")]
    public DateTime? Modified { get; set; }

    /// <summary>
    /// Gets or sets the device that provided the last known progression.
    /// </summary>
    [JsonPropertyName("device")]
    public ProgressionDevice? Device { get; set; }

    /// <summary>
    /// Gets or sets the total progression in the publication, as a fraction between 0 and 1.
    /// </summary>
    [JsonPropertyName("progression")]
    public double? Progression { get; set; }

    /// <summary>
    /// Gets or sets references inside the publication that refine the progression.
    /// </summary>
    [JsonPropertyName("references")]
    public List<string>? References { get; set; }
}
