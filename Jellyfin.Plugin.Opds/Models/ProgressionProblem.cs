using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Opds.Models;

/// <summary>
/// An RFC 7807 problem details payload, as used for OPDS progression errors.
/// </summary>
public sealed class ProgressionProblem
{
    /// <summary>
    /// Gets or sets the problem type URI.
    /// </summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>
    /// Gets or sets the human readable summary of the problem.
    /// </summary>
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>
    /// Gets or sets the HTTP status code.
    /// </summary>
    [JsonPropertyName("status")]
    public int? Status { get; set; }
}
