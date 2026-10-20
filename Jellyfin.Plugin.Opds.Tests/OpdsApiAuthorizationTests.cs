using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.Opds.Models;
using Jellyfin.Plugin.Opds.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.Opds.Tests;

/// <summary>
/// Unit tests for per-item authorization on OPDS content endpoints (M-7).
/// </summary>
[Collection("PluginState")]
public class OpdsApiAuthorizationTests : IDisposable
{
    private readonly string _directory;
    private readonly User _user;
    private readonly Guid _bookId = Guid.NewGuid();
    private readonly Mock<ILibraryManager> _libraryManager = new();
    private readonly Mock<IUserManager> _userManager = new();
    private readonly Mock<IOpdsFeedProvider> _opdsFeedProvider = new();
    private readonly Mock<IPageStreamService> _pageStreamService = new();
    private readonly Mock<IProgressionService> _progressionService = new();
    private readonly Mock<IBasicAuthCache> _basicAuthCache = new();
    private readonly OpdsApi _controller;

    /// <summary>
    /// Initializes a new instance of the <see cref="OpdsApiAuthorizationTests"/> class.
    /// </summary>
    public OpdsApiAuthorizationTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "opds-auth-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(_directory);

        var applicationPaths = new Mock<IApplicationPaths>();
        applicationPaths.SetupGet(x => x.PluginsPath).Returns(_directory);
        applicationPaths.SetupGet(x => x.PluginConfigurationsPath).Returns(_directory);

        var xmlSerializer = new Mock<IXmlSerializer>();
        xmlSerializer
            .Setup(x => x.DeserializeFromFile(It.IsAny<Type>(), It.IsAny<string>()))
            .Throws(new FileNotFoundException());

        var plugin = new OpdsPlugin(applicationPaths.Object, xmlSerializer.Object);
        plugin.Configuration.EnablePageStreaming = true;

        _user = new User("reader", "auth", "pass");

