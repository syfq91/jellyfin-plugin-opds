using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.Opds.Configuration;
using Jellyfin.Plugin.Opds.Models;
using Jellyfin.Plugin.Opds.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.Opds.Tests;

/// <summary>
/// Tests the progression endpoints of the OPDS api.
/// </summary>
[Collection("PluginState")]
public class OpdsApiProgressionTests : IDisposable
{
    private static readonly DateTime Modified = new(2026, 1, 27, 11, 0, 0, DateTimeKind.Utc);

    private readonly string _directory;
    private readonly Guid _bookId = Guid.NewGuid();
    private readonly User _user;
    private readonly Book _book;
    private readonly Mock<ILibraryManager> _libraryManager = new();
    private readonly Mock<IProgressionService> _progressionService = new();
    private readonly Mock<IOpdsFeedProvider> _opdsFeedProvider = new();
    private readonly OpdsApi _controller;

    /// <summary>
    /// Initializes a new instance of the <see cref="OpdsApiProgressionTests"/> class.
    /// </summary>
    public OpdsApiProgressionTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "opds-progression-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(_directory);

        var applicationPaths = new Mock<IApplicationPaths>();
        applicationPaths.SetupGet(x => x.PluginsPath).Returns(_directory);
        applicationPaths.SetupGet(x => x.PluginConfigurationsPath).Returns(_directory);

        var xmlSerializer = new Mock<IXmlSerializer>();
        xmlSerializer
            .Setup(x => x.DeserializeFromFile(It.IsAny<Type>(), It.IsAny<string>()))
            .Throws(new FileNotFoundException());

        // Constructing the plugin makes it the current instance.
        _ = new OpdsPlugin(applicationPaths.Object, xmlSerializer.Object);

        _user = new User("reader", "auth", "pass");
        _book = new Book { RunTimeTicks = 350000 };

