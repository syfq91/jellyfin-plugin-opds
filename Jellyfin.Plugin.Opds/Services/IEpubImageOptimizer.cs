using Jellyfin.Plugin.Opds.Models;
using MediaBrowser.Controller.Entities;

namespace Jellyfin.Plugin.Opds.Services;

/// <summary>
/// Produces EPUB copies with oversized images downscaled, on demand.
/// </summary>
public interface IEpubImageOptimizer
{
    /// <summary>
    /// Gets a file to serve for the given EPUB. When no image needs downscaling,
    /// or the book cannot be repacked, the original file is returned unchanged.
    /// </summary>
    /// <param name="book">The book.</param>
    /// <returns>The result, or <c>null</c> when the book is not an EPUB.</returns>
    OptimizedEpubResult? Optimize(Book book);
}
