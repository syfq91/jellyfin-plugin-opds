using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Opds.Models;

/// <summary>
/// The device that reported an OPDS progression document.
/// </summary>
public sealed class ProgressionDevice
{
    /// <summary>
    /// Gets or sets the unique identifier of the device, as a URI.
    /// </summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>
    /// Gets or sets the display name of the device.
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}
