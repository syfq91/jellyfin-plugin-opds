using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.Opds.Configuration;
using Jellyfin.Plugin.Opds.Models;
using Jellyfin.Plugin.Opds.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Querying;
using MediaBrowser.Model.Search;
using MediaBrowser.Model.Serialization;
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

        var libraryManager = new Mock<ILibraryManager>();
        libraryManager.Setup(x => x.GetItemsResult(It.IsAny<InternalItemsQuery>()))
            .Returns(new QueryResult<BaseItem> { Items = new BaseItem[] { _book } });
        libraryManager.Setup(x => x.GetItemById(It.IsAny<Guid>())).Returns(parent);

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

        var pageStreamService = new Mock<IPageStreamService>();
        pageStreamService.Setup(x => x.GetPageCount(It.IsAny<Book>())).Returns(35);

        var applicationHost = new Mock<MediaBrowser.Controller.IServerApplicationHost>();
        applicationHost.SetupGet(x => x.FriendlyName).Returns("TestServer");

        BaseItem.LibraryManager = libraryManager.Object;

        _provider = new OpdsFeedProvider(
            libraryManager.Object,
            Mock.Of<ISearchManager>(),
            applicationHost.Object,
            userManager.Object,
            pageStreamService.Object,
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
