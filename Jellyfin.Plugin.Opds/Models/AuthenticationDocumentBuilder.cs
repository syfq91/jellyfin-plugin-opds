using System.Collections.Generic;

namespace Jellyfin.Plugin.Opds.Models;

/// <summary>
/// Builds the authentication document served by the plugin.
/// </summary>
internal static class AuthenticationDocumentBuilder
{
    /// <summary>
    /// Builds the authentication document of the plugin.
    /// </summary>
    /// <param name="baseUrl">The base url of the server.</param>
    /// <returns>The authentication document.</returns>
    internal static AuthenticationDocument Create(string baseUrl)
    {
        return new AuthenticationDocument
        {
            Id = baseUrl + AuthenticationConstants.DocumentPath,
            Title = "Jellyfin",
            Description = "Sign in with your Jellyfin username and password.",
            Authentication = new List<AuthenticationObject>
            {
                new()
                {
                    Type = AuthenticationConstants.BasicAuthenticationType,
                    Labels = new AuthenticationLabels
                    {
                        Login = "Username",
                        Password = "Password"
                    }
                }
            }
        };
    }
}