        var userManager = new Mock<IUserManager>();
        userManager
            .Setup(x => x.AuthenticateUser(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
            .ReturnsAsync(_user);
        userManager.Setup(x => x.GetUserById(It.IsAny<Guid>())).Returns(_user);

        _libraryManager.Setup(x => x.GetItemById<Book>(It.IsAny<Guid>())).Returns(_book);

        _controller = new OpdsApi(
            userManager.Object,
            _opdsFeedProvider.Object,
            Mock.Of<IPageStreamService>(),
            _libraryManager.Object,
            _progressionService.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = CreateHttpContext(true) }
        };
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public async Task GetProgression_Returns_Document_With_Media_Type()
    {
        _progressionService
            .Setup(x => x.GetProgression(_book, _user))
            .Returns(new ProgressionDocument
            {
                Progression = 0.5,
                Modified = Modified,
                Device = new ProgressionDevice { Id = "urn:uuid:test", Name = "Test Reader" }
            });

        var result = await _controller.GetProgression(_bookId);

        var content = Assert.IsType<ContentResult>(result);
        Assert.Equal(ProgressionConstants.MediaType, content.ContentType);
        Assert.Null(content.StatusCode);

        using var json = JsonDocument.Parse(content.Content!);
        Assert.Equal(0.5, json.RootElement.GetProperty("progression").GetDouble());
        Assert.Equal("2026-01-27T11:00:00Z", json.RootElement.GetProperty("modified").GetString());
        Assert.Equal("urn:uuid:test", json.RootElement.GetProperty("device").GetProperty("id").GetString());
        Assert.Equal("Test Reader", json.RootElement.GetProperty("device").GetProperty("name").GetString());
        Assert.False(json.RootElement.TryGetProperty("title", out _));
        Assert.False(json.RootElement.TryGetProperty("references", out _));
    }

    [Fact]
    public async Task GetProgression_Returns_Empty_When_Unknown()
    {
        _progressionService
            .Setup(x => x.GetProgression(_book, _user))
            .Returns((ProgressionDocument?)null);

        var result = await _controller.GetProgression(_bookId);

        Assert.IsType<EmptyResult>(result);
    }

    [Fact]
    public async Task GetProgression_Returns_NotFound_For_Unknown_Book()
    {
        _libraryManager.Setup(x => x.GetItemById<Book>(_bookId)).Returns((Book?)null);

        var result = await _controller.GetProgression(_bookId);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Progression_Endpoints_Deny_Anonymous_Access()
    {
        OpdsPlugin.Instance!.Configuration.AllowAnonymousAccess = true;
        _controller.ControllerContext.HttpContext = CreateHttpContext(false);

        var result = await _controller.GetProgression(_bookId);

        // The 401 carries the authentication document of the plugin.
        var content = Assert.IsType<ContentResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, content.StatusCode);
        Assert.Equal(AuthenticationConstants.MediaType, content.ContentType);
        Assert.True(_controller.HttpContext.Response.Headers.ContainsKey("WWW-Authenticate"));
        Assert.Contains(AuthenticationConstants.Rel, _controller.HttpContext.Response.Headers["Link"].ToString());

        using var json = JsonDocument.Parse(content.Content!);
        Assert.Equal("Jellyfin", json.RootElement.GetProperty("title").GetString());
        Assert.Equal(
            AuthenticationConstants.BasicAuthenticationType,
            json.RootElement.GetProperty("authentication")[0].GetProperty("type").GetString());
        _progressionService.Verify(x => x.GetProgression(It.IsAny<Book>(), It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public void GetAuthenticationDocument_Returns_Document()
    {
        var result = _controller.GetAuthenticationDocument();

        var content = Assert.IsType<ContentResult>(result);
        Assert.Equal(AuthenticationConstants.MediaType, content.ContentType);
        Assert.Null(content.StatusCode);

        using var json = JsonDocument.Parse(content.Content!);
        Assert.Equal("/opds/authentication.json", json.RootElement.GetProperty("id").GetString());
        Assert.Equal("Jellyfin", json.RootElement.GetProperty("title").GetString());
        Assert.Equal(1, json.RootElement.GetProperty("authentication").GetArrayLength());
        Assert.Equal(
            AuthenticationConstants.BasicAuthenticationType,
            json.RootElement.GetProperty("authentication")[0].GetProperty("type").GetString());
        Assert.Equal("Username", json.RootElement.GetProperty("authentication")[0].GetProperty("labels").GetProperty("login").GetString());
        Assert.Equal("Password", json.RootElement.GetProperty("authentication")[0].GetProperty("labels").GetProperty("password").GetString());
    }

    [Fact]
    public async Task Progression_Responses_Are_Not_Cached()
    {
        _progressionService
            .Setup(x => x.GetProgression(_book, _user))
            .Returns(new ProgressionDocument
            {
                Progression = 0.5,
                Modified = Modified,
                Device = new ProgressionDevice { Id = "urn:uuid:test", Name = "Test Reader" }
            });

        await _controller.GetProgression(_bookId);

        Assert.Equal("no-store", _controller.HttpContext.Response.Headers["Cache-Control"].ToString());
    }

    [Fact]
    public async Task UpdateProgression_Returns_Document_On_Success()
    {
        _progressionService
            .Setup(x => x.UpdateProgression(_book, _user, It.IsAny<ProgressionDocument?>()))
            .Returns(new ProgressionUpdateResult(
                ProgressionUpdateStatus.Success,
                new ProgressionDocument
                {
                    Progression = 0.25,
                    Modified = Modified,
                    Device = new ProgressionDevice { Id = "urn:uuid:test", Name = "Test Reader" }
                }));

        var result = await _controller.UpdateProgression(_bookId, CreateIncoming());

        var content = Assert.IsType<ContentResult>(result);
        Assert.Equal(ProgressionConstants.MediaType, content.ContentType);
        Assert.Null(content.StatusCode);

        using var json = JsonDocument.Parse(content.Content!);
        Assert.Equal(0.25, json.RootElement.GetProperty("progression").GetDouble());
    }

    [Fact]
    public async Task UpdateProgression_Passes_Document_To_Service()
    {
        _progressionService
            .Setup(x => x.UpdateProgression(_book, _user, It.IsAny<ProgressionDocument?>()))
            .Returns(new ProgressionUpdateResult(
                ProgressionUpdateStatus.Success,
                new ProgressionDocument { Progression = 0.5, Modified = Modified }));

        var incoming = CreateIncoming();
        await _controller.UpdateProgression(_bookId, incoming);

        _progressionService.Verify(x => x.UpdateProgression(_book, _user, incoming), Times.Once);
    }

    [Fact]
    public async Task UpdateProgression_Returns_Problem_For_Invalid_Payload()
    {
        _progressionService
            .Setup(x => x.UpdateProgression(_book, _user, It.IsAny<ProgressionDocument?>()))
            .Returns(new ProgressionUpdateResult(ProgressionUpdateStatus.InvalidPayload, null));

        var result = await _controller.UpdateProgression(_bookId, null);

        var content = Assert.IsType<ContentResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, content.StatusCode);
        Assert.Equal(ProgressionConstants.ProblemMediaType, content.ContentType);

        using var json = JsonDocument.Parse(content.Content!);
        Assert.Equal(ProgressionConstants.InvalidPayloadType, json.RootElement.GetProperty("type").GetString());
        Assert.Equal("Progression could not be updated due to an invalid payload.", json.RootElement.GetProperty("title").GetString());
        Assert.Equal(400, json.RootElement.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task UpdateProgression_Returns_Conflict_For_Stale_Progression()
    {
        _progressionService
            .Setup(x => x.UpdateProgression(_book, _user, It.IsAny<ProgressionDocument?>()))
            .Returns(new ProgressionUpdateResult(ProgressionUpdateStatus.StaleProgression, null));

        var result = await _controller.UpdateProgression(_bookId, CreateIncoming());

        var content = Assert.IsType<ContentResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, content.StatusCode);

        using var json = JsonDocument.Parse(content.Content!);
        Assert.Equal(ProgressionConstants.DateConflictType, json.RootElement.GetProperty("type").GetString());
        Assert.Equal("A more recent progression point is already available.", json.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public async Task UpdateProgression_Returns_NotFound_For_Unknown_Book()
    {
        _libraryManager.Setup(x => x.GetItemById<Book>(_bookId)).Returns((Book?)null);

        var result = await _controller.UpdateProgression(_bookId, CreateIncoming());

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetRootFeed_Output_DoesNotEmitByteOrderMark()
    {
        _opdsFeedProvider.Setup(x => x.GetFeeds(It.IsAny<string>())).Returns(new FeedDto());

        var result = await _controller.GetRootFeed();
        var fileResult = Assert.IsType<FileStreamResult>(result);
        var stream = fileResult.FileStream;
        stream.Seek(0, SeekOrigin.Begin);
        var firstByte = stream.ReadByte();
        Assert.Equal((int)'<', firstByte);
    }

    [Fact]
    public async Task GetSearchDescription_Output_DoesNotEmitByteOrderMark()
    {
        _opdsFeedProvider.Setup(x => x.GetSearchDescription(It.IsAny<string>()))
            .Returns(new OpenSearchDescriptionDto { Xmlns = "http://a9.com/-/spec/opensearch/1.1/" });

        var result = await _controller.GetSearchDescription();
        var fileResult = Assert.IsType<FileStreamResult>(result);
        var stream = fileResult.FileStream;
        stream.Seek(0, SeekOrigin.Begin);
        var firstByte = stream.ReadByte();
        Assert.Equal((int)'<', firstByte);
    }

    private static ProgressionDocument CreateIncoming()
    {
        return new ProgressionDocument
        {
            Progression = 0.5,
            Modified = Modified,
            Device = new ProgressionDevice { Id = "urn:uuid:test", Name = "Test Reader" }
        };
    }

    private static DefaultHttpContext CreateHttpContext(bool authenticated)
    {
        var context = new DefaultHttpContext();
        if (authenticated)
        {
            var token = Convert.ToBase64String(Encoding.UTF8.GetBytes("reader:password"));
            context.Request.Headers.Authorization = "Basic " + token;
        }

        return context;
    }
}
