namespace Jellyfin.Plugin.Opds.Models;

/// <summary>
/// Constants of the Authentication for OPDS specification.
/// </summary>
public static class AuthenticationConstants
{
    /// <summary>
    /// The media type of authentication documents.
    /// </summary>
    public const string MediaType = "application/opds-authentication+json";

    /// <summary>
    /// The link relation of authentication documents.
    /// </summary>
    public const string Rel = "http://opds-spec.org/auth/document";

    /// <summary>
    /// The authentication flow type of HTTP Basic authentication.
    /// </summary>
    public const string BasicAuthenticationType = "http://opds-spec.org/auth/basic";

    /// <summary>
    /// The path of the authentication document.
    /// </summary>
    public const string DocumentPath = "/opds/authentication.json";
}
