using System;
using System.IO;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.Opds.Models;
using Jellyfin.Plugin.Opds.Services;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Controller.Authentication;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace Jellyfin.Plugin.Opds;

/// <summary>
/// Opds endpoints.
/// </summary>
[Route("opds")]
public class OpdsApi : ControllerBase
{
    private const string AuthHeaderKey = "WWW-Authenticate";
    private const string AuthHeaderValue = "Basic realm=\"Login Required\"";

    private readonly IUserManager _userManager;
    private readonly IOpdsFeedProvider _opdsFeedProvider;
    private readonly IPageStreamService _pageStreamService;
    private readonly ILibraryManager _libraryManager;
    private readonly IProgressionService _progressionService;

    /// <summary>
    /// Initializes a new instance of the <see cref="OpdsApi"/> class.
    /// </summary>
    /// <param name="userManager">Instance of the <see cref="IUserManager"/> interface.</param>
    /// <param name="opdsFeedProvider">The opds feed provider.</param>
    /// <param name="pageStreamService">The page stream service.</param>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="progressionService">The progression service.</param>
    public OpdsApi(
        IUserManager userManager,
        IOpdsFeedProvider opdsFeedProvider,
        IPageStreamService pageStreamService,
        ILibraryManager libraryManager,
        IProgressionService progressionService)
    {
        _userManager = userManager;
        _opdsFeedProvider = opdsFeedProvider;
        _pageStreamService = pageStreamService;
        _libraryManager = libraryManager;
        _progressionService = progressionService;
    }

    /// <summary>
    /// Gets the root feed.
    /// </summary>
    /// <returns>The root feed xml.</returns>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult> GetRootFeed()
    {
        try
        {
            await AuthorizeAsync().ConfigureAwait(false);
            var feeds = _opdsFeedProvider.GetFeeds(Request.PathBase);
            return BuildOutput(feeds);
        }
        catch (AuthenticationException)
        {
            Response.Headers.Append(AuthHeaderKey, AuthHeaderValue);
            return StatusCode(StatusCodes.Status401Unauthorized);
        }
    }

