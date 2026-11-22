using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Jellyfin.Plugin.Opds.Services;
using MediaBrowser.Controller.Drawing;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Drawing;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.Opds.Tests;

/// <summary>
/// Tests the on-demand EPUB image downscaling service.
/// </summary>
public class EpubImageOptimizerTests : IDisposable
{
    private readonly string _directory;
    private readonly string _cacheDirectory;
    private readonly Mock<IImageEncoder> _imageEncoder;
    private readonly List<string> _logMessages = new();
    private readonly EpubImageOptimizer _optimizer;
    private ImageProcessingOptions? _capturedOptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="EpubImageOptimizerTests"/> class.
    /// </summary>
    public EpubImageOptimizerTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "opds-epub-optimizer-tests-" + Guid.NewGuid());
        _cacheDirectory = Path.Combine(_directory, "cache");
        Directory.CreateDirectory(_directory);

        _imageEncoder = new Mock<IImageEncoder>();
        _imageEncoder.SetupGet(x => x.SupportsImageEncoding).Returns(true);
        _imageEncoder.SetupGet(x => x.SupportedInputFormats).Returns(new[] { "jpg", "png" });
        _imageEncoder.SetupGet(x => x.SupportedOutputFormats).Returns(new[] { ImageFormat.Jpg, ImageFormat.Png });
        _imageEncoder.Setup(x => x.GetImageSize(It.IsAny<string>()))
            .Returns((string path) => path.Contains("big", StringComparison.Ordinal)
                ? new ImageDimensions(2000, 3000)
                : new ImageDimensions(100, 100));
        _imageEncoder
            .Setup(x => x.EncodeImage(
                It.IsAny<string>(),
                It.IsAny<DateTime>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<ImageOrientation?>(),
                It.IsAny<int>(),
                It.IsAny<ImageProcessingOptions>(),
                It.IsAny<ImageFormat>()))
            .Returns((
                string input,
                DateTime dateModified,
                string output,
                bool autoOrient,
                ImageOrientation? orientation,
                int quality,
                ImageProcessingOptions options,
                ImageFormat format) =>
            {
                _capturedOptions = options;
                File.WriteAllBytes(output, new byte[] { 9, 9 });
                return output;
            });

        _optimizer = new EpubImageOptimizer(
            _imageEncoder.Object,
            _cacheDirectory,
            10,
            new CapturingLogger(_logMessages));
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
    public void Optimize_DownscalesOversizedImages_AndPreservesOtherEntries()
    {
        var source = CreateEpub(withMimetype: true, drm: false, prePaginated: false, withBigImage: true);
        var book = new Book { Id = Guid.NewGuid(), Path = source };

        var result = _optimizer.Optimize(book);

        Assert.NotNull(result);
        Assert.True(
            result.WasOptimized,
            $"path={result.Path}; cache={string.Join(",", Directory.GetFiles(_cacheDirectory).Select(Path.GetFileName))}; log={string.Join(" | ", _logMessages)}");
        Assert.NotEqual(source, result.Path);

        using var stream = File.OpenRead(result.Path);
        var header = new byte[10];
        stream.ReadExactly(header);
        Assert.Equal(0, BitConverter.ToUInt16(header, 8)); // mimetype is stored, not deflated

        using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        Assert.Equal("mimetype", zip.Entries[0].FullName);
        Assert.Equal(
            new[] { "mimetype", "META-INF/container.xml", "OEBPS/content.opf", "OEBPS/big.jpg", "OEBPS/small.jpg", "OEBPS/notes.txt" },
            zip.Entries.Select(e => e.FullName).ToArray());
        Assert.Equal(new byte[] { 9, 9 }, ReadEntry(zip, "OEBPS/big.jpg"));
        Assert.Equal(new byte[] { 4, 5 }, ReadEntry(zip, "OEBPS/small.jpg"));
        Assert.Equal(new byte[] { 10 }, ReadEntry(zip, "OEBPS/notes.txt"));

        Assert.NotNull(_capturedOptions);
        Assert.Equal(1600, _capturedOptions.MaxWidth);
        Assert.Equal(1600, _capturedOptions.MaxHeight);
        Assert.Equal(82, _capturedOptions.Quality);
    }

    [Fact]
    public void Optimize_ReturnsOriginal_WhenNoImageExceedsTheLimits()
    {
        var source = CreateEpub(withMimetype: true, drm: false, prePaginated: false, withBigImage: false);

        var result = _optimizer.Optimize(new Book { Id = Guid.NewGuid(), Path = source });

        Assert.NotNull(result);
        Assert.False(result.WasOptimized);
        Assert.Equal(source, result.Path);
        _imageEncoder.Verify(
            x => x.EncodeImage(
                It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<bool>(),
                It.IsAny<ImageOrientation?>(), It.IsAny<int>(), It.IsAny<ImageProcessingOptions>(),
                It.IsAny<ImageFormat>()),
            Times.Never);
    }

    [Fact]
    public void Optimize_ReturnsOriginal_WhenEpubIsEncrypted()
    {
        var source = CreateEpub(withMimetype: true, drm: true, prePaginated: false, withBigImage: true);

        var result = _optimizer.Optimize(new Book { Id = Guid.NewGuid(), Path = source });

        Assert.NotNull(result);
        Assert.False(result.WasOptimized);
        Assert.Equal(source, result.Path);
    }

    [Fact]
    public void Optimize_ReturnsOriginal_WhenEpubIsFixedLayout()
    {
        var source = CreateEpub(withMimetype: true, drm: false, prePaginated: true, withBigImage: true);

        var result = _optimizer.Optimize(new Book { Id = Guid.NewGuid(), Path = source });

        Assert.NotNull(result);
        Assert.False(result.WasOptimized);
        Assert.Equal(source, result.Path);
    }

    [Fact]
    public void Optimize_ReturnsOriginal_WhenMimetypeEntryIsMissing()
    {
        var source = CreateEpub(withMimetype: false, drm: false, prePaginated: false, withBigImage: true);

        var result = _optimizer.Optimize(new Book { Id = Guid.NewGuid(), Path = source });

        Assert.NotNull(result);
        Assert.False(result.WasOptimized);
        Assert.Equal(source, result.Path);
    }

    [Fact]
    public void Optimize_ReusesCachedCopy_ForUnchangedSource()
    {
        var source = CreateEpub(withMimetype: true, drm: false, prePaginated: false, withBigImage: true);
        var book = new Book { Id = Guid.NewGuid(), Path = source };

        var first = _optimizer.Optimize(book);
        var second = _optimizer.Optimize(book);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(first.Path, second.Path);
        _imageEncoder.Verify(
            x => x.EncodeImage(
                It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<bool>(),
                It.IsAny<ImageOrientation?>(), It.IsAny<int>(), It.IsAny<ImageProcessingOptions>(),
                It.IsAny<ImageFormat>()),
            Times.Once);
    }

    [Fact]
    public void Optimize_RegeneratesCopy_WhenSourceChanges()
    {
        var source = CreateEpub(withMimetype: true, drm: false, prePaginated: false, withBigImage: true);
        var book = new Book { Id = Guid.NewGuid(), Path = source };

        var first = _optimizer.Optimize(book);
        File.SetLastWriteTimeUtc(source, DateTime.UtcNow.AddMinutes(10));
        var second = _optimizer.Optimize(book);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotEqual(first.Path, second.Path);
    }

    [Fact]
    public void Optimize_ReturnsNull_ForNonEpub()
    {
        var path = Path.Combine(_directory, "book.cbz");
        File.WriteAllBytes(path, new byte[] { 1 });

        Assert.Null(_optimizer.Optimize(new Book { Id = Guid.NewGuid(), Path = path }));
    }

    private string CreateEpub(bool withMimetype, bool drm, bool prePaginated, bool withBigImage)
    {
        var path = Path.Combine(_directory, "book-" + Guid.NewGuid().ToString("N") + ".epub");
        using var stream = File.Create(path);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);

        if (withMimetype)
        {
            AddEntry(zip, "mimetype", Encoding.UTF8.GetBytes("application/epub+zip"), CompressionLevel.NoCompression);
        }

        AddEntry(zip, "META-INF/container.xml", Encoding.UTF8.GetBytes(
            "<?xml version=\"1.0\"?><container version=\"1.0\" xmlns=\"urn:oasis:names:tc:opendocument:xmlns:container\"><rootfiles/></container>"));
        var opf = "<?xml version=\"1.0\"?><package xmlns=\"http://www.idpf.org/2007/opf\">"
            + (prePaginated ? "<meta property=\"rendition:layout\">pre-paginated</meta>" : string.Empty)
            + "</package>";
        AddEntry(zip, "OEBPS/content.opf", Encoding.UTF8.GetBytes(opf));
        if (withBigImage)
        {
            AddEntry(zip, "OEBPS/big.jpg", new byte[] { 1, 2, 3 });
        }

        AddEntry(zip, "OEBPS/small.jpg", new byte[] { 4, 5 });
        AddEntry(zip, "OEBPS/notes.txt", new byte[] { 10 });
        if (drm)
        {
            AddEntry(zip, "META-INF/encryption.xml", Encoding.UTF8.GetBytes("<encryption/>"));
        }

        return path;
    }

    private static void AddEntry(ZipArchive zip, string name, byte[] content, CompressionLevel level = CompressionLevel.Optimal)
    {
        var entry = zip.CreateEntry(name, level);
        using var stream = entry.Open();
        stream.Write(content, 0, content.Length);
    }

    private sealed class CapturingLogger : ILogger<EpubImageOptimizer>
    {
        private readonly List<string> _messages;

        public CapturingLogger(List<string> messages)
        {
            _messages = messages;
        }

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
            => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => _messages.Add(exception?.ToString() ?? formatter(state, exception));

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }

    private static byte[] ReadEntry(ZipArchive zip, string name)
    {
        var entry = zip.GetEntry(name);
        Assert.NotNull(entry);
        using var stream = entry.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
