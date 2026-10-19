using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Opds.Models;

/// <summary>
/// Alternate labels of the fields of an authentication flow.
/// </summary>
public sealed class AuthenticationLabels
{
    /// <summary>
    /// Gets or sets the alternate label of the login field.
    /// </summary>
    [JsonPropertyName("login")]
    public string? Login { get; set; }

    /// <summary>
    /// Gets or sets the alternate label of the password field.
    /// </summary>
    [JsonPropertyName("password")]
    public string? Password { get; set; }
}
