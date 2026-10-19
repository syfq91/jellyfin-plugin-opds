using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Opds.Models;

/// <summary>
/// Serializes the JSON documents served by the plugin.
/// </summary>
internal static class ProgressionSerializer
{
    /// <summary>
    /// The serializer options shared by every JSON document of the plugin.
    /// </summary>
    internal static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Serializes a progression document.
    /// </summary>
    /// <param name="document">The progression document.</param>
    /// <returns>The JSON representation.</returns>
    internal static string Serialize(ProgressionDocument document)
        => JsonSerializer.Serialize(document, Options);

    /// <summary>
    /// Serializes a problem details payload.
    /// </summary>
    /// <param name="problem">The problem details payload.</param>
    /// <returns>The JSON representation.</returns>
    internal static string Serialize(ProgressionProblem problem)
        => JsonSerializer.Serialize(problem, Options);

    /// <summary>
    /// Serializes an authentication document.
    /// </summary>
    /// <param name="document">The authentication document.</param>
    /// <returns>The JSON representation.</returns>
    internal static string Serialize(AuthenticationDocument document)
        => JsonSerializer.Serialize(document, Options);
}
