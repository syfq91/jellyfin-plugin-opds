using System;
using System.IO;
using System.IO.Compression;
using Jellyfin.Plugin.Opds.Models;
using Jellyfin.Plugin.Opds.Services;
using MediaBrowser.Controller.Drawing;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.Opds.Tests;

/// <summary>
/// Tests the page stream service against a real comic book archive.
/// </summary>
public class PageStreamServiceTests : IDisposable
{
    private static readonly byte[] FirstPageBytes = { 1, 1, 1 };
    private static readonly byte[] SecondPageBytes = { 2, 2, 2 };
    private static readonly byte[] TenthPageBytes = { 10, 10 };
    private static readonly byte[] NestedPageBytes = { 3 };

    private readonly string _directory;
    private readonly string _archivePath;

    /// <summary>
    /// Initializes a new instance of the <see cref="PageStreamServiceTests"/> class.
    /// </summary>
    public PageStreamServiceTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "opds-pse-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(_directory);
        _archivePath = Path.Combine(_directory, "book.cbz");

        CreateArchive(
            _archivePath,
            ("001.jpg", FirstPageBytes),
            ("002.png", SecondPageBytes),
            ("010.jpg", TenthPageBytes),
            ("sub/003.jpg", NestedPageBytes),
            ("ComicInfo.xml", new byte[] { 9 }),
            (".DS_Store", new byte[] { 8 }));
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
    public void GetPageCount_Counts_Images_Only()
    {
        var service = CreateService(CreateBook());

        Assert.Equal(4, service.GetPageCount(CreateBook()));
    }

    [Fact]
    public void GetPageCount_Returns_Zero_For_Unsupported_Format()
    {
        var book = new Book { Path = Path.Combine(_directory, "book.epub") };
        var service = CreateService(book);

        Assert.Equal(0, service.GetPageCount(book));
    }

    [Fact]
    public void GetPage_Serves_Jpeg_Pages_As_Is()
    {
        var service = CreateService(CreateBook());

        var page = service.GetPage(Guid.NewGuid(), 0, null);

        Assert.NotNull(page);
        Assert.Equal("image/jpeg", page.ContentType);
        Assert.Equal(FirstPageBytes, page.Content);
    }

    [Fact]
    public void GetPage_Uses_Natural_Page_Order()
    {
        var service = CreateService(CreateBook());

        // Pages are 001.jpg, 002.png, 010.jpg, sub/003.jpg, so page 2 is 010.jpg.
        var page = service.GetPage(Guid.NewGuid(), 2, null);

        Assert.NotNull(page);
        Assert.Equal(TenthPageBytes, page.Content);
    }

    [Fact]
    public void GetPage_Serves_Other_Formats_As_Is_When_Not_Convertible()
    {
        var service = CreateService(CreateBook());

        var page = service.GetPage(Guid.NewGuid(), 1, null);

        Assert.NotNull(page);
        Assert.Equal("image/png", page.ContentType);
        Assert.Equal(SecondPageBytes, page.Content);
    }

    [Fact]
    public void GetPage_Returns_Null_For_Invalid_Pages()
    {
        var service = CreateService(CreateBook());

        Assert.Null(service.GetPage(Guid.NewGuid(), -1, null));
        Assert.Null(service.GetPage(Guid.NewGuid(), 4, null));
        Assert.Null(service.GetPage(Guid.Empty, 0, null));
    }

    [Fact]
    public void GetPage_Returns_Null_For_Unknown_Book()
    {
        var libraryManager = new Mock<ILibraryManager>();
        libraryManager.Setup(x => x.GetItemById(It.IsAny<Guid>())).Returns((BaseItem?)null);

        var service = new PageStreamService(
            libraryManager.Object,
            new Mock<IImageEncoder>().Object,
            Mock.Of<IPdfPageSource>(),
            Mock.Of<ILogger<PageStreamService>>());

        Assert.Null(service.GetPage(Guid.NewGuid(), 0, null));
    }

    [Fact]
    public void GetPageCount_Counts_Pdf_Pages()
    {
        var book = CreatePdfBook();
        var pdfPageSource = new Mock<IPdfPageSource>();
        pdfPageSource.Setup(x => x.GetPageCount(book.Path!)).Returns(7);
        var service = CreateService(book, pdfPageSource.Object);

        Assert.Equal(7, service.GetPageCount(book));
    }

    [Fact]
    public void GetPageCount_Caches_Pdf_Page_Count()
    {
        var book = CreatePdfBook();
        var pdfPageSource = new Mock<IPdfPageSource>();
        pdfPageSource.Setup(x => x.GetPageCount(book.Path!)).Returns(7);
        var service = CreateService(book, pdfPageSource.Object);

        Assert.Equal(7, service.GetPageCount(book));
        Assert.Equal(7, service.GetPageCount(book));

        pdfPageSource.Verify(x => x.GetPageCount(book.Path!), Times.Once);
    }

    [Fact]
    public void GetPageCount_Returns_Zero_For_Unreadable_Pdf()
    {
        var book = CreatePdfBook();
        var pdfPageSource = new Mock<IPdfPageSource>();
        pdfPageSource.Setup(x => x.GetPageCount(book.Path!)).Returns((int?)null);
        var service = CreateService(book, pdfPageSource.Object);

        Assert.Equal(0, service.GetPageCount(book));
    }

