namespace Jellyfin.Plugin.Opds.Models;

/// <summary>
/// Constants of the OPDS progression specification.
/// </summary>
public static class ProgressionConstants
{
    /// <summary>
    /// The link relation of the progression service.
    /// </summary>
    public const string Rel = "http://opds-spec.org/progression";

    /// <summary>
    /// The media type of progression documents.
    /// </summary>
    public const string MediaType = "application/opds-progression+json";

    /// <summary>
    /// The media type of problem details payloads.
    /// </summary>
    public const string ProblemMediaType = "application/problem+json";

    /// <summary>
    /// The problem type of rejected progression payloads.
    /// </summary>
    public const string InvalidPayloadType = "https://registry.opds.io/error#progression-invalid-payload";

    /// <summary>
    /// The problem type of progression updates of another user.
    /// </summary>
    public const string IncorrectUserType = "https://registry.opds.io/error#progression-incorrect-user";

    /// <summary>
    /// The problem type of progression updates that are older than the stored progression.
    /// </summary>
    public const string DateConflictType = "https://registry.opds.io/error#progression-date";
}
