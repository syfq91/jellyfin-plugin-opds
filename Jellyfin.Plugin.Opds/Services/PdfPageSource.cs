using System;
using System.IO;
using Microsoft.Extensions.Logging;
using PDFtoImage;
using SkiaSharp;

namespace Jellyfin.Plugin.Opds.Services;

/// <summary>
/// Renders pages of PDF documents, using the PDFtoImage library that
/// is provided by the Jellyfin server at runtime.
/// </summary>
public class PdfPageSource : IPdfPageSource
{
    /// <summary>
    /// The quality of the rendered jpeg pages.
    /// </summary>
    private const int JpegQuality = 85;

    /// <summary>
    /// The resolution of pages that are rendered without an explicit width,
    /// instead of the 300 dpi default of PDFtoImage.
    /// </summary>
    private const int DefaultDpi = 150;

    private readonly ILogger<PdfPageSource> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfPageSource"/> class.
    /// </summary>
    /// <param name="logger">Instance of the <see cref="ILogger{PdfPageSource}"/> interface.</param>
    public PdfPageSource(ILogger<PdfPageSource> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public int? GetPageCount(string path)
    {
        ArgumentNullException.ThrowIfNullOrEmpty(path);

        try
        {
#pragma warning disable CA1416
            using var stream = File.OpenRead(path);
            return Conversion.GetPageCount(stream);
#pragma warning restore CA1416
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Unable to count pages of {Path}", path);
            return null;
        }
    }

    /// <inheritdoc />
    public byte[]? RenderPage(string path, int pageNumber, int? maxWidth)
    {
        ArgumentNullException.ThrowIfNullOrEmpty(path);
        ArgumentOutOfRangeException.ThrowIfNegative(pageNumber);

        try
        {
            var options = new RenderOptions
            {
                Dpi = DefaultDpi,
                WithAspectRatio = true,
                Width = maxWidth
            };

#pragma warning disable CA1416
            using var stream = File.OpenRead(path);
            using var bitmap = Conversion.ToImage(stream, page: pageNumber, options: options);
            if (bitmap is null)
            {
                return null;
            }

            using var jpeg = bitmap.Encode(SKEncodedImageFormat.Jpeg, JpegQuality);
            return jpeg?.ToArray();
#pragma warning restore CA1416
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Unable to render page {PageNumber} of {Path}", pageNumber, path);
            return null;
        }
    }
}
