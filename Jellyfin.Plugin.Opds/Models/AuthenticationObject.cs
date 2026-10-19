#pragma warning disable CA2227

using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Opds.Models;

/// <summary>
/// An authentication flow of an OPDS authentication document.
/// </summary>
public sealed class AuthenticationObject
{
    /// <summary>
    /// Gets or sets the URI identifying the authentication flow.
    /// </summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>
    /// Gets or sets alternate labels of the fields displayed to the user.
    /// </summary>
    [JsonPropertyName("labels")]
    public AuthenticationLabels? Labels { get; set; }
}
