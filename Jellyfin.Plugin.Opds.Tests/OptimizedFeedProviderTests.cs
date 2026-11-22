using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.Opds.Models;
using Jellyfin.Plugin.Opds.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Drawing;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Querying;
using MediaBrowser.Model.Search;
using MediaBrowser.Model.Serialization;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.Opds.Tests;

/// <summary>
/// Tests the optimized EPUB catalog feeds.
/// </summary>
[Collection("PluginState")]
public class OptimizedFeedProviderTests : IDisposable
{
    private readonly string _directory;
    private readonly Book _firstEpub;
    private readonly Book _secondEpub;
    private readonly Book _comic;
    private readonly Mock<ILibraryManager> _libraryManager;
    private readonly OpdsFeedProvider _provider;

    /// <summary>
    /// Initializes a new instance of the <see cref="OptimizedFeedProviderTests"/> class.
    /// </summary>
    public OptimizedFeedProviderTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "opds-optimized-feed-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(_directory);

        var applicationPaths = new Mock<IApplicationPaths>();
        applicationPaths.SetupGet(x => x.PluginsPath).Returns(_directory);
        applicationPaths.SetupGet(x => x.PluginConfigurationsPath).Returns(_directory);
        var xmlSerializer = new Mock<IXmlSerializer>();
        xmlSerializer
            .Setup(x => x.DeserializeFromFile(It.IsAny<Type>(), It.IsAny<string>()))
            .Throws(new FileNotFoundException());
        _ = new OpdsPlugin(applicationPaths.Object, xmlSerializer.Object);

        _firstEpub = new Book { Id = Guid.NewGuid(), Path = Path.Combine(_directory, "a.epub"), Name = "A", SortName = "A" };
        _secondEpub = new Book { Id = Guid.NewGuid(), Path = Path.Combine(_directory, "b.epub"), Name = "B", SortName = "B" };
        _comic = new Book { Id = Guid.NewGuid(), Path = Path.Combine(_directory, "c.cbz"), Name = "C", SortName = "C" };

        var libraryManager = new Mock<ILibraryManager>();
        libraryManager.Setup(x => x.GetItemsResult(It.IsAny<InternalItemsQuery>()))
            .Returns(new QueryResult<BaseItem>
            {
                Items = new BaseItem[] { _firstEpub, _comic, _secondEpub },
                TotalRecordCount = 3
            });
        libraryManager.Setup(x => x.GetItemById(It.IsAny<Guid>())).Returns(new Folder { Name = "Author" });
        BaseItem.LibraryManager = libraryManager.Object;
        _libraryManager = libraryManager;

        var userManager = new Mock<IUserManager>();
        userManager.Setup(x => x.GetUserById(It.IsAny<Guid>())).Returns(new User("test", "auth", "pass"));

        var applicationHost = new Mock<IServerApplicationHost>();
        applicationHost.SetupGet(x => x.FriendlyName).Returns("TestServer");

        _provider = new OpdsFeedProvider(
            libraryManager.Object,
            Mock.Of<ISearchManager>(),
            applicationHost.Object,
            userManager.Object,
            Mock.Of<IPageStreamService>(),
            Mock.Of<MediaBrowser.Controller.Library.IUserDataManager>());
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
    public void GetOptimizedFeed_ReturnsNavigationFeed_WithSameCategoriesAsRoot()
    {
        var root = _provider.GetFeeds(string.Empty);
        var optimized = _provider.GetOptimizedFeed(string.Empty);

        Assert.NotNull(root.Entries);
        Assert.NotNull(optimized.Entries);
        Assert.Equal(root.Entries.Select(e => e.Title), optimized.Entries.Select(e => e.Title));
        Assert.Equal(
            new[]
            {
                "/opds/optimized/books",
                "/opds/optimized/books/favorite",
                "/opds/optimized/folders",
                "/opds/optimized/genres",
                "/opds/optimized/books/recentlyadded"
            },
            optimized.Entries.Select(e => e.Id));
        Assert.All(optimized.Entries, entry => Assert.Contains(
            entry.Links!,
            link => link.Href!.StartsWith("/opds/optimized/", StringComparison.Ordinal)));
    }

    [Fact]
    public void GetFeeds_DoesNotAdvertiseOptimizedCatalog_EvenWhenEnabled()
    {
        OpdsPlugin.Instance!.Configuration.EnableOptimizedEpub = true;

        var feed = _provider.GetFeeds(string.Empty);

        Assert.NotNull(feed.Entries);
        Assert.DoesNotContain(feed.Entries, entry => entry.Id == OptimizedConstants.FeedPath);
    }

