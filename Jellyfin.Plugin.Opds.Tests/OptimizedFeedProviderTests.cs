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
/// Tests the dedicated optimized EPUB feed.
/// </summary>
[Collection("PluginState")]
public class OptimizedFeedProviderTests : IDisposable
{
    private readonly string _directory;
    private readonly Book _firstEpub;
    private readonly Book _secondEpub;
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

        _firstEpub = new Book { Path = Path.Combine(_directory, "a.epub"), Name = "A", SortName = "A" };
        _secondEpub = new Book { Path = Path.Combine(_directory, "b.epub"), Name = "B", SortName = "B" };
        var comic = new Book { Path = Path.Combine(_directory, "c.cbz"), Name = "C", SortName = "C" };

        var libraryManager = new Mock<ILibraryManager>();
        libraryManager.Setup(x => x.GetItemsResult(It.IsAny<InternalItemsQuery>()))
            .Returns(new QueryResult<BaseItem>
            {
                Items = new BaseItem[] { _firstEpub, comic, _secondEpub },
                TotalRecordCount = 3
            });
        libraryManager.Setup(x => x.GetItemById(It.IsAny<Guid>())).Returns(new Folder { Name = "Author" });
        BaseItem.LibraryManager = libraryManager.Object;

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
    public void GetOptimizedFeed_OnlyIncludesEpubs_WithOptimizedAcquisitionLink()
    {
        var feed = _provider.GetOptimizedFeed(string.Empty, Guid.Empty);

        Assert.Equal(2, feed.TotalResults);
        Assert.NotNull(feed.Entries);
        Assert.Equal(2, feed.Entries.Count);
        Assert.All(feed.Entries, entry => Assert.Contains(
            entry.Links!,
            link => link.Rel == OptimizedConstants.AcquisitionRel
                && link.Type == OptimizedConstants.MediaType
                && link.Title == OptimizedConstants.LinkTitle
                && link.Href == OptimizedConstants.DownloadPath
                    + (entry.Id == _firstEpub.Id.ToString() ? _firstEpub.Id.ToString() : _secondEpub.Id.ToString())));

        // The original /opds/download/ acquisition link must be removed so that
        // OPDS clients don't pick it up instead of the optimized link.
        Assert.All(feed.Entries, entry => Assert.DoesNotContain(
            entry.Links!,
            link => link.Rel == "http://opds-spec.org/acquisition"
                && link.Href != null
                && link.Href.Contains("/opds/download/", StringComparison.Ordinal)));
    }

    [Fact]
    public void GetOptimizedFeed_Paginates()
    {
        var feed = _provider.GetOptimizedFeed(string.Empty, Guid.Empty, startIndex: 1, limit: 1);

        Assert.Equal(2, feed.TotalResults);
        Assert.Equal(1, feed.StartIndex);
        Assert.Equal(1, feed.ItemsPerPage);
        Assert.NotNull(feed.Entries);
        Assert.Single(feed.Entries);
        Assert.DoesNotContain(feed.Links, link => link.Rel == "next");
        Assert.Contains(feed.Links, link => link.Rel == "previous");
    }

    [Fact]
    public void GetFeeds_AdvertisesOptimizedFeed_WhenEnabled()
    {
        OpdsPlugin.Instance!.Configuration.EnableOptimizedEpub = true;

        var feed = _provider.GetFeeds(string.Empty);

        Assert.NotNull(feed.Entries);
        Assert.Contains(feed.Entries, entry => entry.Id == OptimizedConstants.FeedPath);
    }

    [Fact]
    public void GetFeeds_DoesNotAdvertiseOptimizedFeed_WhenDisabled()
    {
        OpdsPlugin.Instance!.Configuration.EnableOptimizedEpub = false;

        var feed = _provider.GetFeeds(string.Empty);

        Assert.NotNull(feed.Entries);
        Assert.DoesNotContain(feed.Entries, entry => entry.Id == OptimizedConstants.FeedPath);
    }
}
