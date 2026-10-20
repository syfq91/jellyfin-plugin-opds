using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Mime;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.Opds.Models;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Drawing;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Net;
using MediaBrowser.Model.Querying;
using MediaBrowser.Model.Search;

namespace Jellyfin.Plugin.Opds.Services;

/// <summary>
/// OPDS feed provider.
/// </summary>
public class OpdsFeedProvider : IOpdsFeedProvider
{
    private static readonly BaseItemKind[] BookItemTypes = { BaseItemKind.Book };

    private static readonly AuthorDto PluginAuthor = new("Jellyfin", "https://github.com/jellyfin/jellyfin-plugin-opds");

    private static readonly IReadOnlyDictionary<Guid, UserItemData> EmptyUserItemData = new Dictionary<Guid, UserItemData>();

    private readonly ILibraryManager _libraryManager;
    private readonly ISearchManager _searchManager;
    private readonly IServerApplicationHost _serverApplicationHost;
    private readonly IUserManager _userManager;
    private readonly IPageStreamService _pageStreamService;
    private readonly IUserDataManager _userDataManager;
    private readonly IImageEncoder? _imageEncoder;

    /// <summary>
    /// Initializes a new instance of the <see cref="OpdsFeedProvider"/> class.
    /// </summary>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="searchManager">Instance of the <see cref="ISearchManager"/> interface.</param>
    /// <param name="serverApplicationHost">Instance of the <see cref="IServerApplicationHost"/> interface.</param>
    /// <param name="userManager">Instance of the <see cref="IUserManager"/> interface.</param>
    /// <param name="pageStreamService">Instance of the <see cref="IPageStreamService"/> interface.</param>
    /// <param name="userDataManager">Instance of the <see cref="IUserDataManager"/> interface.</param>
    /// <param name="imageEncoder">Optional instance of the <see cref="IImageEncoder"/> interface.</param>
    public OpdsFeedProvider(
        ILibraryManager libraryManager,
        ISearchManager searchManager,
        IServerApplicationHost serverApplicationHost,
        IUserManager userManager,
        IPageStreamService pageStreamService,
        IUserDataManager userDataManager,
        IImageEncoder? imageEncoder = null)
    {
        _libraryManager = libraryManager;
        _searchManager = searchManager;
        _serverApplicationHost = serverApplicationHost;
        _userManager = userManager;
        _pageStreamService = pageStreamService;
        _userDataManager = userDataManager;
        _imageEncoder = imageEncoder;
    }

    /// <inheritdoc />
    public FeedDto GetFeeds(string baseUrl)
    {
        var timestamp = DateTime.UtcNow;
        var links = new List<LinkDto>
        {
            new("self", baseUrl + "/opds", "application/atom+xml;profile=opds-catalog;kind=navigation"),
            new("start", baseUrl + "/opds", "application/atom+xml;profile=opds-catalog;kind=navigation", "Start"),
            new("search", baseUrl + "/opds/osd", "application/opensearchdescription+xml"),
            new("search", baseUrl + "/opds/search/{searchTerms}", "application/atom+xml", "Search")
        };

        // The authentication document is only advertised when it is required.
        if (!OpdsPlugin.Instance!.Configuration.AllowAnonymousAccess)
        {
            links.Add(new LinkDto(
                AuthenticationConstants.Rel,
                baseUrl + AuthenticationConstants.DocumentPath,
                AuthenticationConstants.MediaType));
        }

        return new FeedDto
        {
            Id = Guid.NewGuid().ToString(),
            Links = links.ToArray(),
            Title = GetFeedName("Feeds"),
            Author = PluginAuthor,
            Entries = new List<EntryDto>
            {
                new(
                    "Alphabetical Books",
                    "/opds/books",
                    new ContentDto("text", "Books sorted alphabetically"),
                    timestamp)
                {
                    Links = new List<LinkDto>
                    {
                        new(baseUrl + "/opds/books", "application/atom+xml;profile=opds-catalog")
                    }
                },
                new(
                    "Favorite Books",
                    "/opds/books/favorite",
                    new ContentDto("text", "Favorite books"),
                    timestamp)
                {
                    Links = new List<LinkDto>
                    {
                        new(baseUrl + "/opds/books/favorite", "application/atom+xml;profile=opds-catalog")
                    }
                },
                new(
                    "Folders",
                    "/opds/folders",
                    new ContentDto("text", "Browse books by folder"),
                    timestamp)
                {
                    Links = new List<LinkDto>
                    {
                        new(baseUrl + "/opds/folders", "application/atom+xml;profile=opds-catalog")
                    }
                },
                new(
                    "Genres",
                    "/opds/genres",
                    new ContentDto("text", "Book genres"),
                    timestamp)
                {
                    Links = new List<LinkDto>
                    {
                        new(baseUrl + "/opds/genres", "application/atom+xml;profile=opds-catalog")
                    }
                },
                new(
                    "Recently Added",
                    "/opds/books/recentlyadded",
                    new ContentDto("text", "Recently added books"),
                    timestamp)
                {
                    Links = new List<LinkDto>
                    {
                        new(baseUrl + "/opds/books/recentlyadded", "application/atom+xml;profile=opds-catalog")
                    }
                }
            }
        };
    }

