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
/// Provides page by page access to comic book archives and PDF documents,
/// as used by the OPDS page streaming extension.
/// </summary>
public class PageStreamService : IPageStreamService
{
    /// <summary>
    /// The default maximum amount of page lists kept in the cache.
    /// </summary>
    private const int DefaultMaxCacheEntries = 1000;

    /// <summary>
    /// The maximum width of a resized page.
    /// </summary>
    private const int MaxPageWidth = 4096;

    /// <summary>
    /// The extension of PDF documents that can be streamed page by page.
    /// </summary>
    private const string PdfExtension = ".pdf";

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

    /// <summary>
    /// The default maximum allowed size in bytes for an extracted archive page entry (100 MB).
    /// </summary>
    public const long DefaultMaxPageSizeBytes = 100 * 1024 * 1024;

    private readonly ConcurrentDictionary<string, CacheEntry> _pageCache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, PdfCacheEntry> _pdfPageCache = new(StringComparer.Ordinal);

    private readonly ILibraryManager _libraryManager;
    private readonly IImageEncoder _imageEncoder;
    private readonly IPdfPageSource _pdfPageSource;
    private readonly ILogger<PageStreamService> _logger;
    private readonly int _maxCacheEntries;
    private readonly long _maxPageSizeBytes;

    /// <summary>
    /// Initializes a new instance of the <see cref="PageStreamService"/> class.
    /// </summary>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="imageEncoder">Instance of the <see cref="IImageEncoder"/> interface.</param>
    /// <param name="pdfPageSource">Instance of the <see cref="IPdfPageSource"/> interface.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{PageStreamService}"/> interface.</param>
    /// <param name="maxPageSizeBytes">Optional maximum page size limit in bytes.</param>
    public PageStreamService(
        ILibraryManager libraryManager,
        IImageEncoder imageEncoder,
        IPdfPageSource pdfPageSource,
        ILogger<PageStreamService> logger,
        long maxPageSizeBytes = DefaultMaxPageSizeBytes)
        : this(libraryManager, imageEncoder, pdfPageSource, logger, DefaultMaxCacheEntries, maxPageSizeBytes)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PageStreamService"/> class with custom cache entry limit.
    /// </summary>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="imageEncoder">Instance of the <see cref="IImageEncoder"/> interface.</param>
    /// <param name="pdfPageSource">Instance of the <see cref="IPdfPageSource"/> interface.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{PageStreamService}"/> interface.</param>
    /// <param name="maxCacheEntries">The maximum number of cache entries.</param>
    /// <param name="maxPageSizeBytes">Optional maximum page size limit in bytes.</param>
    internal PageStreamService(
        ILibraryManager libraryManager,
        IImageEncoder imageEncoder,
        IPdfPageSource pdfPageSource,
        ILogger<PageStreamService> logger,
        int maxCacheEntries,
        long maxPageSizeBytes = DefaultMaxPageSizeBytes)
    {
        _libraryManager = libraryManager;
        _imageEncoder = imageEncoder;
        _pdfPageSource = pdfPageSource;
        _logger = logger;
        _maxCacheEntries = maxCacheEntries > 0 ? maxCacheEntries : DefaultMaxCacheEntries;
        _maxPageSizeBytes = maxPageSizeBytes > 0 ? maxPageSizeBytes : DefaultMaxPageSizeBytes;
    }

    internal int GetPageCacheCount() => _pageCache.Count;

    internal bool IsPageCached(string path) => _pageCache.ContainsKey(path);

    internal int GetPdfPageCacheCount() => _pdfPageCache.Count;

    internal bool IsPdfPageCached(string path) => _pdfPageCache.ContainsKey(path);

    /// <inheritdoc />
    public int GetPageCount(Book book)
    {
        ArgumentNullException.ThrowIfNull(book);

        if (IsPdf(book.Path))
        {
            return TryGetPdfPageCount(book, out var pageCount) ? pageCount : 0;
        }

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
        if (book is null)
        {
            return null;
        }

        if (IsPdf(book.Path))
        {
            if (!TryGetPdfPageCount(book, out var pageCount) || pageNumber >= pageCount)
            {
                return null;
            }

            return ExtractPdfPage(book, pageNumber, maxWidth);
        }

        var path = book.Path;
        if (!IsStreamable(path))
        {
            return null;
        }

        var file = new FileInfo(path);
        if (!file.Exists)
        {
            return null;
        }

        if (_pageCache.TryGetValue(path, out var cached)
            && cached.Size == file.Length
            && cached.LastWriteTimeUtc == file.LastWriteTimeUtc)
        {
            if (pageNumber >= cached.Pages.Count)
            {
                return null;
            }

            return ExtractPage(book, cached.Pages[pageNumber], maxWidth);
        }

        return ExtractPageAndCacheList(book, file, pageNumber, maxWidth);
    }