    /// <summary>
    /// Gets the list of letters to filter by.
    /// </summary>
    /// <returns>The alphabetical feed xml.</returns>
    [HttpGet("Books")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult> GetAlphabeticalRootFeed()
    {
        try
        {
            await AuthorizeAsync().ConfigureAwait(false);
            var feeds = _opdsFeedProvider.GetAlphabeticalFeed(Request.PathBase);
            return BuildOutput(feeds);
        }
        catch (AuthenticationException)
        {
            Response.Headers.Append(AuthHeaderKey, AuthHeaderValue);
            return StatusCode(StatusCodes.Status401Unauthorized);
        }
    }

    /// <summary>
    /// Gets the list of genres.
    /// </summary>
    /// <returns>The genres feed xml.</returns>
    [HttpGet("Genres")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult> GetGenres()
    {
        try
        {
            var userId = await AuthorizeAsync().ConfigureAwait(false);
            var feeds = _opdsFeedProvider.GetBookGenres(Request.PathBase, userId);
            return BuildOutput(feeds);
        }
        catch (AuthenticationException)
        {
            Response.Headers.Append(AuthHeaderKey, AuthHeaderValue);
            return StatusCode(StatusCodes.Status401Unauthorized);
        }
    }

    /// <summary>
    /// Gets the list of recently added books.
    /// </summary>
    /// <returns>The recently added feed xml.</returns>
    [HttpGet("Books/RecentlyAdded")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult> GetRecentlyAdded()
    {
        try
        {
            var userId = await AuthorizeAsync().ConfigureAwait(false);
            var feeds = _opdsFeedProvider.GetRecentlyAdded(Request.PathBase, userId);
            return BuildOutput(feeds);
        }
        catch (AuthenticationException)
        {
            Response.Headers.Append(AuthHeaderKey, AuthHeaderValue);
            return StatusCode(StatusCodes.Status401Unauthorized);
        }
    }

    /// <summary>
    /// Gets the list of favorite books.
    /// </summary>
    /// <returns>The recently added feed xml.</returns>
    [HttpGet("Books/Favorite")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult> GetFavoriteBooks()
    {
        try
        {
            var userId = await AuthorizeAsync().ConfigureAwait(false);
            var feeds = _opdsFeedProvider.GetFavoriteBooks(Request.PathBase, userId);
            return BuildOutput(feeds);
        }
        catch (AuthenticationException)
        {
            Response.Headers.Append(AuthHeaderKey, AuthHeaderValue);
            return StatusCode(StatusCodes.Status401Unauthorized);
        }
    }

    /// <summary>
    /// Gets the list of letters to filter by.
    /// </summary>
    /// <param name="startFilter">The start filter.</param>
    /// <returns>The alphabetical feed xml.</returns>
    [HttpGet("Books/Letter/{startFilter}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult> GetAlphabeticalFeed([FromRoute] string startFilter)
    {
        try
        {
            var userId = await AuthorizeAsync().ConfigureAwait(false);
            var feeds = _opdsFeedProvider.GetAllBooks(Request.PathBase, userId, startFilter);
            return BuildOutput(feeds);
        }
        catch (AuthenticationException)
        {
            Response.Headers.Append(AuthHeaderKey, AuthHeaderValue);
            return StatusCode(StatusCodes.Status401Unauthorized);
        }
    }

    /// <summary>
    /// Gets the list of books in the genre.
    /// </summary>
    /// <param name="genreId">The genre id.</param>
    /// <returns>The books feed xml.</returns>
    [HttpGet("Genres/{genreId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult> GetBooksByGenre([FromRoute] Guid genreId)
    {
        try
        {
            var userId = await AuthorizeAsync().ConfigureAwait(false);
            var feeds = _opdsFeedProvider.GetBooksByGenre(Request.PathBase, userId, genreId);
            return BuildOutput(feeds);
        }
        catch (AuthenticationException)
        {
            Response.Headers.Append(AuthHeaderKey, AuthHeaderValue);
            return StatusCode(StatusCodes.Status401Unauthorized);
        }
    }

    /// <summary>
    /// Gets the search result.
    /// </summary>
    /// <param name="searchTerms">The search terms.</param>
    /// <returns>The search feed xml.</returns>
    [HttpGet("Search/{searchTerms}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult> SearchBookFromRoute(string searchTerms)
    {
        try
        {
            var userId = await AuthorizeAsync().ConfigureAwait(false);
            var feeds = await _opdsFeedProvider.SearchBooks(Request.PathBase, userId, searchTerms)
                .ConfigureAwait(false);
            return BuildOutput(feeds);
        }
        catch (AuthenticationException)
        {
            Response.Headers.Append(AuthHeaderKey, AuthHeaderValue);
            return StatusCode(StatusCodes.Status401Unauthorized);
        }
    }

    /// <summary>
    /// Gets the search result.
    /// For backwards compatibility, there are multiple accepted query
    /// parameters for the search terms.
    /// </summary>
    /// <param name="searchTerms">The search terms as historically used by this plugin.</param>
    /// <param name="q">The search terms as defined by the OPDS spec.</param>
    /// <param name="query">The search terms as some OPDS clients provide them.</param>
    /// <returns>The search feed xml.</returns>
    [HttpGet("Search")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult> SearchBookFromQuery(
        [FromQuery] string? searchTerms = null,
        [FromQuery] string? q = null,
        [FromQuery] string? query = null)
    {
        searchTerms ??= q ?? query;

        // None of the query parameters was given, but we need at least one, so
        // it's a Bad Request.
        if (searchTerms == null)
        {
            return StatusCode(StatusCodes.Status400BadRequest);
        }

        try
        {
            var userId = await AuthorizeAsync().ConfigureAwait(false);
            var feeds = await _opdsFeedProvider.SearchBooks(Request.PathBase, userId, searchTerms)
                .ConfigureAwait(false);
            return BuildOutput(feeds);
        }
        catch (AuthenticationException)
        {
            Response.Headers.Append(AuthHeaderKey, AuthHeaderValue);
            return StatusCode(StatusCodes.Status401Unauthorized);
        }
    }

    /// <summary>
    /// Gets the search description.
    /// </summary>
    /// <returns>The search description xml.</returns>
    [HttpGet("osd")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult> GetSearchDescription()
    {
        try
        {
            await AuthorizeAsync().ConfigureAwait(false);
            var searchDescription = _opdsFeedProvider.GetSearchDescription(Request.PathBase);
            return BuildOutput(searchDescription);
        }
        catch (AuthenticationException)
        {
            Response.Headers.Append(AuthHeaderKey, AuthHeaderValue);
            return StatusCode(StatusCodes.Status401Unauthorized);
        }
    }

    /// <summary>
    /// Gets the authentication document, as defined by the
    /// Authentication for OPDS specification.
    /// </summary>
    /// <returns>The authentication document.</returns>
    [HttpGet("authentication.json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult GetAuthenticationDocument()
    {
        var json = ProgressionSerializer.Serialize(AuthenticationDocumentBuilder.Create(Request.PathBase));
        return new ContentResult
        {
            Content = json,
            ContentType = AuthenticationConstants.MediaType
        };
    }

    /// <summary>
    /// Gets the book image.
    /// </summary>
    /// <param name="bookId">The book id.</param>
    /// <returns>The book image.</returns>
    [HttpGet("Cover/{bookId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> GetBookImage(Guid bookId)
    {
        try
        {
            await AuthorizeAsync().ConfigureAwait(false);
            var imagePath = _opdsFeedProvider.GetBookImage(bookId);
            if (string.IsNullOrEmpty(imagePath))
            {
                return NotFound();
            }

            return PhysicalFile(imagePath, MimeTypes.GetMimeType(imagePath));
        }
        catch (AuthenticationException)
        {
            Response.Headers.Append(AuthHeaderKey, AuthHeaderValue);
            return StatusCode(StatusCodes.Status401Unauthorized);
        }
    }

    /// <summary>
    /// Gets the book file.
    /// </summary>
    /// <param name="bookId">The book id.</param>
    /// <returns>The book image.</returns>
    [HttpGet("Download/{bookId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> DownloadBook(Guid bookId)
    {
        try
        {
            await AuthorizeAsync().ConfigureAwait(false);
            var bookPath = _opdsFeedProvider.GetBook(bookId);
            if (string.IsNullOrEmpty(bookPath))
            {
                return NotFound();
            }

            var fileName = Path.GetFileName(bookPath);
            return PhysicalFile(bookPath, MimeTypes.GetMimeType(bookPath), fileName);
        }
        catch (AuthenticationException)
        {
            Response.Headers.Append(AuthHeaderKey, AuthHeaderValue);
            return StatusCode(StatusCodes.Status401Unauthorized);
        }
    }

    /// <summary>
    /// Gets a single page of a book as an image.
    /// Used by OPDS clients supporting the page streaming extension.
    /// </summary>
    /// <param name="bookId">The book id.</param>
    /// <param name="page">The zero-based number of the requested page.</param>
    /// <param name="width">The optional maximum width of the returned page.</param>
    /// <returns>The page image.</returns>
    [HttpGet("Stream/{bookId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> GetBookPage([FromRoute] Guid bookId, [FromQuery] int page = 0, [FromQuery] int? width = null)
    {
        try
        {
            await AuthorizeAsync().ConfigureAwait(false);

            if (!OpdsPlugin.Instance!.Configuration.EnablePageStreaming)
            {
                return NotFound();
            }

            var result = _pageStreamService.GetPage(bookId, page, width);
            if (result is null)
            {
                return NotFound();
            }

            return File(result.Content, result.ContentType);
        }
        catch (AuthenticationException)
        {
            Response.Headers.Append(AuthHeaderKey, AuthHeaderValue);
            return StatusCode(StatusCodes.Status401Unauthorized);
        }
    }

    /// <summary>
    /// Gets the reading progression of a book.
    /// Used by OPDS clients supporting the OPDS progression specification.
    /// </summary>
    /// <param name="bookId">The book id.</param>
    /// <returns>The progression document, or an empty payload when no progression is known.</returns>
    [HttpGet("Progression/{bookId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> GetProgression([FromRoute] Guid bookId)
    {
        try
        {
            // Progressions are personal and change over time; never cache them.
            Response.Headers["Cache-Control"] = "no-store";

            var user = await AuthorizeProgressionAsync().ConfigureAwait(false);
            if (user is null)
            {
                return UnauthorizedResult();
            }

            var book = _libraryManager.GetItemById<Book>(bookId);
            if (book is null)
            {
                return NotFound();
            }

            if (!book.IsVisibleStandalone(user))
            {
                return ProgressionProblem(
                    StatusCodes.Status403Forbidden,
                    ProgressionConstants.IncorrectUserType,
                    "Progression could not be updated for the current user.");
            }

            var document = _progressionService.GetProgression(book, user);
            if (document is null)
            {
                // The specification allows an empty payload when no
                // progression has been communicated yet.
                return new EmptyResult();
            }

            return ProgressionDocumentResult(document);
        }
        catch (AuthenticationException)
        {
            return UnauthorizedResult();
        }
    }

    /// <summary>
    /// Updates the reading progression of a book.
    /// Used by OPDS clients supporting the OPDS progression specification.
    /// </summary>
    /// <param name="bookId">The book id.</param>
    /// <param name="document">The progression document reported by the client.</param>
    /// <returns>The resulting progression document.</returns>
    [HttpPut("Progression/{bookId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> UpdateProgression([FromRoute] Guid bookId, [FromBody] ProgressionDocument? document)
    {
        try
        {
            // Progressions are personal and change over time; never cache them.
            Response.Headers["Cache-Control"] = "no-store";

            var user = await AuthorizeProgressionAsync().ConfigureAwait(false);
            if (user is null)
            {
                return UnauthorizedResult();
            }

            var book = _libraryManager.GetItemById<Book>(bookId);
            if (book is null)
            {
                return NotFound();
            }

            if (!book.IsVisibleStandalone(user))
            {
                return ProgressionProblem(
                    StatusCodes.Status403Forbidden,
                    ProgressionConstants.IncorrectUserType,
                    "Progression could not be updated for the current user.");
            }

            var result = _progressionService.UpdateProgression(book, user, document);
            switch (result.Status)
            {
                case ProgressionUpdateStatus.InvalidPayload:
                    return ProgressionProblem(
                        StatusCodes.Status400BadRequest,
                        ProgressionConstants.InvalidPayloadType,
                        "Progression could not be updated due to an invalid payload.");
                case ProgressionUpdateStatus.StaleProgression:
                    return ProgressionProblem(
                        StatusCodes.Status409Conflict,
                        ProgressionConstants.DateConflictType,
                        "A more recent progression point is already available.");
                default:
                    return ProgressionDocumentResult(result.Document!);
            }
        }
        catch (AuthenticationException)
        {
            return UnauthorizedResult();
        }
    }

    private async Task<Guid> AuthorizeAsync()
    {
        var allowAnonymous = OpdsPlugin.Instance!.Configuration.AllowAnonymousAccess;

        Request.Headers.TryGetValue(HeaderNames.Authorization, out var authorizationHeader);
        if (string.IsNullOrEmpty(authorizationHeader) || authorizationHeader.Count == 0)
        {
            if (allowAnonymous)
            {
                return Guid.Empty;
            }

            throw new AuthenticationException("Basic Authentication is required");
        }

        var authenticationHeaderValue = AuthenticationHeaderValue.Parse(authorizationHeader!);
        if (string.IsNullOrEmpty(authenticationHeaderValue.Parameter)
            || !string.Equals("Basic", authenticationHeaderValue.Scheme, StringComparison.OrdinalIgnoreCase))
        {
            if (allowAnonymous)
            {
                return Guid.Empty;
            }

            throw new AuthenticationException("Basic Authentication is required");
        }

        var credentialString = Encoding.UTF8.GetString(Convert.FromBase64String(authenticationHeaderValue.Parameter));
        var credentialSplitIndex = credentialString.IndexOf(':', StringComparison.Ordinal);
        if (credentialSplitIndex < 0)
        {
            if (allowAnonymous)
            {
                return Guid.Empty;
            }

            throw new AuthenticationException("Basic Authentication is required");
        }

        var username = credentialString[..credentialSplitIndex];
        var password = credentialString[(credentialSplitIndex + 1)..];

        var user = await _userManager.AuthenticateUser(
                username,
                password,
                HttpContext.GetNormalizedRemoteIP().ToString(),
                true)
            .ConfigureAwait(false);

        if (user is null)
        {
            if (allowAnonymous)
            {
                return Guid.Empty;
            }

            throw new AuthenticationException("Basic Authentication is required");
        }

        return user.Id;
    }

    private FileStreamResult BuildOutput(FeedDto outputFeed)
    {
        var memoryStream = new MemoryStream();
        var serializer = XmlHelper.Create(typeof(FeedDto), outputFeed.Xmlns);
        using (var writer = new StreamWriter(memoryStream, Encoding.UTF8, IODefaults.StreamWriterBufferSize, true))
        using (var textWriter = new XmlTextWriter(writer))
        {
            textWriter.Formatting = Formatting.Indented;
            serializer.Serialize(textWriter, outputFeed);
        }

        memoryStream.Seek(0, SeekOrigin.Begin);
        return File(memoryStream, "application/atom+xml; charset=utf-8");
    }

    private FileStreamResult BuildOutput(OpenSearchDescriptionDto outputFeed)
    {
        var memoryStream = new MemoryStream();
        var serializer = XmlHelper.Create(typeof(OpenSearchDescriptionDto), outputFeed.Xmlns!);
        using (var writer = new StreamWriter(memoryStream, Encoding.UTF8, IODefaults.StreamWriterBufferSize, true))
        using (var textWriter = new XmlTextWriter(writer))
        {
            textWriter.Formatting = Formatting.Indented;
            serializer.Serialize(textWriter, outputFeed);
        }

        memoryStream.Seek(0, SeekOrigin.Begin);
        return File(memoryStream, "application/atom+xml; charset=utf-8");
    }

    /// <summary>
    /// Authenticates the request for the progression endpoints, which
    /// always require a user (unlike the other endpoints, anonymous
    /// access is not supported for per-user progression).
    /// </summary>
    /// <returns>The authenticated user, or <c>null</c> when the request is not authenticated.</returns>
    private async Task<User?> AuthorizeProgressionAsync()
    {
        var userId = await AuthorizeAsync().ConfigureAwait(false);
        if (userId == Guid.Empty)
        {
            return null;
        }

        return _userManager.GetUserById(userId);
    }

    /// <summary>
    /// Builds the 401 response of the progression endpoints, carrying the
    /// authentication document described by Authentication for OPDS, as
    /// well as a link to it.
    /// </summary>
    /// <returns>The 401 response.</returns>
    private ContentResult UnauthorizedResult()
    {
        string baseUrl = Request.PathBase;
        Response.Headers.Append(AuthHeaderKey, AuthHeaderValue);
        Response.Headers["Link"] = "<" + baseUrl + AuthenticationConstants.DocumentPath
            + ">; rel=\"" + AuthenticationConstants.Rel
            + "\"; type=\"" + AuthenticationConstants.MediaType + "\"";

        var json = ProgressionSerializer.Serialize(AuthenticationDocumentBuilder.Create(baseUrl));
        return new ContentResult
        {
            Content = json,
            ContentType = AuthenticationConstants.MediaType,
            StatusCode = StatusCodes.Status401Unauthorized
        };
    }

    private ContentResult ProgressionDocumentResult(ProgressionDocument document)
    {
        var json = ProgressionSerializer.Serialize(document);
        return Content(json, ProgressionConstants.MediaType);
    }

    private ContentResult ProgressionProblem(int statusCode, string type, string title)
    {
        var problem = new ProgressionProblem
        {
            Type = type,
            Title = title,
            Status = statusCode
        };

        var json = ProgressionSerializer.Serialize(problem);
        return new ContentResult
        {
            Content = json,
            ContentType = ProgressionConstants.ProblemMediaType,
            StatusCode = statusCode
        };
    }
}