    /// <inheritdoc />
    public FeedDto GetAlphabeticalFeed(string baseUrl)
    {
        var utcNow = DateTime.UtcNow;
        var entries = new List<EntryDto>
        {
            new(
                "All",
                "/opds/books/letter/00",
                utcNow)
            {
                Links = new List<LinkDto>
                {
                    new(
                        "subsection",
                        baseUrl + "/opds/books/letter/00",
                        "application/atom+xml;profile=opds-catalog")
                }
            }
        };

        // TODO add entries based on library contents first char.
        for (var i = 'A'; i <= 'Z'; i++)
        {
            var letter = char.ToString(i);
            entries.Add(new EntryDto(
                letter,
                "/opds/books/letter/" + letter,
                utcNow)
            {
                Links = new List<LinkDto>
                {
                    new(
                        "subsection",
                        baseUrl + "/opds/books/letter/" + letter,
                        "application/atom+xml;profile=opds-catalog")
                }
            });
        }

        return new FeedDto
        {
            Id = Guid.NewGuid().ToString(),
            Author = PluginAuthor,
            Title = GetFeedName("Alphabetical"),
            Links = new[]
            {
                new LinkDto("self", baseUrl + "/opds/books?", "application/atom+xml;profile=opds-catalog;type=feed;kind=navigation"),
                new LinkDto("start", baseUrl + "/opds", "application/atom+xml;profile=opds-catalog;type=feed;kind=navigation"),
                new LinkDto("up", baseUrl + "/opds", "application/atom+xml;profile=opds-catalog;type=feed;kind=navigation"),
                new LinkDto("search", baseUrl + "/opds/osd", "application/opensearchdescription+xml"),
                new LinkDto("search", baseUrl + "/opds/search/{searchTerms}", "application/atom+xml", "Search")
            },
            Entries = entries
        };
    }

    /// <inheritdoc />
    public FeedDto GetBookGenres(string baseUrl, Guid userId)
    {
        var feedDto = new FeedDto
        {
            Id = Guid.NewGuid().ToString(),
            Author = PluginAuthor,
            Title = GetFeedName("Genres"),
            Links = new[]
            {
                new LinkDto("self", baseUrl + "/opds/genres?", "application/atom+xml;profile=opds-catalog;type=feed;kind=navigation"),
                new LinkDto("start", baseUrl + "/opds", "application/atom+xml;profile=opds-catalog;type=feed;kind=navigation"),
                new LinkDto("up", baseUrl + "/opds", "application/atom+xml;profile=opds-catalog;type=feed;kind=navigation"),
                new LinkDto("search", baseUrl + "/opds/osd", "application/opensearchdescription+xml"),
                new LinkDto("search", baseUrl + "/opds/search/{searchTerms}", "application/atom+xml", "Search")
            },
            Entries = new List<EntryDto>()
        };

        var query = new InternalItemsQuery
        {
            IncludeItemTypes = BookItemTypes,
            OrderBy = [(ItemSortBy.SortName, SortOrder.Ascending)],
            Recursive = true,
            DtoOptions = new DtoOptions()
        };

        if (userId != Guid.Empty)
        {
            var user = _userManager.GetUserById(userId);
            if (user is not null)
            {
                query.SetUser(user);
            }
        }

        var utcNow = DateTime.UtcNow;
        var queryResult = _libraryManager.GetGenres(query);
        if (queryResult is not null)
        {
            foreach (var (item, _) in queryResult.Items)
            {
                feedDto.Entries.Add(new EntryDto(
                    item.Name,
                    "/opds/genres/" + item.Id,
                    utcNow)
                {
                    Links = new List<LinkDto>
                    {
                        new(
                            "subsection",
                            baseUrl + "/opds/genres/" + item.Id,
                            "application/atom+xml;profile=opds-catalog")
                    }
                });
            }
        }

        return feedDto;
    }

