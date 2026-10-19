using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.Opds.Models;
using MediaBrowser.Controller.Drawing;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Net;
using Microsoft.Extensions.Logging;
using SharpCompress.Archives;

namespace Jellyfin.Plugin.Opds.Services;

/// <summary>
/// Provides page by page access to comic book archives, as used by the
/// OPDS page streaming extension.
/// </summary>
public class PageStreamService : IPageStreamService
{
    /// <summary>
    /// The maximum amount of page lists kept in the cache.
    /// </summary>
    private const int MaxCacheEntries = 1000;

    /// <summary>
    /// The maximum width of a resized page.
    /// </summary>
    private const int MaxPageWidth = 4096;

    /// <summary>
    /// The extensions of archives that can be streamed page by page.
    /// </summary>
    private static readonly string[] StreamableExtensions =
    {
        ".cbz", ".cbr", ".cb7", ".cbt", ".zip", ".rar", ".7z", ".tar"
    };

    /// <summary>
    /// The extensions of the images that are treated as pages of a document.
    /// </summary>
    private static readonly string[] ImageExtensions =
    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".tif", ".tiff"
    };

    /// <summary>
    /// The output formats supported when converting pages.
    /// </summary>
    private static readonly IReadOnlyCollection<ImageFormat> SupportedOutputFormats = new[] { ImageFormat.Jpg };

    private readonly ConcurrentDictionary<string, CacheEntry> _pageCache = new(StringComparer.Ordinal);

    private readonly ILibraryManager _libraryManager;
    private readonly IImageEncoder _imageEncoder;
    private readonly ILogger<PageStreamService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PageStreamService"/> class.
    /// </summary>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="imageEncoder">Instance of the <see cref="IImageEncoder"/> interface.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{PageStreamService}"/> interface.</param>
    public PageStreamService(
        ILibraryManager libraryManager,
        IImageEncoder imageEncoder,
        ILogger<PageStreamService> logger)
    {
        _libraryManager = libraryManager;
        _imageEncoder = imageEncoder;
        _logger = logger;
    }

    /// <inheritdoc />
    public int GetPageCount(Book book)
    {
        ArgumentNullException.ThrowIfNull(book);

        if (!TryGetPageList(book, out var pages))
        {
            return 0;
        }

        return pages.Count;
    }

    /// <inheritdoc />
    public PageStreamResult? GetPage(Guid bookId, int pageNumber, int? maxWidth)
    {
        if (bookId == Guid.Empty || pageNumber < 0)
        {
            return null;
        }

        if (maxWidth is <= 0)
        {
            maxWidth = null;
        }
        else if (maxWidth > MaxPageWidth)
        {
            maxWidth = MaxPageWidth;
        }

        var book = GetBook(bookId);
        if (book is null || !TryGetPageList(book, out var pages))
        {
            return null;
        }

        if (pageNumber >= pages.Count)
        {
            return null;
        }

        return ExtractPage(book, pages[pageNumber], maxWidth);
    }

    private static bool IsImageEntry(IArchiveEntry entry)
    {
        if (entry.IsDirectory || string.IsNullOrEmpty(entry.Key))
        {
            return false;
        }

        // Skip junk files, e.g. the ones macOS adds to archives.
        var fileName = Path.GetFileName(entry.Key);
        if (fileName.Length == 0 || fileName[0] == '.')
        {
            return false;
        }

        return ImageExtensions.Contains(Path.GetExtension(fileName), StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsStreamable(string? path)
    {
        return !string.IsNullOrEmpty(path)
            && StreamableExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    }

    private static void DeleteTemporaryFile(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private Book? GetBook(Guid bookId)
    {
        var item = _libraryManager.GetItemById(bookId);
        if (item is Book book && IsStreamable(book.Path))
        {
            return book;
        }

        return null;
    }

    /// <summary>
    /// Gets the ordered list of the image entries of the book's archive,
    /// using a cache keyed on the path of the file.
    /// </summary>
    /// <param name="book">The book.</param>
    /// <param name="pages">The ordered list of image entries.</param>
    /// <returns>Whether the page list could be determined.</returns>
    private bool TryGetPageList(Book book, out IReadOnlyList<string> pages)
    {
        pages = Array.Empty<string>();

        var path = book.Path;
        if (!IsStreamable(path))
        {
            return false;
        }

        var file = new FileInfo(path);
        if (!file.Exists)
        {
            return false;
        }

        if (_pageCache.TryGetValue(path, out var cached)
            && cached.Size == file.Length
            && cached.LastWriteTimeUtc == file.LastWriteTimeUtc)
        {
            pages = cached.Pages;
            return true;
        }

        try
        {
            using var stream = File.OpenRead(path);
            using var archive = ArchiveFactory.OpenArchive(stream);

            // Only actual images are pages, so that e.g. ComicInfo.xml is not counted.
            // The entries are sorted naturally so that page 2 comes before page 10.
            var pageList = archive.Entries
                .Where(IsImageEntry)
                .Select(entry => entry.Key!)
                .OrderBy(key => key, NaturalSortComparer.Instance)
                .ToArray();

            if (_pageCache.Count >= MaxCacheEntries)
            {
                _pageCache.Clear();
            }

            var cacheEntry = new CacheEntry(file.LastWriteTimeUtc, file.Length, pageList);
            _pageCache[path] = cacheEntry;

            pages = pageList;
            return true;
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Unable to read pages from {Path}", path);
            return false;
        }
    }

    /// <summary>
    /// Extracts a single page from the book's archive.
    /// </summary>
    /// <param name="book">The book.</param>
    /// <param name="entryKey">The archive entry key of the page.</param>
    /// <param name="maxWidth">The optional maximum width of the page.</param>
    /// <returns>The page image, or <c>null</c> when the page could not be extracted.</returns>
    private PageStreamResult? ExtractPage(Book book, string entryKey, int? maxWidth)
    {
        var path = book.Path;
        try
        {
            byte[] imageBytes;
            string extension;
            using (var stream = File.OpenRead(path))
            using (var archive = ArchiveFactory.OpenArchive(stream))
            {
                var entry = archive.Entries.FirstOrDefault(e => string.Equals(e.Key, entryKey, StringComparison.Ordinal));
                if (entry is null)
                {
                    return null;
                }

                using var entryStream = entry.OpenEntryStream();
                using var buffer = new MemoryStream();
                entryStream.CopyTo(buffer);
                imageBytes = buffer.ToArray();
                extension = Path.GetExtension(entryKey);
            }

            // Fast path: jpeg pages without resizing are served as-is.
            if (maxWidth is null
                && (extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
                    || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)))
            {
                return new PageStreamResult(imageBytes, PseConstants.JpegMediaType);
            }

            var jpegBytes = ConvertToJpeg(imageBytes, extension, maxWidth);
            if (jpegBytes is not null)
            {
                return new PageStreamResult(jpegBytes, PseConstants.JpegMediaType);
            }

            // Fall back to the original image when it cannot be converted,
            // e.g. when the format is not supported by the image encoder.
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Serving page {EntryKey} without conversion", entryKey);
            }

            return new PageStreamResult(imageBytes, MimeTypes.GetMimeType(entryKey));
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Unable to extract page {EntryKey} from {Path}", entryKey, path);
            return null;
        }
    }

    /// <summary>
    /// Converts a page image to jpeg.
    /// </summary>
    /// <param name="imageBytes">The original image bytes.</param>
    /// <param name="extension">The file extension of the original image.</param>
    /// <param name="maxWidth">The optional maximum width of the page.</param>
    /// <returns>The jpeg image bytes, or <c>null</c> when the image cannot be converted.</returns>
    private byte[]? ConvertToJpeg(byte[] imageBytes, string extension, int? maxWidth)
    {
        if (!_imageEncoder.SupportsImageEncoding
            || !_imageEncoder.SupportedInputFormats.Contains(extension.TrimStart('.'), StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        var inputPath = Path.Combine(Path.GetTempPath(), "opds-pse-" + Guid.NewGuid() + extension);
        var outputPath = inputPath + ".jpg";
        try
        {
            File.WriteAllBytes(inputPath, imageBytes);

            var options = new ImageProcessingOptions
            {
                MaxWidth = maxWidth,
                Quality = 85,
                SupportedOutputFormats = SupportedOutputFormats
            };

            // Always auto-orient and use a quality below 90 to make sure
            // the image encoder never passes the original file through.
            var encodedPath = _imageEncoder.EncodeImage(
                inputPath,
                DateTime.UtcNow,
                outputPath,
                true,
                null,
                85,
                options,
                ImageFormat.Jpg);

            return File.ReadAllBytes(encodedPath);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Unable to convert page image to jpeg");
            return null;
        }
        finally
        {
            DeleteTemporaryFile(inputPath);
            DeleteTemporaryFile(outputPath);
        }
    }

    /// <summary>
    /// A cached list of the pages of a book.
    /// </summary>
    /// <param name="LastWriteTimeUtc">The last write time of the book's file.</param>
    /// <param name="Size">The size of the book's file.</param>
    /// <param name="Pages">The ordered list of image entries.</param>
    private sealed record CacheEntry(DateTime LastWriteTimeUtc, long Size, IReadOnlyList<string> Pages);
}
