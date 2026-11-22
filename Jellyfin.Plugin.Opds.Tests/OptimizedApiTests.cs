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
using MediaBrowser.Model.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.Opds.Tests;

/// <summary>
/// Tests the optimized EPUB endpoints.
/// </summary>
[Collection("PluginState")]
public class OptimizedApiTests : IDisposable
{
    private readonly string _directory;
    private readonly string _epubPath;
    private readonly Book _book;
    private readonly User _user;
    private readonly Mock<IBasicAuthCache> _basicAuthCache = new();
    private readonly Mock<IEpubImageOptimizer> _optimizer = new();
    private readonly OpdsApi _controller;

    /// <summary>
    /// Initializes a new instance of the <see cref="OptimizedApiTests"/> class.
    /// </summary>
    public OptimizedApiTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "opds-optimized-api-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(_directory);
        _epubPath = Path.Combine(_directory, "book.epub");
        File.WriteAllBytes(_epubPath, new byte[] { 1, 2, 3 });

        var applicationPaths = new Mock<IApplicationPaths>();
        applicationPaths.SetupGet(x => x.PluginsPath).Returns(_directory);
        applicationPaths.SetupGet(x => x.PluginConfigurationsPath).Returns(_directory);
        var xmlSerializer = new Mock<IXmlSerializer>();
        xmlSerializer
            .Setup(x => x.DeserializeFromFile(It.IsAny<Type>(), It.IsAny<string>()))
            .Throws(new FileNotFoundException());
        _ = new OpdsPlugin(applicationPaths.Object, xmlSerializer.Object);

        _user = new User("reader", "auth", "pass");
        var userManager = new Mock<IUserManager>();
        userManager
            .Setup(x => x.AuthenticateUser(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
            .ReturnsAsync(_user);
        userManager.Setup(x => x.GetUserById(It.IsAny<Guid>())).Returns(_user);

        _book = new AccessibleBook { Path = _epubPath };
        var libraryManager = new Mock<ILibraryManager>();
        libraryManager.Setup(x => x.GetItemById<Book>(_book.Id)).Returns(_book);

        _basicAuthCache
            .Setup(x => x.GetOrAuthenticateAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<Func<Task<Guid?>>>(), It.IsAny<Func<Guid, bool>>()))
            .ReturnsAsync(_user.Id);

        var feedProvider = new Mock<IOpdsFeedProvider>();
        feedProvider.Setup(x => x.GetOptimizedFeed(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<int?>(), It.IsAny<int?>()))
            .Returns(new FeedDto { Id = "urn:jellyfin:opds:optimized", Title = "Optimized" });

        _controller = new OpdsApi(
            userManager.Object,
            feedProvider.Object,
            Mock.Of<IPageStreamService>(),
            libraryManager.Object,
            Mock.Of<IProgressionService>(),
            _basicAuthCache.Object,
            _optimizer.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = CreateHttpContext() }
        };
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (OpdsPlugin.Instance is not null)
        {
            OpdsPlugin.Instance.Configuration.EnableOptimizedEpub = false;
        }

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
    public async Task GetOptimizedFeed_ReturnsNotFound_WhenDisabled()
    {
        OpdsPlugin.Instance!.Configuration.EnableOptimizedEpub = false;

        var result = await _controller.GetOptimizedFeed();

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetOptimizedFeed_ReturnsFeed_WhenEnabled()
    {
        OpdsPlugin.Instance!.Configuration.EnableOptimizedEpub = true;

        var result = await _controller.GetOptimizedFeed();

        Assert.IsType<FileStreamResult>(result);
    }

    [Fact]
    public async Task GetOptimizedBook_ReturnsNotFound_WhenDisabled()
    {
        OpdsPlugin.Instance!.Configuration.EnableOptimizedEpub = false;

        var result = await _controller.GetOptimizedBook(_book.Id);

        Assert.IsType<NotFoundResult>(result);
        _optimizer.Verify(x => x.Optimize(It.IsAny<Book>()), Times.Never);
    }

    [Fact]
    public async Task GetOptimizedBook_ServesOptimizedFile_WhenEnabled()
    {
        OpdsPlugin.Instance!.Configuration.EnableOptimizedEpub = true;
        var optimizedPath = Path.Combine(_directory, "optimized.epub");
        File.WriteAllBytes(optimizedPath, new byte[] { 9 });
        _optimizer.Setup(x => x.Optimize(_book)).Returns(new OptimizedEpubResult(optimizedPath, true));

        var result = await _controller.GetOptimizedBook(_book.Id);

        var file = Assert.IsType<PhysicalFileResult>(result);
        Assert.Equal(optimizedPath, file.FileName);
        Assert.Equal(OptimizedConstants.MediaType, file.ContentType);
        Assert.Equal("book.epub", file.FileDownloadName);
    }

    [Fact]
    public async Task GetOptimizedBook_ServesOriginal_WhenNoDownscalingNeeded()
    {
        OpdsPlugin.Instance!.Configuration.EnableOptimizedEpub = true;
        _optimizer.Setup(x => x.Optimize(_book)).Returns(new OptimizedEpubResult(_epubPath, false));

        var result = await _controller.GetOptimizedBook(_book.Id);

        var file = Assert.IsType<PhysicalFileResult>(result);
        Assert.Equal(_epubPath, file.FileName);
        Assert.Equal(OptimizedConstants.MediaType, file.ContentType);
    }

    [Fact]
    public async Task GetOptimizedBook_ReturnsNotFound_WhenBookIsNotAnEpub()
    {
        OpdsPlugin.Instance!.Configuration.EnableOptimizedEpub = true;
        _optimizer.Setup(x => x.Optimize(_book)).Returns((OptimizedEpubResult?)null);

        var result = await _controller.GetOptimizedBook(_book.Id);

        Assert.IsType<NotFoundResult>(result);
    }

    private static DefaultHttpContext CreateHttpContext()
    {
        var context = new DefaultHttpContext();
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes("reader:password"));
        context.Request.Headers.Authorization = "Basic " + token;
        return context;
    }

    private sealed class AccessibleBook : Book
    {
        public override bool IsVisibleStandalone(User user) => true;
    }
}