    [Fact]
    public void OptimizedCategoryFeed_EpubsUseOptimizedAcquisitionLink_OtherFormatsKeepOriginal()
    {
        var feed = _provider.GetRecentlyAdded(string.Empty, Guid.Empty, optimized: true);

        Assert.NotNull(feed.Entries);
        Assert.Equal(3, feed.Entries.Count);

        foreach (var epub in new[] { _firstEpub, _secondEpub })
        {
            var entry = feed.Entries.Single(e => e.Id == epub.Id.ToString());
            Assert.Contains(entry.Links!, link =>
                link.Rel == OptimizedConstants.AcquisitionRel
                && link.Type == OptimizedConstants.MediaType
                && link.Title == OptimizedConstants.LinkTitle
                && link.Href == OptimizedConstants.DownloadPath + epub.Id);
            Assert.DoesNotContain(entry.Links!, link =>
                link.Href != null && link.Href.Contains("/opds/download/", StringComparison.Ordinal));
        }

        var comicEntry = feed.Entries.Single(e => e.Id == _comic.Id.ToString());
        Assert.Contains(comicEntry.Links!, link =>
            link.Rel == "http://opds-spec.org/acquisition"
            && link.Href == "/opds/download/" + _comic.Id);
        Assert.DoesNotContain(comicEntry.Links!, link =>
            link.Href == OptimizedConstants.DownloadPath + _comic.Id);
    }

    [Fact]
    public void OptimizedCategoryFeed_UsesOptimizedNavigationLinks()
    {
        var feed = _provider.GetRecentlyAdded(string.Empty, Guid.Empty, optimized: true);

        Assert.Contains(feed.Links, link => link.Rel == "self" && link.Href!.StartsWith("/opds/optimized/books/recentlyadded", StringComparison.Ordinal));
        Assert.Contains(feed.Links, link => link.Rel == "start" && link.Href == "/opds/optimized");
        Assert.Contains(feed.Links, link => link.Rel == "up" && link.Href == "/opds/optimized");
        Assert.Contains(feed.Links, link => link.Rel == "search" && link.Href == "/opds/optimized/search/{searchTerms}");
        Assert.Contains(feed.Links, link => link.Rel == "search" && link.Href == "/opds/optimized/osd");
    }

    [Fact]
    public void OptimizedAlphabeticalFeed_UsesOptimizedSubsectionLinks()
    {
        var feed = _provider.GetAlphabeticalFeed("/base", optimized: true);

        Assert.NotNull(feed.Entries);
        Assert.All(feed.Entries, entry =>
        {
            Assert.StartsWith("/opds/optimized/books/letter/", entry.Id, StringComparison.Ordinal);
            Assert.Contains(entry.Links!, link =>
                link.Rel == "subsection"
                && link.Href!.StartsWith("/base/opds/optimized/books/letter/", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void OptimizedFoldersFeed_UsesOptimizedSubsectionLinks()
    {
        var folderId = Guid.NewGuid();
        var folder = new Folder { Id = folderId, Name = "Comics" };
        _firstEpub.ParentId = folderId;
        _libraryManager.Setup(x => x.GetItemById(folderId)).Returns(folder);
        _libraryManager
            .Setup(x => x.GetItemsResult(It.Is<InternalItemsQuery>(q => q.Recursive && q.Limit == 100)))
            .Returns(new QueryResult<BaseItem> { Items = new BaseItem[] { _firstEpub }, TotalRecordCount = 1 });

        var rootFeed = _provider.GetRootFolders(string.Empty, Guid.Empty, optimized: true);
        Assert.NotNull(rootFeed.Entries);
        Assert.NotEmpty(rootFeed.Entries);
        Assert.All(rootFeed.Entries, entry =>
        {
            Assert.StartsWith("/opds/optimized/folders/", entry.Id, StringComparison.Ordinal);
            Assert.Contains(entry.Links!, link =>
                link.Rel == "subsection"
                && link.Href!.StartsWith("/opds/optimized/folders/", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void OptimizedSearchDescription_PointsToOptimizedCatalog()
    {
        var description = _provider.GetSearchDescription("/base", optimized: true);

        Assert.NotNull(description.Url);
        Assert.Contains(description.Url, url => url.Template == "/base/opds/optimized/search/{searchTerms}");
        Assert.Contains(description.Url, url => url.Template == "/base/opds/optimized/search?query={searchTerms}");
    }
}
