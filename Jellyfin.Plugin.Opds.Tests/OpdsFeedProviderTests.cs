using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.Opds.Configuration;
using Jellyfin.Plugin.Opds.Models;
using Jellyfin.Plugin.Opds.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Drawing;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using MediaBrowser.Model.Search;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.Opds.Tests;

/// <summary>
/// Tests the emission of page streaming links by the feed provider.
/// </summary>
[Collection("PluginState")]
public class OpdsFeedProviderTests : IDisposable
{
    private const string AtomNamespace = "http://www.w3.org/2005/Atom";
    private const string PseNamespace = "http://vaemendis.net/opds-pse/ns";

    private static readonly long ProgressTicks = 5 * PseConstants.TicksPerPage;
    private static readonly DateTime LastPlayedDate = new(2020, 5, 6, 7, 8, 9, DateTimeKind.Utc);

    private readonly string _directory;
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _parentId = Guid.NewGuid();
    private readonly Book _book;
    private readonly Mock<ILibraryManager> _libraryManager;
    private readonly Mock<IPageStreamService> _pageStreamService;
    private readonly OpdsFeedProvider _provider;

    /// <summary>
    /// Initializes a new instance of the <see cref="OpdsFeedProviderTests"/> class.
    /// </summary>
    public OpdsFeedProviderTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "opds-pse-feed-tests-" + Guid.NewGuid());
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

        _book = new Book
        {
            ParentId = _parentId,
            Path = Path.Combine(_directory, "book.cbz")
        };

        var parent = new Folder { Name = "Author" };

        _libraryManager = new Mock<ILibraryManager>();
        _libraryManager.Setup(x => x.GetItemsResult(It.IsAny<InternalItemsQuery>()))
            .Returns(new QueryResult<BaseItem> { Items = new BaseItem[] { _book }, TotalRecordCount = 1 });
        _libraryManager.Setup(x => x.GetItemById(It.IsAny<Guid>())).Returns(parent);

        var user = new User("test", "auth", "pass");

        var userManager = new Mock<IUserManager>();
        userManager.Setup(x => x.GetUserById(It.IsAny<Guid>())).Returns(user);

        var userDataManager = new Mock<IUserDataManager>();
        userDataManager.Setup(x => x.GetUserDataBatch(It.IsAny<System.Collections.Generic.IReadOnlyList<BaseItem>>(), user))
            .Returns(new System.Collections.Generic.Dictionary<Guid, UserItemData>
            {
                [_book.Id] = new UserItemData
                {
                    Key = _book.Id.ToString(),
                    PlaybackPositionTicks = ProgressTicks,
                    LastPlayedDate = LastPlayedDate
                }
            });

        _pageStreamService = new Mock<IPageStreamService>();
        _pageStreamService.Setup(x => x.GetPageCount(It.IsAny<Book>())).Returns(35);

        var applicationHost = new Mock<MediaBrowser.Controller.IServerApplicationHost>();
        applicationHost.SetupGet(x => x.FriendlyName).Returns("TestServer");

        BaseItem.LibraryManager = _libraryManager.Object;

        _provider = new OpdsFeedProvider(
            _libraryManager.Object,
            Mock.Of<ISearchManager>(),
            applicationHost.Object,
            userManager.Object,
            _pageStreamService.Object,
            userDataManager.Object);
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
    public void StreamLink_Includes_Reading_Progress()
    {
        OpdsPlugin.Instance!.Configuration.EnablePageStreaming = true;

        var feed = _provider.GetRecentlyAdded("/base", _userId);

        var link = GetStreamLink(feed);
        Assert.NotNull(link);
        Assert.Equal("35", link.Attribute(XName.Get("count", PseNamespace))?.Value);
        Assert.Equal("5", link.Attribute(XName.Get("lastRead", PseNamespace))?.Value);
        Assert.Equal("2020-05-06T07:08:09Z", link.Attribute(XName.Get("lastReadDate", PseNamespace))?.Value);
    }

    [Fact]
    public void StreamLink_Is_Omitted_When_Disabled()
    {
        OpdsPlugin.Instance!.Configuration.EnablePageStreaming = false;

        var feed = _provider.GetRecentlyAdded("/base", _userId);

        var link = GetStreamLink(feed);
        Assert.Null(link);
    }

    [Fact]
    public void Progression_Link_Is_Emitted_When_Enabled()
    {
        OpdsPlugin.Instance!.Configuration.EnableProgression = true;

        var feed = _provider.GetRecentlyAdded("/base", _userId);

        var link = GetLink(feed, ProgressionConstants.Rel);
        Assert.NotNull(link);
        Assert.Equal(ProgressionConstants.MediaType, link!.Attribute("type")?.Value);
        Assert.StartsWith("/base/opds/progression/", link.Attribute("href")?.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void Progression_Link_Is_Omitted_When_Disabled()
    {
        OpdsPlugin.Instance!.Configuration.EnableProgression = false;

        var feed = _provider.GetRecentlyAdded("/base", _userId);

        Assert.Null(GetLink(feed, ProgressionConstants.Rel));
    }

    [Fact]
    public void Root_Feed_Advertises_Authentication_Document_When_Required()
    {
        OpdsPlugin.Instance!.Configuration.AllowAnonymousAccess = false;

        var feed = _provider.GetFeeds("/base");

        var link = GetLink(feed, AuthenticationConstants.Rel);
        Assert.NotNull(link);
        Assert.Equal(AuthenticationConstants.MediaType, link!.Attribute("type")?.Value);
        Assert.Equal("/base/opds/authentication.json", link.Attribute("href")?.Value);
    }

    [Fact]
    public void Root_Feed_Omits_Authentication_Document_When_Anonymous_Access_Allowed()
    {
        OpdsPlugin.Instance!.Configuration.AllowAnonymousAccess = true;

        var feed = _provider.GetFeeds("/base");

        Assert.Null(GetLink(feed, AuthenticationConstants.Rel));
    }

    [Fact]
    public void Pagination_Links_And_OpenSearch_Elements_Are_Emitted()
    {
        _libraryManager.Setup(x => x.GetItemsResult(It.IsAny<InternalItemsQuery>()))
            .Returns(new QueryResult<BaseItem> { Items = new BaseItem[] { _book }, TotalRecordCount = 120 });

        var feed = _provider.GetRecentlyAdded("/base", _userId, startIndex: 50, limit: 50);

        Assert.Equal(120, feed.TotalResults);
        Assert.Equal(50, feed.StartIndex);
        Assert.Equal(50, feed.ItemsPerPage);

        var firstLink = feed.Links?.FirstOrDefault(l => l.Rel == "first");
        var prevLink = feed.Links?.FirstOrDefault(l => l.Rel == "prev" || l.Rel == "previous");
        var nextLink = feed.Links?.FirstOrDefault(l => l.Rel == "next");
        var lastLink = feed.Links?.FirstOrDefault(l => l.Rel == "last");

        Assert.NotNull(firstLink);
        Assert.NotNull(prevLink);
        Assert.NotNull(nextLink);
        Assert.NotNull(lastLink);

        Assert.Contains("startIndex=0", firstLink.Href, StringComparison.Ordinal);
        Assert.Contains("startIndex=0", prevLink.Href, StringComparison.Ordinal);
        Assert.Contains("startIndex=100", nextLink.Href, StringComparison.Ordinal);
        Assert.Contains("startIndex=100", lastLink.Href, StringComparison.Ordinal);

        var xml = Serialize(feed);
        Assert.Contains("<opensearch:totalResults>120</opensearch:totalResults>", xml, StringComparison.Ordinal);
        Assert.Contains("<opensearch:startIndex>50</opensearch:startIndex>", xml, StringComparison.Ordinal);
        Assert.Contains("<opensearch:itemsPerPage>50</opensearch:itemsPerPage>", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void Entry_Contains_Thumbnail_And_Cover_Links()
    {
        _book.SetImage(
            new ItemImageInfo
            {
                Type = MediaBrowser.Model.Entities.ImageType.Primary,
                Path = Path.Combine(_directory, "cover.jpg")
            },
            0);

        try
        {
            var feed = _provider.GetRecentlyAdded("/base", _userId);
            var entry = feed.Entries?.FirstOrDefault();
            Assert.NotNull(entry);

            var thumbLink = entry.Links?.FirstOrDefault(l => l.Rel == "http://opds-spec.org/image/thumbnail");
            var coverLink = entry.Links?.FirstOrDefault(l => l.Rel == "http://opds-spec.org/image");

            Assert.NotNull(thumbLink);
            Assert.NotNull(coverLink);
            Assert.Equal($"/base/opds/thumbnail/{_book.Id}", thumbLink.Href);
            Assert.Equal($"/base/opds/cover/{_book.Id}", coverLink.Href);
        }
        finally
        {
            _book.ImageInfos = Array.Empty<ItemImageInfo>();
        }
    }

    [Fact]
    public void FastPath_PageCount_Uses_RunTimeTicks_When_Available()
    {
        OpdsPlugin.Instance!.Configuration.EnablePageStreaming = true;
        _book.RunTimeTicks = 77 * PseConstants.TicksPerPage;

        var feed = _provider.GetRecentlyAdded("/base", _userId);

        var link = GetStreamLink(feed);
        Assert.NotNull(link);
        Assert.Equal("77", link.Attribute(XName.Get("count", PseNamespace))?.Value);
        _pageStreamService.Verify(x => x.GetPageCount(It.IsAny<Book>()), Times.Never);
    }

    [Fact]
    public void Root_Feed_Includes_Folders_Entry()
    {
        var feed = _provider.GetFeeds("/base");
        var folderEntry = feed.Entries?.FirstOrDefault(e => e.Title == "Folders");
        Assert.NotNull(folderEntry);
        Assert.Equal("/opds/folders", folderEntry.Id);
        var link = folderEntry.Links?.FirstOrDefault();
        Assert.NotNull(link);
        Assert.Equal("/base/opds/folders", link.Href);
    }

    [Fact]
    public void GetRootFolders_Returns_Folders_Containing_Books()
    {
        var folderId = Guid.NewGuid();
        var folder = new Folder { Id = folderId, Name = "Comics" };

        _libraryManager.Setup(x => x.GetItemsResult(It.Is<InternalItemsQuery>(q => q.Recursive && q.Limit == 1)))
            .Returns(new QueryResult<BaseItem> { Items = new BaseItem[] { _book }, TotalRecordCount = 1 });
        _libraryManager.Setup(x => x.GetItemsResult(It.Is<InternalItemsQuery>(q => q.Recursive && q.Limit == 100)))
            .Returns(new QueryResult<BaseItem> { Items = new BaseItem[] { _book }, TotalRecordCount = 1 });
        _libraryManager.Setup(x => x.GetItemById(folderId)).Returns(folder);
        _book.ParentId = folderId;

        var feed = _provider.GetRootFolders("/base", _userId);

        Assert.NotNull(feed);
        var entry = feed.Entries?.FirstOrDefault(e => e.Title == "Comics");
        Assert.NotNull(entry);
        Assert.Equal($"/base/opds/folders/{folderId}", entry.Links?.FirstOrDefault()?.Href);
    }

    [Fact]
    public void GetFolder_Returns_Subfolders_And_Books()
    {
        var rootFolderId = Guid.NewGuid();
        var rootFolder = new Folder { Id = rootFolderId, Name = "Comics Library" };

        var subFolderId = Guid.NewGuid();
        var subFolder = new Folder { Id = subFolderId, Name = "Batman", ParentId = rootFolderId };

        var bookItem = new Book
        {
            Id = Guid.NewGuid(),
            Name = "Batman Year One",
            ParentId = rootFolderId,
            Path = Path.Combine(_directory, "batman.cbz")
        };

        _libraryManager.Setup(x => x.GetItemById(rootFolderId)).Returns(rootFolder);
        _libraryManager.Setup(x => x.GetItemById(subFolderId)).Returns(subFolder);

        // Subfolder check ContainsBooks:
        _libraryManager.Setup(x => x.GetItemsResult(It.Is<InternalItemsQuery>(q => q.ParentId == subFolderId && q.Recursive)))
            .Returns(new QueryResult<BaseItem> { Items = new BaseItem[] { bookItem }, TotalRecordCount = 1 });

        // Direct children query:
        _libraryManager.Setup(x => x.GetItemsResult(It.Is<InternalItemsQuery>(q => q.ParentId == rootFolderId && !q.Recursive)))
            .Returns(new QueryResult<BaseItem> { Items = new BaseItem[] { subFolder, bookItem }, TotalRecordCount = 2 });

        var feed = _provider.GetFolder("/base", _userId, rootFolderId);

        Assert.NotNull(feed);
        Assert.Equal(2, feed.Entries?.Count);

        var subFolderEntry = feed.Entries?.FirstOrDefault(e => e.Title == "Batman");
        var bookEntry = feed.Entries?.FirstOrDefault(e => e.Title == "Batman Year One");

        Assert.NotNull(subFolderEntry);
        Assert.NotNull(bookEntry);

        Assert.Equal($"/base/opds/folders/{subFolderId}", subFolderEntry.Links?.FirstOrDefault(l => l.Rel == "subsection")?.Href);
        Assert.NotNull(bookEntry.Links?.FirstOrDefault(l => l.Rel == "http://opds-spec.org/acquisition"));

        // Verify "up" link points to root folders
        var upLink = feed.Links?.FirstOrDefault(l => l.Rel == "up");
        Assert.NotNull(upLink);
        Assert.Equal("/base/opds/folders", upLink.Href);
    }

    [Fact]
    public void GetFolder_Returns_Null_When_Folder_NotFound()
    {
        var nonExistentId = Guid.NewGuid();
        _libraryManager.Setup(x => x.GetItemById(nonExistentId)).Returns((BaseItem?)null);

        var feed = _provider.GetFolder("/base", _userId, nonExistentId);
        Assert.Null(feed);
    }

    [Fact]
    public void GetFolder_Resolves_UserView_To_CollectionFolder()
    {
        var collFolderId = Guid.NewGuid();
        var collFolder = new CollectionFolder { Id = collFolderId, Name = "Books Collection" };

        var userViewId = Guid.NewGuid();
        var userView = new UserView
        {
            Id = userViewId,
            Name = "Books",
            DisplayParentId = collFolderId
        };

        var bookItem = new Book
        {
            Id = Guid.NewGuid(),
            Name = "Dune",
            ParentId = collFolderId,
            Path = Path.Combine(_directory, "dune.epub")
        };

        _libraryManager.Setup(x => x.GetItemById(userViewId)).Returns(userView);
        _libraryManager.Setup(x => x.GetItemById(collFolderId)).Returns(collFolder);

        // Fallback query under collection folder:
        _libraryManager.Setup(x => x.GetItemsResult(It.Is<InternalItemsQuery>(q => q.ParentId == collFolderId && !q.Recursive)))
            .Returns(new QueryResult<BaseItem> { Items = new BaseItem[] { bookItem }, TotalRecordCount = 1 });

        var feed = _provider.GetFolder("/base", _userId, userViewId);

        Assert.NotNull(feed);
        Assert.Equal("Books Collection - TestServer", feed.Title);
        var entry = feed.Entries?.FirstOrDefault(e => e.Title == "Dune");
        Assert.NotNull(entry);
    }

    [Fact]
    public void GetFolder_Uses_Direct_Children_When_Available()
    {
        var rootFolderId = Guid.NewGuid();
        var subFolderId = Guid.NewGuid();
        var subFolder = new Folder { Id = subFolderId, Name = "SciFi", ParentId = rootFolderId };

        var bookItem = new Book
        {
            Id = Guid.NewGuid(),
            Name = "Hyperion",
            ParentId = rootFolderId,
            Path = Path.Combine(_directory, "hyperion.epub")
        };

        var testFolder = new TestFolder(new BaseItem[] { subFolder, bookItem })
        {
            Id = rootFolderId,
            Name = "My Library"
        };

        _libraryManager.Setup(x => x.GetItemById(rootFolderId)).Returns(testFolder);
        _libraryManager.Setup(x => x.GetItemById(subFolderId)).Returns(subFolder);

        // Subfolder check ContainsBooks:
        _libraryManager.Setup(x => x.GetItemsResult(It.Is<InternalItemsQuery>(q => q.ParentId == subFolderId && q.Recursive)))
            .Returns(new QueryResult<BaseItem> { Items = new BaseItem[] { bookItem }, TotalRecordCount = 1 });

        var feed = _provider.GetFolder("/base", _userId, rootFolderId);

        Assert.NotNull(feed);
        Assert.Equal(2, feed.Entries?.Count);
        Assert.Contains(feed.Entries!, e => e.Title == "SciFi");
        Assert.Contains(feed.Entries!, e => e.Title == "Hyperion");
    }

    [Fact]
    public void GetRootFolders_Resolves_UserViews_To_CollectionFolders()
    {
        var collFolderId = Guid.NewGuid();
        var collFolder = new CollectionFolder { Id = collFolderId, Name = "Audiobooks Library" };

        var userViewId = Guid.NewGuid();
        var userView = new UserView
        {
            Id = userViewId,
            Name = "Audiobooks",
            DisplayParentId = collFolderId
        };

        var userRoot = new Mock<Folder>();
        userRoot.Setup(r => r.GetChildren(It.IsAny<User>(), true, It.IsAny<InternalItemsQuery>()))
            .Returns(new List<BaseItem> { userView });

        _libraryManager.Setup(x => x.GetUserRootFolder()).Returns(userRoot.Object);
        _libraryManager.Setup(x => x.GetItemById(collFolderId)).Returns(collFolder);
        _libraryManager.Setup(x => x.GetItemById(userViewId)).Returns(userView);

        _libraryManager.Setup(x => x.GetItemsResult(It.Is<InternalItemsQuery>(q => q.ParentId == collFolderId && q.Recursive)))
            .Returns(new QueryResult<BaseItem> { Items = new BaseItem[] { _book }, TotalRecordCount = 1 });

        var feed = _provider.GetRootFolders("/base", _userId);

        Assert.NotNull(feed);
        var entry = feed.Entries?.FirstOrDefault(e => e.Title == "Audiobooks Library");
        Assert.NotNull(entry);
        Assert.Equal($"/base/opds/folders/{collFolderId}", entry.Links?.FirstOrDefault()?.Href);
    }

    [Fact]
    public async Task SearchBooks_UrlEncodes_SearchTerm_In_SelfLink_And_Pagination()
    {
        var searchManager = new Mock<ISearchManager>();
        searchManager.Setup(x => x.GetSearchHintsAsync(It.IsAny<SearchQuery>(), It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync(new QueryResult<SearchHintInfo>
            {
                Items = new[] { new SearchHintInfo { Item = _book } },
                TotalRecordCount = 100
            });

        var applicationHost = new Mock<MediaBrowser.Controller.IServerApplicationHost>();
        applicationHost.SetupGet(x => x.FriendlyName).Returns("TestServer");

        var userManager = new Mock<IUserManager>();
        userManager.Setup(x => x.GetUserById(It.IsAny<Guid>())).Returns(new User("test", "auth", "pass"));

        var provider = new OpdsFeedProvider(
            _libraryManager.Object,
            searchManager.Object,
            applicationHost.Object,
            userManager.Object,
            _pageStreamService.Object,
            Mock.Of<IUserDataManager>());

        var searchTerm = "C# & C++ programming?";
        var encodedTerm = Uri.EscapeDataString(searchTerm);

        var feed = await provider.SearchBooks("http://localhost", _userId, searchTerm, 0, 50);

        var selfLink = feed.Links?.FirstOrDefault(l => l.Rel == "self");
        Assert.NotNull(selfLink);
        Assert.Equal($"http://localhost/opds/search/{encodedTerm}?startIndex=0&limit=50", selfLink.Href);

        var nextLink = feed.Links?.FirstOrDefault(l => l.Rel == "next");
        Assert.NotNull(nextLink);
        Assert.Contains(encodedTerm, nextLink.Href, StringComparison.Ordinal);
    }

    [Fact]
    public void GetBookThumbnail_Uses_BookId_In_CacheKey_And_Plugin_CachePath()
    {
        var coverFile = Path.Combine(_directory, "cover.jpg");
        File.WriteAllBytes(coverFile, new byte[] { 1, 2, 3 });

        _book.SetImage(new ItemImageInfo { Type = ImageType.Primary, Path = coverFile }, 0);
        _libraryManager.Setup(x => x.GetItemById(_book.Id)).Returns(_book);

        var appPaths = new Mock<IApplicationPaths>();
        var cacheDir = Path.Combine(_directory, "cache");
        appPaths.SetupGet(x => x.CachePath).Returns(cacheDir);

        var imageEncoder = new Mock<IImageEncoder>();
        imageEncoder.SetupGet(x => x.SupportsImageEncoding).Returns(true);
        imageEncoder.SetupGet(x => x.SupportedInputFormats).Returns(new[] { "jpg" });
        imageEncoder.Setup(x => x.EncodeImage(
                It.IsAny<string>(),
                It.IsAny<DateTime>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<ImageOrientation?>(),
                It.IsAny<int>(),
                It.IsAny<ImageProcessingOptions>(),
                It.IsAny<ImageFormat>()))
            .Returns<string, DateTime, string, bool, ImageOrientation?, int, ImageProcessingOptions, ImageFormat>((inPath, date, outPath, crop, orientation, quality, opt, fmt) =>
            {
                File.WriteAllBytes(outPath, new byte[] { 4, 5, 6 });
                return outPath;
            });

        var provider = new OpdsFeedProvider(
            _libraryManager.Object,
            Mock.Of<ISearchManager>(),
            Mock.Of<MediaBrowser.Controller.IServerApplicationHost>(),
            Mock.Of<IUserManager>(),
            _pageStreamService.Object,
            Mock.Of<IUserDataManager>(),
            imageEncoder.Object,
            appPaths.Object);

        var thumbnail = provider.GetBookThumbnail(_book.Id, 240);

        Assert.NotNull(thumbnail);
        Assert.Equal(new byte[] { 4, 5, 6 }, thumbnail);

        var expectedThumbDir = Path.Combine(cacheDir, "opds-thumbnails");
        Assert.True(Directory.Exists(expectedThumbDir));
        var cachedFiles = Directory.GetFiles(expectedThumbDir);
        Assert.Single(cachedFiles);
        Assert.Contains(_book.Id.ToString("N"), cachedFiles[0], StringComparison.Ordinal);
    }

    [Fact]
    public void GetBookThumbnail_CleansUp_TempFiles_On_Encoding_Failure()
    {
        var coverFile = Path.Combine(_directory, "cover_fail.jpg");
        File.WriteAllBytes(coverFile, new byte[] { 1, 2, 3 });

        _book.SetImage(new ItemImageInfo { Type = ImageType.Primary, Path = coverFile }, 0);
        _libraryManager.Setup(x => x.GetItemById(_book.Id)).Returns(_book);

        var appPaths = new Mock<IApplicationPaths>();
        var cacheDir = Path.Combine(_directory, "cache-fail");
        appPaths.SetupGet(x => x.CachePath).Returns(cacheDir);

        var imageEncoder = new Mock<IImageEncoder>();
        imageEncoder.SetupGet(x => x.SupportsImageEncoding).Returns(true);
        imageEncoder.SetupGet(x => x.SupportedInputFormats).Returns(new[] { "jpg" });
        imageEncoder.Setup(x => x.EncodeImage(
                It.IsAny<string>(),
                It.IsAny<DateTime>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<ImageOrientation?>(),
                It.IsAny<int>(),
                It.IsAny<ImageProcessingOptions>(),
                It.IsAny<ImageFormat>()))
            .Returns<string, DateTime, string, bool, ImageOrientation?, int, ImageProcessingOptions, ImageFormat>((inPath, date, outPath, crop, orientation, quality, opt, fmt) =>
            {
                File.WriteAllBytes(outPath, new byte[] { 7, 8, 9 });
                throw new InvalidOperationException("Encoder crashed");
            });

        var logger = new Mock<ILogger<OpdsFeedProvider>>();
        var provider = new OpdsFeedProvider(
            _libraryManager.Object,
            Mock.Of<ISearchManager>(),
            Mock.Of<MediaBrowser.Controller.IServerApplicationHost>(),
            Mock.Of<IUserManager>(),
            _pageStreamService.Object,
            Mock.Of<IUserDataManager>(),
            imageEncoder.Object,
            appPaths.Object,
            logger.Object);

        var thumbnail = provider.GetBookThumbnail(_book.Id, 240);

        Assert.Null(thumbnail);
        logger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                It.IsAny<Exception>(),
                It.Is<Func<It.IsAnyType, Exception?, string>>((v, t) => true)),
            Times.Once);

        var expectedThumbDir = Path.Combine(cacheDir, "opds-thumbnails");
        if (Directory.Exists(expectedThumbDir))
        {
            var tempFiles = Directory.GetFiles(expectedThumbDir, "*.tmp");
            Assert.Empty(tempFiles);
        }
    }

    [Fact]
    public void GetFolder_AnonymousUser_Returns_Null_When_Folder_Does_Not_Contain_Books()
    {
        var moviesFolderId = Guid.NewGuid();
        var moviesFolder = new Folder { Id = moviesFolderId, Name = "Movies" };
        _libraryManager.Setup(x => x.GetItemById(moviesFolderId)).Returns(moviesFolder);
        _libraryManager.Setup(x => x.GetItemsResult(It.Is<InternalItemsQuery>(q => q.ParentId == moviesFolderId && q.Recursive)))
            .Returns(new QueryResult<BaseItem> { Items = Array.Empty<BaseItem>(), TotalRecordCount = 0 });

        var feed = _provider.GetFolder("/base", Guid.Empty, moviesFolderId);

        Assert.Null(feed);
    }

    [Fact]
    public void GetFolder_AnonymousUser_Returns_Feed_When_Folder_Is_Books_Collection()
    {
        var booksFolderId = Guid.NewGuid();
        var booksFolder = new CollectionFolder
        {
            Id = booksFolderId,
            Name = "Books",
            CollectionType = CollectionType.books
        };
        _libraryManager.Setup(x => x.GetItemById(booksFolderId)).Returns(booksFolder);

        var feed = _provider.GetFolder("/base", Guid.Empty, booksFolderId);

        Assert.NotNull(feed);
        Assert.Equal("Books - TestServer", feed.Title);
    }

    [Fact]
    public void GetRootFolders_LogsWarning_When_Exception_Thrown()
    {
        var logger = new Mock<ILogger<OpdsFeedProvider>>();
        var failingLibraryManager = new Mock<ILibraryManager>();
        failingLibraryManager.Setup(x => x.GetUserRootFolder()).Throws(new InvalidOperationException("DB disk error"));

        var appHost = new Mock<MediaBrowser.Controller.IServerApplicationHost>();
        appHost.SetupGet(x => x.FriendlyName).Returns("TestServer");

        var provider = new OpdsFeedProvider(
            failingLibraryManager.Object,
            Mock.Of<ISearchManager>(),
            appHost.Object,
            Mock.Of<IUserManager>(),
            _pageStreamService.Object,
            Mock.Of<IUserDataManager>(),
            logger: logger.Object);

        var feed = provider.GetRootFolders("/base", _userId);

        Assert.NotNull(feed);
        logger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                It.IsAny<Exception>(),
                It.Is<Func<It.IsAnyType, Exception?, string>>((v, t) => true)),
            Times.AtLeastOnce);
    }

    private sealed class TestFolder : Folder
    {
        private readonly List<BaseItem> _mockChildren;

        public TestFolder(IEnumerable<BaseItem> children)
        {
            _mockChildren = children.ToList();
        }

        public override IEnumerable<BaseItem> Children => _mockChildren;

        public override IReadOnlyList<BaseItem> GetChildren(User user, bool includeLinkedChildren, InternalItemsQuery? query = null)
        {
            return _mockChildren;
        }
    }

    private static XElement? GetStreamLink(FeedDto feed)
    {
        var xml = Serialize(feed);
        return XDocument.Parse(xml)
            .Descendants(XName.Get("link", AtomNamespace))
            .FirstOrDefault(e => string.Equals(e.Attribute("rel")?.Value, PseConstants.StreamRel, StringComparison.Ordinal));
    }

    private static XElement? GetLink(FeedDto feed, string rel)
    {
        var xml = Serialize(feed);
        return XDocument.Parse(xml)
            .Descendants(XName.Get("link", AtomNamespace))
            .FirstOrDefault(e => string.Equals(e.Attribute("rel")?.Value, rel, StringComparison.Ordinal));
    }

    private static string Serialize(FeedDto feed)
    {
        using var writer = new StringWriter();
        XmlHelper.Create(typeof(FeedDto), AtomNamespace).Serialize(writer, feed);
        return writer.ToString();
    }
}
