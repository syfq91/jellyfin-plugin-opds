using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mime;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.Opds.Models;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
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

    /// <summary>
    /// Initializes a new instance of the <see cref="OpdsFeedProvider"/> class.
    /// </summary>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="searchManager">Instance of the <see cref="ISearchManager"/> interface.</param>
    /// <param name="serverApplicationHost">Instance of the <see cref="IServerApplicationHost"/> interface.</param>
    /// <param name="userManager">Instance of the <see cref="IUserManager"/> interface.</param>
    /// <param name="pageStreamService">Instance of the <see cref="IPageStreamService"/> interface.</param>
    /// <param name="userDataManager">Instance of the <see cref="IUserDataManager"/> interface.</param>
    public OpdsFeedProvider(
        ILibraryManager libraryManager,
        ISearchManager searchManager,
        IServerApplicationHost serverApplicationHost,
        IUserManager userManager,
        IPageStreamService pageStreamService,
        IUserDataManager userDataManager)
    {
        _libraryManager = libraryManager;
        _searchManager = searchManager;
        _serverApplicationHost = serverApplicationHost;
        _userManager = userManager;
        _pageStreamService = pageStreamService;
        _userDataManager = userDataManager;
    }

    /// <inheritdoc />
    public FeedDto GetFeeds(string baseUrl)
    {
        var timestamp = DateTime.UtcNow;
        return new FeedDto
        {
            Id = Guid.NewGuid().ToString(),
            Links = new[]
            {
                new LinkDto("self", baseUrl + "/opds", "application/atom+xml;profile=opds-catalog;kind=navigation"),
                new LinkDto("start", baseUrl + "/opds", "application/atom+xml;profile=opds-catalog;kind=navigation", "Start"),
                new LinkDto("search", baseUrl + "/opds/osd", "application/opensearchdescription+xml"),
                new LinkDto("search", baseUrl + "/opds/search/{searchTerms}", "application/atom+xml", "Search")
            },
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
    public FeedDto GetRecentlyAdded(string baseUrl, Guid userId)
    {
        var feedDto = new FeedDto
        {
            Id = Guid.NewGuid().ToString(),
            Author = PluginAuthor,
            Title = GetFeedName("Recently Added"),
            Links = new[]
            {
                new LinkDto("self", baseUrl + "/opds/recentlyadded?", "application/atom+xml;profile=opds-catalog;type=feed;kind=navigation"),
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
            Limit = 25
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
        if (queryResult is not null)
        {
            var books = queryResult.Items.OfType<Book>().ToList();
            var userItemData = GetUserItemData(userId, books);
            foreach (var book in books)
            {
                feedDto.Entries.Add(CreateEntry(book, baseUrl, userItemData));
            }
        }

        return feedDto;
    }

    /// <inheritdoc />
    public FeedDto GetFavoriteBooks(string baseUrl, Guid userId)
    {
        var entries = new List<EntryDto>();

        // Favorites require a user to be set, so just return empty list otherwise.
        if (userId != Guid.Empty)
        {
            var query = new InternalItemsQuery
            {
                IncludeItemTypes = BookItemTypes,
                OrderBy = new (ItemSortBy, SortOrder)[] { (ItemSortBy.SortName, SortOrder.Ascending) },
                IsFavorite = true,
                Recursive = true,
                DtoOptions = new DtoOptions()
            };

            var user = _userManager.GetUserById(userId);
            if (user is not null)
            {
                query.SetUser(user);
            }

            var items = _libraryManager.GetItemList(query);
            if (items is not null)
            {
                var books = items.OfType<Book>().ToList();
                var userItemData = GetUserItemData(userId, books);
                foreach (var book in books)
                {
                    entries.Add(CreateEntry(book, baseUrl, userItemData));
                }
            }
        }

        return new FeedDto
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
    }

    /// <inheritdoc />
    public FeedDto GetAllBooks(string baseUrl, Guid userId, string filterStart)
    {
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

        var items = _libraryManager.GetItemList(query);
        var entries = new List<EntryDto>();
        if (items is not null)
        {
            var books = items.OfType<Book>().ToList();
            var userItemData = GetUserItemData(userId, books);
            foreach (var book in books)
            {
                entries.Add(CreateEntry(book, baseUrl, userItemData));
            }
        }

        var title = string.IsNullOrEmpty(filterStart)
            ? "Books"
            : "Books - " + filterStart;

        return new FeedDto
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
    }

    /// <inheritdoc />
    public FeedDto GetBooksByGenre(string baseUrl, Guid userId, Guid genreId)
    {
        var query = new InternalItemsQuery
        {
            IncludeItemTypes = BookItemTypes,
            OrderBy = new (ItemSortBy, SortOrder)[] { (ItemSortBy.SortName, SortOrder.Ascending) },
            GenreIds = new[] { genreId },
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

        var queryResult = _libraryManager.GetItemList(query);
        var entries = new List<EntryDto>();
        if (queryResult is not null)
        {
            var books = queryResult.OfType<Book>().ToList();
            var userItemData = GetUserItemData(userId, books);
            foreach (var book in books)
            {
                entries.Add(CreateEntry(book, baseUrl, userItemData));
            }
        }

        var genre = _libraryManager.GetItemById(genreId);

        return new FeedDto
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
    public async Task<FeedDto> SearchBooks(string baseUrl, Guid userId, string searchTerm)
    {
        var searchResult = await _searchManager.GetSearchHintsAsync(new SearchQuery
        {
            Limit = 100,
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
        foreach (var book in books)
        {
            entries.Add(CreateEntry(book, baseUrl, userItemData));
        }

        return new FeedDto
        {
            Id = Guid.NewGuid().ToString(),
            Links = new[]
            {
                new LinkDto("self", baseUrl + "/opds/search/" + searchTerm + "?", "application/atom+xml;profile=opds-catalog;kind=navigation"),
                new LinkDto("start", baseUrl + "/opds", "application/atom+xml;profile=opds-catalog;kind=navigation", "Start"),
                new LinkDto("up", baseUrl + "/opds", "application/atom+xml;profile=opds-catalog;type=feed;kind=navigation"),
                new LinkDto("search", baseUrl + "/opds/osd", "application/opensearchdescription+xml"),
                new LinkDto("search", baseUrl + "/opds/search/{searchTerms}", "application/atom+xml", "Search")
            },
            Title = GetFeedName(searchTerm),
            Author = PluginAuthor,
            Entries = entries
        };
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

    private EntryDto CreateEntry(Book book, string baseUrl, IReadOnlyDictionary<Guid, UserItemData> userItemData)
    {
        var entry = new EntryDto(
            book.Name,
            book.Id.ToString(),
            book.DateModified)
        {
            Author = new AuthorDto
            {
                Name = book.GetParent().Name
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
                entry.Links.Add(new("http://opds-spec.org/image/thumbnail", baseUrl + "/opds/cover/" + book.Id, imageMimeType));
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
            var pageCount = _pageStreamService.GetPageCount(book);
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
}