    /// <inheritdoc />
    public FeedDto GetRootFolders(string baseUrl, Guid userId, int? startIndex = null, int? limit = null)
    {
        var actualStartIndex = NormalizeStartIndex(startIndex);
        var actualLimit = NormalizeLimit(limit);
        var user = userId != Guid.Empty ? _userManager.GetUserById(userId) : null;

        var feedDto = new FeedDto
        {
            Id = Guid.NewGuid().ToString(),
            Author = PluginAuthor,
            Title = GetFeedName("Folders"),
            Links = new[]
            {
                new LinkDto("self", baseUrl + "/opds/folders", "application/atom+xml;profile=opds-catalog;type=feed;kind=navigation"),
                new LinkDto("start", baseUrl + "/opds", "application/atom+xml;profile=opds-catalog;type=feed;kind=navigation"),
                new LinkDto("up", baseUrl + "/opds", "application/atom+xml;profile=opds-catalog;type=feed;kind=navigation"),
                new LinkDto("search", baseUrl + "/opds/osd", "application/opensearchdescription+xml"),
                new LinkDto("search", baseUrl + "/opds/search/{searchTerms}", "application/atom+xml", "Search")
            },
            Entries = new List<EntryDto>()
        };

        IEnumerable<Folder>? topLevelFolders = null;
        try
        {
            topLevelFolders = _libraryManager.GetUserRootFolder()?.GetChildren(user, true)?.OfType<Folder>();
        }
        catch (Exception)
        {
        }

        if (topLevelFolders is null || !topLevelFolders.Any())
        {
            try
            {
                topLevelFolders = _libraryManager.RootFolder?.Children?.OfType<Folder>();
            }
            catch (Exception)
            {
            }
        }

        var bookFolders = new List<Folder>();
        if (topLevelFolders is not null)
        {
            foreach (var topFolder in topLevelFolders)
            {
                var folder = ResolveCollectionFolder(topFolder);
                if (ContainsBooks(folder, user) && !bookFolders.Any(f => f.Id == folder.Id))
                {
                    bookFolders.Add(folder);
                }
            }
        }

        if (bookFolders.Count == 0)
        {
            var sampleQuery = new InternalItemsQuery
            {
                IncludeItemTypes = BookItemTypes,
                Recursive = true,
                Limit = 100
            };
            if (user is not null)
            {
                sampleQuery.SetUser(user);
            }

            var sampleResult = _libraryManager.GetItemsResult(sampleQuery);
            if (sampleResult?.Items is not null)
            {
                var parentIds = sampleResult.Items.Select(b => b.ParentId).Where(id => id != Guid.Empty).Distinct();
                foreach (var pId in parentIds)
                {
                    if (_libraryManager.GetItemById(pId) is Folder pFolder && !bookFolders.Any(f => f.Id == pFolder.Id))
                    {
                        bookFolders.Add(pFolder);
                    }
                }
            }
        }

        bookFolders = bookFolders.OrderBy(f => f.SortName ?? f.Name, StringComparer.OrdinalIgnoreCase).ToList();
        var totalCount = bookFolders.Count;
        var pagedFolders = bookFolders.Skip(actualStartIndex).Take(actualLimit).ToList();

        foreach (var folder in pagedFolders)
        {
            var entry = new EntryDto(
                folder.Name,
                "/opds/folders/" + folder.Id,
                folder.DateModified != default ? folder.DateModified : DateTime.UtcNow)
            {
                Summary = folder.Overview,
                Links = new List<LinkDto>
                {
                    new(
                        "subsection",
                        baseUrl + "/opds/folders/" + folder.Id,
                        "application/atom+xml;profile=opds-catalog")
                }
            };

            if (!string.IsNullOrEmpty(folder.PrimaryImagePath))
            {
                var mime = MimeTypes.GetMimeType(folder.PrimaryImagePath);
                if (!string.IsNullOrEmpty(mime))
                {
                    entry.Links.Add(new("http://opds-spec.org/image", baseUrl + "/opds/cover/" + folder.Id, mime));
                    entry.Links.Add(new("http://opds-spec.org/image/thumbnail", baseUrl + "/opds/thumbnail/" + folder.Id, "image/jpeg"));
                }
            }

            feedDto.Entries.Add(entry);
        }

        ApplyPagination(feedDto, baseUrl + "/opds/folders", actualStartIndex, actualLimit, totalCount);
        return feedDto;
    }

    /// <inheritdoc />
    public FeedDto? GetFolder(string baseUrl, Guid userId, Guid folderId, int? startIndex = null, int? limit = null)
    {
        var folder = _libraryManager.GetItemById(folderId) as Folder;
        if (folder is null)
        {
            return null;
        }

        var user = userId != Guid.Empty ? _userManager.GetUserById(userId) : null;
        if (user is not null && !folder.IsVisibleStandalone(user))
        {
            return null;
        }

        folder = ResolveCollectionFolder(folder);

        var actualStartIndex = NormalizeStartIndex(startIndex);
        var actualLimit = NormalizeLimit(limit);

        string upLinkUrl;
        if (folder.ParentId != Guid.Empty)
        {
            var parent = _libraryManager.GetItemById(folder.ParentId) as Folder;
            if (parent is not null && parent is not UserRootFolder && parent.Id != _libraryManager.RootFolder?.Id)
            {
                upLinkUrl = baseUrl + "/opds/folders/" + parent.Id;
            }
            else
            {
                upLinkUrl = baseUrl + "/opds/folders";
            }
        }
        else
        {
            upLinkUrl = baseUrl + "/opds/folders";
        }

        var feedDto = new FeedDto
        {
            Id = Guid.NewGuid().ToString(),
            Author = PluginAuthor,
            Title = GetFeedName(folder.Name),
            Links = new[]
            {
                new LinkDto("self", baseUrl + "/opds/folders/" + folderId, "application/atom+xml;profile=opds-catalog"),
                new LinkDto("start", baseUrl + "/opds", "application/atom+xml;profile=opds-catalog;type=feed;kind=navigation"),
                new LinkDto("up", upLinkUrl, "application/atom+xml;profile=opds-catalog;type=feed;kind=navigation"),
                new LinkDto("search", baseUrl + "/opds/osd", "application/opensearchdescription+xml"),
                new LinkDto("search", baseUrl + "/opds/search/{searchTerms}", "application/atom+xml", "Search")
            },
            Entries = new List<EntryDto>()
        };

        var items = new List<BaseItem>();
        try
        {
            var children = user is not null
                ? folder.GetChildren(user, true)
                : folder.Children;

            if (children is not null)
            {
                items.AddRange(children);
            }
        }
        catch (Exception)
        {
        }

        if (items.Count == 0)
        {
            var query = new InternalItemsQuery
            {
                Parent = folder,
                ParentId = folder.Id,
                Recursive = false,
                DtoOptions = new DtoOptions(),
                OrderBy = new (ItemSortBy, SortOrder)[] { (ItemSortBy.SortName, SortOrder.Ascending) }
            };

            if (user is not null)
            {
                query.SetUser(user);
            }

            var queryResult = _libraryManager.GetItemsResult(query);
            if (queryResult?.Items is not null)
            {
                items.AddRange(queryResult.Items);
            }
        }

        var subfolders = new List<Folder>();
        var books = new List<Book>();

        foreach (var item in items.DistinctBy(i => i.Id))
        {
            if (item is Book book)
            {
                books.Add(book);
            }
            else if (item is Folder subFolder && ContainsBooks(subFolder, user))
            {
                subfolders.Add(subFolder);
            }
        }

        subfolders = subfolders.OrderBy(f => f.SortName ?? f.Name, StringComparer.OrdinalIgnoreCase).ToList();
        books = books.OrderBy(b => b.SortName ?? b.Name, StringComparer.OrdinalIgnoreCase).ToList();

        var allItems = new List<BaseItem>(subfolders.Count + books.Count);
        allItems.AddRange(subfolders);
        allItems.AddRange(books);

        var totalCount = allItems.Count;
        var pagedItems = allItems.Skip(actualStartIndex).Take(actualLimit).ToList();

        var pagedBooks = pagedItems.OfType<Book>().ToList();
        var userItemData = GetUserItemData(userId, pagedBooks);
        var parentNameCache = new Dictionary<Guid, string>
        {
            [folder.Id] = folder.Name
        };

        foreach (var item in pagedItems)
        {
            if (item is Folder subFolder)
            {
                var entry = new EntryDto(
                    subFolder.Name,
                    "/opds/folders/" + subFolder.Id,
                    subFolder.DateModified != default ? subFolder.DateModified : DateTime.UtcNow)
                {
                    Summary = subFolder.Overview,
                    Links = new List<LinkDto>
                    {
                        new(
                            "subsection",
                            baseUrl + "/opds/folders/" + subFolder.Id,
                            "application/atom+xml;profile=opds-catalog")
                    }
                };

                if (!string.IsNullOrEmpty(subFolder.PrimaryImagePath))
                {
                    var mime = MimeTypes.GetMimeType(subFolder.PrimaryImagePath);
                    if (!string.IsNullOrEmpty(mime))
                    {
                        entry.Links.Add(new("http://opds-spec.org/image", baseUrl + "/opds/cover/" + subFolder.Id, mime));
                        entry.Links.Add(new("http://opds-spec.org/image/thumbnail", baseUrl + "/opds/thumbnail/" + subFolder.Id, "image/jpeg"));
                    }
                }

                feedDto.Entries.Add(entry);
            }
            else if (item is Book book)
            {
                feedDto.Entries.Add(CreateEntry(book, baseUrl, userItemData, parentNameCache));
            }
        }

        ApplyPagination(feedDto, baseUrl + "/opds/folders/" + folderId, actualStartIndex, actualLimit, totalCount);
        return feedDto;
    }

