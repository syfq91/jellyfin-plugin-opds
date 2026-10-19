#pragma warning disable CA2227
#pragma warning disable CA1002

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Opds.Models;

/// <summary>
/// An OPDS authentication document, as defined by the Authentication for
/// OPDS specification.
/// </summary>
public sealed class AuthenticationDocument
{
    /// <summary>
    /// Gets or sets the unique identifier and canonical location of the document.
    /// </summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>
    /// Gets or sets the title of the catalog being accessed.
    /// </summary>
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>
    /// Gets or sets a description displayed to the user.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the supported authentication flows.
    /// </summary>
    [JsonPropertyName("authentication")]
    public List<AuthenticationObject>? Authentication { get; set; }
}
