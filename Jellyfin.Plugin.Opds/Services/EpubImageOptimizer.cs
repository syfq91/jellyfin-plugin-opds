using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Jellyfin.Plugin.Opds.Models;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Drawing;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Drawing;
using Microsoft.Extensions.Logging;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Writers.Zip;

namespace Jellyfin.Plugin.Opds.Services;

/// <summary>
/// Repacks EPUB publications with oversized raster images downscaled. The EPUB structure,
/// entry names and formats are preserved so that all internal references stay valid.
/// </summary>
public sealed class EpubImageOptimizer : IEpubImageOptimizer
{
    private const int DefaultMaxCacheEntries = 256;
    private const long MaxEntryBytes = 100L * 1024 * 1024;
    private const long MaxOutputBytes = 1024L * 1024 * 1024;
    private const int DefaultMaxWidth = 1200;
    private const int DefaultMaxHeight = 1600;
    private const int DefaultQuality = 82;
    private const string MimetypeKey = "mimetype";
    private const string MimetypeContent = "application/epub+zip";
    private const string EncryptionKey = "META-INF/encryption.xml";

    private readonly ConcurrentDictionary<string, object> _gates = new(StringComparer.Ordinal);
    private readonly IImageEncoder _imageEncoder;
    private readonly string _cacheDirectory;
    private readonly int _maxCacheEntries;
    private readonly ILogger<EpubImageOptimizer> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="EpubImageOptimizer"/> class.
    /// </summary>
    /// <param name="imageEncoder">Instance of the <see cref="IImageEncoder"/> interface.</param>
    /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{EpubImageOptimizer}"/> interface.</param>
    public EpubImageOptimizer(
        IImageEncoder imageEncoder,
        IApplicationPaths applicationPaths,
        ILogger<EpubImageOptimizer> logger)
        : this(
            imageEncoder,
            Path.Combine(applicationPaths.CachePath, "opds-optimized"),
            DefaultMaxCacheEntries,
            logger)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="EpubImageOptimizer"/> class with a custom cache directory.
    /// </summary>
    /// <param name="imageEncoder">Instance of the <see cref="IImageEncoder"/> interface.</param>
    /// <param name="cacheDirectory">The directory in which optimized copies are cached.</param>
    /// <param name="maxCacheEntries">The maximum number of cached copies.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{EpubImageOptimizer}"/> interface.</param>
    internal EpubImageOptimizer(
        IImageEncoder imageEncoder,
        string cacheDirectory,
        int maxCacheEntries,
        ILogger<EpubImageOptimizer> logger)
    {
        _imageEncoder = imageEncoder;
        _cacheDirectory = cacheDirectory;
        _maxCacheEntries = maxCacheEntries > 0 ? maxCacheEntries : DefaultMaxCacheEntries;
        _logger = logger;
    }

    /// <inheritdoc />
    public OptimizedEpubResult? Optimize(Book book)
    {
        ArgumentNullException.ThrowIfNull(book);

        var sourcePath = book.Path;
        if (string.IsNullOrEmpty(sourcePath)
            || !string.Equals(Path.GetExtension(sourcePath), ".epub", StringComparison.OrdinalIgnoreCase)
            || !File.Exists(sourcePath))
        {
            return null;
        }

        var file = new FileInfo(sourcePath);
        var maxWidth = GetMaxWidth();
        var maxHeight = GetMaxHeight();
        var quality = GetQuality();
        var key = ComputeCacheKey(book.Id, file, maxWidth, maxHeight, quality);
        var cachePath = Path.Combine(_cacheDirectory, key + ".epub");

        if (IsValidCachedFile(cachePath))
        {
            return new OptimizedEpubResult(cachePath, true);
        }

        var gate = _gates.GetOrAdd(key, static _ => new object());
        lock (gate)
        {
            try
            {
                if (IsValidCachedFile(cachePath))
                {
                    return new OptimizedEpubResult(cachePath, true);
                }

                return CreateOptimizedCopy(sourcePath, cachePath, maxWidth, maxHeight, quality)
                    ? new OptimizedEpubResult(cachePath, true)
                    : new OptimizedEpubResult(sourcePath, false);
            }
            finally
            {
                if (_gates.TryGetValue(key, out var current) && ReferenceEquals(current, gate))
                {
                    _gates.TryRemove(key, out _);
                }
            }
        }
    }