    [Fact]
    public void GetPage_Renders_Pdf_Pages_With_Clamped_Width()
    {
        var jpegBytes = new byte[] { 1, 2, 3 };
        var book = CreatePdfBook();
        var pdfPageSource = new Mock<IPdfPageSource>();
        pdfPageSource.Setup(x => x.GetPageCount(book.Path!)).Returns(7);
        pdfPageSource.Setup(x => x.RenderPage(book.Path!, 2, 4096)).Returns(jpegBytes);
        var service = CreateService(book, pdfPageSource.Object);

        var page = service.GetPage(Guid.NewGuid(), 2, 99999);

        Assert.NotNull(page);
        Assert.Equal("image/jpeg", page.ContentType);
        Assert.Equal(jpegBytes, page.Content);
        pdfPageSource.Verify(x => x.RenderPage(book.Path!, 2, 4096), Times.Once);
    }

    [Fact]
    public void GetPage_Returns_Null_For_Out_Of_Range_Pdf_Page()
    {
        var book = CreatePdfBook();
        var pdfPageSource = new Mock<IPdfPageSource>();
        pdfPageSource.Setup(x => x.GetPageCount(book.Path!)).Returns(7);
        var service = CreateService(book, pdfPageSource.Object);

        Assert.Null(service.GetPage(Guid.NewGuid(), 7, null));
        pdfPageSource.Verify(x => x.RenderPage(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int?>()), Times.Never);
    }

    [Fact]
    public void GetPage_Returns_Null_For_Failed_Pdf_Render()
    {
        var book = CreatePdfBook();
        var pdfPageSource = new Mock<IPdfPageSource>();
        pdfPageSource.Setup(x => x.GetPageCount(book.Path!)).Returns(7);
        pdfPageSource.Setup(x => x.RenderPage(book.Path!, 0, It.IsAny<int?>())).Returns((byte[]?)null);
        var service = CreateService(book, pdfPageSource.Object);

        Assert.Null(service.GetPage(Guid.NewGuid(), 0, null));
    }

    [Fact]
    public void GetPageReference_Returns_Entry_Key_In_Natural_Order()
    {
        var service = CreateService(CreateBook());

        Assert.Equal("001.jpg", service.GetPageReference(CreateBook(), 0));
        Assert.Equal("010.jpg", service.GetPageReference(CreateBook(), 2));
        Assert.Equal("sub/003.jpg", service.GetPageReference(CreateBook(), 3));
    }

    [Fact]
    public void GetPageReference_Returns_Null_For_Invalid_Page()
    {
        var service = CreateService(CreateBook());

        Assert.Null(service.GetPageReference(CreateBook(), -1));
        Assert.Null(service.GetPageReference(CreateBook(), 4));
    }

    [Fact]
    public void GetPageReference_Returns_Null_For_Unsupported_Format()
    {
        var book = new Book { Path = Path.Combine(_directory, "book.epub") };
        var service = CreateService(book);

        Assert.Null(service.GetPageReference(book, 0));
    }

    [Fact]
    public void GetPageReference_Returns_One_Based_Pdf_Page()
    {
        var book = CreatePdfBook();
        var pdfPageSource = new Mock<IPdfPageSource>();
        pdfPageSource.Setup(x => x.GetPageCount(book.Path!)).Returns(7);
        var service = CreateService(book, pdfPageSource.Object);

        Assert.Equal("#page=1", service.GetPageReference(book, 0));
        Assert.Equal("#page=7", service.GetPageReference(book, 6));
        Assert.Null(service.GetPageReference(book, 7));
    }

    private static void CreateArchive(string path, params (string Key, byte[] Content)[] entries)
    {
        using var fileStream = File.Open(path, FileMode.Create);
        using var archive = new ZipArchive(fileStream, ZipArchiveMode.Create);
        foreach (var (key, content) in entries)
        {
            var entry = archive.CreateEntry(key);
            using var stream = entry.Open();
            stream.Write(content, 0, content.Length);
        }
    }

    private PageStreamService CreateService(Book book, IPdfPageSource? pdfPageSource = null)
    {
        var libraryManager = new Mock<ILibraryManager>();
        libraryManager.Setup(x => x.GetItemById(It.IsAny<Guid>())).Returns(book);

        // The image encoder is mocked as unsupported, so all non-jpeg
        // pages are served without conversion.
        var imageEncoder = new Mock<IImageEncoder>();
        imageEncoder.SetupGet(x => x.SupportsImageEncoding).Returns(false);

        return new PageStreamService(
            libraryManager.Object,
            imageEncoder.Object,
            pdfPageSource ?? Mock.Of<IPdfPageSource>(),
            Mock.Of<ILogger<PageStreamService>>());
    }

    private Book CreateBook()
    {
        return new Book { Path = _archivePath };
    }

    private Book CreatePdfBook()
    {
        var path = Path.Combine(_directory, "book.pdf");
        File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
        return new Book { Path = path };
    }
}
