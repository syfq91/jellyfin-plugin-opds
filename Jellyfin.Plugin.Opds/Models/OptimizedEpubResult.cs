namespace Jellyfin.Plugin.Opds.Models;

/// <summary>
/// The outcome of an optimized EPUB request.
/// </summary>
/// <param name="Path">The path of the file to serve.</param>
/// <param name="WasOptimized">Whether a downscaled copy was produced.</param>
public sealed record OptimizedEpubResult(string Path, bool WasOptimized);