    private bool CreateOptimizedCopy(string sourcePath, string cachePath, int maxWidth, int maxHeight, int quality)
    {
        Directory.CreateDirectory(_cacheDirectory);
        var tempPath = cachePath + ".tmp";
        var downscaled = false;
        long totalWritten = 0;

        try
        {
            using (var sourceStream = File.OpenRead(sourcePath))
            using (var archive = ArchiveFactory.OpenArchive(sourceStream))
            {
                var entries = archive.Entries.Where(e => !e.IsDirectory).ToList();
                var mimetype = entries.FirstOrDefault(
                    e => string.Equals(e.Key, MimetypeKey, StringComparison.Ordinal));

                if (mimetype is null || !HasExpectedMimetype(mimetype))
                {
                    return false;
                }

                using var outputStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);
                using var writer = new ZipWriter(outputStream, new ZipWriterOptions(CompressionType.Deflate));

                // The EPUB specification requires this entry to be first and stored uncompressed.
                using (var mimetypeStream = mimetype.OpenEntryStream())
                {
                    writer.Write(
                        MimetypeKey,
                        mimetypeStream,
                        new ZipWriterEntryOptions
                        {
                            CompressionType = CompressionType.None,
                            CompressionLevel = 0
                        });
                }

                foreach (var entry in entries)
                {
                    if (ReferenceEquals(entry, mimetype))
                    {
                        continue;
                    }

                    var key = entry.Key;
                    if (string.IsNullOrEmpty(key))
                    {
                        return false;
                    }

                    if (string.Equals(NormalizeKey(key), EncryptionKey, StringComparison.OrdinalIgnoreCase))
                    {
                        // Repacking an encrypted EPUB would break its encryption metadata.
                        return false;
                    }

                    var bytes = ReadBytes(entry, MaxEntryBytes);
                    if (bytes is null)
                    {
                        return false;
                    }

                    if (string.Equals(Path.GetExtension(key), ".opf", StringComparison.OrdinalIgnoreCase)
                        && ContainsPrePaginatedLayout(bytes))
                    {
                        // Fixed-layout books rely on the original pixel dimensions of their images.
                        return false;
                    }

                    var extension = Path.GetExtension(key);
                    if (ImageFormatFor(extension) is ImageFormat format)
                    {
                        bytes = DownscaleIfNeeded(
                            bytes,
                            extension,
                            format,
                            maxWidth,
                            maxHeight,
                            quality,
                            key,
                            ref downscaled);
                    }

                    totalWritten += bytes.Length;
                    if (totalWritten > MaxOutputBytes)
                    {
                        return false;
                    }

                    using var entryStream = new MemoryStream(bytes, writable: false);
                    writer.Write(key, entryStream, new ZipWriterEntryOptions { CompressionType = CompressionType.Deflate });
                }
            }

            if (!downscaled)
            {
                // Nothing changed, so there is no reason to keep a repacked copy.
                return false;
            }

            if (new FileInfo(tempPath).Length == 0)
            {
                return false;
            }

            EvictIfNeeded();
            File.Move(tempPath, cachePath, overwrite: true);
            return true;
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Unable to optimize EPUB {CachePath}", cachePath);
            return false;
        }
        finally
        {
            DeleteTemporaryFile(tempPath);
        }
    }

    private byte[] DownscaleIfNeeded(
        byte[] original,
        string extension,
        ImageFormat format,
        int maxWidth,
        int maxHeight,
        int quality,
        string entryKey,
        ref bool downscaled)
    {
        if (!_imageEncoder.SupportsImageEncoding
            || !_imageEncoder.SupportedInputFormats.Contains(extension.TrimStart('.'), StringComparer.OrdinalIgnoreCase)
            || !_imageEncoder.SupportedOutputFormats.Contains(format))
        {
            return original;
        }

        var hint = Path.GetFileNameWithoutExtension(entryKey);
        if (string.IsNullOrEmpty(hint))
        {
            hint = "page";
        }

        var safeHint = new string(hint
            .Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_')
            .ToArray());
        var name = "opds-epub-" + safeHint + "-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        var inputPath = Path.Combine(Path.GetTempPath(), name + extension);
        var outputPath = inputPath + ".out";
        try
        {
            File.WriteAllBytes(inputPath, original);
            var size = _imageEncoder.GetImageSize(inputPath);
            if (size.Width <= maxWidth && size.Height <= maxHeight)
            {
                return original;
            }

            var options = new ImageProcessingOptions
            {
                MaxWidth = maxWidth,
                MaxHeight = maxHeight,
                Quality = quality,
                SupportedOutputFormats = new[] { format }
            };

            var encodedPath = _imageEncoder.EncodeImage(
                inputPath,
                DateTime.UtcNow,
                outputPath,
                true,
                null,
                quality,
                options,
                format);

            if (!File.Exists(encodedPath))
            {
                return original;
            }

            var result = File.ReadAllBytes(encodedPath);
            downscaled = true;
            return result;
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Unable to downscale EPUB image {Extension}", extension);
            return original;
        }
        finally
        {
            DeleteTemporaryFile(inputPath);
            DeleteTemporaryFile(outputPath);
        }
    }

    private static byte[]? ReadBytes(IArchiveEntry entry, long maxBytes)
    {
        using var input = entry.OpenEntryStream();
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        long total = 0;
        int read;
        while ((read = input.Read(chunk, 0, chunk.Length)) > 0)
        {
            total += read;
            if (total > maxBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static bool HasExpectedMimetype(IArchiveEntry entry)
    {
        var bytes = ReadBytes(entry, 4096);
        if (bytes is null)
        {
            return false;
        }

        var text = Encoding.UTF8.GetString(bytes).Trim('\0', '\r', '\n', ' ');
        return string.Equals(text, MimetypeContent, StringComparison.Ordinal);
    }

    private static bool ContainsPrePaginatedLayout(byte[] opfBytes)
    {
        return Encoding.UTF8.GetString(opfBytes).Contains("pre-paginated", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeKey(string key) => key.Replace('\\', '/');

    private static ImageFormat? ImageFormatFor(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => ImageFormat.Jpg,
            ".png" => ImageFormat.Png,
            ".gif" => ImageFormat.Gif,
            ".webp" => ImageFormat.Webp,
            ".bmp" => ImageFormat.Bmp,
            _ => null
        };
    }

    private static string ComputeCacheKey(Guid bookId, FileInfo file, int maxWidth, int maxHeight, int quality)
    {
        var raw = string.Format(
            CultureInfo.InvariantCulture,
            "{0:N}:{1}:{2}:{3}:{4}:{5}:{6}",
            bookId,
            file.Length,
            file.LastWriteTimeUtc.Ticks,
            maxWidth,
            maxHeight,
            quality,
            OptimizedConstants.CacheVersion);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    }

    private static bool IsValidCachedFile(string path)
    {
        var info = new FileInfo(path);
        return info.Exists
            && info.Length > 0
            && (info.Attributes & FileAttributes.ReparsePoint) == 0;
    }

    private static void DeleteTemporaryFile(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private void EvictIfNeeded()
    {
        if (_maxCacheEntries <= 0)
        {
            return;
        }

        try
        {
            var files = new DirectoryInfo(_cacheDirectory).GetFiles("*.epub");
            if (files.Length < _maxCacheEntries)
            {
                return;
            }

            var evictCount = Math.Max(1, _maxCacheEntries / 4);
            foreach (var file in files.OrderBy(f => f.LastWriteTimeUtc).Take(evictCount))
            {
                try
                {
                    file.Delete();
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Unable to evict optimized EPUB cache entries");
        }
    }

    private static int GetMaxWidth()
    {
        var configured = OpdsPlugin.Instance?.Configuration.OptimizedMaxWidth ?? DefaultMaxWidth;
        return configured is > 0 and <= 4096 ? configured : DefaultMaxWidth;
    }

    private static int GetMaxHeight()
    {
        var configured = OpdsPlugin.Instance?.Configuration.OptimizedMaxHeight ?? DefaultMaxHeight;
        return configured is > 0 and <= 4096 ? configured : DefaultMaxHeight;
    }

    private static int GetQuality()
    {
        var configured = OpdsPlugin.Instance?.Configuration.OptimizedImageQuality ?? DefaultQuality;
        return configured is > 0 and < 90 ? configured : DefaultQuality;
    }
}