    /// <inheritdoc />
    public FeedDto GetRecentlyAdded(string baseUrl, Guid userId, int? startIndex = null, int? limit = null)
    {
        var actualStartIndex = NormalizeStartIndex(startIndex);
        var actualLimit = NormalizeLimit(limit);

        var feedDto = new FeedDto
        {
            Id = Guid.NewGuid().ToString(),
            Author = PluginAuthor,
            Title = GetFeedName("Recently Added"),
            Links = new[]
            {
                new LinkDto("self", baseUrl + "/opds/books/recentlyadded?", "application/atom+xml;profile=opds-catalog;type=feed;kind=navigation"),
                new LinkDto("start", baseUrl + "/opds", "application/atom+xml;profile=opds-catalog;type=feed;kind=navigation"),
                new LinkDto("up", baseUrl + "/opds", "application/atom+xml;profile=opds-catalog;type=feed;kind=navigation"),
                new LinkDto("search", baseUrl + "/opds/osd", "application/opensearchdescription+xml"),
                new LinkDto("search", baseUrl + "/opds/search/{searchTerms}", "application/atom+xml", "Search")
            },
            Entries = new List<EntryDto>()
        };

        var query = new InternalItemsQuery
        {
            IncludeItemTypes = BookItemTypes,
            OrderBy = new (ItemSortBy, SortOrder)[] { (ItemSortBy.DateCreated, SortOrder.Descending) },
            Recursive = true,
            DtoOptions = new DtoOptions(),
            StartIndex = actualStartIndex,
            Limit = actualLimit
        };

        if (userId != Guid.Empty)
        {
            var user = _userManager.GetUserById(userId);
            if (user is not null)
            {
                query.SetUser(user);
            }
        }

        var totalCount = 0;
        var queryResult = _libraryManager.GetItemsResult(query);
        if (queryResult is not null)
        {
            totalCount = queryResult.TotalRecordCount;
            var books = queryResult.Items.OfType<Book>().ToList();
            var userItemData = GetUserItemData(userId, books);
            var parentNameCache = new Dictionary<Guid, string>();
            foreach (var book in books)
            {
                feedDto.Entries.Add(CreateEntry(book, baseUrl, userItemData, parentNameCache));
            }
        }

        ApplyPagination(feedDto, baseUrl + "/opds/books/recentlyadded", actualStartIndex, actualLimit, totalCount);
        return feedDto;
    }