    /// <inheritdoc />
    public string? GetPageReference(Book book, int pageNumber)
    {
        if (book is null || pageNumber < 0)
        {
            return null;
        }

        if (IsPdf(book.Path))
        {
            // PDF references carry the 1-based page number (RFC 8118, section 3).
            return TryGetPdfPageCount(book, out var pageCount) && pageNumber < pageCount
                ? "#page=" + (pageNumber + 1)
                : null;
        }

        // Archive references are the path of the page inside the archive,
        // in the same natural order that the streaming endpoint uses.
        return TryGetPageList(book, out var pages) && pageNumber < pages.Count
            ? pages[pageNumber]
            : null;
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

    private static bool IsPdf(string? path)
    {
        return !string.IsNullOrEmpty(path)
            && PdfExtension.Equals(Path.GetExtension(path), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsStreamable(string? path)
    {
        return IsPdf(path)
            || (!string.IsNullOrEmpty(path)
                && StreamableExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase));
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

            EvictPageCacheIfFull();
            var cacheEntry = new CacheEntry(file.LastWriteTimeUtc, file.Length, pageList, DateTime.UtcNow);
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
    /// Gets the number of pages of the book's PDF document,
    /// using a cache keyed on the path of the file.
    /// </summary>
    /// <param name="book">The book.</param>
    /// <param name="pageCount">The number of pages of the document.</param>
    /// <returns>Whether the page count could be determined.</returns>
    private bool TryGetPdfPageCount(Book book, out int pageCount)
    {
        pageCount = 0;

        var path = book.Path;
        if (!IsPdf(path))
        {
            return false;
        }

        var file = new FileInfo(path);
        if (!file.Exists)
        {
            return false;
        }

        if (_pdfPageCache.TryGetValue(path, out var cached)
            && cached.Size == file.Length
            && cached.LastWriteTimeUtc == file.LastWriteTimeUtc)
        {
            pageCount = cached.PageCount;
            return true;
        }

        var count = _pdfPageSource.GetPageCount(path);
        if (count is null or <= 0)
        {
            return false;
        }

        EvictPdfPageCacheIfFull();
        _pdfPageCache[path] = new PdfCacheEntry(file.LastWriteTimeUtc, file.Length, count.Value, DateTime.UtcNow);
        pageCount = count.Value;
        return true;
    }

    /// <summary>
    /// Renders a single page of the book's PDF document as jpeg.
    /// </summary>
    /// <param name="book">The book.</param>
    /// <param name="pageNumber">The zero-based number of the page.</param>
    /// <param name="maxWidth">The optional maximum width of the page.</param>
    /// <returns>The page image, or <c>null</c> when the page could not be rendered.</returns>
    private PageStreamResult? ExtractPdfPage(Book book, int pageNumber, int? maxWidth)
    {
        var path = book.Path;
        try
        {
            var jpegBytes = _pdfPageSource.RenderPage(path, pageNumber, maxWidth);
            if (jpegBytes is null)
            {
                return null;
            }

            return new PageStreamResult(jpegBytes, PseConstants.JpegMediaType);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Unable to render page {PageNumber} of {Path}", pageNumber, path);
            return null;
        }
    }

    /// <summary>
    /// Evicts the oldest 25% of entries from the page list cache if it has reached capacity.
    /// </summary>
    private void EvictPageCacheIfFull()
    {
        if (_pageCache.Count >= _maxCacheEntries)
        {
            var evictCount = Math.Max(1, _maxCacheEntries / 4);
            var toRemove = _pageCache
                .OrderBy(p => p.Value.CachedAtUtc)
                .Take(evictCount)
                .Select(p => p.Key)
                .ToArray();

            foreach (var key in toRemove)
            {
                _pageCache.TryRemove(key, out _);
            }
        }
    }

    /// <summary>
    /// Evicts the oldest 25% of entries from the PDF page count cache if it has reached capacity.
    /// </summary>
    private void EvictPdfPageCacheIfFull()
    {
        if (_pdfPageCache.Count >= _maxCacheEntries)
        {
            var evictCount = Math.Max(1, _maxCacheEntries / 4);
            var toRemove = _pdfPageCache
                .OrderBy(p => p.Value.CachedAtUtc)
                .Take(evictCount)
                .Select(p => p.Key)
                .ToArray();

            foreach (var key in toRemove)
            {
                _pdfPageCache.TryRemove(key, out _);
            }
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
            byte[]? imageBytes;
            using (var stream = File.OpenRead(path))
            using (var archive = ArchiveFactory.OpenArchive(stream))
            {
                var entry = archive.Entries.FirstOrDefault(e => string.Equals(e.Key, entryKey, StringComparison.Ordinal));
                if (entry is null)
                {
                    return null;
                }

                imageBytes = ReadEntryBytes(entry, entryKey, path);
                if (imageBytes is null)
                {
                    return null;
                }
            }

            return ProcessImageBytes(imageBytes, entryKey, maxWidth);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Unable to extract page {EntryKey} from {Path}", entryKey, path);
            return null;
        }
    }

    /// <summary>
    /// Enumerates all pages, updates the page cache, and extracts the requested page in a single archive open.
    /// </summary>
    /// <param name="book">The book.</param>
    /// <param name="file">File info for cache validation.</param>
    /// <param name="pageNumber">The zero-based page number.</param>
    /// <param name="maxWidth">The optional maximum width of the page.</param>
    /// <returns>The page image, or <c>null</c> when the page could not be extracted.</returns>
    private PageStreamResult? ExtractPageAndCacheList(Book book, FileInfo file, int pageNumber, int? maxWidth)
    {
        var path = book.Path;
        try
        {
            byte[]? imageBytes;
            string entryKey;
            using (var stream = File.OpenRead(path))
            using (var archive = ArchiveFactory.OpenArchive(stream))
            {
                var imageEntries = archive.Entries
                    .Where(IsImageEntry)
                    .OrderBy(entry => entry.Key!, NaturalSortComparer.Instance)
                    .ToArray();

                var pageList = imageEntries.Select(e => e.Key!).ToArray();

                EvictPageCacheIfFull();
                _pageCache[path] = new CacheEntry(file.LastWriteTimeUtc, file.Length, pageList, DateTime.UtcNow);

                if (pageNumber >= imageEntries.Length)
                {
                    return null;
                }

                var entry = imageEntries[pageNumber];
                entryKey = entry.Key!;

                imageBytes = ReadEntryBytes(entry, entryKey, path);
                if (imageBytes is null)
                {
                    return null;
                }
            }

            return ProcessImageBytes(imageBytes, entryKey, maxWidth);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Unable to read pages from {Path}", path);
            return null;
        }
    }

    /// <summary>
    /// Reads and size-checks the bytes of an archive entry.
    /// </summary>
    /// <param name="entry">The archive entry.</param>
    /// <param name="entryKey">The key of the archive entry.</param>
    /// <param name="path">The path to the archive file.</param>
    /// <returns>The extracted byte array, or <c>null</c> if it exceeds the maximum page size.</returns>
    private byte[]? ReadEntryBytes(IArchiveEntry entry, string entryKey, string path)
    {
        using var entryStream = entry.OpenEntryStream();
        using var buffer = new MemoryStream();
        var totalRead = 0L;
        var readBuffer = new byte[81920];
        int bytesRead;
        while ((bytesRead = entryStream.Read(readBuffer, 0, readBuffer.Length)) > 0)
        {
            totalRead += bytesRead;
            if (totalRead > _maxPageSizeBytes)
            {
                _logger.LogWarning("Page {EntryKey} in {Path} exceeds maximum page size limit of {Limit} bytes", entryKey, path, _maxPageSizeBytes);
                return null;
            }

            buffer.Write(readBuffer, 0, bytesRead);
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// Processes extracted image bytes, applying jpeg resizing or conversion if necessary.
    /// </summary>
    /// <param name="imageBytes">The raw image bytes.</param>
    /// <param name="entryKey">The entry key for MIME type detection.</param>
    /// <param name="maxWidth">The optional maximum width.</param>
    /// <returns>The resulting page stream result, or <c>null</c> if conversion failed.</returns>
    private PageStreamResult? ProcessImageBytes(byte[] imageBytes, string entryKey, int? maxWidth)
    {
        var extension = Path.GetExtension(entryKey);

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
    /// <param name="CachedAtUtc">The UTC timestamp when this entry was cached.</param>
    private sealed record CacheEntry(DateTime LastWriteTimeUtc, long Size, IReadOnlyList<string> Pages, DateTime CachedAtUtc);

    /// <summary>
    /// A cached page count of a PDF document.
    /// </summary>
    /// <param name="LastWriteTimeUtc">The last write time of the book's file.</param>
    /// <param name="Size">The size of the book's file.</param>
    /// <param name="PageCount">The number of pages of the document.</param>
    /// <param name="CachedAtUtc">The UTC timestamp when this entry was cached.</param>
    private sealed record PdfCacheEntry(DateTime LastWriteTimeUtc, long Size, int PageCount, DateTime CachedAtUtc);
}