        _userManager
            .Setup(x => x.AuthenticateUser(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
            .ReturnsAsync(_user);
        _userManager.Setup(x => x.GetUserById(It.IsAny<Guid>())).Returns(_user);

        _basicAuthCache
            .Setup(x => x.GetOrAuthenticateAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Func<Task<Guid?>>>(),
                It.IsAny<Func<Guid, bool>>()))
            .ReturnsAsync(_user.Id);

        _controller = new OpdsApi(
            _userManager.Object,
            _opdsFeedProvider.Object,
            _pageStreamService.Object,
            _libraryManager.Object,
            _progressionService.Object,
            _basicAuthCache.Object)
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
    public async Task DownloadBook_Returns_PhysicalFile_When_Authorized()
    {
        var book = new AccessibleBook { Path = "/books/test.epub" };
        _libraryManager.Setup(x => x.GetItemById<Book>(_bookId)).Returns(book);
        _opdsFeedProvider.Setup(x => x.GetBook(_bookId)).Returns(book.Path);

        var result = await _controller.DownloadBook(_bookId);

        var fileResult = Assert.IsType<PhysicalFileResult>(result);
        Assert.Equal(book.Path, fileResult.FileName);
    }

    [Fact]
    public async Task DownloadBook_Returns_NotFound_When_Book_Not_Visible()
    {
        var book = new InaccessibleBook { Path = "/books/restricted.epub" };
        _libraryManager.Setup(x => x.GetItemById<Book>(_bookId)).Returns(book);

        var result = await _controller.DownloadBook(_bookId);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task DownloadBook_Returns_NotFound_When_Book_Does_Not_Exist()
    {
        _libraryManager.Setup(x => x.GetItemById<Book>(_bookId)).Returns((Book?)null);

        var result = await _controller.DownloadBook(_bookId);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetBookImage_Returns_PhysicalFile_When_Authorized()
    {
        var book = new AccessibleBook();
        book.SetImage(new ItemImageInfo { Type = ImageType.Primary, Path = "/covers/test.jpg" }, 0);
        _libraryManager.Setup(x => x.GetItemById<Book>(_bookId)).Returns(book);
        _opdsFeedProvider.Setup(x => x.GetBookImage(_bookId)).Returns(book.PrimaryImagePath);

        var result = await _controller.GetBookImage(_bookId);

        var fileResult = Assert.IsType<PhysicalFileResult>(result);
        Assert.Equal(book.PrimaryImagePath, fileResult.FileName);
    }

    [Fact]
    public async Task GetBookImage_Returns_NotFound_When_Book_Not_Visible()
    {
        var book = new InaccessibleBook();
        book.SetImage(new ItemImageInfo { Type = ImageType.Primary, Path = "/covers/secret.jpg" }, 0);
        _libraryManager.Setup(x => x.GetItemById<Book>(_bookId)).Returns(book);

        var result = await _controller.GetBookImage(_bookId);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetBookThumbnail_Returns_Thumbnail_When_Authorized()
    {
        var book = new AccessibleBook();
        book.SetImage(new ItemImageInfo { Type = ImageType.Primary, Path = "/covers/test.jpg" }, 0);
        _libraryManager.Setup(x => x.GetItemById<Book>(_bookId)).Returns(book);
        _opdsFeedProvider.Setup(x => x.GetBookImage(_bookId)).Returns(book.PrimaryImagePath);
        _opdsFeedProvider.Setup(x => x.GetBookThumbnail(_bookId, It.IsAny<int>())).Returns(new byte[] { 1, 2, 3 });

        var result = await _controller.GetBookThumbnail(_bookId);

        var fileResult = Assert.IsType<FileContentResult>(result);
        Assert.Equal("image/jpeg", fileResult.ContentType);
    }

    [Fact]
    public async Task GetBookThumbnail_Returns_NotFound_When_Book_Not_Visible()
    {
        var book = new InaccessibleBook();
        book.SetImage(new ItemImageInfo { Type = ImageType.Primary, Path = "/covers/secret.jpg" }, 0);
        _libraryManager.Setup(x => x.GetItemById<Book>(_bookId)).Returns(book);

        var result = await _controller.GetBookThumbnail(_bookId);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetBookPage_Returns_Page_When_Authorized()
    {
        var book = new AccessibleBook { Path = "/comics/test.cbz" };
        _libraryManager.Setup(x => x.GetItemById<Book>(_bookId)).Returns(book);
        _pageStreamService
            .Setup(x => x.GetPage(_bookId, 0, null))
            .Returns(new PageStreamResult(new byte[] { 4, 5, 6 }, "image/jpeg"));

        var result = await _controller.GetBookPage(_bookId);

        var fileResult = Assert.IsType<FileContentResult>(result);
        Assert.Equal("image/jpeg", fileResult.ContentType);
    }

    [Fact]
    public async Task GetBookPage_Returns_NotFound_When_Book_Not_Visible()
    {
        var book = new InaccessibleBook { Path = "/comics/secret.cbz" };
        _libraryManager.Setup(x => x.GetItemById<Book>(_bookId)).Returns(book);

        var result = await _controller.GetBookPage(_bookId);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetAuthorizedBook_Returns_NotFound_When_User_Not_Found()
    {
        var book = new AccessibleBook { Path = "/books/test.epub" };
        _libraryManager.Setup(x => x.GetItemById<Book>(_bookId)).Returns(book);
        _userManager.Setup(x => x.GetUserById(It.IsAny<Guid>())).Returns((User?)null);

        var result = await _controller.DownloadBook(_bookId);

        Assert.IsType<NotFoundResult>(result);
    }

    [Theory]
    [InlineData("INVALID")]
    [InlineData("AB")]
    [InlineData("")]
    public async Task GetAlphabeticalFeed_Returns_BadRequest_When_Filter_Is_Invalid(string filter)
    {
        var result = await _controller.GetAlphabeticalFeed(filter);
        Assert.IsType<BadRequestResult>(result);
    }

    [Fact]
    public async Task GetAlphabeticalFeed_Accepts_00_Or_Single_Character()
    {
        _opdsFeedProvider
            .Setup(x => x.GetAllBooks(It.IsAny<string>(), It.IsAny<Guid>(), "00", It.IsAny<int?>(), It.IsAny<int?>()))
            .Returns(new FeedDto { Title = "All Books" });

        var result = await _controller.GetAlphabeticalFeed("00");
        Assert.IsType<FileStreamResult>(result);

        _opdsFeedProvider
            .Setup(x => x.GetAllBooks(It.IsAny<string>(), It.IsAny<Guid>(), "A", It.IsAny<int?>(), It.IsAny<int?>()))
            .Returns(new FeedDto { Title = "Books - A" });

        var resultA = await _controller.GetAlphabeticalFeed("A");
        Assert.IsType<FileStreamResult>(resultA);
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

    private sealed class AccessibleBook : Book
    {
        public override bool IsVisibleStandalone(User user) => true;
    }

    private sealed class InaccessibleBook : Book
    {
        public override bool IsVisibleStandalone(User user) => false;
    }
}