    /// <inheritdoc />
    public FeedDto GetFavoriteBooks(string baseUrl, Guid userId, int? startIndex = null, int? limit = null)
    {
        var actualStartIndex = NormalizeStartIndex(startIndex);
        var actualLimit = NormalizeLimit(limit);
        var entries = new List<EntryDto>();
        var totalCount = 0;

        // Favorites require a user to be set, so just return empty list otherwise.
        if (userId != Guid.Empty)
        {
            var query = new InternalItemsQuery
            {
                IncludeItemTypes = BookItemTypes,
                OrderBy = new (ItemSortBy, SortOrder)[] { (ItemSortBy.SortName, SortOrder.Ascending) },
                IsFavorite = true,
                Recursive = true,
                DtoOptions = new DtoOptions(),
                StartIndex = actualStartIndex,
                Limit = actualLimit
            };

            var user = _userManager.GetUserById(userId);
            if (user is not null)
            {
                query.SetUser(user);
            }

            var queryResult = _libraryManager.GetItemsResult(query);
            if (queryResult is not null)
            {
                totalCount = queryResult.TotalRecordCount;
                var books = queryResult.Items.OfType<Book>().ToList();
                var userItemData = GetUserItemData(userId, books);
                var parentNameCache = new Dictionary<Guid, string>();
                foreach (var book in books)
                {
                    entries.Add(CreateEntry(book, baseUrl, userItemData, parentNameCache));
                }
            }
        }

        var feed = new FeedDto
        {
            Id = Guid.NewGuid().ToString(),
            Author = PluginAuthor,
            Title = GetFeedName("Favorites"),
            Links = new[]
            {
                new LinkDto("self", baseUrl + "/opds/books/favorite", "application/atom+xml;profile=opds-catalog;type=feed;kind=navigation"),
                new LinkDto("start", baseUrl + "/opds", "application/atom+xml;profile=opds-catalog;type=feed;kind=navigation"),
                new LinkDto("up", baseUrl + "/opds", "application/atom+xml;profile=opds-catalog;type=feed;kind=navigation"),
                new LinkDto("search", baseUrl + "/opds/osd", "application/opensearchdescription+xml"),
                new LinkDto("search", baseUrl + "/opds/search/{searchTerms}", "application/atom+xml", "Search")
            },
            Entries = entries
        };

        ApplyPagination(feed, baseUrl + "/opds/books/favorite", actualStartIndex, actualLimit, totalCount);
        return feed;
    }

    /// <inheritdoc />
    public FeedDto GetAllBooks(string baseUrl, Guid userId, string filterStart, int? startIndex = null, int? limit = null)
    {
        var actualStartIndex = NormalizeStartIndex(startIndex);
        var actualLimit = NormalizeLimit(limit);

        if (filterStart.Length != 1)
        {
            filterStart = string.Empty;
        }

        var query = new InternalItemsQuery
        {
            IncludeItemTypes = BookItemTypes,
            OrderBy = new (ItemSortBy, SortOrder)[] { (ItemSortBy.SortName, SortOrder.Ascending) },
            NameStartsWith = filterStart,
            Recursive = true,
            DtoOptions = new DtoOptions(),
            StartIndex = actualStartIndex,
            Limit = actualLimit
        };

        if (userId != Guid.Empty)
        {
            var user = _userManager.GetUserById(userId);
            if (user is not null)
            {
                query.SetUser(user);
            }
        }

        var queryResult = _libraryManager.GetItemsResult(query);
        var entries = new List<EntryDto>();
        var totalCount = 0;
        if (queryResult is not null)
        {
            totalCount = queryResult.TotalRecordCount;
            var books = queryResult.Items.OfType<Book>().ToList();
            var userItemData = GetUserItemData(userId, books);
            var parentNameCache = new Dictionary<Guid, string>();
            foreach (var book in books)
            {
                entries.Add(CreateEntry(book, baseUrl, userItemData, parentNameCache));
            }
        }

        var title = string.IsNullOrEmpty(filterStart)
            ? "Books"
            : "Books - " + filterStart;

        var feed = new FeedDto
        {
            Id = Guid.NewGuid().ToString(),
            Author = PluginAuthor,
            Title = GetFeedName(title),
            Links = new[]
            {
                new LinkDto("self", baseUrl + "/opds/books/letter/" + filterStart + "?", "application/atom+xml;profile=opds-catalog;type=feed;kind=navigation"),
                new LinkDto("start", baseUrl + "/opds", "application/atom+xml;profile=opds-catalog;type=feed;kind=navigation"),
                new LinkDto("up", baseUrl + "/opds", "application/atom+xml;profile=opds-catalog;type=feed;kind=navigation"),
                new LinkDto("search", baseUrl + "/opds/osd", "application/opensearchdescription+xml"),
                new LinkDto("search", baseUrl + "/opds/search/{searchTerms}", "application/atom+xml", "Search")
            },
            Entries = entries
        };

        var paginationPath = baseUrl + "/opds/books/letter/" + (string.IsNullOrEmpty(filterStart) ? "00" : filterStart);
        ApplyPagination(feed, paginationPath, actualStartIndex, actualLimit, totalCount);
        return feed;
    }

    /// <inheritdoc />
    public FeedDto GetBooksByGenre(string baseUrl, Guid userId, Guid genreId, int? startIndex = null, int? limit = null)
    {
        var actualStartIndex = NormalizeStartIndex(startIndex);
        var actualLimit = NormalizeLimit(limit);

        var query = new InternalItemsQuery
        {
            IncludeItemTypes = BookItemTypes,
            OrderBy = new (ItemSortBy, SortOrder)[] { (ItemSortBy.SortName, SortOrder.Ascending) },
            GenreIds = new[] { genreId },
            DtoOptions = new DtoOptions(),
            StartIndex = actualStartIndex,
            Limit = actualLimit
        };

        if (userId != Guid.Empty)
        {
            var user = _userManager.GetUserById(userId);
            if (user is not null)
            {
                query.SetUser(user);
            }
        }

        var queryResult = _libraryManager.GetItemsResult(query);
        var entries = new List<EntryDto>();
        var totalCount = 0;
        if (queryResult is not null)
        {
            totalCount = queryResult.TotalRecordCount;
            var books = queryResult.Items.OfType<Book>().ToList();
            var userItemData = GetUserItemData(userId, books);
            var parentNameCache = new Dictionary<Guid, string>();
            foreach (var book in books)
            {
                entries.Add(CreateEntry(book, baseUrl, userItemData, parentNameCache));
            }
        }

        var genre = _libraryManager.GetItemById(genreId);

        var feed = new FeedDto
        {
            Id = Guid.NewGuid().ToString(),
            Author = PluginAuthor,
            Title = GetFeedName(genre?.Name ?? "Genre"),
            Links = new[]
            {
                new LinkDto("self", baseUrl + "/opds/genres/" + genreId + "?", "application/atom+xml;profile=opds-catalog;type=feed;kind=navigation"),
                new LinkDto("start", baseUrl + "/opds", "application/atom+xml;profile=opds-catalog;type=feed;kind=navigation"),
                new LinkDto("up", baseUrl + "/opds", "application/atom+xml;profile=opds-catalog;type=feed;kind=navigation"),
                new LinkDto("search", baseUrl + "/opds/osd", "application/opensearchdescription+xml"),
                new LinkDto("search", baseUrl + "/opds/search/{searchTerms}", "application/atom+xml", "Search")
            },
            Entries = entries
        };

        ApplyPagination(feed, baseUrl + "/opds/genres/" + genreId, actualStartIndex, actualLimit, totalCount);
        return feed;
    }

    /// <inheritdoc />
    public string? GetBookImage(Guid bookId)
    {
        var item = _libraryManager.GetItemById(bookId);
        return item?.PrimaryImagePath;
    }

    /// <inheritdoc />
    public string? GetBook(Guid bookId)
    {
        var item = _libraryManager.GetItemById(bookId);
        return item?.Path;
    }

    /// <inheritdoc />
    public DateTime? GetBookImageLastModified(Guid bookId)
    {
        var item = _libraryManager.GetItemById(bookId);
        if (item is null || string.IsNullOrEmpty(item.PrimaryImagePath))
        {
            return null;
        }

        var imagePath = item.PrimaryImagePath;
        return item.DateModified != default ? item.DateModified : File.GetLastWriteTimeUtc(imagePath);
    }

    /// <inheritdoc />
    public byte[]? GetBookThumbnail(Guid bookId, int maxWidth)
    {
        var imagePath = GetBookImage(bookId);
        if (string.IsNullOrEmpty(imagePath) || _imageEncoder is null || !_imageEncoder.SupportsImageEncoding)
        {
            return null;
        }

        try
        {
            var extension = Path.GetExtension(imagePath);
            if (!_imageEncoder.SupportedInputFormats.Contains(extension.TrimStart('.'), StringComparer.OrdinalIgnoreCase))
            {
                return null;
            }

            var fileInfo = new FileInfo(imagePath);
            if (!fileInfo.Exists)
            {
                return null;
            }

            var cacheKey = $"opds-thumb-{fileInfo.Length}-{fileInfo.LastWriteTimeUtc.Ticks}-{maxWidth}.jpg";
            var cachePath = Path.Combine(Path.GetTempPath(), cacheKey);

            if (File.Exists(cachePath))
            {
                return File.ReadAllBytes(cachePath);
            }

            var options = new ImageProcessingOptions
            {
                MaxWidth = maxWidth,
                Quality = 80,
                SupportedOutputFormats = new[] { ImageFormat.Jpg }
            };

            var tempOutput = cachePath + ".tmp";
            var encodedPath = _imageEncoder.EncodeImage(
                imagePath,
                fileInfo.LastWriteTimeUtc,
                tempOutput,
                true,
                null,
                80,
                options,
                ImageFormat.Jpg);

            if (File.Exists(encodedPath))
            {
                if (encodedPath != cachePath)
                {
                    File.Move(encodedPath, cachePath, true);
                }

                return File.ReadAllBytes(cachePath);
            }

            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<FeedDto> SearchBooks(string baseUrl, Guid userId, string searchTerm, int? startIndex = null, int? limit = null)
    {
        var actualStartIndex = NormalizeStartIndex(startIndex);
        var actualLimit = NormalizeLimit(limit);

        var searchResult = await _searchManager.GetSearchHintsAsync(new SearchQuery
        {
            StartIndex = actualStartIndex,
            Limit = actualLimit,
            SearchTerm = searchTerm,
            IncludeItemTypes = BookItemTypes,
            UserId = userId
        }).ConfigureAwait(false);

        var books = searchResult.Items
            .Select(result => result.Item)
            .OfType<Book>()
            .ToList();

        var entries = new List<EntryDto>(books.Count);
        var userItemData = GetUserItemData(userId, books);
        var parentNameCache = new Dictionary<Guid, string>();
        foreach (var book in books)
        {
            entries.Add(CreateEntry(book, baseUrl, userItemData, parentNameCache));
        }

        var feed = new FeedDto
        {
            Id = Guid.NewGuid().ToString(),
            Links = new[]
            {
                new LinkDto("self", baseUrl + "/opds/search/" + searchTerm + "?", "application/atom+xml;profile=opds-catalog;kind=navigation"),
                new LinkDto("start", baseUrl + "/opds", "application/atom+xml;profile=opds-catalog;kind=navigation"),
                new LinkDto("up", baseUrl + "/opds", "application/atom+xml;profile=opds-catalog;type=feed;kind=navigation"),
                new LinkDto("search", baseUrl + "/opds/osd", "application/opensearchdescription+xml"),
                new LinkDto("search", baseUrl + "/opds/search/{searchTerms}", "application/atom+xml", "Search")
            },
            Title = GetFeedName(searchTerm),
            Author = PluginAuthor,
            Entries = entries
        };

        ApplyPagination(feed, baseUrl + "/opds/search/" + Uri.EscapeDataString(searchTerm), actualStartIndex, actualLimit, searchResult.TotalRecordCount);
        return feed;
    }

    /// <inheritdoc />
    public OpenSearchDescriptionDto GetSearchDescription(string baseUrl)
    {
        var dto = new OpenSearchDescriptionDto
        {
            Xmlns = "http://a9.com/-/spec/opensearch/1.1/",
            Description = "Jellyfin eBook Catalog",
            Developer = "Jellyfin",
            Contact = "https://github.com/jellyfin/jellyfin-plugin-opds",
            SyndicationRight = "open",
            Language = "en-EN",
            OutputEncoding = "UTF-8",
            InputEncoding = "UTF-8",
            ShortName = GetFeedName("Search"),
            LongName = GetFeedName("Search"),
            Url = new[]
            {
                new OpenSearchUrlDto
                {
                    Type = MediaTypeNames.Text.Html,
                    Template = baseUrl + "/opds/search/{searchTerms}"
                },
                new OpenSearchUrlDto
                {
                    Type = "application/atom+xml",
                    Template = baseUrl + "/opds/search?query={searchTerms}"
                }
            }
        };

        return dto;
    }

    private string GetFeedName(string title)
    {
        var serverName = _serverApplicationHost.FriendlyName;
        return title + " - " + (string.IsNullOrEmpty(serverName) ? "Jellyfin" : serverName);
    }

    private EntryDto CreateEntry(
        Book book,
        string baseUrl,
        IReadOnlyDictionary<Guid, UserItemData> userItemData,
        Dictionary<Guid, string>? parentNameCache = null)
    {
        string? authorName = null;
        if (book.ParentId != Guid.Empty && parentNameCache is not null && parentNameCache.TryGetValue(book.ParentId, out var cachedName))
        {
            authorName = cachedName;
        }
        else
        {
            authorName = book.GetParent()?.Name;
            if (book.ParentId != Guid.Empty && parentNameCache is not null && !string.IsNullOrEmpty(authorName))
            {
                parentNameCache[book.ParentId] = authorName;
            }
        }

        var entry = new EntryDto(
            book.Name,
            book.Id.ToString(),
            book.DateModified)
        {
            Author = new AuthorDto
            {
                Name = authorName ?? "Unknown"
            },
            Summary = book.Overview,
            Links = new List<LinkDto>()
        };

        if (!string.IsNullOrEmpty(book.PrimaryImagePath))
        {
            var imageMimeType = MimeTypes.GetMimeType(book.PrimaryImagePath);
            if (!string.IsNullOrEmpty(imageMimeType))
            {
                entry.Links.Add(new("http://opds-spec.org/image", baseUrl + "/opds/cover/" + book.Id, imageMimeType));
                entry.Links.Add(new("http://opds-spec.org/image/thumbnail", baseUrl + "/opds/thumbnail/" + book.Id, "image/jpeg"));
            }
        }

        if (!string.IsNullOrEmpty(book.Path))
        {
            var bookMimeType = MimeTypes.GetMimeType(book.Path);
            if (!string.IsNullOrEmpty(bookMimeType))
            {
                entry.Links.Add(new("http://opds-spec.org/acquisition", baseUrl + "/opds/download/" + book.Id, bookMimeType)
                {
                    UpdateTime = book.DateModified,
                    Length = book.Size
                });
            }
        }

        if (OpdsPlugin.Instance!.Configuration.EnablePageStreaming)
        {
            var totalTicks = book.RunTimeTicks.GetValueOrDefault();
            var pageCount = totalTicks > 10000
                ? (int)(totalTicks / 10000)
                : _pageStreamService.GetPageCount(book);
            if (pageCount > 0)
            {
                var streamLink = new LinkDto(
                    PseConstants.StreamRel,
                    baseUrl + "/opds/stream/" + book.Id + "?page={pageNumber}&width={maxWidth}",
                    PseConstants.JpegMediaType)
                {
                    PageCount = pageCount
                };

                if (userItemData.TryGetValue(book.Id, out var data))
                {
                    streamLink.LastRead = PseConstants.GetLastRead(data.PlaybackPositionTicks, pageCount);
                    if (streamLink.LastRead is not null)
                    {
                        streamLink.LastReadDate = data.LastPlayedDate;
                    }
                }

                entry.Links.Add(streamLink);
            }
        }

        // The progression service covers every book, including the formats
        // that cannot be streamed page by page, e.g. EPUB.
        if (OpdsPlugin.Instance!.Configuration.EnableProgression)
        {
            entry.Links.Add(new LinkDto(
                ProgressionConstants.Rel,
                baseUrl + "/opds/progression/" + book.Id,
                ProgressionConstants.MediaType));
        }

        return entry;
    }

    /// <summary>
    /// Gets the user data of the given items in a single batch operation.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <param name="items">The items to get the user data of.</param>
    /// <returns>The user data of the items, keyed on the item id.</returns>
    private IReadOnlyDictionary<Guid, UserItemData> GetUserItemData(Guid userId, List<Book> items)
    {
        // The user data is only used for the reading progress of the page
        // streaming extension, so don't query it when that is not exposed.
        if (userId == Guid.Empty
            || items.Count == 0
            || !OpdsPlugin.Instance!.Configuration.EnablePageStreaming)
        {
            return EmptyUserItemData;
        }

        var user = _userManager.GetUserById(userId);
        if (user is null)
        {
            return EmptyUserItemData;
        }

        return _userDataManager.GetUserDataBatch(items, user);
    }

    private static int NormalizeLimit(int? limit)
    {
        var defaultLimit = OpdsPlugin.Instance?.Configuration.DefaultPageSize ?? 50;
        if (defaultLimit <= 0)
        {
            defaultLimit = 50;
        }

        if (limit is null or <= 0)
        {
            return defaultLimit;
        }

        return Math.Min(limit.Value, 500);
    }

    private static int NormalizeStartIndex(int? startIndex)
    {
        return startIndex.HasValue && startIndex.Value > 0 ? startIndex.Value : 0;
    }

    private static void ApplyPagination(
        FeedDto feed,
        string feedUrl,
        int startIndex,
        int limit,
        int totalCount)
    {
        feed.TotalResults = totalCount;
        feed.StartIndex = startIndex;
        feed.ItemsPerPage = limit;

        var links = new List<LinkDto>(feed.Links);

        var selfUrl = BuildPagedUrl(feedUrl, startIndex, limit);
        var selfIndex = links.FindIndex(l => string.Equals(l.Rel, "self", StringComparison.OrdinalIgnoreCase));
        if (selfIndex >= 0)
        {
            links[selfIndex] = new LinkDto("self", selfUrl, "application/atom+xml;profile=opds-catalog;type=feed;kind=navigation");
        }
        else
        {
            links.Insert(0, new LinkDto("self", selfUrl, "application/atom+xml;profile=opds-catalog;type=feed;kind=navigation"));
        }

        links.Add(new LinkDto("first", BuildPagedUrl(feedUrl, 0, limit), "application/atom+xml;profile=opds-catalog;type=feed;kind=navigation"));

        if (startIndex > 0)
        {
            var prevStartIndex = Math.Max(0, startIndex - limit);
            links.Add(new LinkDto("previous", BuildPagedUrl(feedUrl, prevStartIndex, limit), "application/atom+xml;profile=opds-catalog;type=feed;kind=navigation"));
        }

        if (startIndex + limit < totalCount)
        {
            var nextStartIndex = startIndex + limit;
            links.Add(new LinkDto("next", BuildPagedUrl(feedUrl, nextStartIndex, limit), "application/atom+xml;profile=opds-catalog;type=feed;kind=navigation"));
        }

        if (totalCount > 0)
        {
            var lastStartIndex = Math.Max(0, ((totalCount - 1) / limit) * limit);
            links.Add(new LinkDto("last", BuildPagedUrl(feedUrl, lastStartIndex, limit), "application/atom+xml;profile=opds-catalog;type=feed;kind=navigation"));
        }

        feed.Links = links.ToArray();
    }

    private static string BuildPagedUrl(string baseUrlPath, int startIndex, int limit)
    {
        var cleanUrl = baseUrlPath.TrimEnd('?');
        var separator = cleanUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return $"{cleanUrl}{separator}startIndex={startIndex}&limit={limit}";
    }

    private Folder ResolveCollectionFolder(Folder folder)
    {
        if (folder is UserView userView)
        {
            var target = (userView.DisplayParentId != Guid.Empty ? _libraryManager.GetItemById(userView.DisplayParentId) as Folder : null)
                ?? (userView.ParentId != Guid.Empty ? _libraryManager.GetItemById(userView.ParentId) as Folder : null);

            if (target is not null)
            {
                return target;
            }
        }

        return folder;
    }

    private bool ContainsBooks(Folder folder, User? user)
    {
        return ContainsBooks(folder, user, new HashSet<Guid>());
    }

    private bool ContainsBooks(Folder folder, User? user, HashSet<Guid> visited)
    {
        if (!visited.Add(folder.Id))
        {
            return false;
        }

        folder = ResolveCollectionFolder(folder);

        if (folder is IHasCollectionType hasCollectionType
            && hasCollectionType.CollectionType == CollectionType.books)
        {
            return true;
        }

        try
        {
            var children = user is not null ? folder.GetChildren(user, true) : folder.Children;
            if (children is not null && children.Any(c => c is Book || (c is Folder f && ContainsBooks(f, user, visited))))
            {
                return true;
            }
        }
        catch (Exception)
        {
        }

        var query = new InternalItemsQuery
        {
            Parent = folder,
            ParentId = folder.Id,
            IncludeItemTypes = BookItemTypes,
            Recursive = true,
            Limit = 1
        };

        if (user is not null)
        {
            query.SetUser(user);
        }

        var result = _libraryManager.GetItemsResult(query);
        return result is not null && (result.TotalRecordCount > 0 || (result.Items is not null && result.Items.Count > 0));
    }
}
